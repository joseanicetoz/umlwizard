using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using UmlWizard.App.ViewModels;

namespace UmlWizard.App.Views;

/// <summary>The AI chat panel; Ctrl+Enter (⌘+Enter on macOS) sends, and the conversation scrolls to its newest message.</summary>
public sealed partial class AiChatPanel : UserControl
{
    private AiChatViewModel? _chat;

    public AiChatPanel()
    {
        InitializeComponent();

        // On the way in: the text box would otherwise take Enter for a new line first.
        InputBox.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // A click anywhere in the input box, not just on its text, puts the caret in the text.
        Composer.PointerPressed += (_, _) => InputBox.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_chat is not null)
        {
            _chat.Entries.CollectionChanged -= OnEntriesChanged;
        }

        _chat = DataContext as AiChatViewModel;
        if (_chat is not null)
        {
            _chat.Entries.CollectionChanged += OnEntriesChanged;
            _chat.Refresh();
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => Conversation.ScrollToEnd(), DispatcherPriority.Background);

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(Shortcuts.CommandModifier) && _chat?.SendCommand.CanExecute(null) == true)
        {
            _chat.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
