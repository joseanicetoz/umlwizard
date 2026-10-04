using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using UmlWizard.Ai;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.ViewModels;

/// <summary>
/// The Preferences window: General (projects folder, display language), Appearance (theme, user interface font) and
/// Diagram (the default fills, border and thickness of the shapes of each diagram type). Every change is applied and
/// saved at once, through the callback, so the window has no OK or Cancel.
/// </summary>
public sealed partial class PreferencesViewModel : ObservableObject
{
    /// <summary>The font size of the user interface when none is set (the Fluent theme's).</summary>
    public const double DefaultFontSize = 14;

    public const double MinFontSize = 10;

    public const double MaxFontSize = 20;

    private readonly IDialogService _dialogs;
    private readonly Action<AppSettings> _apply;
    private readonly ISecretStore _secrets;
    private readonly Func<AiServiceSettings, string?, IAiProvider?> _aiProviders;
    private string? _keyName;
    private bool _keyChanged;
    private AppSettings _settings;
    private bool _loading = true;

    /// <param name="settings">The settings as they are now.</param>
    /// <param name="apply">Applies and saves the settings after every change.</param>
    /// <param name="theme">The theme the application is shown in, whose colours are the defaults shown for the diagrams.</param>
    /// <param name="secrets">Where the API key of the AI service is kept.</param>
    /// <param name="aiProviders">Makes the AI service's provider, for Test Connection.</param>
    public PreferencesViewModel(
        AppSettings settings, IDialogService dialogs, Action<AppSettings> apply, RenderTheme? theme = null,
        ISecretStore? secrets = null, Func<AiServiceSettings, string?, IAiProvider?>? aiProviders = null)
    {
        _settings = settings;
        _dialogs = dialogs;
        _apply = apply;
        _secrets = secrets ?? new InMemorySecretStore();
        _aiProviders = aiProviders ?? ((s, key) => AiProviders.Create(s, key));
        SelectedAiProvider = AiProviderOptions.First(o => o.Kind == settings.Ai.Provider);
        AiEndpoint = settings.Ai.Endpoint ?? string.Empty;
        AiModel = settings.Ai.Model ?? string.Empty;
        SelectedAiChangeMode = AiChangeModeOptions.First(o => o.Mode == settings.Ai.ChangeMode);
        AiWarningAccepted = settings.AiWarningAccepted;
        LoadApiKey();
        theme ??= RenderTheme.Light;

        ProjectsFolder = settings.ProjectsFolder ?? string.Empty;
        SelectedDisplayLanguage = DisplayLanguages.FirstOrDefault(l => l.Culture == settings.DisplayLanguage) ?? DisplayLanguages[0];
        SelectedTheme = Themes.First(t => t.Theme == settings.Theme);
        Fonts = [new FontOption(null, Strings.Preferences_DefaultFont), .. SystemFontNames().Select(n => new FontOption(n, n))];
        SelectedFont = Fonts.FirstOrDefault(f => f.Family == settings.UiFontFamily) ?? Fonts[0];
        FontSize = settings.UiFontSize is { } size ? (decimal)size : null;
        DiagramPages = [.. Enum.GetValues<DiagramType>().Select(t => new DiagramStylePage(t, settings.StyleFor(t), theme, OnDiagramStyleChanged, PickColorAsync))];
        SelectedDiagramPage = DiagramPages[0];
        _loading = false;
    }

    public string Title => Strings.Preferences_Title;

    #region General

    /// <summary>The folder new projects are created in by default; empty for the Documents folder.</summary>
    [ObservableProperty]
    public partial string ProjectsFolder { get; set; }

    partial void OnProjectsFolderChanged(string value) =>
        Apply(s => s with { ProjectsFolder = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });

    /// <summary>Shown in the projects folder box while it is empty.</summary>
    public string ProjectsFolderWatermark { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [RelayCommand]
    private async Task BrowseProjectsFolder()
    {
        if (await _dialogs.PickFolderAsync(Strings.Preferences_ProjectsFolder_Pick) is { } folder)
        {
            ProjectsFolder = folder;
        }
    }

    /// <summary>The languages the user interface can be shown in: English only, so far.</summary>
    public IReadOnlyList<DisplayLanguageOption> DisplayLanguages { get; } = [new("en", "English")];

    [ObservableProperty]
    public partial DisplayLanguageOption SelectedDisplayLanguage { get; set; }

    partial void OnSelectedDisplayLanguageChanged(DisplayLanguageOption value) => Apply(s => s with { DisplayLanguage = value.Culture });

    #endregion

    #region Appearance

    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new(AppTheme.System, Strings.Preferences_Theme_System),
        new(AppTheme.Light, Strings.Preferences_Theme_Light),
        new(AppTheme.Dark, Strings.Preferences_Theme_Dark),
    ];

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; }

    partial void OnSelectedThemeChanged(ThemeOption value) => Apply(s => s with { Theme = value.Theme });

    /// <summary>The fonts the user interface can use: the default, then the system's fonts by name.</summary>
    public IReadOnlyList<FontOption> Fonts { get; }

    [ObservableProperty]
    public partial FontOption SelectedFont { get; set; }

    partial void OnSelectedFontChanged(FontOption value)
    {
        OnPropertyChanged(nameof(PreviewFontFamily));
        Apply(s => s with { UiFontFamily = value.Family });
    }

    /// <summary>The font the preview shows: the chosen one, or the application's default.</summary>
    public FontFamily PreviewFontFamily => SelectedFont?.Family is { } family ? new FontFamily(family) : App.DefaultUiFontFamily;

    /// <summary>The font size the preview shows: the chosen one, or the default.</summary>
    public double PreviewFontSize => FontSize is { } size ? Math.Clamp((double)size, MinFontSize, MaxFontSize) : DefaultFontSize;

    /// <summary>The font size of the user interface; <c>null</c> for the default.</summary>
    [ObservableProperty]
    public partial decimal? FontSize { get; set; }

    partial void OnFontSizeChanged(decimal? value)
    {
        OnPropertyChanged(nameof(PreviewFontSize));
        Apply(s => s with { UiFontSize = value is { } size ? Math.Clamp((double)size, MinFontSize, MaxFontSize) : null });
    }

    public decimal MinimumFontSize => (decimal)MinFontSize;

    public decimal MaximumFontSize => (decimal)MaxFontSize;

    /// <summary>Shown in the font size box while it is empty.</summary>
    public string FontSizeWatermark { get; } = Loc.Format(Strings.Preferences_DefaultValue, DefaultFontSize);

    #endregion

    #region Diagram

    /// <summary>A page per diagram type, with the default look of its shapes.</summary>
    public IReadOnlyList<DiagramStylePage> DiagramPages { get; }

    [ObservableProperty]
    public partial DiagramStylePage SelectedDiagramPage { get; set; }

    private void OnDiagramStyleChanged(DiagramStylePage page)
    {
        var styles = new Dictionary<string, DiagramStyle>(_settings.DiagramStyles);
        var style = page.ToStyle();
        if (style.IsEmpty)
        {
            styles.Remove(page.Type.ToString());
        }
        else
        {
            styles[page.Type.ToString()] = style;
        }

        Apply(s => s with { DiagramStyles = styles });
    }

    #endregion

    /// <summary>Opens the colour picker on a colour; the one chosen, or <c>null</c> when cancelled.</summary>
    private Task<string?> PickColorAsync(string title, SKColor color) => ColorPickerViewModel.PickAsync(_dialogs, title, color);

    #region AI

    /// <summary>Whether the user accepted the warning; the AI service cannot be set up until they do.</summary>
    [ObservableProperty]
    public partial bool AiWarningAccepted { get; set; }

    partial void OnAiWarningAcceptedChanged(bool value)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
        if (value || _loading)
        {
            Apply(s => s with { AiWarningAccepted = value });
        }
        else
        {
            _ = WithdrawAiWarningAsync();
        }
    }

    /// <summary>Unticked: the AI service is turned off, after asking when there is one; cancelling ticks the box again.</summary>
    private async Task WithdrawAiWarningAsync()
    {
        if (await ConfirmTurnOffAiAsync())
        {
            Apply(s => s with { AiWarningAccepted = false });
        }
        else
        {
            AiWarningAccepted = true;
        }
    }

    /// <summary>
    /// Whether the window can close: yes once the warning is accepted; otherwise the AI service is turned off, after
    /// asking when there is one.
    /// </summary>
    public async Task<bool> CanCloseAsync() => AiWarningAccepted || await ConfirmTurnOffAiAsync();

    /// <summary>Turns the AI service off, asking first when one is set up; false when the user cancels.</summary>
    private async Task<bool> ConfirmTurnOffAiAsync()
    {
        if (IsAiEnabled && !await _dialogs.ConfirmContinueAsync(
                Strings.Message_AiTurnOff_Title, Loc.Format(Strings.Message_AiTurnOff, SelectedAiProvider.Name)))
        {
            return false;
        }

        ResetAi();
        return true;
    }

    /// <summary>Sets the AI service back to None and forgets its settings and the API keys of every service.</summary>
    private void ResetAi()
    {
        SelectedAiProvider = AiProviderOptions[0];
        AiEndpoint = string.Empty;
        AiModel = string.Empty;
        SelectedAiChangeMode = AiChangeModeOptions[0];
        foreach (var option in AiProviderOptions.Where(o => o.Kind != AiProviderKind.None))
        {
            _secrets.Set(new AiServiceSettings { Provider = option.Kind }.SecretName, null);
        }

        TestResult = null;
    }

    public IReadOnlyList<AiProviderOption> AiProviderOptions { get; } =
    [
        new(AiProviderKind.None, Strings.Preferences_Ai_None),
        new(AiProviderKind.Anthropic, Strings.Preferences_Ai_Anthropic),
        new(AiProviderKind.OpenAiCompatible, Strings.Preferences_Ai_OpenAi),
    ];

    /// <summary>The AI service; None turns the AI commands off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAiEnabled), nameof(AiEndpointWatermark), nameof(AiModelWatermark), nameof(ApiKeyNote))]
    public partial AiProviderOption SelectedAiProvider { get; set; }

    partial void OnSelectedAiProviderChanged(AiProviderOption value)
    {
        // Keep the key typed for the service before, then show the new service's.
        SaveApiKey();
        LoadApiKey();
        IsApiKeyRevealed = false;
        TestResult = null;
        ApplyAi();
    }

    public bool IsAiEnabled => SelectedAiProvider.Kind != AiProviderKind.None;

    /// <summary>The service's address; empty for the default, shown as the watermark.</summary>
    [ObservableProperty]
    public partial string AiEndpoint { get; set; }

    partial void OnAiEndpointChanged(string value) => ApplyAi();

    public string AiEndpointWatermark => AiServiceSettings.DefaultEndpoint(SelectedAiProvider.Kind);

    /// <summary>The model; empty for the default, shown as the watermark.</summary>
    [ObservableProperty]
    public partial string AiModel { get; set; }

    partial void OnAiModelChanged(string value) => ApplyAi();

    public string AiModelWatermark => SelectedAiProvider.Kind == AiProviderKind.Anthropic
        ? AiServiceSettings.AnthropicModel
        : Strings.Preferences_Ai_Model_Example;

    public IReadOnlyList<AiChangeModeOption> AiChangeModeOptions { get; } =
    [
        new(AiChangeMode.Tools, Strings.Preferences_Ai_Changes_Tools),
        new(AiChangeMode.WholeDiagram, Strings.Preferences_Ai_Changes_Whole),
    ];

    /// <summary>How the AI changes diagrams in the chat: with tool calls, or with the whole revised diagram.</summary>
    [ObservableProperty]
    public partial AiChangeModeOption SelectedAiChangeMode { get; set; }

    partial void OnSelectedAiChangeModeChanged(AiChangeModeOption value) => ApplyAi();

    /// <summary>The API key as typed; kept in the credential store when the window closes or the connection is tested.</summary>
    [ObservableProperty]
    public partial string ApiKey { get; set; } = string.Empty;

    partial void OnApiKeyChanged(string value)
    {
        _keyChanged = true;
        TestResult = null;
    }

    /// <summary>Whether the API key is shown as typed rather than as dots.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealApiKeyTip))]
    public partial bool IsApiKeyRevealed { get; set; }

    public string RevealApiKeyTip => IsApiKeyRevealed ? Strings.Preferences_Ai_HideKey : Strings.Preferences_Ai_ShowKey;

    [RelayCommand]
    private void ToggleApiKeyRevealed() => IsApiKeyRevealed = !IsApiKeyRevealed;

    /// <summary>Where the key is kept, and the environment variable read when there is none.</summary>
    public string ApiKeyNote => _secrets.IsPersistent
        ? Loc.Format(Strings.Preferences_Ai_Key_Stored, AiSettings.KeyEnvironmentVariable ?? string.Empty)
        : Loc.Format(Strings.Preferences_Ai_Key_NotStored, AiSettings.KeyEnvironmentVariable ?? string.Empty);

    /// <summary>The service as set in the window.</summary>
    public AiServiceSettings AiSettings => new()
    {
        Provider = SelectedAiProvider.Kind,
        Endpoint = string.IsNullOrWhiteSpace(AiEndpoint) ? null : AiEndpoint.Trim(),
        Model = string.IsNullOrWhiteSpace(AiModel) ? null : AiModel.Trim(),
        ChangeMode = SelectedAiChangeMode?.Mode ?? AiChangeMode.Tools,
    };

    /// <summary>What Test Connection found; <c>null</c> before it is clicked.</summary>
    [ObservableProperty]
    public partial string? TestResult { get; set; }

    [ObservableProperty]
    public partial bool TestFailed { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial bool IsTesting { get; set; }

    private bool CanTestConnection => AiWarningAccepted && IsAiEnabled && !IsTesting;

    /// <summary>Sends a one-line request to the service and says whether it answered.</summary>
    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnection()
    {
        SaveApiKey();
        var settings = AiSettings;
        if (_aiProviders(settings, AiKeys.Get(_secrets, settings)) is not { } provider)
        {
            return;
        }

        IsTesting = true;
        TestResult = Loc.Format(Strings.Preferences_Ai_Testing, provider.DisplayName);
        TestFailed = false;
        try
        {
            await provider.CompleteAsync("You check that a connection works. Reply with the single word OK.", "Are you there?");
            TestResult = Loc.Format(Strings.Preferences_Ai_Test_Ok, provider.DisplayName);
        }
        catch (AiException ex)
        {
            TestResult = ex.Message;
            TestFailed = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>Keeps the API key typed in the credential store (none when the box is empty); called when the window closes.</summary>
    public void SaveApiKey()
    {
        if (_keyChanged && _keyName is not null)
        {
            _secrets.Set(_keyName, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim());
        }

        _keyChanged = false;
    }

    private void LoadApiKey()
    {
        _keyName = IsAiEnabled ? AiSettings.SecretName : null;
        ApiKey = _keyName is null ? string.Empty : _secrets.Get(_keyName) ?? string.Empty;
        _keyChanged = false;
    }

    private void ApplyAi()
    {
        OnPropertyChanged(nameof(ApiKeyNote));
        TestConnectionCommand.NotifyCanExecuteChanged();
        Apply(s => s with { Ai = AiSettings });
    }

    #endregion

    /// <summary>The settings as they are now, with every change made in the window.</summary>
    public AppSettings Settings => _settings;

    private void Apply(Func<AppSettings, AppSettings> change)
    {
        if (_loading)
        {
            return;
        }

        _settings = change(_settings);
        _apply(_settings);
    }

    private static IEnumerable<string> SystemFontNames()
    {
        try
        {
            return FontManager.Current.SystemFonts.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (InvalidOperationException)
        {
            // No font manager, e.g. without a running application.
            return [];
        }
    }
}

/// <summary>An AI service choice, with its name.</summary>
public sealed record AiProviderOption(AiProviderKind Kind, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A way the AI changes diagrams, with its name.</summary>
public sealed record AiChangeModeOption(AiChangeMode Mode, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A language the user interface can be shown in.</summary>
public sealed record DisplayLanguageOption(string Culture, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A theme choice, with its name.</summary>
public sealed record ThemeOption(AppTheme Theme, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A font the user interface can use; <c>null</c> <see cref="Family"/> is the default.</summary>
public sealed record FontOption(string? Family, string Name)
{
    public override string ToString() => Name;
}

/// <summary>The default look of the shapes of one diagram type: a fill per kind of element, the border and its thickness.</summary>
public sealed partial class DiagramStylePage : ObservableObject
{
    private readonly Action<DiagramStylePage> _changed;
    private bool _loading = true;

    public DiagramStylePage(
        DiagramType type, DiagramStyle? style, RenderTheme theme, Action<DiagramStylePage> changed,
        Func<string, SKColor, Task<string?>>? pick = null)
    {
        Type = type;
        _changed = changed;
        Name = Strings.ResourceManager.GetString($"DiagramType_{type}", Strings.Culture) ?? type.ToString();
        Fills = [.. FillSlot.For(type).Select(slot => new ColorSetting(
            slot.Key, LabelOf(slot.Key), slot.Default(theme), style?.Fills.GetValueOrDefault(slot.Key), _ => Changed(), pick))];
        Border = new ColorSetting("Border", Strings.Preferences_Border, theme.Border, style?.Border, _ => Changed(), pick);
        BorderWidth = style?.BorderWidth is { } width ? (decimal)width : null;
        _loading = false;
    }

    public DiagramType Type { get; }

    /// <summary>The diagram type's name, e.g. "Class diagram".</summary>
    public string Name { get; }

    public IReadOnlyList<ColorSetting> Fills { get; }

    public ColorSetting Border { get; }

    /// <summary>The border thickness of every shape; <c>null</c> for the default.</summary>
    [ObservableProperty]
    public partial decimal? BorderWidth { get; set; }

    partial void OnBorderWidthChanged(decimal? value) => Changed();

    public decimal MinimumBorderWidth => (decimal)ShapeStyle.MinBorderWidth;

    public decimal MaximumBorderWidth => (decimal)ShapeStyle.MaxBorderWidth;

    /// <summary>Shown in the thickness box while it is empty.</summary>
    public string BorderWidthWatermark { get; } = Loc.Format(Strings.Preferences_DefaultValue, ShapeStyle.DefaultBorderWidth);

    /// <summary>Puts every fill, the border and its thickness back to the theme's.</summary>
    [RelayCommand]
    private void Reset()
    {
        _loading = true;
        foreach (var fill in Fills)
        {
            fill.UseDefault();
        }

        Border.UseDefault();
        BorderWidth = null;
        _loading = false;
        Changed();
    }

    public DiagramStyle ToStyle() => new()
    {
        Fills = Fills.Where(f => f.Hex is not null).ToDictionary(f => f.Key, f => f.Hex!),
        Border = Border.Hex,
        BorderWidth = BorderWidth is { } width ? Math.Clamp((double)width, ShapeStyle.MinBorderWidth, ShapeStyle.MaxBorderWidth) : null,
    };

    private void Changed()
    {
        if (!_loading)
        {
            _changed(this);
        }
    }

    private static string LabelOf(string key) =>
        Strings.ResourceManager.GetString($"Preferences_Fill_{key}", Strings.Culture) ?? key;
}

/// <summary>
/// A colour that can be left to its default or set: chosen in the colour picker or typed as <c>#RRGGBB</c>.
/// </summary>
public sealed partial class ColorSetting : ObservableObject
{
    private readonly SKColor _default;
    private readonly Action<ColorSetting> _changed;
    private readonly Func<string, SKColor, Task<string?>>? _pick;

    public ColorSetting(
        string key, string label, SKColor defaultColor, string? hex, Action<ColorSetting> changed, Func<string, SKColor, Task<string?>>? pick = null)
    {
        _pick = pick;
        Key = key;
        Label = label;
        _default = defaultColor;
        _changed = changed;
        Hex = ShapeColors.Parse(hex) is { } color ? ShapeColors.ToHex(color) : null;
        Text = Hex ?? string.Empty;
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>The colour that is set, as <c>#RRGGBB</c>; <c>null</c> for the default.</summary>
    public string? Hex { get; private set; }

    public bool IsDefault => Hex is null;

    /// <summary>The colour the shapes get: the one set, else the default.</summary>
    public IBrush Brush => new SolidColorBrush(ToAvalonia(ShapeColors.Parse(Hex) ?? _default));

    /// <summary>The default, as <c>#RRGGBB</c>; shown in the box while no colour is set.</summary>
    public string DefaultHex => ShapeColors.ToHex(_default);

    /// <summary>What is typed in the box: a colour sets it, an empty box goes back to the default, anything else is ignored.</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    partial void OnTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Set(null);
        }
        else if (ShapeColors.Parse(value) is { } color)
        {
            Set(ShapeColors.ToHex(color));
        }
    }

    /// <summary>Opens the colour picker on the colour (the default while none is set).</summary>
    [RelayCommand]
    private async Task Edit()
    {
        if (_pick is not null && await _pick(Label, ShapeColors.Parse(Hex) ?? _default) is { } hex)
        {
            Text = hex;
        }
    }

    [RelayCommand]
    public void UseDefault() => Text = string.Empty;

    private void Set(string? hex)
    {
        if (hex == Hex)
        {
            return;
        }

        Hex = hex;
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(Brush));
        _changed(this);
    }

    private static Color ToAvalonia(SKColor color) => new(color.Alpha, color.Red, color.Green, color.Blue);
}
