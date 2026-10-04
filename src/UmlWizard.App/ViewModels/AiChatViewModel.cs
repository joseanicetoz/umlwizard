using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmlWizard.Ai;
using UmlWizard.App.Resources;

namespace UmlWizard.App.ViewModels;

/// <summary>Who wrote a message of the chat.</summary>
public enum ChatRole
{
    User,
    Assistant,
    Error,
}

/// <summary>A message of the chat panel; the AI's text grows while its answer streams in.</summary>
public sealed partial class ChatEntry(ChatRole role, string text) : ObservableObject
{
    public ChatRole Role { get; } = role;

    [ObservableProperty]
    public partial string Text { get; set; } = text;

    public bool IsUser => Role == ChatRole.User;

    public bool IsAssistant => Role == ChatRole.Assistant;

    public bool IsError => Role == ChatRole.Error;
}

/// <summary>What the chat needs from the window: the service, the settings, and the project's consent to send its diagrams.</summary>
/// <param name="Provider">The AI service now, or <c>null</c> while none is set up.</param>
/// <param name="ChangeMode">How the AI changes diagrams, from Preferences → AI.</param>
/// <param name="OpenPreferences">Opens Preferences, to set up a service.</param>
/// <param name="ProjectName">The name of the open project.</param>
/// <param name="IsAllowed">Whether the user allowed the project's diagrams to be sent.</param>
/// <param name="Allow">Records that the user allowed it, for the project.</param>
public sealed record AiChatServices(
    Func<IAiProvider?> Provider,
    Func<AiChangeMode> ChangeMode,
    Func<Task> OpenPreferences,
    Func<string> ProjectName,
    Func<bool> IsAllowed,
    Action Allow);

/// <summary>
/// The AI chat about one diagram (View → AI Chat): the conversation, and the changes the AI proposes, which the user
/// applies as one undoable step or discards. The service is the one of Preferences → AI at the time of each message.
/// Before the first message of a project, the user is asked once whether its diagrams may be sent.
/// </summary>
public sealed partial class AiChatViewModel : ObservableObject
{
    private readonly DiagramViewModel _diagram;
    private readonly AiChatServices _services;
    private readonly AiChatSession _session = new();
    private CancellationTokenSource? _request;

    public AiChatViewModel(DiagramViewModel diagram, AiChatServices services)
    {
        _diagram = diagram;
        _services = services;
    }

    public ObservableCollection<ChatEntry> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>What the user is writing.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(ApplyCommand), nameof(DiscardCommand))]
    public partial bool IsWorking { get; set; }

    /// <summary>True while the user is asked whether the project's diagrams may be sent to the service.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool NeedsConsent { get; set; }

    /// <summary>The service, or <c>null</c> while none is set up; checked again when the panel is shown.</summary>
    public string? ServiceName => _services.Provider()?.DisplayName;

    public bool IsSetUp => ServiceName is not null;

    /// <summary>Says where messages and the diagram go, and what the AI may do.</summary>
    public string Note => ServiceName is { } name ? Loc.Format(Strings.AiChat_Note_CanEdit, name) : Strings.AiChat_NotSetUp;

    /// <summary>The question asked before the project's first message is sent.</summary>
    public string ConsentText => Loc.Format(Strings.AiChat_Consent, _services.ProjectName(), ServiceName ?? string.Empty);

    /// <summary>The changes the AI proposes, one per line; empty when there are none.</summary>
    public IReadOnlyList<string> Changes => _session.DescribeChanges(_diagram.Document);

    public bool HasChanges => _session.HasChanges;

    private bool CanSend => !IsWorking && !NeedsConsent && !string.IsNullOrWhiteSpace(Input);

    /// <summary>Sends the message, with the diagram, and shows the answer as it comes and the changes proposed.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (_services.Provider() is not { } provider)
        {
            Refresh();
            return;
        }

        if (!_services.IsAllowed())
        {
            // The message waits in the box until the user answers.
            NeedsConsent = true;
            OnPropertyChanged(nameof(ConsentText));
            return;
        }

        var text = Input.Trim();
        Input = string.Empty;
        Entries.Add(new ChatEntry(ChatRole.User, text));
        OnPropertyChanged(nameof(IsEmpty));
        IsWorking = true;
        _session.ChangeMode = _services.ChangeMode();
        ChatEntry? answer = null;
        void Show(string sofar)
        {
            if (answer is null)
            {
                answer = new ChatEntry(ChatRole.Assistant, sofar);
                Entries.Add(answer);
            }
            else
            {
                answer.Text = sofar;
            }
        }

        using (_request = new CancellationTokenSource())
        {
            try
            {
                Show((await _session.SendAsync(provider, _diagram.Document, text, _request.Token, Show)).Trim());
            }
            catch (Exception ex) when (ex is OperationCanceledException or AiException)
            {
                // Nothing of a failed request is kept, not even the part of the answer that came.
                if (answer is not null)
                {
                    Entries.Remove(answer);
                }

                Entries.Add(new ChatEntry(ChatRole.Error, ex is AiException ? ex.Message : Strings.AiChat_Stopped));
            }
            finally
            {
                _request = null;
                IsWorking = false;
                Refresh();
            }
        }
    }

    /// <summary>Allows the project's diagrams to be sent, from now on, and sends the message that waited.</summary>
    [RelayCommand]
    private async Task AllowSending()
    {
        _services.Allow();
        NeedsConsent = false;
        if (SendCommand.CanExecute(null))
        {
            await SendCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Does not send; the message stays in the box.</summary>
    [RelayCommand]
    private void CancelSending() => NeedsConsent = false;

    /// <summary>Stops the request that runs; nothing it did is kept.</summary>
    [RelayCommand]
    private void Stop() => _request?.Cancel();

    private bool CanDecide => HasChanges && !IsWorking;

    /// <summary>Applies the changes to the diagram, as one step that Undo takes back.</summary>
    [RelayCommand(CanExecute = nameof(CanDecide))]
    private void Apply()
    {
        if (_session.TakeChanges() is { } changed)
        {
            _diagram.Editor.ReplaceContent(changed);
        }

        Refresh();
    }

    /// <summary>Drops the changes; the diagram stays as it was.</summary>
    [RelayCommand(CanExecute = nameof(CanDecide))]
    private void Discard()
    {
        _session.Discard();
        Refresh();
    }

    /// <summary>Opens Preferences, to set up a service.</summary>
    [RelayCommand]
    private async Task SetUp()
    {
        await _services.OpenPreferences();
        Refresh();
    }

    /// <summary>Updates what depends on the service and the changes, e.g. after Preferences closes.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ServiceName));
        OnPropertyChanged(nameof(IsSetUp));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(ConsentText));
        OnPropertyChanged(nameof(Changes));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsEmpty));
        ApplyCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }
}
