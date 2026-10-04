using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;

namespace UmlWizard.App.Services;

public interface IDialogService
{
    Task<string?> PickOpenFileAsync(string title, string fileTypeName, IReadOnlyList<string> patterns);

    Task<string?> PickSaveFileAsync(
        string title, string suggestedName, string defaultExtension, string fileTypeName, IReadOnlyList<string> patterns);

    Task<string?> PickFolderAsync(string title);

    /// <summary>Shows a modal OK/Cancel dialog for <paramref name="viewModel"/>; true when accepted.</summary>
    Task<bool> ShowDialogAsync(DialogViewModel viewModel);

    Task<ConfirmResult> ConfirmAsync(string title, string message, bool allowCancel = false);

    /// <summary>Asks whether to go on with something that cannot be undone, with Continue and Cancel; true to continue.</summary>
    Task<bool> ConfirmContinueAsync(string title, string message);

    Task ShowErrorAsync(string title, string message);

    /// <summary>Shows information with an OK button.</summary>
    Task ShowMessageAsync(string title, string message);

    Task ShowAboutAsync(AboutViewModel viewModel);

    /// <summary>Shows the Preferences window; its changes apply as they are made, so it only has a Close button.</summary>
    Task ShowPreferencesAsync(PreferencesViewModel viewModel);

    /// <summary>Opens a file in the system's viewer for its type; false when it could not be opened.</summary>
    Task<bool> OpenFileAsync(string path);
}
