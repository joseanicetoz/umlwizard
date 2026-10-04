namespace UmlWizard.Core.Rendering;

/// <param name="Index">Row index for <see cref="HitPart.ClassAttribute"/> and <see cref="HitPart.ClassOperation"/>.</param>
public sealed record HitInfo(string ElementId, HitPart Part, int Index = -1)
{
    /// <summary>True when the hit is on a node (class or lifeline) that connectors can attach to.</summary>
    public bool IsNode => Part is not (HitPart.Relationship or HitPart.Message or HitPart.ResizeHandle or HitPart.Text or HitPart.NoteAttachment or HitPart.Boundary or HitPart.Edge);
}
