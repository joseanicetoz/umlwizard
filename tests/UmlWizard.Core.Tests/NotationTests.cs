using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;

namespace UmlWizard.Core.Tests;

public sealed class NotationTests
{
    [Fact]
    public void ParseAttribute_ReadsVisibilityTypeAndDefault()
    {
        var attribute = UmlNotation.ParseAttribute("# static items: Dictionary<string, int> = new()")!;

        Assert.Equal(UmlVisibility.Protected, attribute.Visibility);
        Assert.True(attribute.IsStatic);
        Assert.Equal("items", attribute.Name);
        Assert.Equal("Dictionary<string, int>", attribute.Type);
        Assert.Equal("new()", attribute.DefaultValue);
    }

    [Fact]
    public void ParseAttribute_UsesDefaultVisibilityAndRejectsEmptyName()
    {
        Assert.Equal(UmlVisibility.Package, UmlNotation.ParseAttribute("count", UmlVisibility.Package)!.Visibility);
        Assert.Null(UmlNotation.ParseAttribute("+ : int"));
        Assert.Null(UmlNotation.ParseAttribute("   "));
    }

    [Fact]
    public void ParseOperation_ReadsParametersAndReturnType()
    {
        var operation = UmlNotation.ParseOperation("abstract -find(id: int, filter: Func<Order, bool>): Order?")!;

        Assert.Equal(UmlVisibility.Private, operation.Visibility);
        Assert.True(operation.IsAbstract);
        Assert.Equal("find", operation.Name);
        Assert.Equal("Order?", operation.ReturnType);
        Assert.Equal(2, operation.Parameters.Count);
        Assert.Equal("Func<Order, bool>", operation.Parameters[1].Type);
    }

    [Theory]
    [InlineData("+name: string = \"x\"")]
    [InlineData("-count: int")]
    [InlineData("~flag")]
    public void Attribute_FormatParseRoundTrip(string text) =>
        Assert.Equal(text, UmlNotation.Format(UmlNotation.ParseAttribute(text)!));

    [Theory]
    [InlineData("+run()")]
    [InlineData("#save(order: Order, force: bool): void")]
    public void Operation_FormatParseRoundTrip(string text) =>
        Assert.Equal(text, UmlNotation.Format(UmlNotation.ParseOperation(text)!));

    [Fact]
    public void Lifeline_ParsesNameAndType()
    {
        Assert.Equal(("shop", "Controller"), UmlNotation.ParseLifeline("shop : Controller"));
        Assert.Equal(("", "Gateway"), UmlNotation.ParseLifeline(": Gateway"));
        Assert.Equal(("alice", (string?)null), UmlNotation.ParseLifeline("alice"));
    }
}
