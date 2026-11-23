using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;

namespace SimpleRay.App.Tests;

/// <summary>
/// Loads a window's real XAML with the WPF XAML reader, without its code-behind, so tests can
/// check the markup (and its bindings) without constructing the window class.
/// </summary>
internal static class WindowXaml
{
    /// <summary>Parses <c>src/SimpleRay.App/{name}.xaml</c>. Must run on an STA thread.</summary>
    public static Window Load(string name)
    {
        var xaml = PrepareForReader(
            File.ReadAllText(FindSource($"src/SimpleRay.App/{name}.xaml")),
            File.ReadAllText(FindSource($"src/SimpleRay.App/{name}.xaml.cs")));
        EnsurePackScheme();
        return (Window)XamlReader.Parse(xaml);
    }

    /// <summary>Runs <paramref name="action"/> on a fresh STA thread and rethrows its failure.</summary>
    public static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>
    /// Makes the compiled-XAML file loadable by XamlReader: drops x:Class, drops attributes wired
    /// to code-behind handlers, and names the assembly for local clr-namespaces.
    /// </summary>
    internal static string PrepareForReader(string xaml, string codeBehind)
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

    internal static string FindSource(string relative)
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
