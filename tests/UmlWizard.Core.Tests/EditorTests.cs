using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Core.Tests;

public sealed class EditorTests
{
    private static DiagramEditor ClassEditor() => new(DiagramDocument.Create("Test", DiagramType.Class));

    private static DiagramEditor SequenceEditor() => new(DiagramDocument.Create("Test", DiagramType.Sequence));

    [Fact]
    public void AddClass_CreatesModelAndLayoutAndSelects()
    {
        var editor = ClassEditor();

        var cls = editor.AddClass(ClassifierKind.Interface, 100, 50);

        Assert.Equal("IInterface1", cls.Name);
        Assert.Equal(100, editor.Layout.Find(cls.Id)!.X);
        Assert.Equal(cls.Id, editor.SelectedId);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void Delete_Class_RemovesAttachedRelationshipsAndLayouts()
    {
        var editor = ClassEditor();
        var a = editor.AddClass(ClassifierKind.Class, 0, 0);
        var b = editor.AddClass(ClassifierKind.Class, 300, 0);
        var relationship = editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;

        var removed = editor.Delete(a.Id);

        Assert.Equal([a.Id, relationship.Id], removed);
        Assert.Empty(((ClassDiagramModel)editor.Model).Relationships);
        Assert.Null(editor.Layout.Find(a.Id));
    }

    [Fact]
    public void AddRelationship_RejectsSelfReference()
    {
        var editor = ClassEditor();
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);

        Assert.Null(editor.AddRelationship(RelationshipKind.Inheritance, cls.Id, cls.Id));
    }

    [Fact]
    public void UndoRedo_RestoresState()
    {
        var editor = ClassEditor();
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        editor.Delete(cls.Id);

        Assert.True(editor.Undo());
        Assert.NotNull(editor.Model.FindElement(cls.Id));
        Assert.True(editor.Undo());
        Assert.Empty(editor.Model.AllElements());
        Assert.True(editor.Redo());
        Assert.NotNull(editor.Model.FindElement(cls.Id));
    }

    [Fact]
    public void CutPaste_RecreatesClassWithNewIdAndOriginalName()
    {
        var editor = ClassEditor();
        var cls = editor.AddClass(ClassifierKind.Class, 10, 10);
        cls.Attributes.Add(new UmlAttribute { Name = "id", Type = "int" });

        var text = editor.Cut(cls.Id);
        var pastedId = editor.Paste(text, 200, 120);

        var pasted = (UmlClass)editor.Model.FindElement(pastedId!)!;
        Assert.NotEqual(cls.Id, pastedId);
        Assert.Equal(cls.Name, pasted.Name);
        Assert.Equal("id", pasted.Attributes[0].Name);
        Assert.Equal(200, editor.Layout.Find(pastedId!)!.X);
    }

    [Fact]
    public void CopyPaste_RenamesDuplicateAndRejectsOtherDiagramType()
    {
        var editor = ClassEditor();
        var cls = editor.AddClass(ClassifierKind.Class, 10, 10);
        var text = editor.Copy(cls.Id);

        var pasted = (UmlClass)editor.Model.FindElement(editor.Paste(text)!)!;

        Assert.Equal(cls.Name + "2", pasted.Name);
        Assert.Null(SequenceEditor().Paste(text));
    }

    [Fact]
    public void InlineEdit_NewAttribute_IsParsedAndUndoable()
    {
        var editor = ClassEditor();
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);

        var session = editor.BeginInlineEdit(new HitInfo(cls.Id, HitPart.ClassAttributesArea))!;
        session.Commit("+ name: string");

        Assert.Equal(UmlVisibility.Public, cls.Attributes.Single().Visibility);
        Assert.Equal("string", cls.Attributes.Single().Type);

        editor.Undo();
        Assert.Empty(((UmlClass)editor.Model.FindElement(cls.Id)!).Attributes);
    }

    [Fact]
    public void InlineEdit_CancelledNewMember_IsRemoved()
    {
        var editor = ClassEditor();
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);

        editor.BeginInlineEdit(new HitInfo(cls.Id, HitPart.ClassOperationsArea))!.Cancel();

        Assert.Empty(cls.Operations);
    }

    [Fact]
    public void InlineEdit_EmptyTextDeletesExistingMember()
    {
        var editor = ClassEditor();
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        cls.Operations.Add(new UmlOperation { Name = "run" });

        editor.BeginInlineEdit(new HitInfo(cls.Id, HitPart.ClassOperation, 0))!.Commit("  ");

        Assert.Empty(cls.Operations);
    }

    [Fact]
    public void Sequence_MessagesStayBelowLifelineHeads()
    {
        var editor = SequenceEditor();
        var a = editor.AddLifeline(LifelineKind.Actor, 0);
        var b = editor.AddLifeline(LifelineKind.Object, 200);

        var message = editor.AddMessage(MessageKind.Synchronous, a.Id, b.Id, y: 0)!;
        var minimum = editor.Layout.Find(message.Id)!.Y;
        editor.MoveElementTo(message.Id, 0, -500);

        Assert.True(minimum > SequenceDiagramRenderer.DefaultTop + SequenceDiagramRenderer.ActorHeadHeight);
        Assert.Equal(minimum, editor.Layout.Find(message.Id)!.Y);
    }

    [Fact]
    public void Sequence_DeletingLifelineRemovesItsMessages()
    {
        var editor = SequenceEditor();
        var a = editor.AddLifeline(LifelineKind.Object, 0);
        var b = editor.AddLifeline(LifelineKind.Object, 200);
        editor.AddMessage(MessageKind.Synchronous, a.Id, b.Id, 120);
        editor.AddMessage(MessageKind.Reply, b.Id, b.Id, 160);

        editor.Delete(b.Id);

        Assert.Empty(((SequenceDiagramModel)editor.Model).Messages);
    }
}
