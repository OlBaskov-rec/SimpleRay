using System.Windows.Data;
using System.Windows.Markup;

namespace SimpleRay.App.Localization;

/// <summary>
/// XAML markup extension: <c>{loc:Loc Key}</c> binds a property to the current
/// localized string for <c>Key</c>. Because it targets the LocalizationManager
/// indexer, switching language updates every use live.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
