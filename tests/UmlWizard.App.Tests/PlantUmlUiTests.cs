using Avalonia.Headless.XUnit;
using UmlWizard.App.ViewModels;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class PlantUmlUiTests
{
    private const string TwoDiagrams = """
        @startuml
        title Orders
        class Order
        class OrderLine
        Order *-- OrderLine
        @enduml

        @startuml
        title Checkout
        actor Customer
        Customer -> Shop : checkout()
        alt paid
          Shop --> Customer : receipt
        end
        @enduml
        """;

    [AvaloniaFact]
    public async Task ImportPlantUml_AddsEveryDiagramOfTheFileToTheSelectedFolder_AndListsWhatWasLeftOut()
    {
        using var f = new ExplorerFixture();
        var file = Path.Combine(f.Folder, "shop.puml");
        File.WriteAllText(file, TwoDiagrams);
        f.Services.OpenFilePath = file;
        f.Main.SelectedNode = f.Node("Nested");

        await f.Main.ImportPlantUmlCommand.ExecuteAsync(null);

        var orders = f.Saved.Diagrams.Single(d => d.Name == "Orders");
        var checkout = f.Saved.Diagrams.Single(d => d.Name == "Checkout");
        Assert.Equal(DiagramType.Class, orders.DiagramType);
        Assert.Equal(["Order", "OrderLine"], ((ClassDiagramModel)orders.Model).Classes.Select(c => c.Name));
        Assert.Equal(DiagramType.Sequence, checkout.DiagramType);
        Assert.Equal(2, ((SequenceDiagramModel)checkout.Model).Messages.Count);
        Assert.All(new[] { orders, checkout }, d => Assert.Equal("Nested", f.Node(d.Name).Parent?.Name));
        Assert.Equal("Checkout", f.Main.ActiveDiagram?.Name);

        var message = Assert.Single(f.Services.Messages);
        Assert.StartsWith("Created 'Orders', 'Checkout'. Some parts of the PlantUML could not be kept:", message);
        Assert.Contains("• Checkout: 1 combined fragment (alt, loop, opt, …) was left out; its messages were kept.", message);
        Assert.Empty(f.Services.Errors);
    }

    [AvaloniaFact]
    public async Task ImportPlantUml_ReportsFilesThatAreNotPlantUml_AndAddsNothing()
    {
        using var f = new ExplorerFixture();
        var file = Path.Combine(f.Folder, "notes.puml");
        File.WriteAllText(file, "@startuml\nshopping list: milk, eggs\n@enduml");
        f.Services.OpenFilePath = file;
        var diagrams = f.Main.Project!.Diagrams.Count;

        await f.Main.ImportPlantUmlCommand.ExecuteAsync(null);

        Assert.StartsWith("The file could not be read as PlantUML:", Assert.Single(f.Services.Errors));
        Assert.Equal(diagrams, f.Main.Project.Diagrams.Count);
    }

    [AvaloniaFact]
    public async Task ExportPlantUml_WritesTheSelectedDiagram_WhichImportsBack()
    {
        using var f = new ExplorerFixture();
        var file = Path.Combine(f.Folder, "shop.puml");
        File.WriteAllText(file, TwoDiagrams);
        f.Services.OpenFilePath = file;
        await f.Main.ImportPlantUmlCommand.ExecuteAsync(null);
        f.Services.Messages.Clear();

        f.Main.SelectedNode = f.Node("Orders");
        Assert.True(f.Main.ExportPlantUmlCommand.CanExecute(null));
        var exported = Path.Combine(f.Folder, "orders.puml");
        f.Services.SaveFilePath = exported;
        await f.Main.ExportPlantUmlCommand.ExecuteAsync(null);

        var text = File.ReadAllText(exported);
        Assert.StartsWith("@startuml\ntitle Orders\nclass Order\nclass OrderLine\n", text);
        Assert.Contains("Order *-- OrderLine", text);
        Assert.Equal($"The PlantUML was written to {exported}.", Assert.Single(f.Services.Messages));
    }

    [AvaloniaFact]
    public void ExportPlantUml_NeedsADiagramSelected()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("Nested");

        Assert.False(f.Main.ExportPlantUmlCommand.CanExecute(null));
    }
}
