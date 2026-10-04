using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>
/// Diagram → Create with AI: what the diagram should show, and its type (or the AI's choice). Says which service the
/// description is sent to.
/// </summary>
public sealed partial class AiDiagramViewModel : DialogViewModel
{
    /// <param name="serviceName">The AI service and model, e.g. "Claude (claude-sonnet-5-5)".</param>
    public AiDiagramViewModel(string serviceName)
    {
        ServiceNote = Loc.Format(Strings.Dialog_AiDiagram_Service, serviceName);
        Types =
        [
            new(null, Strings.Dialog_AiDiagram_AnyType),
            .. Enum.GetValues<DiagramType>().Select(t => new AiDiagramTypeOption(t, Strings.ResourceManager.GetString($"DiagramType_{t}", Strings.Culture) ?? t.ToString())),
        ];
        SelectedType = Types[0];
    }

    public override string Title => Strings.Dialog_AiDiagram_Title;

    /// <summary>What the diagram should show, in the user's words.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>"Let the AI choose", then every diagram type.</summary>
    public IReadOnlyList<AiDiagramTypeOption> Types { get; }

    [ObservableProperty]
    public partial AiDiagramTypeOption SelectedType { get; set; }

    /// <summary>Says which service the description goes to.</summary>
    public string ServiceNote { get; }

    protected override string? Validate() =>
        string.IsNullOrWhiteSpace(Description) ? Strings.Dialog_AiDiagram_NeedsDescription : null;
}

/// <summary>A diagram type to ask for; <c>null</c> lets the AI choose.</summary>
public sealed record AiDiagramTypeOption(DiagramType? Type, string Name)
{
    public override string ToString() => Name;
}
