using Avalonia;
using Material.Icons;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>A destination of <see cref="MoveToViewModel"/>: a folder, or the top level when <see cref="FolderId"/> is <c>null</c>.</summary>
public sealed record MoveTarget(string? FolderId, string Label, int Depth)
{
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    public MaterialIconKind Icon => FolderId is null ? MaterialIconKind.HomeOutline : MaterialIconKind.FolderOutline;
}
