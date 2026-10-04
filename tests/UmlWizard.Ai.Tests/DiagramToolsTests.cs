using SkiaSharp;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Ai.Tests;

public sealed class DiagramToolsTests
{
    private static AiToolResult Run(DiagramTools tools, string name, string arguments) => tools.Run(ScriptedChat.Call(name, arguments));

    private static void Ok(DiagramTools tools, string name, string arguments)
    {
        var result = Run(tools, name, arguments);
        Assert.False(result.IsError, result.Content);
    }

    private static SKRect Rect(DiagramDocument document, string id)
    {
        var l = document.Layout.Find(id)!;
        return SKRect.Create((float)l.X, (float)l.Y, (float)l.Width, (float)l.Height);
    }

    /// <summary>A state machine: initial → Created → Paid, placed.</summary>
    private static DiagramDocument Orders()
    {
        var document = DiagramDocument.Create("Orders", DiagramType.StateMachine);
        var tools = new GraphDiagramTools(document);
        Ok(tools, "add_node", """{"kind": "initial_state"}""");
        Ok(tools, "add_node", """{"kind": "state", "name": "Created"}""");
        Ok(tools, "add_node", """{"kind": "state", "name": "Paid"}""");
        Ok(tools, "add_edge", """{"source": "initial_state#1", "target": "Created"}""");
        Ok(tools, "add_edge", """{"source": "Created", "target": "Paid", "label": "pay"}""");
        tools.Place();
        return document;
    }

    [Fact]
    public void GraphTools_ReferToNodesByName_OrByKindAndNumber_AndKeepTheEditorsRules()
    {
        var document = Orders();
        var model = (GraphDiagramModel)document.Model;
        var tools = new GraphDiagramTools(document);

        var added = Run(tools, "add_node", """{"kind": "choice"}""");
        Assert.Equal("Added choice choice#1.", added.Content);
        Ok(tools, "add_edge", """{"source": "paid", "target": "choice#1", "label": "ship [in stock]"}""");
        Ok(tools, "add_node", """{"kind": "final_state"}""");
        Ok(tools, "add_edge", """{"source": "choice#1", "target": "final_state#1"}""");

        // The rules of the editor: nothing goes back into an initial state, and kinds are those of the diagram type.
        Assert.Contains("initial", Run(tools, "add_edge", """{"source": "Paid", "target": "initial_state#1"}""").Content);
        Assert.Contains("Unknown kind", Run(tools, "add_node", """{"kind": "actor"}""").Content);
        Assert.Contains("There is nothing named Shipped", Run(tools, "rename_node", """{"node": "Shipped", "new_name": "X"}""").Content);
        Assert.Contains("already a state named Paid", Run(tools, "add_node", """{"kind": "state", "name": "Paid"}""").Content);

        Ok(tools, "set_edge_label", """{"source": "Created", "target": "Paid", "label": "pay / capture()"}""");
        Ok(tools, "rename_node", """{"node": "Created", "new_name": "New"}""");
        Assert.Equal("pay / capture()", model.Edges.Single(e => model.FindNode(e.SourceId)!.Name == "New").Label);

        var outline = tools.Outline();
        Assert.Contains("- initial_state#1 (initial_state)", outline);
        Assert.Contains("- choice#1 (choice)", outline);
        Assert.Contains("- New -> Paid (transition): pay / capture()", outline);
        Assert.Contains("- Paid -> choice#1 (transition): ship [in stock]", outline);

        Ok(tools, "remove_edge", """{"source": "choice#1", "target": "final_state#1"}""");
        Ok(tools, "delete_node", """{"node": "final_state#1"}""");
        Assert.DoesNotContain(model.Nodes, n => n.Kind == GraphNodeKind.FinalState);
        Assert.True(tools.Changed);
    }

    [Fact]
    public void GraphPlacement_PutsNewNodesNextToTheirNeighbours_WithoutMovingOrOverlappingTheOthers()
    {
        var document = Orders();
        var before = document.Layout.Elements.ToDictionary(l => l.ElementId, l => (l.X, l.Y));
        var tools = new GraphDiagramTools(document);
        Ok(tools, "add_node", """{"kind": "state", "name": "Shipped"}""");
        Ok(tools, "add_node", """{"kind": "state", "name": "Cancelled"}""");
        Ok(tools, "add_edge", """{"source": "Paid", "target": "Shipped"}""");
        Ok(tools, "add_note", """{"text": "Paid orders ship within a day.", "attach_to": "Shipped"}""");

        tools.Place();

        foreach (var (id, position) in before)
        {
            Assert.Equal(position, (document.Layout.Find(id)!.X, document.Layout.Find(id)!.Y));
        }

        var model = (GraphDiagramModel)document.Model;
        var rects = model.Nodes.Select(n => n.Id).Concat(model.Notes.Select(n => n.Id)).Select(id => Rect(document, id)).ToList();
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].IntersectsWith(rects[j]), $"{i} overlaps {j}");
            }
        }

        // Next to its neighbour, not somewhere far away.
        var paid = Rect(document, model.Nodes.Single(n => n.Name == "Paid").Id);
        var shipped = Rect(document, model.Nodes.Single(n => n.Name == "Shipped").Id);
        Assert.True(Math.Abs(shipped.MidX - paid.MidX) + Math.Abs(shipped.MidY - paid.MidY) < 300);
    }

    [Fact]
    public void InADeploymentDiagram_WhatIsDeployed_GoesInsideItsNode_WhichGrowsToHoldIt()
    {
        var document = DiagramDocument.Create("Deployment", DiagramType.Deployment);
        var model = (GraphDiagramModel)document.Model;
        var tools = new GraphDiagramTools(document);
        Ok(tools, "add_node", """{"kind": "device", "name": "Web Server"}""");
        Ok(tools, "add_node", """{"kind": "execution_environment", "name": "Kestrel", "inside": "Web Server"}""");
        for (var i = 1; i <= 5; i++)
        {
            Ok(tools, "add_node", $$"""{"kind": "artifact", "name": "shop{{i}}.dll", "inside": "Kestrel"}""");
        }

        Ok(tools, "add_node", """{"kind": "node", "name": "Database"}""");
        Ok(tools, "add_edge", """{"source": "Web Server", "target": "Database", "kind": "communication_path", "label": "TCP"}""");
        Assert.Contains("holds nothing", Run(tools, "add_node", """{"kind": "artifact", "name": "x.dll", "inside": "shop1.dll"}""").Content);
        Assert.Contains("Unknown edge kind", Run(tools, "add_edge", """{"source": "Web Server", "target": "Database", "kind": "transition"}""").Content);
        tools.Place();

        // Containment is where nodes are drawn: as the editor and Arrange see it.
        var id = (string name) => model.Nodes.Single(n => n.Name == name).Id;
        var containers = DiagramArranger.ContainerOf(document, model);
        Assert.Equal(id("Web Server"), containers[id("Kestrel")]);
        for (var i = 1; i <= 5; i++)
        {
            Assert.Equal(id("Kestrel"), containers[id($"shop{i}.dll")]);
        }

        Assert.False(containers.ContainsKey(id("Database")));
        Assert.Contains("- Kestrel (execution_environment), inside Web Server", tools.Outline());

        // Moving an artifact out of its environment, into the database node.
        var more = new GraphDiagramTools(document);
        Ok(more, "move_node", """{"node": "shop5.dll", "inside": "Database"}""");
        more.Place();
        Assert.Equal(id("Database"), DiagramArranger.ContainerOf(document, model)[id("shop5.dll")]);
    }

    [Fact]
    public void InAUseCaseDiagram_UseCasesGoInTheSystem()
    {
        var document = DiagramDocument.Create("Shop", DiagramType.UseCase);
        var tools = new GraphDiagramTools(document);
        Ok(tools, "add_node", """{"kind": "system_boundary", "name": "Online Shop"}""");
        Ok(tools, "add_node", """{"kind": "actor", "name": "Customer"}""");
        Ok(tools, "add_node", """{"kind": "use_case", "name": "Place order", "inside": "Online Shop"}""");
        Ok(tools, "add_node", """{"kind": "use_case", "name": "Pay", "inside": "Online Shop"}""");
        Ok(tools, "add_edge", """{"source": "Customer", "target": "Place order", "kind": "association"}""");
        Ok(tools, "add_edge", """{"source": "Place order", "target": "Pay", "kind": "include"}""");
        Assert.Contains("two use cases", Run(tools, "add_edge", """{"source": "Customer", "target": "Pay", "kind": "include"}""").Content);
        tools.Place();

        var model = (GraphDiagramModel)document.Model;
        var containers = DiagramArranger.ContainerOf(document, model);
        var boundary = model.Nodes.Single(n => n.Kind == GraphNodeKind.SystemBoundary).Id;
        Assert.All(model.Nodes.Where(n => n.Kind == GraphNodeKind.UseCase), n => Assert.Equal(boundary, containers[n.Id]));
        Assert.False(containers.ContainsKey(model.Nodes.Single(n => n.Kind == GraphNodeKind.Actor).Id));
    }

    [Fact]
    public void SequenceTools_NumberMessagesInTimeOrder_AndANewMessagePushesTheLaterOnesDown()
    {
        var document = DiagramDocument.Create("Checkout", DiagramType.Sequence);
        var model = (SequenceDiagramModel)document.Model;
        var tools = new SequenceDiagramTools(document);
        Ok(tools, "add_lifeline", """{"name": "customer", "kind": "actor"}""");
        Ok(tools, "add_lifeline", """{"name": "shop", "type": "ShopController"}""");
        Ok(tools, "add_message", """{"source": "customer", "target": "shop", "text": "checkout()"}""");
        Ok(tools, "add_message", """{"source": "shop", "target": "customer", "text": "receipt", "kind": "reply"}""");
        tools.Place();
        var shop = model.Lifelines.Single(l => l.Name == "shop");
        var customer = model.Lifelines.Single(l => l.Name == "customer");
        Assert.True(document.Layout.Find(shop.Id)!.X > document.Layout.Find(customer.Id)!.X);
        var firstY = document.Layout.Find(model.Messages[0].Id)!.Y;
        var replyY = document.Layout.Find(model.Messages[1].Id)!.Y;
        Assert.True(replyY > firstY);

        var more = new SequenceDiagramTools(document);
        Ok(more, "add_lifeline", """{"name": "payments", "type": "PaymentService"}""");
        Assert.Equal("Added message 2: shop -> payments: pay(total).", Run(more, "add_message", """{"source": "shop", "target": "payments", "text": "pay(total)", "position": 2}""").Content);
        Ok(more, "set_message", """{"number": 3, "text": "receipt(order)"}""");
        Assert.Contains("numbered 1 to 3", Run(more, "remove_message", """{"number": 7}""").Content);
        var outline = more.Outline();
        Assert.Contains("- shop : ShopController (object)", outline);
        Assert.Contains("2. shop -> payments: pay(total) (synchronous)", outline);
        Assert.Contains("3. shop -> customer: receipt(order) (reply)", outline);
        more.Place();

        var pay = model.Messages.Single(m => m.Text == "pay(total)");
        var receipt = model.Messages.Single(m => m.Text == "receipt(order)");
        Assert.Equal(firstY, document.Layout.Find(model.Messages[0].Id)!.Y);
        Assert.True(document.Layout.Find(pay.Id)!.Y > firstY);
        Assert.True(document.Layout.Find(receipt.Id)!.Y > document.Layout.Find(pay.Id)!.Y);
        Assert.True(document.Layout.Find(model.Lifelines.Single(l => l.Name == "payments").Id)!.X > document.Layout.Find(shop.Id)!.X);

        // Deleting a lifeline takes its messages, and renumbers the rest.
        var last = new SequenceDiagramTools(document);
        Ok(last, "delete_lifeline", """{"lifeline": "payments"}""");
        Assert.Equal(2, last.Order.Count);
        Assert.Contains("2. shop -> customer: receipt(order) (reply)", last.Outline());
    }

    [Fact]
    public void TheChanges_OfGraphAndSequenceDiagrams_AreDescribed()
    {
        var before = Orders();
        var after = Core.Serialization.DiagramSerializer.Clone(before);
        var tools = new GraphDiagramTools(after);
        Ok(tools, "add_node", """{"kind": "state", "name": "Shipped"}""");
        Ok(tools, "add_edge", """{"source": "Paid", "target": "Shipped", "label": "ship"}""");
        Ok(tools, "rename_node", """{"node": "Created", "new_name": "New"}""");
        Ok(tools, "set_edge_label", """{"source": "New", "target": "Paid", "label": "pay [valid]"}""");
        tools.Place();

        Assert.Equal(
            ["~ Created renamed to New", "+ state Shipped", "~ New → Paid (transition) : pay [valid]", "+ Paid → Shipped (transition) : ship"],
            DiagramChanges.Describe(before, after));

        var sequence = DiagramDocument.Create("S", DiagramType.Sequence);
        var s = new SequenceDiagramTools(sequence);
        Ok(s, "add_lifeline", """{"name": "a"}""");
        Ok(s, "add_lifeline", """{"name": "b"}""");
        Ok(s, "add_message", """{"source": "a", "target": "b", "text": "go()"}""");
        s.Place();
        var changed = Core.Serialization.DiagramSerializer.Clone(sequence);
        var c = new SequenceDiagramTools(changed);
        Ok(c, "add_message", """{"source": "b", "target": "a", "text": "done", "kind": "reply"}""");
        Ok(c, "set_lifeline", """{"lifeline": "b", "type": "Service"}""");
        c.Place();
        Assert.Equal(["~ b is now b : Service", "+ 2. b → a: done"], DiagramChanges.Describe(sequence, changed));
    }
}
