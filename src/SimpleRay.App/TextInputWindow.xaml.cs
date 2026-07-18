using System.Windows;

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

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
