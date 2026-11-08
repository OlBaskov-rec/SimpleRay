using System.Windows;
using SimpleRay.App.ViewModels;

namespace SimpleRay.App;

/// <summary>Lists the saved subscriptions with per-row refresh / edit / remove.</summary>
public partial class SubscriptionsWindow : Window
{
    private readonly SubscriptionsViewModel _viewModel;

    public SubscriptionsWindow(ISubscriptionHost host)
    {
        InitializeComponent();
        DataContext = _viewModel = new SubscriptionsViewModel(host);
        Closed += (_, _) => _viewModel.Detach();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
