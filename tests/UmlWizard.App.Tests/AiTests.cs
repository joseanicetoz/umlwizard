using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.Ai;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

/// <summary>An AI service that answers from a script; no test talks to a real one.</summary>
internal sealed class FakeAiProvider(Func<string, string, CancellationToken, Task<string>> answer) : IAiProvider
{
    public List<string> Messages { get; } = [];

    public string DisplayName => "Fake AI";

    public static FakeAiProvider Answering(string reply) => new((_, _, _) => Task.FromResult(reply));

    public Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return answer(instructions, message, cancellationToken);
    }
}

public sealed class AiTests
{
    private const string Lifecycle = """
        @startuml
        title Order Lifecycle
        [*] --> Created
        Created --> Paid : pay
        Paid --> [*]
        @enduml
        """;

    private static readonly AiServiceSettings Claude = new() { Provider = AiProviderKind.Anthropic };

    [AvaloniaFact]
    public async Task ThePreferencesAiTab_ChoosesTheService_AndKeepsTheKeyInTheCredentialStore()
    {
        var store = new InMemorySettingsStore();
        var secrets = new InMemorySecretStore();
        var provider = FakeAiProvider.Answering("OK");
        (AiServiceSettings Settings, string? Key)? asked = null;
        var preferences = new PreferencesViewModel(store.Load(), new FakeServices(), store.Save, secrets: secrets,
            aiProviders: (settings, key) => { asked = (settings, key); return provider; });

        Assert.False(preferences.IsAiEnabled);
        Assert.False(preferences.TestConnectionCommand.CanExecute(null));

        preferences.AiWarningAccepted = true;
        Assert.True(store.Load().AiWarningAccepted);
        preferences.SelectedAiProvider = preferences.AiProviderOptions.Single(o => o.Kind == AiProviderKind.Anthropic);
        Assert.Equal(AiProviderKind.Anthropic, store.Load().Ai.Provider);
        Assert.Equal("claude-sonnet-5-5", preferences.AiModelWatermark);
        Assert.Equal("https://api.anthropic.com", preferences.AiEndpointWatermark);

        preferences.AiModel = "claude-opus-5-5";
        Assert.Equal("claude-opus-5-5", store.Load().Ai.Model);

        // The key is kept in the credential store when the window closes, never in the settings.
        preferences.ApiKey = "sk-ant-secret";
        Assert.Null(secrets.Get("ai.Anthropic.apiKey"));
        preferences.SaveApiKey();
        Assert.Equal("sk-ant-secret", secrets.Get("ai.Anthropic.apiKey"));
        Assert.DoesNotContain("sk-ant-secret", System.Text.Json.JsonSerializer.Serialize(store.Load()));

        // Test Connection asks the service, with the settings and key, and says it answered.
        await preferences.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal(("claude-opus-5-5", "sk-ant-secret"), (asked!.Value.Settings.EffectiveModel, asked.Value.Key));
        Assert.Equal("Fake AI answered: the connection works.", preferences.TestResult);
        Assert.False(preferences.TestFailed);

        // Each service has its own key.
        preferences.SelectedAiProvider = preferences.AiProviderOptions.Single(o => o.Kind == AiProviderKind.OpenAiCompatible);
        Assert.Equal(string.Empty, preferences.ApiKey);
        preferences.SelectedAiProvider = preferences.AiProviderOptions.Single(o => o.Kind == AiProviderKind.Anthropic);
        Assert.Equal("sk-ant-secret", preferences.ApiKey);

        // Emptying the box forgets the key.
        preferences.ApiKey = "";
        preferences.SaveApiKey();
        Assert.Null(secrets.Get("ai.Anthropic.apiKey"));
    }

    [AvaloniaFact]
    public async Task AFailedConnectionTest_ShowsTheServicesMessage()
    {
        var preferences = new PreferencesViewModel(new AppSettings { Ai = Claude, AiWarningAccepted = true }, new FakeServices(), _ => { },
            aiProviders: (_, _) => new FakeAiProvider((_, _, _) => throw new AiException("Claude refused the API key: invalid x-api-key")));

        await preferences.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(preferences.TestFailed);
        Assert.Equal("Claude refused the API key: invalid x-api-key", preferences.TestResult);
    }

    [AvaloniaFact]
    public void TheAiTab_ShowsTheServicesSettings_OnlyWhenThereIsOne()
    {
        var preferences = new PreferencesViewModel(new AppSettings(), new FakeServices(), _ => { });
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();
        var tabs = window.FindControl<TabControl>("PreferencesTabs")!;
        Assert.Equal(["General", "Appearance", "Diagram", "AI"], tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!));
        tabs.SelectedItem = window.FindControl<TabItem>("AiTab");
        Dispatcher.UIThread.RunJobs();

        var key = window.FindControl<TextBox>("ApiKeyBox")!;
        Assert.False(key.IsEffectivelyVisible);
        preferences.SelectedAiProvider = preferences.AiProviderOptions[1];
        Dispatcher.UIThread.RunJobs();
        Assert.True(key.IsEffectivelyVisible);
        Assert.Equal('•', key.PasswordChar);
        Assert.True(window.FindControl<Button>("TestConnectionButton")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void TheApiKey_CanBeShown_AndIsHiddenAgainForAnotherService()
    {
        var preferences = new PreferencesViewModel(new AppSettings { Ai = Claude, AiWarningAccepted = true }, new FakeServices(), _ => { });
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();
        window.FindControl<TabControl>("PreferencesTabs")!.SelectedItem = window.FindControl<TabItem>("AiTab");
        Dispatcher.UIThread.RunJobs();
        var key = window.FindControl<TextBox>("ApiKeyBox")!;
        var reveal = window.FindControl<Button>("RevealApiKeyButton")!;

        Assert.False(key.RevealPassword);
        Assert.Equal("Show the API key", ToolTip.GetTip(reveal));

        reveal.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(key.RevealPassword);
        Assert.Equal("Hide the API key", ToolTip.GetTip(reveal));

        reveal.Command.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(key.RevealPassword);

        reveal.Command.Execute(null);
        preferences.SelectedAiProvider = preferences.AiProviderOptions.Single(o => o.Kind == AiProviderKind.OpenAiCompatible);
        Dispatcher.UIThread.RunJobs();
        Assert.False(key.RevealPassword);
        window.Close();
    }

    [AvaloniaFact]
    public void TheAiTab_CannotBeSetUp_UntilTheWarningIsAccepted()
    {
        var store = new InMemorySettingsStore(new AppSettings { Ai = Claude });
        var preferences = new PreferencesViewModel(store.Load(), new FakeServices(), store.Save);
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();
        window.FindControl<TabControl>("PreferencesTabs")!.SelectedItem = window.FindControl<TabItem>("AiTab");
        Dispatcher.UIThread.RunJobs();

        var accept = window.FindControl<CheckBox>("AiAcceptBox")!;
        Assert.True(window.FindControl<Border>("AiWarning")!.IsEffectivelyVisible);
        Assert.Equal("I accept", accept.Content);
        Assert.False(accept.IsChecked);
        Assert.False(window.FindControl<ComboBox>("AiProviderBox")!.IsEffectivelyEnabled);
        Assert.False(window.FindControl<TextBox>("ApiKeyBox")!.IsEffectivelyEnabled);
        Assert.False(preferences.TestConnectionCommand.CanExecute(null));

        accept.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(store.Load().AiWarningAccepted);
        Assert.True(window.FindControl<ComboBox>("AiProviderBox")!.IsEffectivelyEnabled);
        Assert.True(window.FindControl<TextBox>("ApiKeyBox")!.IsEffectivelyEnabled);
        Assert.True(preferences.TestConnectionCommand.CanExecute(null));
        window.Close();

        // Accepted once is accepted for good.
        Assert.True(new PreferencesViewModel(store.Load(), new FakeServices(), _ => { }).AiWarningAccepted);
    }

    [AvaloniaFact]
    public void UntickingTheWarning_WithAServiceSetUp_AsksFirst_AndCancellingKeepsIt()
    {
        var store = new InMemorySettingsStore(new AppSettings { Ai = Claude with { Model = "claude-opus-5-5" }, AiWarningAccepted = true });
        var secrets = new InMemorySecretStore();
        secrets.Set("ai.Anthropic.apiKey", "sk-ant-secret");
        var dialogs = new FakeServices { ContinueAnswer = false };
        var preferences = new PreferencesViewModel(store.Load(), dialogs, store.Save, secrets: secrets);

        preferences.AiWarningAccepted = false;

        Assert.Equal(
            "The AI service cannot be used without accepting the warning.\n\n"
            + "If you continue, the AI configuration (Anthropic (Claude)) and its API key are removed, and the AI service is set back to None.",
            Assert.Single(dialogs.ConfirmMessages).ReplaceLineEndings("\n"));
        Assert.True(preferences.AiWarningAccepted);
        Assert.True(store.Load().AiWarningAccepted);
        Assert.Equal(AiProviderKind.Anthropic, store.Load().Ai.Provider);
        Assert.Equal("sk-ant-secret", secrets.Get("ai.Anthropic.apiKey"));
    }

    [AvaloniaFact]
    public void UntickingTheWarning_AndContinuing_SetsTheServiceBackToNone_AndForgetsItsKey()
    {
        var store = new InMemorySettingsStore(new AppSettings { Ai = Claude with { Model = "claude-opus-5-5" }, AiWarningAccepted = true });
        var secrets = new InMemorySecretStore();
        secrets.Set("ai.Anthropic.apiKey", "sk-ant-secret");
        secrets.Set("ai.OpenAiCompatible.apiKey", "sk-other");
        var preferences = new PreferencesViewModel(store.Load(), new FakeServices { ContinueAnswer = true }, store.Save, secrets: secrets);

        preferences.AiWarningAccepted = false;

        Assert.False(preferences.AiWarningAccepted);
        Assert.False(preferences.IsAiEnabled);
        Assert.Equal(string.Empty, preferences.AiModel);
        Assert.False(store.Load().AiWarningAccepted);
        Assert.Equal(new AiServiceSettings(), store.Load().Ai);
        Assert.Null(secrets.Get("ai.Anthropic.apiKey"));
        Assert.Null(secrets.Get("ai.OpenAiCompatible.apiKey"));
    }

    [AvaloniaFact]
    public void UntickingTheWarning_WithoutAService_AsksNothing()
    {
        var store = new InMemorySettingsStore(new AppSettings { AiWarningAccepted = true });
        var dialogs = new FakeServices();
        var preferences = new PreferencesViewModel(store.Load(), dialogs, store.Save);

        preferences.AiWarningAccepted = false;

        Assert.Empty(dialogs.ConfirmMessages);
        Assert.False(store.Load().AiWarningAccepted);
    }

    [AvaloniaFact]
    public void ClosingPreferences_WithAServiceButTheWarningNotAccepted_AsksBeforeTurningItOff()
    {
        // E.g. a service set up before the warning had to be accepted.
        var store = new InMemorySettingsStore(new AppSettings { Ai = Claude });
        var dialogs = new FakeServices { ContinueAnswer = false };
        var preferences = new PreferencesViewModel(store.Load(), dialogs, store.Save);
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();

        // Cancelled: the window stays open, the service set up.
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);
        Assert.Single(dialogs.ConfirmMessages);
        Assert.Equal(AiProviderKind.Anthropic, store.Load().Ai.Provider);

        // Continued: the service is turned off and the window closes.
        dialogs.ContinueAnswer = true;
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
        Assert.Equal(AiProviderKind.None, store.Load().Ai.Provider);
    }

    [AvaloniaFact]
    public void TheAiTab_ChoosesHowTheAiChangesDiagrams()
    {
        var store = new InMemorySettingsStore(new AppSettings { Ai = Claude, AiWarningAccepted = true });
        var preferences = new PreferencesViewModel(store.Load(), new FakeServices(), store.Save);
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();
        window.FindControl<TabControl>("PreferencesTabs")!.SelectedItem = window.FindControl<TabItem>("AiTab");
        Dispatcher.UIThread.RunJobs();

        var box = window.FindControl<ComboBox>("AiChangeModeBox")!;
        Assert.True(box.IsEffectivelyVisible);
        Assert.Equal(AiChangeMode.Tools, preferences.SelectedAiChangeMode.Mode);
        Assert.Equal(["With tool calls (recommended)", "As a whole diagram, for models without tool calling"],
            preferences.AiChangeModeOptions.Select(o => o.Name));

        box.SelectedIndex = 1;
        Assert.Equal(AiChangeMode.WholeDiagram, store.Load().Ai.ChangeMode);
        Assert.Equal(AiChangeMode.WholeDiagram, new PreferencesViewModel(store.Load(), new FakeServices(), _ => { }).SelectedAiChangeMode.Mode);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CreateWithAi_WithoutAService_OffersToSetOneUp()
    {
        using var f = new ExplorerFixture();
        f.Services.ConfirmAnswer = ConfirmResult.Yes;
        var main = new MainWindowViewModel(f.Services, f.Services);
        await main.OpenProjectFileAsync(f.Path);

        await main.CreateDiagramWithAiCommand.ExecuteAsync(null);

        Assert.Contains("No AI service is set up yet", Assert.Single(f.Services.ConfirmMessages));
        Assert.Single(f.Services.ShownPreferences);
    }

    [AvaloniaFact]
    public async Task CreateWithAi_AddsTheDiagramTheServiceDescribed_AndOpensIt()
    {
        using var f = new ExplorerFixture();
        var provider = FakeAiProvider.Answering(Lifecycle);
        var main = new MainWindowViewModel(f.Services, f.Services, settings: new InMemorySettingsStore(new AppSettings { Ai = Claude }),
            aiProviders: (_, _) => provider);
        await main.OpenProjectFileAsync(f.Path);
        var before = main.Project!.Diagrams.Count;
        f.Services.FillDialog = d =>
        {
            var dialog = Assert.IsType<AiDiagramViewModel>(d);
            Assert.Contains("Fake AI", dialog.ServiceNote);
            Assert.Null(dialog.SelectedType.Type);
            dialog.Description = "The lifecycle of an order";
            dialog.SelectedType = dialog.Types.Single(t => t.Type == DiagramType.StateMachine);
        };

        await main.CreateDiagramWithAiCommand.ExecuteAsync(null);

        Assert.Equal(before + 1, main.Project.Diagrams.Count);
        var document = main.Project.Diagrams[^1];
        Assert.Equal(("Order Lifecycle", DiagramType.StateMachine), (document.Name, document.DiagramType));
        Assert.Equal("Created with Fake AI from: The lifecycle of an order", document.Description);
        Assert.Equal(document.Id, main.ActiveDiagram!.Id);
        Assert.Contains("UML state machine diagram", Assert.Single(provider.Messages));
        Assert.False(main.IsBusy);
        Assert.Empty(f.Services.Errors);
    }

    [AvaloniaFact]
    public async Task CreateWithAi_ShowsWhatItIsDoing_AndCancelStopsIt()
    {
        using var f = new ExplorerFixture();
        var started = new TaskCompletionSource();
        var provider = new FakeAiProvider(async (_, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Lifecycle;
        });
        var main = new MainWindowViewModel(f.Services, f.Services, settings: new InMemorySettingsStore(new AppSettings { Ai = Claude }),
            aiProviders: (_, _) => provider);
        await main.OpenProjectFileAsync(f.Path);
        var before = main.Project!.Diagrams.Count;
        f.Services.FillDialog = d => ((AiDiagramViewModel)d).Description = "anything";
        var window = new MainWindow { DataContext = main };
        window.Show();

        var running = main.CreateDiagramWithAiCommand.ExecuteAsync(null);
        await started.Task;
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.IsBusy);
        Assert.Equal("Creating the diagram with Fake AI…", main.BusyText);
        Assert.True(window.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "BusyOverlay").IsEffectivelyVisible);

        main.CancelBusyCommand.Execute(null);
        await running;
        Dispatcher.UIThread.RunJobs();
        Assert.False(main.IsBusy);
        Assert.Equal(before, main.Project.Diagrams.Count);
        Assert.Empty(f.Services.Errors);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CreateWithAi_ShowsTheServicesError()
    {
        using var f = new ExplorerFixture();
        var main = new MainWindowViewModel(f.Services, f.Services, settings: new InMemorySettingsStore(new AppSettings { Ai = Claude }),
            aiProviders: (_, _) => new FakeAiProvider((_, _, _) => throw new AiException("Claude is busy or the account's limit is reached: x")));
        await main.OpenProjectFileAsync(f.Path);
        f.Services.FillDialog = d => ((AiDiagramViewModel)d).Description = "anything";

        await main.CreateDiagramWithAiCommand.ExecuteAsync(null);

        Assert.Equal("Claude is busy or the account's limit is reached: x", Assert.Single(f.Services.Errors));
        Assert.False(main.IsBusy);
    }

    [AvaloniaFact]
    public void TheDiagramMenu_OffersCreateWithAi()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var diagram = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "_Diagram");
        var headers = diagram.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Equal(headers.IndexOf("_New Diagram") + 1, headers.IndexOf("_Create with AI…"));
        window.Close();
    }

    [Fact]
    public void ApiKeys_FallBackToTheEnvironment()
    {
        var secrets = new InMemorySecretStore();
        var settings = new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible };
        var previous = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "from-environment");
            Assert.Equal("from-environment", AiKeys.Get(secrets, settings));
            secrets.Set(settings.SecretName, "from-store");
            Assert.Equal("from-store", AiKeys.Get(secrets, settings));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previous);
        }
    }

    [Fact]
    public void TheWindowsCredentialStore_KeepsAndForgetsASecret()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = SystemSecretStore.Create();
        var name = $"test.{Guid.NewGuid():N}";
        try
        {
            Assert.True(store.IsPersistent);
            Assert.Null(store.Get(name));
            Assert.True(store.Set(name, "s3cret ✓"));
            Assert.Equal("s3cret ✓", store.Get(name));
        }
        finally
        {
            store.Set(name, null);
        }

        Assert.Null(store.Get(name));
    }
}
