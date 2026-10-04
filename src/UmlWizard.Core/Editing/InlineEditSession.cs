using UmlWizard.Core.Rendering;

namespace UmlWizard.Core.Editing;

/// <summary>
/// An in-progress in-line text edit of one element part. Created by <see cref="DiagramEditor.BeginInlineEdit"/>;
/// must be finished with <see cref="Commit"/> or <see cref="Cancel"/>.
/// </summary>
public sealed class InlineEditSession
{
    private readonly DiagramEditor _editor;

    internal InlineEditSession(
        DiagramEditor editor, string elementId, HitPart part, int index, string initialText, string snapshot, bool isNew)
    {
        _editor = editor;
        ElementId = elementId;
        Part = part;
        Index = index;
        InitialText = initialText;
        Snapshot = snapshot;
        IsNew = isNew;
    }

    public string ElementId { get; }

    /// <summary>The edited part; for a new member this is the row part, not the compartment.</summary>
    public HitPart Part { get; }

    public int Index { get; }

    public string InitialText { get; }

    /// <summary>True when the session edits a freshly added class member.</summary>
    public bool IsNew { get; }

    public bool IsCompleted { get; private set; }

    internal string Snapshot { get; }

    public void Commit(string text)
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;
        _editor.CompleteInlineEdit(this, text);
    }

    public void Cancel()
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;
        _editor.CancelInlineEdit(this);
    }
}
