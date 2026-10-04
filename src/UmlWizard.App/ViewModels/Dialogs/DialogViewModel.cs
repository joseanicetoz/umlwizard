using CommunityToolkit.Mvvm.ComponentModel;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Base for view models shown in the modal OK/Cancel <c>DialogWindow</c>.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    public abstract string Title { get; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Validates the input; the dialog only closes when this returns true.</summary>
    public bool TryAccept()
    {
        ErrorMessage = Validate();
        return ErrorMessage is null;
    }

    /// <returns>An error message, or <c>null</c> when the input is valid.</returns>
    protected virtual string? Validate() => null;
}
