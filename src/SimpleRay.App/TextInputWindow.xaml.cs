using System.Windows;
using System.Windows.Controls;

namespace SimpleRay.App;

/// <summary>Minimal single-line text prompt dialog. Returns the entered <see cref="Value"/>.</summary>
public partial class TextInputWindow : Window
{
    public string Value => Input.Text.Trim();

    public TextInputWindow(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    /// <summary>Shows a short help line under the prompt whose tooltip carries <paramref name="details"/>.</summary>
    public TextInputWindow WithHint(string line, string details)
    {
        HintText.Text = line;
        HintText.ToolTip = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        HintText.Visibility = Visibility.Visible;
        return this;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
