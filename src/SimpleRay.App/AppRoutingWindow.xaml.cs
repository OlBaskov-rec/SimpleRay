using System.Windows;
using SimpleRay.App.ViewModels;
using SimpleRay.Core.Models;

namespace SimpleRay.App;

public partial class AppRoutingWindow : Window
{
    public AppRoutingWindow(RoutingSettings routing)
    {
        InitializeComponent();
        DataContext = new AppRoutingViewModel(routing);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
