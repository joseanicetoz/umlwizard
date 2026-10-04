using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration.Java;

/// <summary>
/// Java: one file per type, in folders that follow the package. Attributes and association ends become fields;
/// operations throw <c>UnsupportedOperationException</c>; enumeration literals are UPPER_SNAKE_CASE.
/// </summary>
internal sealed class JavaWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    private static readonly HashSet<string> Keywords =
    [
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do",
        "double", "else", "enum", "extends", "final", "finally", "float", "for", "goto", "if", "implements", "import",
        "instanceof", "int", "interface", "long", "native", "new", "package", "private", "protected", "public", "return",
        "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try", "void",
        "volatile", "while", "true", "false", "null", "var", "record", "yield",
    ];

    private readonly SortedSet<string> _imports = new(StringComparer.Ordinal);

    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        _imports.Clear();
        var body = new CodeWriter();
        WriteType(body, type);

        var package = Naming.NamespaceParts(Options.Namespace).Select(p => AvoidKeyword(p.ToLowerInvariant(), Keywords)).ToList();
        var file = new CodeWriter();
        file.Line("// " + GeneratedBy);
        if (package.Count > 0)
        {
            file.Blank().Line($"package {string.Join('.', package)};");
        }

        file.Blank();
        foreach (var import in _imports)
        {
            file.Line($"import {import};");
        }

        var folder = package.Count > 0 ? string.Join('/', package) + "/" : string.Empty;
        yield return new GeneratedFile($"{folder}{type.Name}.java", file + "\n" + body);
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
            header = $"public interface {type.Name}" + List(" extends ", type.Interfaces);
        }
        else
        {
            header = $"public {(type.IsAbstract ? "abstract " : string.Empty)}class {type.Name}" +
                     List(" extends ", type.BaseClasses) + List(" implements ", type.Interfaces);
        }

        using (w.Block(header))
        {
            WriteMembers(w, type);
        }

        static string List(string keyword, List<TypeModel> types) =>
            types.Count == 0 ? string.Empty : keyword + string.Join(", ", types.Select(t => t.Name));
    }

    private void WriteEnum(CodeWriter w, TypeModel type)
    {
        using (w.Block($"public enum {type.Name}"))
        {
            var literals = type.Class.Attributes;
            if (literals.Count == 0 && type.Operations.Count > 0)
            {
                w.Line(";");
            }

            for (var i = 0; i < literals.Count; i++)
            {
                w.Line(AvoidKeyword(Naming.Cased(literals[i].Name, Naming.UpperSnake), Keywords) + (i == literals.Count - 1 ? ";" : ","));
            }

            if (literals.Any(l => !string.IsNullOrWhiteSpace(l.DefaultValue)))
            {
                Warnings.Add($"{type.Name}: Java enumeration constants have no values; the values in the diagram are not generated.");
            }

            WriteMembers(w, type);
        }
    }

    private void WriteMembers(CodeWriter w, TypeModel type)
    {
        if (!type.IsEnum)
        {
            foreach (var attribute in type.Attributes.Where(a => !a.IsImplicit))
            {
                WriteAttribute(w, type, attribute.Attribute);
            }
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

    private void WriteAttribute(CodeWriter w, TypeModel owner, UmlAttribute attribute)
    {
        var initializer = string.IsNullOrWhiteSpace(attribute.DefaultValue) ? string.Empty : " = " + Default(attribute);
        if (owner.IsInterface)
        {
            // Interface fields are constants: public, static, final and initialized.
            if (initializer.Length == 0)
            {
                Warnings.Add($"{owner.Name}.{attribute.Name}: Java interfaces can only have constants; the attribute has no value and is not generated.");
                return;
            }

            w.Line($"{TypeName(attribute.Type, boxed: false)} {Naming.Cased(attribute.Name, Naming.UpperSnake)}{initializer};");
            return;
        }

        var modifiers = Modifiers(Visibility(attribute.Visibility), attribute.IsStatic ? "static" : null);
        w.Line($"{modifiers}{TypeName(attribute.Type, boxed: false)} {MemberName(attribute.Name)}{initializer};");
    }

    private void WriteEnd(CodeWriter w, TypeModel owner, AssociationEnd end)
    {
        if (owner.IsInterface)
        {
            Warnings.Add($"{owner.Name}: Java interfaces cannot have fields; the association with {end.Target.Name} is not generated.");
            return;
        }

        if (end.Label is { } label)
        {
            w.Line($"/** {Comment(label)} */");
        }

        if (end.IsMany)
        {
            _imports.Add("java.util.List");
            _imports.Add("java.util.ArrayList");
            w.Line($"private List<{end.Target.Name}> {MemberName(end.Name)} = new ArrayList<>();");
        }
        else
        {
            w.Line($"private {end.Target.Name} {MemberName(end.Name)};");
        }
    }

    private void WriteOperation(CodeWriter w, TypeModel owner, OperationModel model)
    {
        var operation = model.Operation;
        var parameters = string.Join(", ", operation.Parameters.Select(p => $"{TypeName(p.Type, boxed: false)} {MemberName(p.Name)}"));
        if (model.IsConstructor)
        {
            if (owner.IsInterface)
            {
                return;
            }

            using (w.Block($"{Modifiers(Visibility(operation.Visibility))}{owner.Name}({parameters})"))
            {
            }

            return;
        }

        var returnType = string.IsNullOrWhiteSpace(operation.ReturnType) ? "void" : TypeName(operation.ReturnType, boxed: false);
        var name = MemberName(operation.Name);
        if (owner.IsInterface)
        {
            if (!model.IsStatic)
            {
                w.Line($"{returnType} {name}({parameters});");
                return;
            }

            using (w.Block($"static {returnType} {name}({parameters})"))
            {
                w.Line("throw new UnsupportedOperationException(\"Not implemented\");");
            }

            return;
        }

        var visibility = model.ImplementsInterface ? UmlVisibility.Public : operation.Visibility;
        if (model.IsAbstract && visibility == UmlVisibility.Private)
        {
            Warnings.Add($"{owner.Name}.{operation.Name}: Java abstract methods cannot be private; it is generated as protected.");
            visibility = UmlVisibility.Protected;
        }

        if (model.IsOverride || model.ImplementsInterface)
        {
            w.Line("@Override");
        }

        var signature = $"{Modifiers(Visibility(visibility), model.IsStatic ? "static" : null, model.IsAbstract ? "abstract" : null)}{returnType} {name}({parameters})";
        if (model.IsAbstract)
        {
            w.Line(signature + ";");
            return;
        }

        using (w.Block(signature))
        {
            w.Line("throw new UnsupportedOperationException(\"Not implemented\");");
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
            w.Line(line.Length == 0 ? " * <p>" : " * " + Comment(line));
        }

        w.Line(" */");
    }

    /// <summary>The Java spelling of a type; primitives are boxed inside type arguments.</summary>
    private string TypeName(string? text, bool boxed)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Object";
        }

        return TypeExpression.Parse(text) is { } type ? TypeName(type, boxed) : text.Trim();
    }

    private string TypeName(TypeExpression type, bool boxed)
    {
        var isArray = type.ArrayRank > 0;
        var name = Model.Find(type.Name)?.Name ?? BuiltIn(type.Name) switch
        {
            "string" => "String",
            "int" => boxed && !isArray ? "Integer" : "int",
            "long" => boxed && !isArray ? "Long" : "long",
            "short" => boxed && !isArray ? "Short" : "short",
            "byte" => boxed && !isArray ? "Byte" : "byte",
            "double" => boxed && !isArray ? "Double" : "double",
            "float" => boxed && !isArray ? "Float" : "float",
            "bool" => boxed && !isArray ? "Boolean" : "boolean",
            "char" => boxed && !isArray ? "Character" : "char",
            "void" => boxed ? "Void" : "void",
            "object" => "Object",
            "decimal" => Import("java.math.BigDecimal"),
            "datetime" => Import("java.time.LocalDateTime"),
            "guid" => Import("java.util.UUID"),
            "list" => Import("java.util.List"),
            "set" => Import("java.util.Set"),
            "map" => Import("java.util.Map"),
            _ => type.Name,
        };

        if (type.Arguments.Count > 0)
        {
            name += "<" + string.Join(", ", type.Arguments.Select(a => TypeName(a, boxed: true))) + ">";
        }

        return name + string.Concat(Enumerable.Repeat("[]", type.ArrayRank));
    }

    private string Import(string qualifiedName)
    {
        _imports.Add(qualifiedName);
        return qualifiedName[(qualifiedName.LastIndexOf('.') + 1)..];
    }

    private string Default(UmlAttribute attribute) =>
        AttributeDefault(attribute, "null", (enumeration, literal) =>
            $"{enumeration.Name}.{AvoidKeyword(Naming.Cased(literal.Name, Naming.UpperSnake), Keywords)}");

    private static string? Visibility(UmlVisibility visibility) => visibility switch
    {
        UmlVisibility.Public => "public",
        UmlVisibility.Protected => "protected",
        UmlVisibility.Private => "private",
        _ => null,
    };

    /// <summary>The modifiers that are set, each followed by a space.</summary>
    private static string Modifiers(params string?[] modifiers) => string.Concat(modifiers.Where(m => m is not null).Select(m => m + " "));

    private static string MemberName(string name) => AvoidKeyword(Naming.Cased(name, Naming.Camel), Keywords);

    private static string Comment(string text) => text.Replace("*/", "*&#47;");
}
