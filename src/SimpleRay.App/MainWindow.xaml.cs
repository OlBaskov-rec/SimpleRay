using System.Windows;
using SimpleRay.App.ViewModels;

namespace SimpleRay.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(bool connectOnStartup = false)
    {
        InitializeComponent();
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

    private void Tray_Exit(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    // No owner window: this is also reachable from the tray while the window is hidden.
    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(_viewModel.AboutText, _viewModel.AboutTitle,
            MessageBoxButton.OK, MessageBoxImage.Information);
}
