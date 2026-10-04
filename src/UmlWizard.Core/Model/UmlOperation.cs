namespace UmlWizard.Core.Model;

public sealed class UmlOperation
{
    public string Name { get; set; } = "operation";

    public string? ReturnType { get; set; }

    public UmlVisibility Visibility { get; set; } = UmlVisibility.Public;

    public bool IsStatic { get; set; }

    public bool IsAbstract { get; set; }

    public List<UmlParameter> Parameters { get; set; } = [];

    /// <summary>The HTTP method when the operation is a web API endpoint, e.g. <c>GET</c>; shown with <see cref="Path"/>.</summary>
    public string? HttpMethod { get; set; }

    /// <summary>The URL path of a web API endpoint, e.g. <c>/orders/{id}</c>; names in braces are path parameters.</summary>
    public string? Path { get; set; }

    /// <summary>Tagged values, e.g. what an OpenAPI import could not map to the operation.</summary>
    public List<TaggedValue> TaggedValues { get; set; } = [];
}
