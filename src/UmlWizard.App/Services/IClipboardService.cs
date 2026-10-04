using UmlWizard.App.ViewModels.Dialogs;

namespace UmlWizard.App.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);

    Task<string?> GetTextAsync();
}
