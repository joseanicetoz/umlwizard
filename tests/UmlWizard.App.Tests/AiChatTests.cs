using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.Ai;
using UmlWizard.App.Controls;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.App.Tests;

/// <summary>An AI service that holds a conversation from a script of turns; no test talks to a real one.</summary>
internal sealed class FakeChatProvider(Func<IReadOnlyList<AiMessage>, CancellationToken, Task<AiTurn>> answer) : IAiProvider
{
    public string DisplayName => "Fake AI";

    public static FakeChatProvider Scripted(params AiTurn[] turns)
    {
        var queue = new Queue<AiTurn>(turns);
        return new((_, _) => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : new AiTurn("Done.", [])));
    }

    public static AiToolCall Call(string name, string arguments) => new(Guid.NewGuid().ToString("N")[..8], name, (JsonObject)JsonNode.Parse(arguments)!);

    public Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<AiTurn> ChatAsync(string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default) =>
        answer(messages, cancellationToken);
}

/// <summary>An AI service whose answer comes in parts; <paramref name="afterEach"/> runs after each part is reported.</summary>
internal sealed class StreamingChatProvider(IReadOnlyList<string> parts, Action afterEach) : IAiProvider
{
    public string DisplayName => "Streaming AI";

    public Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<AiTurn> ChatAsync(string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiTurn(parts[^1], []));

    public async Task<AiTurn> StreamChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string> onText, CancellationToken cancellationToken = default)
    {
        foreach (var part in parts)
        {
            await Task.Yield();
            onText(part);
            afterEach();
        }

        return new AiTurn(parts[^1], []);
    }
}

public sealed class AiChatTests
{
    private static readonly AppSettings WithClaude = new() { Ai = new AiServiceSettings { Provider = AiProviderKind.Anthropic } };

    /// <summary>Claude, with the fixture's project allowed to be sent.</summary>
    private static AppSettings Allowed(ExplorerFixture f) => WithClaude with { AiAllowedProjects = [Path.GetFullPath(f.Path)] };

    /// <summary>The main view model of a project whose first class diagram is open, with an AI service the project may use.</summary>
    private static (MainWindowViewModel Main, DiagramViewModel Diagram) Open(ExplorerFixture f, IAiProvider? provider, AppSettings? settings = null)
    {
        var main = new MainWindowViewModel(f.Services, f.Services, settings: new InMemorySettingsStore(settings ?? Allowed(f)),
            aiProviders: (_, _) => provider);
        main.OpenProjectFileAsync(f.Path).GetAwaiter().GetResult();
        main.OpenDiagramCommand.Execute(main.AllNodes().OfType<DiagramItemViewModel>().First(d => d.Document.DiagramType == DiagramType.Class));
        return (main, main.ActiveDiagram!);
    }

    [AvaloniaFact]
    public async Task AskingForAChange_ShowsTheProposedChanges_AndApplyMakesThemOneUndoStep()
    {
        using var f = new ExplorerFixture();
        var provider = FakeChatProvider.Scripted(
            new AiTurn(null, [FakeChatProvider.Call("add_class", """{"name": "Payment"}"""), FakeChatProvider.Call("add_attribute", """{"class": "Payment", "attribute": "-amount: decimal"}""")]),
            new AiTurn("I added a Payment class with an amount.", []));
        var (_, diagram) = Open(f, provider);
        var chat = diagram.Chat!;
        var before = DiagramSerializer.Serialize(diagram.Document);
        Assert.True(chat.IsSetUp);
        Assert.Contains("can change the diagram", chat.Note);

        chat.Input = "Add a payment";
        await chat.SendCommand.ExecuteAsync(null);

        Assert.Equal([ChatRole.User, ChatRole.Assistant], chat.Entries.Select(e => e.Role));
        Assert.Equal("I added a Payment class with an amount.", chat.Entries[1].Text);
        Assert.Equal(string.Empty, chat.Input);
        Assert.True(chat.HasChanges);
        Assert.Equal(["+ class Payment", "    + -amount: decimal"], chat.Changes);
        Assert.Equal(before, DiagramSerializer.Serialize(diagram.Document));

        chat.ApplyCommand.Execute(null);
        Assert.False(chat.HasChanges);
        Assert.Contains(((ClassDiagramModel)diagram.Document.Model).Classes, c => c.Name == "Payment");
        Assert.True(diagram.IsDirty);

        diagram.UndoCommand.Execute(null);
        Assert.Equal(before, DiagramSerializer.Serialize(diagram.Document));
    }

    [AvaloniaFact]
    public async Task Discard_LeavesTheDiagramAsItWas()
    {
        using var f = new ExplorerFixture();
        var (_, diagram) = Open(f, FakeChatProvider.Scripted(new AiTurn(null, [FakeChatProvider.Call("add_class", """{"name": "Payment"}""")]), new AiTurn("Added.", [])));
        var chat = diagram.Chat!;
        chat.Input = "Add a payment";
        await chat.SendCommand.ExecuteAsync(null);

        chat.DiscardCommand.Execute(null);

        Assert.False(chat.HasChanges);
        Assert.Empty(chat.Changes);
        Assert.DoesNotContain(((ClassDiagramModel)diagram.Document.Model).Classes, c => c.Name == "Payment");
        Assert.False(diagram.CanUndo);
    }

    [AvaloniaFact]
    public async Task WithoutAService_ThePanelOffersToSetOneUp()
    {
        using var f = new ExplorerFixture();
        var (_, diagram) = Open(f, null, new AppSettings());
        var chat = diagram.Chat!;

        Assert.False(chat.IsSetUp);
        Assert.StartsWith("No AI service is set up yet", chat.Note);
        await chat.SetUpCommand.ExecuteAsync(null);
        Assert.Single(f.Services.ShownPreferences);
    }

    [AvaloniaFact]
    public async Task AServiceError_IsShownInTheConversation()
    {
        using var f = new ExplorerFixture();
        var (_, diagram) = Open(f, new FakeChatProvider((_, _) => throw new AiException("Claude refused the API key: invalid x-api-key")));
        var chat = diagram.Chat!;
        chat.Input = "Hello";

        await chat.SendCommand.ExecuteAsync(null);

        Assert.Equal(ChatRole.Error, chat.Entries[^1].Role);
        Assert.Equal("Claude refused the API key: invalid x-api-key", chat.Entries[^1].Text);
        Assert.False(chat.IsWorking);
    }

    [AvaloniaFact]
    public async Task Stop_EndsTheRequest_KeepingNothingOfIt()
    {
        using var f = new ExplorerFixture();
        var started = new TaskCompletionSource();
        var (_, diagram) = Open(f, new FakeChatProvider(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new AiTurn("Never.", []);
        }));
        var chat = diagram.Chat!;
        chat.Input = "Add a payment";

        var sending = chat.SendCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(chat.IsWorking);
        Assert.False(chat.SendCommand.CanExecute(null));
        chat.StopCommand.Execute(null);
        await sending;

        Assert.False(chat.IsWorking);
        Assert.Equal(ChatRole.Error, chat.Entries[^1].Role);
        Assert.False(chat.HasChanges);
    }

    [AvaloniaFact]
    public async Task OtherDiagramTypes_CanBeChangedToo()
    {
        using var f = new ExplorerFixture();
        var provider = FakeChatProvider.Scripted(
            new AiTurn(null, [FakeChatProvider.Call("add_node", """{"kind": "state", "name": "Paid"}"""), FakeChatProvider.Call("add_edge", """{"source": "Paid", "target": "Paid", "label": "retry"}""")]),
            new AiTurn("I added the state Paid.", []));
        var main = new MainWindowViewModel(f.Services, f.Services, settings: new InMemorySettingsStore(Allowed(f)), aiProviders: (_, _) => provider);
        main.OpenProjectFileAsync(f.Path).GetAwaiter().GetResult();
        main.NewDiagramCommand.Execute(DiagramType.StateMachine);
        Dispatcher.UIThread.RunJobs();
        var chat = main.ActiveDiagram!.Chat!;
        Assert.Contains("can change the diagram", chat.Note);

        chat.Input = "Add a Paid state";
        await chat.SendCommand.ExecuteAsync(null);
        Assert.Equal(["+ state Paid", "+ Paid → Paid (transition) : retry"], chat.Changes);

        chat.ApplyCommand.Execute(null);
        Assert.Contains(((GraphDiagramModel)main.ActiveDiagram.Document.Model).Nodes, n => n.Name == "Paid");
    }

    [AvaloniaFact]
    public async Task TheFirstMessageOfAProject_AsksWhetherItsDiagramsMayBeSent_Once()
    {
        using var f = new ExplorerFixture();
        var provider = FakeChatProvider.Scripted(new AiTurn("Hello.", []), new AiTurn("Again.", []));
        var store = new InMemorySettingsStore(WithClaude);
        var main = new MainWindowViewModel(f.Services, f.Services, settings: store, aiProviders: (_, _) => provider);
        await main.OpenProjectFileAsync(f.Path);
        main.OpenDiagramCommand.Execute(main.AllNodes().OfType<DiagramItemViewModel>().First(d => d.Document.DiagramType == DiagramType.Class));
        var chat = main.ActiveDiagram!.Chat!;
        chat.Input = "Hello";

        await chat.SendCommand.ExecuteAsync(null);
        Assert.True(chat.NeedsConsent);
        Assert.Empty(chat.Entries);
        Assert.Equal("Hello", chat.Input);
        Assert.Contains("\"Shop\"", chat.ConsentText);
        Assert.Contains("Fake AI", chat.ConsentText);
        Assert.False(chat.SendCommand.CanExecute(null));

        // Cancel keeps the message; Allow sends it, and is remembered for the project.
        chat.CancelSendingCommand.Execute(null);
        Assert.False(chat.NeedsConsent);
        Assert.Equal("Hello", chat.Input);
        await chat.SendCommand.ExecuteAsync(null);
        await chat.AllowSendingCommand.ExecuteAsync(null);
        Assert.False(chat.NeedsConsent);
        Assert.Equal("Hello.", chat.Entries[^1].Text);

        chat.Input = "Again";
        await chat.SendCommand.ExecuteAsync(null);
        Assert.Equal("Again.", chat.Entries[^1].Text);
        Assert.False(chat.NeedsConsent);
        Assert.Contains(Path.GetFullPath(f.Path), store.Load().AiAllowedProjects);
    }

    [AvaloniaFact]
    public async Task TheAnswer_IsShownWhileItComes_AndReadAsMarkdown()
    {
        using var f = new ExplorerFixture();
        var seen = new List<string>();
        AiChatViewModel? chat = null;
        var provider = new StreamingChatProvider(["It has **two", "It has **two classes**:\n\n- `Order`\n- Customer"],
            () => seen.Add(chat!.Entries.LastOrDefault(e => e.IsAssistant)?.Text ?? string.Empty));
        var (main, diagram) = Open(f, provider);
        chat = diagram.Chat!;
        var window = new MainWindow { DataContext = main, Width = 1300, Height = 800 };
        window.Show();
        main.ToggleAiChatCommand.Execute(null);
        chat.Input = "What is in it?";

        await chat.SendCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        // The bubble showed the first part before the rest came.
        Assert.Equal("It has **two", seen[0]);
        var bubble = window.GetVisualDescendants().OfType<Border>().Last(b => b.Classes.Contains("assistant"));
        var view = bubble.GetVisualDescendants().OfType<MarkdownView>().Single();
        Assert.True(view.IsVisible);
        var blocks = view.Children.ToList();
        Assert.Equal(3, blocks.Count);
        Assert.Contains(((SelectableTextBlock)blocks[0]).Inlines!.OfType<Run>(), r => r.Text == "two classes" && r.FontWeight == FontWeight.SemiBold);
        var code = ((Grid)blocks[1]).Children.OfType<SelectableTextBlock>().Single().Inlines!.OfType<Run>().Single(r => r.Text == "Order");
        Assert.Contains("monospace", code.FontFamily.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheChat_ReadsInASmallerFont_WithTheCornersOfTheTextBoxes()
    {
        using var f = new ExplorerFixture();
        var (main, diagram) = Open(f, FakeChatProvider.Scripted(
            new AiTurn(null, [FakeChatProvider.Call("add_class", """{"name": "Payment"}""")]), new AiTurn("Added Payment.", [])));
        var window = new MainWindow { DataContext = main, Width = 1300, Height = 800 };
        window.Show();
        main.ToggleAiChatCommand.Execute(null);
        diagram.Chat!.Input = "Add a payment";
        await diagram.Chat.SendCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var panel = window.GetLogicalDescendants().OfType<AiChatPanel>().Single();
        var bubbles = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("bubble")).ToList();
        Assert.Equal(2, bubbles.Count);
        // The corners of every text box, e.g. in Diagram Properties.
        var corners = (CornerRadius)window.FindResource("ControlCornerRadius")!;
        Assert.All(bubbles, b => Assert.Equal(corners, b.CornerRadius));
        Assert.All(panel.GetVisualDescendants().OfType<SelectableTextBlock>(), t => Assert.Equal(12, t.FontSize));

        var input = panel.FindControl<TextBox>("InputBox")!;
        Assert.Equal((12d, corners), (input.FontSize, input.CornerRadius));
        Assert.Equal(corners, panel.FindControl<Border>("ChangesCard")!.CornerRadius);
        Assert.All(new[] { "SendButton", "ApplyButton", "DiscardButton" }, n => Assert.Equal(corners, panel.FindControl<Button>(n)!.CornerRadius));

        // Smaller than the text of the rest of the window.
        Assert.True(12 < window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Shop").FontSize);
        window.Close();
    }

    [AvaloniaFact]
    public void TheChatPanel_IsHiddenAtStartUp_EvenWhenItWasOpenBefore()
    {
        var services = new FakeServices();
        var store = new InMemorySettingsStore(WithClaude);
        var first = new MainWindowViewModel(services, services, settings: store);
        Assert.False(first.IsAiChatVisible);
        first.ToggleAiChatCommand.Execute(null);
        Assert.True(first.IsAiChatVisible);

        // The next run starts without it.
        Assert.False(new MainWindowViewModel(services, services, settings: store).IsAiChatVisible);
    }

    [AvaloniaFact]
    public async Task ViewAiChat_ShowsThePanelOnTheRight_ForTheActiveDiagram_AndCtrlEnterSends()
    {
        using var f = new ExplorerFixture();
        var store = new InMemorySettingsStore(Allowed(f));
        var provider = FakeChatProvider.Scripted(new AiTurn("It has two classes.", []));
        var main = new MainWindowViewModel(f.Services, f.Services, settings: store, aiProviders: (_, _) => provider);
        await main.OpenProjectFileAsync(f.Path);
        var window = new MainWindow { DataContext = main, Width = 1300, Height = 800 };
        window.Show();
        main.OpenDiagramCommand.Execute(main.AllNodes().OfType<DiagramItemViewModel>().First(d => d.Document.DiagramType == DiagramType.Class));
        Dispatcher.UIThread.RunJobs();

        var item = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "_AI Chat");
        Assert.Same(Shortcuts.ToggleAiChat, item.InputGesture);
        var host = window.FindControl<Border>("AiChatHost")!;
        Assert.False(host.IsVisible);

        main.ToggleAiChatCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(host.IsEffectivelyVisible);
        Assert.True(host.Bounds.Width > 300);
        var panel = window.GetLogicalDescendants().OfType<AiChatPanel>().Single();
        Assert.Same(main.ActiveDiagram!.Chat, panel.DataContext);
        Assert.True(host.TranslatePoint(default, window)!.Value.X > window.FindControl<Control>("DocumentTabs")!.TranslatePoint(default, window)!.Value.X);

        // Ctrl+Enter (⌘+Enter on macOS) in the input box sends.
        var input = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "InputBox");
        input.Focus();
        main.ActiveDiagram.Chat!.Input = "What is this?";
        window.KeyPress(Key.Enter, Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control, PhysicalKey.Enter, null);
        for (var i = 0; i < 20 && main.ActiveDiagram.Chat.Entries.Count < 2; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("It has two classes.", main.ActiveDiagram.Chat.Entries[^1].Text);

        main.ToggleAiChatCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(host.IsVisible);
        Assert.Equal(0, window.FindControl<Grid>("Workspace")!.ColumnDefinitions[2].Width.Value);
        window.Close();
    }
}
