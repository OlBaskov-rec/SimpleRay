using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Localization;
using SimpleRay.App.ViewModels;

namespace SimpleRay.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(bool connectOnStartup = false)
    {
        InitializeComponent();
        AboutPopup.CustomPopupPlacementCallback = PlaceAboutPopup;
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.TryAutoConnectOnStartupAsync(connectOnStartup);
        Closed += (_, _) =>
        {
            TrayIcon.Dispose();
            // Blocks until sing-box is fully stopped. Application.Shutdown stops the
            // message pump, so an awaited continuation here might never resume — an
            // orphaned sing-box with strict_route would leave the user offline.
            // Safe to block: the view model dispatches engine events via BeginInvoke.
            _viewModel.ShutdownAsync().GetAwaiter().GetResult();
        };
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        LanguageButton.ContextMenu.PlacementTarget = LanguageButton;
        LanguageButton.ContextMenu.Placement = PlacementMode.Bottom;
        LanguageButton.ContextMenu.IsOpen = true;
    }

    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: LanguageOption language })
            _viewModel.SelectedLanguage = language.Code;
    }

    private void Info_Click(object sender, RoutedEventArgs e) => AboutPopup.IsOpen = !AboutPopup.IsOpen;

    private static CustomPopupPlacement[] PlaceAboutPopup(Size popupSize, Size targetSize, Point offset) =>
        new[] { new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal) };

    private void AboutPopup_Opened(object sender, EventArgs e) => UpdateButton.Focus();

    private void AboutPopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        AboutPopup.IsOpen = false;
        InfoButton.Focus();
        e.Handled = true;
    }

    private void Source_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        UrlLauncher.TryOpen(MainViewModel.SourceUrl);
        e.Handled = true;
    }

    // Rename / Remove act on whichever list was used last: a subscription or a server.
    private void Sources_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
        _viewModel.SetActiveList(sources: true);

    private void Servers_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
        _viewModel.SetActiveList(sources: false);

    // The close button hides to the tray instead of quitting (quit via the tray menu). The first
    // time, a balloon from the tray icon shows where the app went.
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsExiting)
            return;
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        if (_viewModel.TakeFirstTrayHint())
            TrayIcon.ShowBalloonTip(_viewModel.TrayHintTitle, _viewModel.TrayHintText,
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    // Minimize hides the window to the tray instead of the taskbar.
    private void Window_StateChanged(object sender, System.EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void Tray_ShowWindow(object sender, RoutedEventArgs e)
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Tray_ToggleConnect(object sender, RoutedEventArgs e) =>
        _viewModel.ConnectCommand.Execute(null);

    private void Tray_CheckUpdates(object sender, RoutedEventArgs e) =>
        _viewModel.CheckUpdatesCommand.Execute(null);

    private void Tray_Exit(object sender, RoutedEventArgs e) => App.Quit();

    // No owner window: this is also reachable from the tray while the window is hidden.
    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(_viewModel.AboutText, _viewModel.AboutTitle,
            MessageBoxButton.OK, MessageBoxImage.Information);
}
