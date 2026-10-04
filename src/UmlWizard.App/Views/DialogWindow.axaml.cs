using Avalonia.Controls;
using Avalonia.Interactivity;
using UmlWizard.App.ViewModels.Dialogs;

namespace UmlWizard.App.Views;

/// <summary>Modal OK/Cancel window hosting the view of a <see cref="DialogViewModel"/>.</summary>
public sealed partial class DialogWindow : Window
{
    public DialogWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DialogViewModel viewModel && viewModel.TryAccept())
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
