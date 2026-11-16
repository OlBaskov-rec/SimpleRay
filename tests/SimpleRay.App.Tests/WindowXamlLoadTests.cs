using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;

namespace SimpleRay.App.Tests;

/// <summary>
/// Regression (0.3.10–0.3.11): the main window crashed while loading, before any code ran —
/// a Hyperlink got <c>NavigateUri="{x:Static ...SourceUrl}"</c>, a string where WPF wants a Uri.
/// No test built the window, so it shipped. This test parses the real MainWindow.xaml with the
/// WPF XAML reader. The code-behind and MainViewModel are left out (the view model touches the
/// user's data folder), so the markup is checked on its own: every element, resource, style and
/// static value is created exactly as at startup.
/// </summary>
[Collection(GlobalLocalizationCollection.Name)]
public class WindowXamlLoadTests
{
    [Theory]
    [InlineData("MainWindow")]
    [InlineData("AppRoutingWindow")]
    [InlineData("DomainsWindow")]
    [InlineData("TextInputWindow")]
    [InlineData("WebcamQrWindow")]
    public void WindowMarkupLoads(string window)
    {
        var xaml = PrepareForReader(
            File.ReadAllText(FindSource($"src/SimpleRay.App/{window}.xaml")),
            File.ReadAllText(FindSource($"src/SimpleRay.App/{window}.xaml.cs")));

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsurePackScheme();
                var window = (Window)XamlReader.Parse(xaml);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(failure is null, failure?.ToString());
    }

    /// <summary>
    /// Makes the compiled-XAML file loadable by XamlReader: drops x:Class, drops attributes wired
    /// to code-behind handlers, and names the assembly for local clr-namespaces.
    /// </summary>
    private static string PrepareForReader(string xaml, string codeBehind)
    {
        var handlers = Regex.Matches(codeBehind, @"void\s+(\w+)\s*\(\s*object\??\s+\w+\s*,")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.NotEmpty(handlers);

        xaml = Regex.Replace(xaml, @"\s+x:Class=""[^""]*""", "");
        xaml = Regex.Replace(xaml, @"\s+[\w.]+=""(\w+)""",
            m => handlers.Contains(m.Groups[1].Value) ? "" : m.Value);
        xaml = Regex.Replace(xaml, @"xmlns:(\w+)=""clr-namespace:([\w.]+)""",
            m => $@"xmlns:{m.Groups[1].Value}=""clr-namespace:{m.Groups[2].Value};assembly=SimpleRay""");
        // In the test host "application" is not SimpleRay, so name the assembly explicitly.
        xaml = xaml.Replace("pack://application:,,,/Resources/", "pack://application:,,,/SimpleRay;component/Resources/");
        return xaml;
    }

    private static void EnsurePackScheme()
    {
        // Touching PackUriHelper registers the pack:// scheme; the window icon uses it.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        // XAML resolves xmlns URLs only in assemblies already loaded; the app loads the tray
        // library at startup, the test host does not.
        _ = typeof(Hardcodet.Wpf.TaskbarNotification.TaskbarIcon).Assembly;
        if (Application.ResourceAssembly is null)
        {
            try { Application.ResourceAssembly = typeof(SimpleRay.App.MainWindow).Assembly; }
            catch (InvalidOperationException) { /* already set by another test */ }
        }
    }

    private static string FindSource(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
