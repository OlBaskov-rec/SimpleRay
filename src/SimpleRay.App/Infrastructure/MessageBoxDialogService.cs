using System.Windows;

namespace SimpleRay.App.Infrastructure;

/// <summary>Default <see cref="IDialogService"/> backed by WPF's <see cref="MessageBox"/>.</summary>
public sealed class MessageBoxDialogService : IDialogService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
}
