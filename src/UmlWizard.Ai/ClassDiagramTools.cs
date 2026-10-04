using System.Text.Json.Nodes;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;

namespace UmlWizard.Ai;

/// <summary>
/// The tools the model may call to change a class diagram. Classes are referred to by name; members are written in UML
/// notation and read with <see cref="UmlNotation"/>, as when typed in the editor. New classes and notes are placed by
/// <see cref="ClassPlacement"/>.
/// </summary>
public sealed class ClassDiagramTools(DiagramDocument document) : DiagramTools(document)
{
    private readonly ClassDiagramModel _model = (ClassDiagramModel)document.Model;

    public override IReadOnlyList<AiTool> Tools => Definitions;

    public override string Guidance => """
        You can change this class diagram with your tools. Use them when the user asks for a change; refer to classes by
        name. Write members in UML notation (e.g. -total: decimal, +pay(amount: decimal): bool).
        """;

    public override void Place() => ClassPlacement.Place(Document, Added);

    public static IReadOnlyList<AiTool> Definitions { get; } =
    [
        Tool("add_class", "Adds a class, abstract class, interface or enumeration.", """
            {"type": "object", "properties": {
              "name": {"type": "string", "description": "The class's name; unique in the diagram."},
              "kind": {"type": "string", "enum": ["class", "abstract_class", "interface", "enumeration"], "description": "Default: class."},
              "stereotype": {"type": "string", "description": "Optional, without guillemets, e.g. entity."}},
             "required": ["name"]}
            """),
        Tool("set_class", "Changes the kind or stereotype of a class.", """
            {"type": "object", "properties": {
              "class": {"type": "string"},
              "kind": {"type": "string", "enum": ["class", "abstract_class", "interface", "enumeration"]},
              "stereotype": {"type": "string", "description": "Empty to remove it."}},
             "required": ["class"]}
            """),
        Tool("rename_class", "Renames a class.", """
            {"type": "object", "properties": {"class": {"type": "string"}, "new_name": {"type": "string"}}, "required": ["class", "new_name"]}
            """),
        Tool("delete_class", "Deletes a class, with its relationships and the notes' links to it.", """
            {"type": "object", "properties": {"class": {"type": "string"}}, "required": ["class"]}
            """),
        Tool("add_attribute", "Adds an attribute to a class, or a literal to an enumeration.", """
            {"type": "object", "properties": {
              "class": {"type": "string"},
              "attribute": {"type": "string", "description": "UML notation: [visibility] name: Type [= default], visibility + - # ~, prefix static for a static one; e.g. -total: decimal = 0. For an enumeration, just the literal, e.g. Paid."}},
             "required": ["class", "attribute"]}
            """),
        Tool("add_operation", "Adds an operation to a class.", """
            {"type": "object", "properties": {
              "class": {"type": "string"},
              "operation": {"type": "string", "description": "UML notation: [visibility] name(param: Type, ...): ReturnType, prefix static or abstract; e.g. +pay(amount: decimal): bool."}},
             "required": ["class", "operation"]}
            """),
        Tool("remove_member", "Removes an attribute, operation or enumeration literal from a class, by name.", """
            {"type": "object", "properties": {"class": {"type": "string"}, "name": {"type": "string"}}, "required": ["class", "name"]}
            """),
        Tool("add_relationship", "Adds a relationship between two classes.", """
            {"type": "object", "properties": {
              "source": {"type": "string", "description": "The subclass (generalization), the realizing class (realization), the whole (aggregation, composition), or the class that uses or refers to the target."},
              "target": {"type": "string"},
              "kind": {"type": "string", "enum": ["association", "directed_association", "generalization", "realization", "aggregation", "composition", "dependency"]},
              "label": {"type": "string"},
              "source_multiplicity": {"type": "string", "description": "e.g. 1, 0..1, *, 1..*"},
              "target_multiplicity": {"type": "string"}},
             "required": ["source", "target", "kind"]}
            """),
        Tool("remove_relationship", "Removes the relationships from source to target (of a kind, if given).", """
            {"type": "object", "properties": {
              "source": {"type": "string"}, "target": {"type": "string"},
              "kind": {"type": "string", "enum": ["association", "directed_association", "generalization", "realization", "aggregation", "composition", "dependency"]}},
             "required": ["source", "target"]}
            """),
        NoteTool("class"),
    ];

    protected override string? Execute(string name, JsonObject args) => name switch
    {
        "add_class" => AddClass(args),
        "set_class" => SetClass(args),
        "rename_class" => RenameClass(args),
        "delete_class" => DeleteClass(args),
        "add_attribute" => AddAttribute(args),
        "add_operation" => AddOperation(args),
        "remove_member" => RemoveMember(args),
        "add_relationship" => AddRelationship(args),
        "remove_relationship" => RemoveRelationship(args),
        "add_note" => AddNote(args),
        _ => null,
    };

    private string AddClass(JsonObject args)
    {
        var name = Required(args, "name");
        if (_model.Classes.Any(c => c.Name == name))
        {
            throw new ToolException($"There is already a class named {name}.");
        }

        var cls = new UmlClass { Name = name, Kind = Kind(Optional(args, "kind")) ?? ClassifierKind.Class, Stereotype = Stereotype(Optional(args, "stereotype")) };
        _model.Classes.Add(cls);
        Document.Layout.Add(new ElementLayout { ElementId = cls.Id, Width = 160 });
        Added.Add(cls.Id);
        return $"Added {Describe(cls)}.";
    }

    private string SetClass(JsonObject args)
    {
        var cls = Class(Required(args, "class"));
        if (Optional(args, "kind") is { } kind)
        {
            cls.Kind = Kind(kind) ?? throw new ToolException($"Unknown kind {kind}.");
        }

        if (args.ContainsKey("stereotype"))
        {
            cls.Stereotype = Stereotype(Optional(args, "stereotype"));
        }

        return $"{cls.Name} is now {Describe(cls)}.";
    }

    private string RenameClass(JsonObject args)
    {
        var cls = Class(Required(args, "class"));
        var name = Required(args, "new_name");
        if (_model.Classes.Any(c => c != cls && c.Name == name))
        {
            throw new ToolException($"There is already a class named {name}.");
        }

        var old = cls.Name;
        cls.Name = name;
        return $"Renamed {old} to {name}.";
    }

    private string DeleteClass(JsonObject args)
    {
        var cls = Class(Required(args, "class"));
        foreach (var id in _model.RemoveElement(cls.Id))
        {
            Document.Layout.Remove(id);
        }

        Added.Remove(cls.Id);
        return $"Deleted {cls.Name}.";
    }

    private string AddAttribute(JsonObject args)
    {
        var cls = Class(Required(args, "class"));
        var text = Required(args, "attribute");
        var attribute = UmlNotation.ParseAttribute(text,
            cls.Kind == ClassifierKind.Enumeration ? UmlVisibility.Public : UmlVisibility.Private)
            ?? throw new ToolException($"\"{text}\" is not an attribute in UML notation, e.g. -total: decimal.");
        if (cls.Attributes.Any(a => a.Name == attribute.Name))
        {
            throw new ToolException($"{cls.Name} already has an attribute named {attribute.Name}.");
        }

        cls.Attributes.Add(attribute);
        return $"Added {UmlNotation.Format(attribute)} to {cls.Name}.";
    }

    private string AddOperation(JsonObject args)
    {
        var cls = Class(Required(args, "class"));
        var text = Required(args, "operation");
        var operation = UmlNotation.ParseOperation(text)
            ?? throw new ToolException($"\"{text}\" is not an operation in UML notation, e.g. +pay(amount: decimal): bool.");
        if (cls.Kind == ClassifierKind.Interface)
        {
            operation.IsAbstract = false;
        }

        cls.Operations.Add(operation);
        return $"Added {UmlNotation.Format(operation)} to {cls.Name}.";
    }

    private string RemoveMember(JsonObject args)
    {
        var cls = Class(Required(args, "class"));
        var name = Required(args, "name");
        var removed = cls.Attributes.RemoveAll(a => a.Name == name) + cls.Operations.RemoveAll(o => o.Name == name);
        return removed > 0
            ? $"Removed {name} from {cls.Name}."
            : throw new ToolException($"{cls.Name} has no member named {name}. Its members are: {Members(cls)}.");
    }

    private string AddRelationship(JsonObject args)
    {
        var source = Class(Required(args, "source"));
        var target = Class(Required(args, "target"));
        var kind = RelationshipKindOf(Required(args, "kind"));
        if (source == target)
        {
            throw new ToolException("A relationship needs two different classes.");
        }

        if (kind == RelationshipKind.Realization && target.Kind != ClassifierKind.Interface)
        {
            throw new ToolException($"Only an interface can be realized; {target.Name} is not one. Use generalization for a base class.");
        }

        if (_model.Relationships.Any(r => r.SourceId == source.Id && r.TargetId == target.Id && r.Kind == kind))
        {
            throw new ToolException($"{source.Name} already has that relationship with {target.Name}.");
        }

        _model.Relationships.Add(new UmlRelationship
        {
            Kind = kind,
            SourceId = source.Id,
            TargetId = target.Id,
            Label = Optional(args, "label"),
            SourceMultiplicity = Optional(args, "source_multiplicity"),
            TargetMultiplicity = Optional(args, "target_multiplicity"),
        });
        return $"Added {Names[kind]} from {source.Name} to {target.Name}.";
    }

    private string RemoveRelationship(JsonObject args)
    {
        var source = Class(Required(args, "source"));
        var target = Class(Required(args, "target"));
        RelationshipKind? kind = Optional(args, "kind") is { } k ? RelationshipKindOf(k) : null;
        var matching = _model.Relationships
            .Where(r => ((r.SourceId == source.Id && r.TargetId == target.Id) || (r.SourceId == target.Id && r.TargetId == source.Id))
                        && (kind is null || r.Kind == kind))
            .Select(r => r.Id).ToList();
        if (matching.Count == 0)
        {
            throw new ToolException($"There is no such relationship between {source.Name} and {target.Name}.");
        }

        foreach (var id in matching.SelectMany(_model.RemoveElement))
        {
            Document.Layout.Remove(id);
        }

        return $"Removed {matching.Count} relationship(s) between {source.Name} and {target.Name}.";
    }

    private string AddNote(JsonObject args)
    {
        var target = Optional(args, "attach_to") is { } name ? Class(name) : null;
        return AddNote(Required(args, "text"), target, target?.Name);
    }

    /// <summary>The class with a name: exactly, else ignoring case when only one matches.</summary>
    private UmlClass Class(string name)
    {
        var exact = _model.Classes.Where(c => c.Name == name).ToList();
        if (exact.Count == 1)
        {
            return exact[0];
        }

        var loose = _model.Classes.Where(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return loose.Count == 1 ? loose[0]
            : exact.Count + loose.Count > 1 ? throw new ToolException($"More than one class is named {name}.")
            : throw new ToolException($"There is no class named {name}. The classes are: {string.Join(", ", _model.Classes.Select(c => c.Name))}.");
    }

    private static ClassifierKind? Kind(string? kind) => kind switch
    {
        null => null,
        "class" => ClassifierKind.Class,
        "abstract_class" => ClassifierKind.AbstractClass,
        "interface" => ClassifierKind.Interface,
        "enumeration" => ClassifierKind.Enumeration,
        _ => throw new ToolException($"Unknown kind {kind}; use class, abstract_class, interface or enumeration."),
    };

    private static string? Stereotype(string? text) => text?.Trim('«', '»', '<', '>', ' ') is { Length: > 0 } s ? s : null;

    private static RelationshipKind RelationshipKindOf(string kind) => kind switch
    {
        "association" => RelationshipKind.Association,
        "directed_association" => RelationshipKind.DirectedAssociation,
        "generalization" or "inheritance" => RelationshipKind.Inheritance,
        "realization" => RelationshipKind.Realization,
        "aggregation" => RelationshipKind.Aggregation,
        "composition" => RelationshipKind.Composition,
        "dependency" => RelationshipKind.Dependency,
        _ => throw new ToolException($"Unknown relationship kind {kind}."),
    };

    private static readonly Dictionary<RelationshipKind, string> Names = new()
    {
        [RelationshipKind.Association] = "an association",
        [RelationshipKind.DirectedAssociation] = "a directed association",
        [RelationshipKind.Inheritance] = "a generalization",
        [RelationshipKind.Realization] = "a realization",
        [RelationshipKind.Aggregation] = "an aggregation",
        [RelationshipKind.Composition] = "a composition",
        [RelationshipKind.Dependency] = "a dependency",
    };

    private static string Describe(UmlClass cls) => cls.Kind switch
    {
        ClassifierKind.AbstractClass => $"abstract class {cls.Name}",
        ClassifierKind.Interface => $"interface {cls.Name}",
        ClassifierKind.Enumeration => $"enumeration {cls.Name}",
        _ => $"class {cls.Name}",
    } + (cls.Stereotype is { } s ? $" «{s}»" : string.Empty);

    private static string Members(UmlClass cls) =>
        string.Join(", ", cls.Attributes.Select(a => a.Name).Concat(cls.Operations.Select(o => o.Name))) is { Length: > 0 } members ? members : "none";
}
