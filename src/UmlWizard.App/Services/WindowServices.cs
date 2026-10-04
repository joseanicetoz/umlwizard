using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using UmlWizard.App.Resources;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;

namespace UmlWizard.App.Services;

/// <summary>
/// Dialog and clipboard services bound to the main window. A dialog opened from another one (e.g. the colour picker from
/// Preferences) is owned by it, so it opens above it.
/// </summary>
public sealed class WindowServices(Window owner) : IDialogService, IClipboardService
{
    private readonly List<Window> _openDialogs = [];
    private string? _fallbackClipboard;

    /// <summary>The window a new dialog belongs to: the dialog on top, else the main window.</summary>
    private Window Owner => _openDialogs.Count > 0 ? _openDialogs[^1] : owner;

    /// <summary>Shows a dialog owned by <see cref="Owner"/>, and makes it the owner of the dialogs it opens.</summary>
    private async Task<T> ShowModalAsync<T>(Window dialog, Func<Window, Task<T>> show)
    {
        var parent = Owner;
        _openDialogs.Add(dialog);
        try
        {
            return await show(parent);
        }
        finally
        {
            _openDialogs.Remove(dialog);
        }
    }

    public async Task<string?> PickOpenFileAsync(string title, string fileTypeName, IReadOnlyList<string> patterns)
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileTypeName) { Patterns = patterns }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(
        string title, string suggestedName, string defaultExtension, string fileTypeName, IReadOnlyList<string> patterns)
    {
        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = patterns }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ShowDialogAsync(DialogViewModel viewModel)
    {
        var dialog = new DialogWindow { DataContext = viewModel };
        return await ShowModalAsync(dialog, parent => dialog.ShowDialog<bool?>(parent)) == true;
    }

    public Task<ConfirmResult> ConfirmAsync(string title, string message, bool allowCancel = false) =>
        MessageWindow.ShowAsync(Owner, title, message, allowCancel
            ? [(Strings.Button_Yes, ConfirmResult.Yes), (Strings.Button_No, ConfirmResult.No), (Strings.Button_Cancel, ConfirmResult.Cancel)]
            : [(Strings.Button_Yes, ConfirmResult.Yes), (Strings.Button_No, ConfirmResult.No)]);

    public async Task<bool> ConfirmContinueAsync(string title, string message) =>
        await MessageWindow.ShowAsync(Owner, title, message,
            [(Strings.Button_Continue, ConfirmResult.Yes), (Strings.Button_Cancel, ConfirmResult.Cancel)]) == ConfirmResult.Yes;

    public Task ShowErrorAsync(string title, string message) =>
        MessageWindow.ShowAsync(Owner, title, message, [(Strings.Button_OK, ConfirmResult.Yes)]);

    public Task ShowMessageAsync(string title, string message) =>
        MessageWindow.ShowAsync(Owner, title, message, [(Strings.Button_OK, ConfirmResult.Yes)]);

    public Task ShowAboutAsync(AboutViewModel viewModel)
    {
        var window = new AboutWindow { DataContext = viewModel };
        return ShowModalAsync(window, async parent => { await window.ShowDialog(parent); return true; });
    }

    public Task ShowPreferencesAsync(PreferencesViewModel viewModel)
    {
        var window = new PreferencesWindow { DataContext = viewModel };
        return ShowModalAsync(window, async parent => { await window.ShowDialog(parent); return true; });
    }

    public Task<bool> OpenFileAsync(string path) => owner.Launcher.LaunchFileInfoAsync(new FileInfo(path));

    public async Task SetTextAsync(string text)
    {
        _fallbackClipboard = text;
        if (owner.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        if (owner.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            return text;
        }

        return _fallbackClipboard;
    }
}
