namespace UmlWizard.OpenApi;

/// <summary>
/// Names of the tagged values the OpenAPI import writes and the export reads. Values are compact JSON unless noted.
/// </summary>
public static class OpenApiTags
{
    /// <summary>Stereotype of the interfaces that hold the endpoints of one OpenAPI tag.</summary>
    public const string ApiStereotype = "api";

    /// <summary>Diagram: the version of the imported file, e.g. <c>2.0</c> or <c>3.1.0</c> (text).</summary>
    public const string SourceVersion = "openapi.source";

    /// <summary>Diagram: the <c>info</c> object without title and description (those are the diagram's).</summary>
    public const string Info = "openapi.info";

    /// <summary>Diagram: the <c>components</c> other than schemas (security schemes, shared parameters, …).</summary>
    public const string Components = "openapi.components";

    /// <summary>Diagram: prefix of other top-level fields, e.g. <c>openapi.servers</c>, <c>openapi.security</c>.</summary>
    public const string RootPrefix = "openapi.";

    /// <summary>Class: what its schema has beyond its attributes, base classes and enumeration literals.</summary>
    public const string Schema = "openapi.schema";

    /// <summary>Class: the schema name when it is not a valid class name (text).</summary>
    public const string SchemaName = "openapi.name";

    /// <summary>Attribute: what its property schema has beyond the attribute's type and default value.</summary>
    public const string Property = "openapi.property";

    /// <summary>Attribute: <c>true</c> when the property is listed as required (text).</summary>
    public const string Required = "required";

    /// <summary>«api» interface: the OpenAPI tag its endpoints have (text).</summary>
    public const string Tag = "openapi.tag";

    /// <summary>«api» interface: <c>true</c> when it groups endpoints without a tag (text).</summary>
    public const string Untagged = "openapi.untagged";

    /// <summary>Operation: the operation object without what its signature, method and path say.</summary>
    public const string Operation = "openapi.operation";
}
