using System.Text.RegularExpressions;
using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration.Kotlin;

/// <summary>
/// Kotlin: one file per type, <c>Order.kt</c>, in folders that follow the package. Attributes and association ends
/// become properties, initialized as Kotlin requires (a zero value, an empty collection, <c>null</c> for optional ends,
/// otherwise <c>lateinit</c>); static members go into a <c>companion object</c>. Classes that others extend are
/// <c>open</c>; operations call <c>TODO()</c>; enumeration constants are UPPER_SNAKE_CASE.
/// </summary>
internal sealed partial class KotlinWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    /// <summary>Hard keywords, which cannot be names unless quoted with backticks.</summary>
    private static readonly HashSet<string> Keywords =
    [
        "as", "break", "class", "continue", "do", "else", "false", "for", "fun", "if", "in", "interface", "is", "null",
        "object", "package", "return", "super", "this", "throw", "true", "try", "typealias", "typeof", "val", "var", "when",
        "while",
    ];

    private const string Todo = "TODO(\"Not implemented\")";

    private readonly SortedSet<string> _imports = new(StringComparer.Ordinal);

    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        _imports.Clear();
        var body = new CodeWriter();
        WriteType(body, type);

        var package = Naming.NamespaceParts(Options.Namespace).Select(p => Escape(p.ToLowerInvariant())).ToList();
        var file = new CodeWriter();
        file.Line("// " + GeneratedBy);
        if (package.Count > 0)
        {
            file.Blank().Line($"package {string.Join('.', package)}");
        }

        file.Blank();
        foreach (var import in _imports)
        {
            file.Line($"import {import}");
        }

        var folder = package.Count > 0 ? string.Join('/', package.Select(p => p.Trim('`'))) + "/" : string.Empty;
        yield return new GeneratedFile($"{folder}{type.Name}.kt", file + "\n" + body);
    }

    private void WriteType(CodeWriter w, TypeModel type)
    {
        WriteDocumentation(w, type.Documentation);
        if (type.IsEnum)
        {
            WriteEnum(w, type);
            return;
        }

        string header;
        if (type.IsInterface)
        {
            header = $"interface {type.Name}" + (type.Interfaces.Count > 0 ? " : " + string.Join(", ", type.Interfaces.Select(t => t.Name)) : string.Empty);
        }
        else
        {
            // A base class is called in the header, unless the class has constructors, which call it themselves.
            var hasConstructors = type.Operations.Any(o => o.IsConstructor);
            var supertypes = type.BaseClasses.Select(b => hasConstructors ? b.Name : b.Name + "()").Concat(type.Interfaces.Select(i => i.Name)).ToList();
            var modifier = type.IsAbstract ? "abstract " : type.IsBase ? "open " : string.Empty;
            header = $"{modifier}class {type.Name}" + (supertypes.Count > 0 ? " : " + string.Join(", ", supertypes) : string.Empty);
        }

        using (w.Block(header))
        {
            WriteMembers(w, type);
        }
    }

    private void WriteEnum(CodeWriter w, TypeModel type)
    {
        var literals = type.Class.Attributes;
        var values = literals.Select(l => l.DefaultValue?.Trim()).ToList();
        string? valueType = null;
        if (values.Count > 0 && values.All(v => v is not null && IntegerLiteral().IsMatch(v)))
        {
            valueType = "Int";
        }
        else if (values.Count > 0 && values.All(v => v is { Length: >= 2 } && v[0] is '"' or '\'' && v[^1] == v[0]))
        {
            valueType = "String";
            values = values.Select(v => (string?)("\"" + v![1..^1].Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$") + "\"")).ToList();
        }
        else if (values.Any(v => !string.IsNullOrEmpty(v)))
        {
            Warnings.Add($"{type.Name}: the values of its Kotlin enumeration constants must be all numbers or all strings; the values in the diagram are not generated.");
        }

        using (w.Block($"enum class {type.Name}" + (valueType is null ? string.Empty : $"(val value: {valueType})")))
        {
            for (var i = 0; i < literals.Count; i++)
            {
                var name = Naming.Cased(literals[i].Name, Naming.UpperSnake) + (valueType is null ? string.Empty : $"({values[i]})");
                var last = i == literals.Count - 1;
                w.Line(name + (last ? (type.Operations.Count > 0 ? ";" : string.Empty) : ","));
            }

            if (literals.Count == 0 && type.Operations.Count > 0)
            {
                w.Line(";");
            }

            WriteMembers(w, type);
        }
    }

    private void WriteMembers(CodeWriter w, TypeModel type)
    {
        if (!type.IsEnum)
        {
            foreach (var attribute in type.Attributes.Where(a => !a.Attribute.IsStatic))
            {
                WriteAttribute(w, type, attribute);
            }
        }

        w.Blank();
        foreach (var end in type.Ends)
        {
            WriteEnd(w, type, end);
        }

        foreach (var operation in ConstructorsFirst(type).Where(o => !o.IsStatic))
        {
            w.Blank();
            WriteOperation(w, type, operation);
        }

        // Static members belong to the companion object.
        var statics = type.IsEnum ? [] : type.Attributes.Where(a => a.Attribute.IsStatic).ToList();
        var staticOperations = type.Operations.Where(o => o.IsStatic).ToList();
        if (statics.Count == 0 && staticOperations.Count == 0)
        {
            return;
        }

        w.Blank();
        using (w.Block("companion object"))
        {
            foreach (var attribute in statics)
            {
                WriteAttribute(w, type, attribute, inCompanion: true);
            }

            foreach (var operation in staticOperations)
            {
                w.Blank();
                WriteOperation(w, type, operation);
            }
        }
    }

    private void WriteAttribute(CodeWriter w, TypeModel owner, AttributeModel model, bool inCompanion = false)
    {
        var attribute = model.Attribute;
        var name = MemberName(attribute.Name);
        var type = TypeName(attribute.Type);
        var hasDefault = !string.IsNullOrWhiteSpace(attribute.DefaultValue);
        if (owner.IsInterface && !inCompanion)
        {
            // An abstract property, which the classes that implement the interface override.
            if (attribute.Visibility != UmlVisibility.Public)
            {
                Warnings.Add($"{owner.Name}.{attribute.Name}: Kotlin interface properties are public; it is generated as public.");
            }

            if (hasDefault)
            {
                Warnings.Add($"{owner.Name}.{attribute.Name}: Kotlin interface properties cannot have initial values; {attribute.DefaultValue!.Trim()} is not generated.");
            }

            w.Line($"val {name}: {type}");
            return;
        }

        if (owner.IsInterface && !hasDefault)
        {
            Warnings.Add($"{owner.Name}.{attribute.Name}: static properties of Kotlin interfaces need a value; the attribute has none and is not generated.");
            return;
        }

        var modifiers = model.IsImplicit ? "override " : Visibility(attribute.Visibility);
        if (hasDefault)
        {
            var value = Default(attribute);
            w.Line($"{modifiers}var {name}: {type}{(value == "null" ? "?" : string.Empty)} = {value}");
        }
        else if (ZeroValue(attribute.Type) is { } zero)
        {
            w.Line($"{modifiers}var {name}: {type} = {zero}");
        }
        else
        {
            // Assigned before use, e.g. by a constructor.
            w.Line($"{modifiers}lateinit var {name}: {type}");
        }
    }

    private void WriteEnd(CodeWriter w, TypeModel owner, AssociationEnd end)
    {
        if (owner.IsInterface)
        {
            Warnings.Add($"{owner.Name}: Kotlin interfaces cannot have state; the association with {end.Target.Name} is not generated.");
            return;
        }

        if (end.Label is { } label)
        {
            w.Line($"/** {Comment(label)} */");
        }

        var name = MemberName(end.Name);
        if (end.IsMany)
        {
            w.Line($"private val {name}: MutableList<{end.Target.Name}> = mutableListOf()");
        }
        else if (end.IsOptional)
        {
            w.Line($"private var {name}: {end.Target.Name}? = null");
        }
        else
        {
            w.Line($"private lateinit var {name}: {end.Target.Name}");
        }
    }

    private void WriteOperation(CodeWriter w, TypeModel owner, OperationModel model)
    {
        var operation = model.Operation;
        var parameters = string.Join(", ", operation.Parameters.Select(p => $"{MemberName(p.Name)}: {TypeName(p.Type)}"));
        if (model.IsConstructor)
        {
            if (owner.IsInterface)
            {
                return;
            }

            var delegation = owner.BaseClasses.Count > 0 ? " : super()" : string.Empty;
            using (w.Block($"{Visibility(operation.Visibility)}constructor({parameters}){delegation}"))
            {
            }

            return;
        }

        var returnType = string.IsNullOrWhiteSpace(operation.ReturnType) || BuiltIn(operation.ReturnType.Trim()) == "void"
            ? string.Empty
            : ": " + TypeName(operation.ReturnType);
        var name = MemberName(operation.Name);
        if (owner.IsInterface && !model.IsStatic)
        {
            w.Line($"fun {name}({parameters}){returnType}");
            return;
        }

        var visibility = model.ImplementsInterface ? UmlVisibility.Public : operation.Visibility;
        if (model.IsAbstract && visibility == UmlVisibility.Private)
        {
            Warnings.Add($"{owner.Name}.{operation.Name}: Kotlin abstract functions cannot be private; it is generated as protected.");
            visibility = UmlVisibility.Protected;
        }

        var overrides = model.IsOverride || model.ImplementsInterface;
        var modifiers = Visibility(visibility) +
                        (model.IsAbstract ? "abstract " : model.IsVirtual && !overrides ? "open " : string.Empty) +
                        (overrides ? "override " : string.Empty);
        var signature = $"{modifiers}fun {name}({parameters}){returnType}";
        if (model.IsAbstract)
        {
            w.Line(signature);
            return;
        }

        using (w.Block(signature))
        {
            w.Line(Todo);
        }
    }

    private static void WriteDocumentation(CodeWriter w, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        w.Line("/**");
        foreach (var line in lines)
        {
            w.Line(line.Length == 0 ? " *" : " * " + Comment(line));
        }

        w.Line(" */");
    }

    /// <summary>The Kotlin spelling of a type.</summary>
    private string TypeName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Any";
        }

        return TypeExpression.Parse(text) is { } type ? TypeName(type) : text.Trim();
    }

    private string TypeName(TypeExpression type)
    {
        var name = Model.Find(type.Name)?.Name ?? BuiltIn(type.Name) switch
        {
            "string" => "String",
            "int" => "Int",
            "long" => "Long",
            "short" => "Short",
            "byte" => "Byte",
            "double" => "Double",
            "float" => "Float",
            "bool" => "Boolean",
            "char" => "Char",
            "void" => "Unit",
            "object" => "Any",
            "decimal" => Import("java.math.BigDecimal"),
            "datetime" => Import("java.time.LocalDateTime"),
            "guid" => Import("java.util.UUID"),
            "list" => "MutableList",
            "set" => "MutableSet",
            "map" => "MutableMap",
            _ => type.Name,
        };

        if (type.Arguments.Count > 0)
        {
            name += "<" + string.Join(", ", type.Arguments.Select(TypeName)) + ">";
        }
        else if (name is "MutableList" or "MutableSet")
        {
            name += "<Any>";
        }
        else if (name == "MutableMap")
        {
            name += "<String, Any>";
        }

        for (var i = 0; i < type.ArrayRank; i++)
        {
            name = $"Array<{name}>";
        }

        return name;
    }

    /// <summary>
    /// The initial value of a property without one in the diagram: zero for numbers, false, an empty collection; null
    /// for other types (strings, classes, arrays), which are <c>lateinit</c> instead, as Kotlin allows for them.
    /// </summary>
    private string? ZeroValue(string? text)
    {
        if (TypeExpression.Parse(text) is not { ArrayRank: 0 } type || Model.Find(type.Name) is not null)
        {
            return null;
        }

        return BuiltIn(type.Name) switch
        {
            "int" or "short" or "byte" => "0",
            "long" => "0L",
            "double" => "0.0",
            "float" => "0f",
            "bool" => "false",
            "char" => "'\\u0000'",
            "list" => "mutableListOf()",
            "set" => "mutableSetOf()",
            "map" => "mutableMapOf()",
            _ => null,
        };
    }

    /// <summary>A default value as Kotlin needs it: typed number literals, double-quoted strings, enumeration constants.</summary>
    private string Default(UmlAttribute attribute)
    {
        var value = AttributeDefault(attribute, "null", (enumeration, literal) => $"{enumeration.Name}.{Naming.Cased(literal.Name, Naming.UpperSnake)}");
        var type = TypeExpression.Parse(attribute.Type);
        if (type is not { ArrayRank: 0 } || Model.Find(type.Name) is not null)
        {
            return value;
        }

        var isInteger = IntegerLiteral().IsMatch(value);
        var isNumber = isInteger || DecimalLiteral().IsMatch(value);
        return BuiltIn(type.Name) switch
        {
            "double" when isInteger => value + ".0",
            "float" when isNumber => value + "f",
            "long" when isInteger => value + "L",
            "decimal" when isNumber => $"BigDecimal(\"{value}\")",
            "string" when value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' =>
                "\"" + value[1..^1].Replace("\"", "\\\"").Replace("$", "\\$") + "\"",
            _ => value,
        };
    }

    private string Import(string qualifiedName)
    {
        _imports.Add(qualifiedName);
        return qualifiedName[(qualifiedName.LastIndexOf('.') + 1)..];
    }

    private static string Visibility(UmlVisibility visibility) => visibility switch
    {
        UmlVisibility.Private => "private ",
        UmlVisibility.Protected => "protected ",
        UmlVisibility.Package => "internal ",
        _ => string.Empty,
    };

    private static string MemberName(string name) => Escape(Naming.Cased(name, Naming.Camel));

    /// <summary>Kotlin quotes a keyword used as a name with backticks.</summary>
    private static string Escape(string name) => Keywords.Contains(name) ? $"`{name}`" : name;

    private static string Comment(string text) => text.Replace("*/", "*&#47;");

    [GeneratedRegex(@"^-?\d+$")]
    private static partial Regex IntegerLiteral();

    [GeneratedRegex(@"^-?\d+\.\d+$")]
    private static partial Regex DecimalLiteral();
}
