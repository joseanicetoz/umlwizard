using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels;

/// <summary>A diagram entry of the project explorer.</summary>
public sealed class DiagramItemViewModel(DiagramDocument document) : ProjectNodeViewModel
{
    /// <summary>The diagram as saved in the project (open diagrams edit a copy of it).</summary>
    public DiagramDocument Document { get; private set; } = document;

    public override string Id => Document.Id;

    public override string Name => Document.Name;

    public string TypeName => DiagramTypeInfo.For(Document.DiagramType).DisplayName;

    public override MaterialIconKind Icon => IconFor(Document.DiagramType);

    public static MaterialIconKind IconFor(DiagramType type) => DiagramTypeInfo.For(type).Icon;

    public void Update(DiagramDocument document)
    {
        Document = document;
        OnPropertyChanged(nameof(Name));
    }
}
