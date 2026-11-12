using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using SimpleRay.App.Localization;
using SimpleRay.App.ViewModels;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Tests;

/// <summary>
/// The test switches the process-wide UI language, so it runs alone rather than alongside
/// tests that read localized strings.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalLocalizationCollection
{
    public const string Name = "Global localization";
}

/// <summary>
/// Regression: switching the UI language blanked the routing-mode box (red border). The mode
/// list used to be rebuilt on every culture change, so the ComboBox dropped its selection and
/// pushed null into the non-nullable SelectedMode. Drives a real WPF ComboBox with the real
/// mode items and the in-place relabel that MainViewModel.OnCultureChanged performs.
/// </summary>
[Collection(GlobalLocalizationCollection.Name)]
public class RoutingModeLanguageSwitchTests
{
    private sealed class ModeHolder : INotifyPropertyChanged
    {
        private RoutingMode _mode;

        public RoutingMode SelectedMode
        {
            get => _mode;
            set { _mode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMode))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void ModeBox_KeepsSelectionAndRelabels_WhenLanguageChanges()
    {
        RunOnSta(() =>
        {
            var loc = LocalizationManager.Instance;
            var original = loc.CurrentCulture;
            try
            {
                loc.SetCulture("en", persist: false); // never touch the user's saved language
                var items = MainViewModel.CreateModeItems();
                var holder = new ModeHolder { SelectedMode = RoutingMode.Rule };
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedValuePath = nameof(MainViewModel.RoutingModeItem.Mode),
                };
                combo.SetBinding(Selector.SelectedValueProperty,
                    new Binding(nameof(ModeHolder.SelectedMode)) { Source = holder, Mode = BindingMode.TwoWay });

                var before = Assert.IsType<MainViewModel.RoutingModeItem>(combo.SelectedItem);
                string englishLabel = before.Label;

                loc.SetCulture("ru", persist: false);
                foreach (var mode in items)
                    mode.RefreshLabel();

                var after = Assert.IsType<MainViewModel.RoutingModeItem>(combo.SelectedItem);
                Assert.Same(before, after);                          // still the selected item
                Assert.Equal(RoutingMode.Rule, holder.SelectedMode); // nothing pushed back
                Assert.False(Validation.GetHasError(combo));         // no red error border
                Assert.NotEqual(englishLabel, after.Label);          // now in Russian
            }
            finally
            {
                loc.SetCulture(original, persist: false);
            }
        });
    }

    [Fact]
    public void RefreshLabel_RaisesLabelChanged()
    {
        var item = MainViewModel.CreateModeItems()[0];
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.RefreshLabel();

        Assert.Equal(new[] { nameof(MainViewModel.RoutingModeItem.Label) }, raised);
    }

    private static void RunOnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Xunit.Sdk.XunitException("STA test failed: " + failure);
    }
}
