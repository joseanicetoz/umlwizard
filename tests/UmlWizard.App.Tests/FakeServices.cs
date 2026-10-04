using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.Controls;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

internal sealed class FakeServices : IDialogService, IClipboardService
{
    public List<DialogViewModel> ShownDialogs { get; } = [];

    public bool AcceptDialogs { get; set; }

    /// <summary>Simulates user input in a shown dialog before it is accepted or cancelled.</summary>
    public Action<DialogViewModel>? FillDialog { get; set; }

    public string? ClipboardText { get; set; }

    public Task<string?> PickOpenFileAsync(string title, string fileTypeName, IReadOnlyList<string> patterns) =>
        Task.FromResult(OpenFilePath);

    /// <summary>What the open-file picker returns; <c>null</c> (the default) cancels it.</summary>
    public string? OpenFilePath { get; set; }

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string defaultExtension, string fileTypeName,
        IReadOnlyList<string> patterns) => Task.FromResult(SaveFilePath);

    /// <summary>What the save-file picker returns; <c>null</c> cancels it.</summary>
    public string? SaveFilePath { get; set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderPath);

    /// <summary>What the folder picker returns; <c>null</c> cancels it.</summary>
    public string? FolderPath { get; set; }

    public Task<bool> ShowDialogAsync(DialogViewModel viewModel)
    {
        ShownDialogs.Add(viewModel);
        FillDialog?.Invoke(viewModel);
        return Task.FromResult(AcceptDialogs);
    }

    /// <summary>The answer to every confirmation question.</summary>
    public ConfirmResult ConfirmAnswer { get; set; } = ConfirmResult.No;

    public List<string> ConfirmMessages { get; } = [];

    public Task<ConfirmResult> ConfirmAsync(string title, string message, bool allowCancel = false)
    {
        ConfirmMessages.Add(message);
        return Task.FromResult(ConfirmAnswer);
    }

    /// <summary>The answer to every Continue or Cancel question; true continues.</summary>
    public bool ContinueAnswer { get; set; }

    public Task<bool> ConfirmContinueAsync(string title, string message)
    {
        ConfirmMessages.Add(message);
        return Task.FromResult(ContinueAnswer);
    }

    public List<string> Errors { get; } = [];

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public List<string> Messages { get; } = [];

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public List<AboutViewModel> ShownAbout { get; } = [];

    public Task ShowAboutAsync(AboutViewModel viewModel)
    {
        ShownAbout.Add(viewModel);
        return Task.CompletedTask;
    }

    public List<PreferencesViewModel> ShownPreferences { get; } = [];

    public Task ShowPreferencesAsync(PreferencesViewModel viewModel)
    {
        ShownPreferences.Add(viewModel);
        return Task.CompletedTask;
    }

    public List<string> OpenedFiles { get; } = [];

    public Task<bool> OpenFileAsync(string path)
    {
        OpenedFiles.Add(path);
        return Task.FromResult(true);
    }

    public Task SetTextAsync(string text)
    {
        ClipboardText = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() => Task.FromResult(ClipboardText);
}
