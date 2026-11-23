namespace SimpleRay.App.Tests;

/// <summary>
/// Regression (0.3.10–0.3.11): the main window crashed while loading, before any code ran —
/// a Hyperlink got <c>NavigateUri="{x:Static ...SourceUrl}"</c>, a string where WPF wants a Uri.
/// No test built the window, so it shipped. This test parses the real MainWindow.xaml with the
/// WPF XAML reader (see <see cref="WindowXaml"/>) without the code-behind: every element,
/// resource, style and static value is created exactly as at startup. Bindings against a real
/// view model are checked in <see cref="MainViewModelLifecycleTests"/>.
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
        WindowXaml.RunOnSta(() => WindowXaml.Load(window).Close());
    }
}
