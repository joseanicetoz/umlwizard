using UmlWizard.Core.Model;

namespace UmlWizard.Core.Tests;

public sealed class DiagramImportTests
{
    [Fact]
    public void EnsureUniqueName_WhenNameIsUnique_ReturnsOriginalName()
    {
        var existing = new[] { "Diagram 1", "Diagram 2" };
        var unique = DiagramDocument.EnsureUniqueName("New Diagram", existing);
        Assert.Equal("New Diagram", unique);
    }

    [Fact]
    public void EnsureUniqueName_WhenNameExists_AppendsIncrementingNumber()
    {
        var existing = new[] { "Domain", "Domain (1)", "Domain (2)" };
        var unique = DiagramDocument.EnsureUniqueName("Domain", existing);
        Assert.Equal("Domain (3)", unique);
    }

    [Fact]
    public void EnsureUniqueName_IsCaseInsensitive()
    {
        var existing = new[] { "domain" };
        var unique = DiagramDocument.EnsureUniqueName("Domain", existing);
        Assert.Equal("Domain (1)", unique);
    }

    [Fact]
    public void RegenerateIds_ClassDiagram_UpdatesElementIdsAndRelationshipsAndLayout()
    {
        var document = DiagramDocument.Create("Classes", DiagramType.Class);
        var model = (ClassDiagramModel)document.Model;

        var classA = new UmlClass { Name = "ClassA" };
        var classB = new UmlClass { Name = "ClassB" };
        model.Classes.AddRange([classA, classB]);

        var origDocId = document.Id;
        var origAId = classA.Id;
        var origBId = classB.Id;

        var rel = new UmlRelationship
        {
            Kind = RelationshipKind.Association,
            SourceId = origAId,
            TargetId = origBId,
        };
        model.Relationships.Add(rel);

        var note = new NoteElement { Text = "A note" };
        model.Notes.Add(note);
        var origNoteId = note.Id;

        var attachment = new NoteAttachment { NoteId = origNoteId, TargetId = origAId };
        model.NoteAttachments.Add(attachment);

        document.Layout.Add(new ElementLayout { ElementId = origAId, X = 10, Y = 20, Width = 100, Height = 80 });
        document.Layout.Add(new ElementLayout { ElementId = origNoteId, X = 200, Y = 20, Width = 120, Height = 60 });

        document.RegenerateIds();

        Assert.NotEqual(origDocId, document.Id);
        Assert.NotEqual(origAId, classA.Id);
        Assert.NotEqual(origBId, classB.Id);
        Assert.NotEqual(origNoteId, note.Id);

        Assert.Equal(classA.Id, rel.SourceId);
        Assert.Equal(classB.Id, rel.TargetId);

        Assert.Equal(note.Id, attachment.NoteId);
        Assert.Equal(classA.Id, attachment.TargetId);

        Assert.NotNull(document.Layout.Find(classA.Id));
        Assert.NotNull(document.Layout.Find(note.Id));
        Assert.Null(document.Layout.Find(origAId));
    }

    [Fact]
    public void RegenerateIds_SequenceDiagram_UpdatesLifelinesMessagesAndLayout()
    {
        var document = DiagramDocument.Create("Sequence", DiagramType.Sequence);
        var model = (SequenceDiagramModel)document.Model;

        var user = new Lifeline { Name = "User" };
        var service = new Lifeline { Name = "Service" };
        model.Lifelines.AddRange([user, service]);

        var origUserId = user.Id;
        var origServiceId = service.Id;

        var msg = new SequenceMessage
        {
            SourceId = origUserId,
            TargetId = origServiceId,
            Text = "call()",
            Kind = MessageKind.Synchronous,
        };
        model.Messages.Add(msg);

        document.Layout.Add(new ElementLayout { ElementId = origUserId, X = 50, Y = 30, Width = 80, Height = 200 });

        document.RegenerateIds();

        Assert.NotEqual(origUserId, user.Id);
        Assert.NotEqual(origServiceId, service.Id);
        Assert.Equal(user.Id, msg.SourceId);
        Assert.Equal(service.Id, msg.TargetId);
        Assert.NotNull(document.Layout.Find(user.Id));
    }

    [Fact]
    public void RegenerateIds_GraphDiagram_UpdatesNodesEdgesAndLayout()
    {
        var document = DiagramDocument.Create("States", DiagramType.StateMachine);
        var model = (StateMachineDiagramModel)document.Model;

        var initial = new GraphNode { Kind = GraphNodeKind.InitialState };
        var active = new GraphNode { Kind = GraphNodeKind.State, Name = "Active" };
        model.Nodes.AddRange([initial, active]);

        var origInitId = initial.Id;
        var origActiveId = active.Id;

        var transition = new GraphEdge
        {
            SourceId = origInitId,
            TargetId = origActiveId,
            Label = "start",
        };
        model.Edges.Add(transition);

        document.Layout.Add(new ElementLayout { ElementId = origActiveId, X = 100, Y = 100, Width = 120, Height = 60 });

        document.RegenerateIds();

        Assert.NotEqual(origInitId, initial.Id);
        Assert.NotEqual(origActiveId, active.Id);
        Assert.Equal(initial.Id, transition.SourceId);
        Assert.Equal(active.Id, transition.TargetId);
        Assert.NotNull(document.Layout.Find(active.Id));
    }
}
