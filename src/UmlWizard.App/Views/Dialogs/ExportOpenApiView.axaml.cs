using Avalonia.Controls;
using Avalonia.Interactivity;
using UmlWizard.App.ViewModels.Dialogs;

namespace UmlWizard.App.Views.Dialogs;

public sealed partial class ExportOpenApiView : UserControl
{
    public ExportOpenApiView() => InitializeComponent();

    /// <summary>Leaving the folder or the file name checks whether the file already exists.</summary>
    private void OnFieldLostFocus(object? sender, RoutedEventArgs e) => (DataContext as ExportOpenApiViewModel)?.CheckFileExists();
}
