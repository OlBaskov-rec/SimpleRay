using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SimpleRay.App.Infrastructure;

/// <summary>
/// Tooltip text with structure: in a multi-line tip the first line is a bold headline ("what it
/// does" in a few words), the rest follows as written — description, a blank line, then how it
/// works. A one-line tip stays plain. Used by the app-wide ToolTip style in App.xaml.
/// </summary>
public sealed class RichTip : TextBlock
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(object), typeof(RichTip),
        new PropertyMetadata(null, (d, e) => ((RichTip)d).Build(e.NewValue)));

    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void Build(object? value)
    {
        Inlines.Clear();
        var lines = (value?.ToString() ?? "").Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 1)
        {
            Inlines.Add(new Run(lines[0]));
            return;
        }
        Inlines.Add(new Bold(new Run(lines[0])));
        for (int i = 1; i < lines.Length; i++)
        {
            Inlines.Add(new LineBreak());
            Inlines.Add(new Run(lines[i]));
        }
    }
}
