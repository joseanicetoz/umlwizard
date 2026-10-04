using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport;

/// <summary>What an imported type is in UML.</summary>
public enum ImportedTypeKind
{
    Class,
    AbstractClass,
    Interface,
    Enumeration,
}

/// <summary>
/// A type declared in the imported code, as an <see cref="ICodeImporter"/> reads it: the language-neutral input of
/// <see cref="CodeImporter"/>, which merges the parts of a type (partial classes, reopened Ruby classes), links the
/// types to each other and draws them as a class diagram.
/// </summary>
public sealed class ImportedType
{
    /// <summary>The namespace, package or module the type is declared in, as the language writes it; empty for none.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>The type's own name, e.g. <c>Order</c>, without type parameters or containing types.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The types this type is nested in, outermost first; empty for a top-level type.</summary>
    public List<string> DeclaringTypes { get; set; } = [];

    /// <summary>The names of the type parameters of a generic type, e.g. <c>T</c>.</summary>
    public List<string> TypeParameters { get; set; } = [];

    public ImportedTypeKind Kind { get; set; } = ImportedTypeKind.Class;

    /// <summary>A stereotype for what UML has no kind for, e.g. <c>struct</c>, <c>record</c> or <c>module</c>.</summary>
    public string? Stereotype { get; set; }

    /// <summary>
    /// The base classes and implemented (or included) interfaces as written. Those that are types of the import
    /// become inheritance or realization; the others are reported.
    /// </summary>
    public List<TypeRef> BaseTypes { get; set; } = [];

    public List<ImportedAttribute> Attributes { get; set; } = [];

    public List<ImportedOperation> Operations { get; set; } = [];

    /// <summary>The literals of an enumeration.</summary>
    public List<ImportedLiteral> Literals { get; set; } = [];

    /// <summary>
    /// Tagged values for what the diagram has no place for, e.g. <c>php.final = true</c>; kept on the element as they are.
    /// See <see cref="CodeImporter"/> for how they are merged.
    /// </summary>
    public List<TaggedValue> TaggedValues { get; set; } = [];
}

/// <summary>
/// A type as a declaration uses it: <see cref="Text"/> is shown in the diagram; <see cref="Name"/> and
/// <see cref="Arity"/> find the type among the imported ones, looking through nullable types, arrays and collections.
/// </summary>
/// <param name="Text">The type as written, e.g. <c>List&lt;OrderLine&gt;</c>.</param>
/// <param name="Name">The simple name of the type it refers to, e.g. <c>OrderLine</c>; <c>null</c> for built-in types.</param>
/// <param name="Arity">The number of type arguments of <paramref name="Name"/>.</param>
/// <param name="IsMany">True for arrays and collections of <paramref name="Name"/>: an association with multiplicity <c>*</c>.</param>
/// <param name="IsOptional">True for nullable types and pointers: an association with multiplicity <c>0..1</c>.</param>
public sealed record TypeRef(string Text, string? Name = null, int Arity = 0, bool IsMany = false, bool IsOptional = false);

/// <summary>A field or property.</summary>
public sealed class ImportedAttribute
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The type; <c>null</c> when the language does not declare one (Ruby).</summary>
    public TypeRef? Type { get; set; }

    public UmlVisibility Visibility { get; set; } = UmlVisibility.Private;

    public bool IsStatic { get; set; }

    /// <summary>The initial value as written; long values are left out by <see cref="CodeImporter"/>.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Tagged values for what the diagram has no place for, e.g. <c>php.final = true</c>; kept on the element as they are.
    /// See <see cref="CodeImporter"/> for how they are merged.
    /// </summary>
    public List<TaggedValue> TaggedValues { get; set; } = [];
}

/// <summary>A method or constructor.</summary>
public sealed class ImportedOperation
{
    /// <summary>The name; a constructor is named like its type.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The return type as written; <c>null</c> for constructors and when the language does not declare one.</summary>
    public string? ReturnType { get; set; }

    public List<UmlParameter> Parameters { get; set; } = [];

    public UmlVisibility Visibility { get; set; } = UmlVisibility.Public;

    public bool IsStatic { get; set; }

    public bool IsAbstract { get; set; }

    /// <summary>
    /// Tagged values for what the diagram has no place for, e.g. <c>php.final = true</c>; kept on the element as they are.
    /// See <see cref="CodeImporter"/> for how they are merged.
    /// </summary>
    public List<TaggedValue> TaggedValues { get; set; } = [];
}

/// <summary>A literal of an enumeration, with its value when the code gives one.</summary>
public sealed record ImportedLiteral(string Name, string? Value = null)
{
    /// <summary>Tagged values for what the diagram has no place for; kept on the literal's attribute.</summary>
    public List<TaggedValue> TaggedValues { get; init; } = [];
}
