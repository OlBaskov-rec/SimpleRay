using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SimpleRay.App;

/// <summary>Multiline editor for the user's direct-by-domain list (one domain per line).</summary>
public partial class DomainsWindow : Window
{
    /// <summary>The edited list, available after the dialog returns true.</summary>
    public List<string> Domains { get; private set; } = new();

    public DomainsWindow(IEnumerable<string> current)
    {
        InitializeComponent();
        Input.Text = string.Join(Environment.NewLine, current);
        Loaded += (_, _) => Input.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Domains = Input.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        DialogResult = true;
        Close();
    }
}
