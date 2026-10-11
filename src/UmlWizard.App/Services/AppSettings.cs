using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.Services;

/// <summary>Preferences of the application itself (not of a project), kept between runs.</summary>
public sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Whether the Project Explorer panel is shown (View → Project Explorer).</summary>
    public bool ShowProjectExplorer { get; init; } = true;

    /// <summary>The folder new projects are created in by default; the Documents folder when not set.</summary>
    public string? ProjectsFolder { get; init; }

    /// <summary>The language of the user interface, as a culture name of <see cref="Loc.DisplayLanguages"/>; applied at start-up.</summary>
    public string DisplayLanguage { get; init; } = "en";

    /// <summary>The font of the user interface (menus, panels, dialogs); the application's default when not set.</summary>
    public string? UiFontFamily { get; init; }

    /// <summary>The font size of the user interface; the application's default when not set.</summary>
    public double? UiFontSize { get; init; }

    /// <summary>The AI service (Preferences → AI); its API key is in the credential store, not here.</summary>
    public Ai.AiServiceSettings Ai { get; init; } = new();

    /// <summary>Whether the user accepted the warning on Preferences → AI; the AI service cannot be set up until they do.</summary>
    public bool AiWarningAccepted { get; init; }

    /// <summary>
    /// The projects, by full path, whose diagrams the user allowed the AI chat to send to the AI service; asked once per
    /// project, before its first message.
    /// </summary>
    public List<string> AiAllowedProjects { get; init; } = [];

    /// <summary>The user's colours of the colour picker, as <c>#RRGGBB</c>, in the order they were added.</summary>
    public List<string> CustomColors { get; init; } = [];

    /// <summary>The preferred look of the shapes of each diagram type, by <see cref="DiagramType"/> name.</summary>
    public Dictionary<string, DiagramStyle> DiagramStyles { get; init; } = [];

    /// <summary>The preferred look of a diagram type's shapes, or <c>null</c> when it keeps the theme's.</summary>
    public DiagramStyle? StyleFor(DiagramType type) =>
        DiagramStyles.TryGetValue(type.ToString(), out var style) && !style.IsEmpty ? style : null;

    /// <summary>The folder new projects go in: <see cref="ProjectsFolder"/> while it exists, else the Documents folder.</summary>
    public string ProjectsFolderOrDefault =>
        !string.IsNullOrWhiteSpace(ProjectsFolder) && Directory.Exists(ProjectsFolder)
            ? ProjectsFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
