using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class DeploymentDiagramTests
{
    private static DiagramEditor Editor() => new(DiagramDocument.Create("Deployment", DiagramType.Deployment));

    /// <summary>Places a node with its top-left corner at (x, y) and the given size.</summary>
    private static GraphNode Place(DiagramEditor editor, GraphNodeKind kind, double x, double y, double width, double height)
    {
        var node = editor.AddNode(kind, 0, 0);
        editor.ResizeElement(node.Id, width, height);
        (editor.Layout.Find(node.Id)!.X, editor.Layout.Find(node.Id)!.Y) = (x, y);
        return node;
    }

    [Fact]
    public void DeploymentDiagrams_HaveNodesDevicesEnvironmentsArtifactsAndComponents()
    {
        Assert.Equal(
            [GraphNodeKind.Node, GraphNodeKind.Device, GraphNodeKind.ExecutionEnvironment, GraphNodeKind.Artifact, GraphNodeKind.Component],
            GraphRules.NodeKinds(DiagramType.Deployment));
        Assert.Equal(
            [GraphEdgeKind.CommunicationPath, GraphEdgeKind.Deploy, GraphEdgeKind.Manifest, GraphEdgeKind.Dependency],
            GraphRules.EdgeKinds(DiagramType.Deployment));

        var editor = Editor();
        Assert.Equal(["Node1", "Device1", "Environment1", "Artifact1"],
            new[] { GraphNodeKind.Node, GraphNodeKind.Device, GraphNodeKind.ExecutionEnvironment, GraphNodeKind.Artifact }
                .Select(k => editor.AddNode(k, 0, 0).Name));
        Assert.Equal("«executionEnvironment»", GraphRules.Keyword(GraphNodeKind.ExecutionEnvironment));
        Assert.Null(GraphRules.Keyword(GraphNodeKind.Node));
    }

    [Fact]
    public void ConnectionRules()
    {
        var editor = Editor();
        var server = editor.AddNode(GraphNodeKind.Node, 0, 0);
        var phone = editor.AddNode(GraphNodeKind.Device, 300, 0);
        var runtime = editor.AddNode(GraphNodeKind.ExecutionEnvironment, 600, 0);
        var jar = editor.AddNode(GraphNodeKind.Artifact, 0, 300);
        var service = editor.AddNode(GraphNodeKind.Component, 300, 300);

        Assert.Null(editor.CheckEdge(GraphEdgeKind.CommunicationPath, server.Id, phone.Id));
        Assert.Null(editor.CheckEdge(GraphEdgeKind.CommunicationPath, phone.Id, runtime.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.CommunicationPath, server.Id, jar.Id));

        Assert.Null(editor.CheckEdge(GraphEdgeKind.Deploy, jar.Id, runtime.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Deploy, server.Id, jar.Id)); // wrong way round
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Deploy, jar.Id, service.Id));

        Assert.Null(editor.CheckEdge(GraphEdgeKind.Manifest, jar.Id, service.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Manifest, jar.Id, server.Id));

        Assert.Null(editor.CheckEdge(GraphEdgeKind.Dependency, service.Id, jar.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Transition, server.Id, phone.Id));
    }

    [Fact]
    public void MovingANode_TakesNestedEnvironmentsAndArtifactsAlong_ButNotOutsiders()
    {
        var editor = Editor();
        var device = Place(editor, GraphNodeKind.Device, 0, 0, 300, 250);
        var runtime = Place(editor, GraphNodeKind.ExecutionEnvironment, 20, 50, 250, 180);
        var artifact = Place(editor, GraphNodeKind.Artifact, 40, 100, 160, 60);
        var outside = Place(editor, GraphNodeKind.Artifact, 400, 100, 160, 60);

        editor.MoveElementTo(device.Id, 100, 30);

        Assert.Equal((120, 80), (editor.Layout.Find(runtime.Id)!.X, editor.Layout.Find(runtime.Id)!.Y));
        Assert.Equal((140, 130), (editor.Layout.Find(artifact.Id)!.X, editor.Layout.Find(artifact.Id)!.Y));
        Assert.Equal((400, 100), (editor.Layout.Find(outside.Id)!.X, editor.Layout.Find(outside.Id)!.Y));

        // Moving the environment on its own takes only its artifact.
        editor.MoveElementTo(runtime.Id, 130, 80);
        Assert.Equal(150, editor.Layout.Find(artifact.Id)!.X);
        Assert.Equal(100, editor.Layout.Find(device.Id)!.X);
    }

    [Fact]
    public void Clicking_SelectsTheInnermostElement_AndNodesCanBeConnected()
    {
        var editor = Editor();
        var device = Place(editor, GraphNodeKind.Device, 0, 0, 300, 250);
        var runtime = Place(editor, GraphNodeKind.ExecutionEnvironment, 20, 50, 250, 180);
        var artifact = Place(editor, GraphNodeKind.Artifact, 40, 100, 160, 60);
        using var renderer = DiagramRenderer.Create(DiagramType.Deployment);
        renderer.UpdateLayout(editor.Document);

        Assert.Equal(new HitInfo(artifact.Id, HitPart.Node), renderer.HitTest(editor.Document, new SKPoint(100, 130)));
        Assert.Equal(new HitInfo(runtime.Id, HitPart.Node), renderer.HitTest(editor.Document, new SKPoint(230, 200)));
        var outer = renderer.HitTest(editor.Document, new SKPoint(10, 240))!;
        Assert.Equal(new HitInfo(device.Id, HitPart.Node), outer);
        Assert.True(outer.IsNode); // unlike a system boundary, a node can be connected
    }

    [Fact]
    public void Deployments_AreSavedAndDrawn_WithTheirKeywords()
    {
        var editor = Editor();
        var server = Place(editor, GraphNodeKind.Node, 300, 0, 240, 160);
        var jar = Place(editor, GraphNodeKind.Artifact, 0, 40, 150, 60);
        var deploy = editor.AddEdge(GraphEdgeKind.Deploy, jar.Id, server.Id)!;
        using var renderer = DiagramRenderer.Create(DiagramType.Deployment);
        renderer.UpdateLayout(editor.Document);

        var loaded = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(editor.Document));
        Assert.IsType<DeploymentDiagramModel>(loaded.Model);
        Assert.Contains("\"kind\": \"deployment\"", DiagramSerializer.Serialize(editor.Document));
        Assert.Equal(GraphEdgeKind.Deploy, Assert.Single(((GraphDiagramModel)loaded.Model).Edges).Kind);

        // The «deploy» keyword is shown on the edge, and selecting it by its label works.
        var rect = renderer.GetEditRect(editor.Document, deploy.Id, HitPart.Edge)!.Value;
        Assert.Equal(deploy.Id, renderer.HitTest(editor.Document, new SKPoint(rect.MidX, rect.MidY + 1))?.ElementId);
    }
}
