using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SimpleRay.App;

/// <summary>
/// Editor for the user's direct-by-domain list. Domains are entered as tags: type a
/// domain and press comma or Enter to accept it (it becomes a green chip), so the user
/// sees exactly what the program will use. Paste of a comma/newline-separated list works too.
/// </summary>
public partial class DomainsWindow : Window
{
    /// <summary>The edited list, available after the dialog returns true.</summary>
    public List<string> Domains { get; private set; } = new();

    private readonly ObservableCollection<string> _chips = new();
    private bool _suppressTextChanged;

    public DomainsWindow(IEnumerable<string> current, string? title = null, string? hint = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(title)) Title = title; // else keep the XAML default (direct domains)
        if (!string.IsNullOrEmpty(hint)) Hint.Text = hint;
        foreach (var d in current.Select(Normalize).Where(s => s.Length > 0))
            if (!_chips.Contains(d))
                _chips.Add(d);
        Chips.ItemsSource = _chips;
        Loaded += (_, _) => Input.Focus();
    }

    // Typing (or pasting) a comma/newline accepts the text before it as a tag.
    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged) return;
        if (Input.Text.IndexOfAny(new[] { ',', '\r', '\n' }) < 0) return;

        var parts = Input.Text.Split(new[] { ',', '\r', '\n' });
        for (int i = 0; i < parts.Length - 1; i++)
            AddChip(parts[i]);

        // Keep the final (still-being-typed) segment in the box.
        _suppressTextChanged = true;
        Input.Text = parts[^1].TrimStart();
        Input.CaretIndex = Input.Text.Length;
        _suppressTextChanged = false;
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Enter with text accepts the tag; Enter on an empty box confirms the dialog (OK is default).
            if (Input.Text.Trim().Length > 0)
            {
                AddChip(Input.Text);
                Input.Clear();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Back && Input.Text.Length == 0 && _chips.Count > 0)
        {
            // Backspace on an empty box pulls the last tag back for editing.
            Input.Text = _chips[^1];
            Input.CaretIndex = Input.Text.Length;
            _chips.RemoveAt(_chips.Count - 1);
            e.Handled = true;
        }
    }

    private void RemoveChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string s })
            _chips.Remove(s);
    }

    private void AddChip(string raw)
    {
        var d = Normalize(raw);
        if (d.Length > 0 && !_chips.Contains(d))
            _chips.Add(d);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        AddChip(Input.Text); // fold any pending text into a tag
        Domains = _chips.ToList();
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Strips scheme/path/spaces/leading dots and lowercases — mirrors the generator's
    /// CleanDomains so the chip shows exactly what routing will use.
    /// </summary>
    private static string Normalize(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return "";
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        int slash = s.IndexOf('/');
        if (slash >= 0) s = s[..slash];
        return s.Trim().TrimStart('.').Trim();
    }
}
