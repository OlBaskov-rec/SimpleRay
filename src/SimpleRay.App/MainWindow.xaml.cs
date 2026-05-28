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
        Closed += async (_, _) => await _viewModel.ShutdownAsync();
    }
}
