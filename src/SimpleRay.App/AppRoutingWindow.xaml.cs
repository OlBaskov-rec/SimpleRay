using System.Windows;
using System.Windows.Controls;
using SimpleRay.App.ViewModels;
using SimpleRay.Core.Models;

namespace SimpleRay.App;

public partial class AppRoutingWindow : Window
{
    private readonly AppRoutingViewModel _viewModel;

    public AppRoutingWindow(RoutingSettings routing)
    {
        InitializeComponent();
        DataContext = _viewModel = new AppRoutingViewModel(routing);
    }

    /// <summary>
    /// Drops down the catalog of standard utilities, grouped by purpose. Built on each click so
    /// the "already in…" marks reflect the current lists.
    /// </summary>
    private void SystemUtils_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var group in _viewModel.UtilityGroups())
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = group.Title, IsEnabled = false, FontWeight = FontWeights.SemiBold });
            foreach (var item in group.Items)
            {
                var mi = new MenuItem
                {
                    Header = item.Placement is null ? item.Header : $"{item.Header}   ({item.Placement})",
                    ToolTip = item.Description,
                };
                var process = item.Process;
                mi.Click += (_, _) => _viewModel.PickUtility(process);
                menu.Items.Add(mi);
            }
        }
        menu.IsOpen = true;
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
