namespace SimpleRay.App.Infrastructure;

/// <summary>
/// Modal dialogs a view model needs, behind an interface so the view model doesn't
/// reference WPF's MessageBox directly (keeps it unit-testable / substitutable).
/// </summary>
public interface IDialogService
{
    /// <summary>Shows a yes/no question. Returns true only if the user chose yes.</summary>
    bool Confirm(string message, string title);
}
