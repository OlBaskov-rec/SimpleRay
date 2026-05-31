using System.Windows;
using SimpleRay.App.ViewModels;

namespace SimpleRay.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closed += async (_, _) =>
        {
            TrayIcon.Dispose();
            await _viewModel.ShutdownAsync();
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
}
