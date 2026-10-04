using System.Security;
using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration.CSharp;

/// <summary>
/// C#: one file per type in a file-scoped namespace. Public, protected and internal attributes become auto-properties,
/// private ones fields; association ends become private fields; operations throw <c>NotImplementedException</c>.
/// </summary>
internal sealed class CSharpWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
        "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while",
    ];

    private readonly SortedSet<string> _usings = new(StringComparer.Ordinal);

    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        _usings.Clear();
        var body = new CodeWriter(braceOnOwnLine: true);
        WriteType(body, type);

        var file = new CodeWriter();
        file.Line("// " + GeneratedBy).Blank();
        foreach (var ns in _usings)
        {
            file.Line($"using {ns};");
        }

        var parts = Naming.NamespaceParts(Options.Namespace);
        if (parts.Count > 0)
        {
            file.Blank().Line($"namespace {string.Join('.', parts.Select(Escape))};");
        }

        yield return new GeneratedFile(type.Name + ".cs", file + "\n" + body);
    }

    private void WriteType(CodeWriter w, TypeModel type)
    {
        WriteDocumentation(w, type.Documentation);
        if (type.IsEnum)
        {
            WriteEnum(w, type);
            return;
        }

        var bases = type.BaseClasses.Concat(type.Interfaces).Select(t => t.Name).ToList();
        var inheritance = bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty;
        var keyword = type.IsInterface ? "interface" : type.IsAbstract ? "abstract class" : "class";
        using (w.Block($"public {keyword} {type.Name}{inheritance}"))
        {
            foreach (var attribute in type.Attributes)
            {
                WriteAttribute(w, type, attribute);
            }

            w.Blank();
            foreach (var end in type.Ends)
            {
                WriteEnd(w, type, end);
            }

            foreach (var operation in ConstructorsFirst(type))
            {
                w.Blank();
                WriteOperation(w, type, operation);
            }
        }
    }

    private void WriteEnum(CodeWriter w, TypeModel type)
    {
        using (w.Block($"public enum {type.Name}"))
        {
            foreach (var literal in type.Class.Attributes)
            {
                var value = string.IsNullOrWhiteSpace(literal.DefaultValue) ? string.Empty : " = " + literal.DefaultValue.Trim();
                w.Line($"{Escape(Naming.Identifier(literal.Name))}{value},");
            }
        }

        if (type.Operations.Count > 0)
        {
            Warnings.Add($"{type.Name}: C# enumerations cannot have operations; {string.Join(", ", type.Operations.Select(o => o.Operation.Name))} not generated.");
        }
    }

    private void WriteAttribute(CodeWriter w, TypeModel owner, AttributeModel model)
    {
        var attribute = model.Attribute;
        var type = TypeName(attribute.Type);
        if (owner.IsInterface)
        {
            w.Line($"{(attribute.IsStatic ? "static " : string.Empty)}{type} {PropertyName(owner, attribute.Name)} {{ get; set; }}");
            return;
        }

        var initializer = string.IsNullOrWhiteSpace(attribute.DefaultValue) ? string.Empty : " = " + Default(attribute);
        var modifiers = (model.IsImplicit ? "public" : Visibility(attribute.Visibility)) + (attribute.IsStatic ? " static" : string.Empty);
        if (!model.IsImplicit && attribute.Visibility == UmlVisibility.Private)
        {
            w.Line($"{modifiers} {type} {FieldName(attribute.Name)}{initializer};");
        }
        else
        {
            w.Line($"{modifiers} {type} {PropertyName(owner, attribute.Name)} {{ get; set; }}{(initializer.Length > 0 ? initializer + ";" : string.Empty)}");
        }
    }

    private void WriteEnd(CodeWriter w, TypeModel owner, AssociationEnd end)
    {
        if (end.Label is { } label)
        {
            w.Line($"/// <summary>{Xml(label)}</summary>");
        }

        var type = end.IsMany ? Generic("List", end.Target.Name) : end.Target.Name;
        if (owner.IsInterface)
        {
            w.Line($"{type} {PropertyName(owner, end.Name)} {{ get; set; }}");
        }
        else
        {
            w.Line($"private {type} {FieldName(end.Name)}{(end.IsMany ? " = new()" : string.Empty)};");
        }
    }

    private void WriteOperation(CodeWriter w, TypeModel owner, OperationModel model)
    {
        var operation = model.Operation;
        var parameters = string.Join(", ", operation.Parameters.Select(p => $"{TypeName(p.Type)} {ParameterName(p.Name)}"));
        if (model.IsConstructor)
        {
            using (w.Block($"{Visibility(operation.Visibility)} {owner.Name}({parameters})"))
            {
            }

            return;
        }

        var returnType = string.IsNullOrWhiteSpace(operation.ReturnType) ? "void" : TypeName(operation.ReturnType);
        var name = MethodName(owner, operation.Name);
        if (owner.IsInterface && !model.IsStatic)
        {
            w.Line($"{returnType} {name}({parameters});");
            return;
        }

        var visibility = model.ImplementsInterface || owner.IsInterface ? UmlVisibility.Public : operation.Visibility;
        if (model.IsAbstract && visibility == UmlVisibility.Private)
        {
            Warnings.Add($"{owner.Name}.{operation.Name}: C# abstract members cannot be private; it is generated as protected.");
            visibility = UmlVisibility.Protected;
        }

        var modifiers = new List<string> { Visibility(visibility) };
        if (model.IsStatic)
        {
            modifiers.Add("static");
        }
        else if (model.IsAbstract)
        {
            modifiers.Add(model.IsOverride ? "abstract override" : "abstract");
        }
        else if (model.IsOverride)
        {
            modifiers.Add("override");
        }
        else if (model.IsVirtual)
        {
            modifiers.Add("virtual");
        }

        var signature = $"{string.Join(' ', modifiers)} {returnType} {name}({parameters})";
        if (model.IsAbstract && !model.IsStatic)
        {
            w.Line(signature + ";");
            return;
        }

        _usings.Add("System");
        using (w.Block(signature))
        {
            w.Line("throw new NotImplementedException();");
        }
    }

    private static void WriteDocumentation(CodeWriter w, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        w.Line("/// <summary>");
        foreach (var line in lines)
        {
            w.Line(line.Length == 0 ? "///" : "/// " + Xml(line));
        }

        w.Line("/// </summary>");
    }

    /// <summary>The C# spelling of a type from the diagram; <c>object</c> when it is missing.</summary>
    private string TypeName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "object";
        }

        return TypeExpression.Parse(text) is { } type ? TypeName(type) : text.Trim();
    }

    private string TypeName(TypeExpression type)
    {
        var name = Model.Find(type.Name)?.Name ?? BuiltIn(type.Name) switch
        {
            "string" or "int" or "long" or "short" or "byte" or "double" or "float" or "decimal" or "bool" or "char" or "void" or "object" =>
                BuiltIn(type.Name)!,
            "datetime" => Using("System", "DateTime"),
            "guid" => Using("System", "Guid"),
            "list" => Using("System.Collections.Generic", "List"),
            "set" => Using("System.Collections.Generic", "HashSet"),
            "map" => Using("System.Collections.Generic", "Dictionary"),
            _ => type.Name,
        };

        if (type.Arguments.Count > 0)
        {
            name += "<" + string.Join(", ", type.Arguments.Select(TypeName)) + ">";
        }

        return name + string.Concat(Enumerable.Repeat("[]", type.ArrayRank));
    }

    private string Generic(string collection, string element) => $"{Using("System.Collections.Generic", collection)}<{element}>";

    private string Using(string ns, string name)
    {
        _usings.Add(ns);
        return name;
    }

    private string Default(UmlAttribute attribute) =>
        AttributeDefault(attribute, "null", (enumeration, literal) => $"{enumeration.Name}.{Escape(Naming.Identifier(literal.Name))}");

    private static string Visibility(UmlVisibility visibility) => visibility switch
    {
        UmlVisibility.Public => "public",
        UmlVisibility.Protected => "protected",
        UmlVisibility.Package => "internal",
        _ => "private",
    };

    /// <summary>PascalCase; a member cannot be named like its class, so such a name gets "Value" appended.</summary>
    private static string PropertyName(TypeModel owner, string name)
    {
        var pascal = Naming.Cased(name, Naming.Pascal);
        return pascal == owner.Name ? pascal + "Value" : Escape(pascal);
    }

    private static string MethodName(TypeModel owner, string name)
    {
        var pascal = Naming.Cased(name, Naming.Pascal);
        return pascal == owner.Name ? pascal + "Operation" : Escape(pascal);
    }

    private static string FieldName(string name) => "_" + Naming.Cased(name, Naming.Camel);

    private static string ParameterName(string name) => Escape(Naming.Cased(name, Naming.Camel));

    private static string Escape(string name) => Keywords.Contains(name) ? "@" + name : name;

    private static string Xml(string text) => SecurityElement.Escape(text) ?? string.Empty;
}
