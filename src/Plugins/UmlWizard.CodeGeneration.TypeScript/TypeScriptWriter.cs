using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration.TypeScript;

/// <summary>
/// TypeScript: one ES module per type, <c>Order.ts</c>, in folders that follow the namespace, importing the other types
/// it uses (<c>import type</c> when it only uses them as types). Attributes and association ends become properties
/// (<c>!</c> when they have no initial value, so the code compiles with <c>strict</c>); operations throw
/// <c>Error</c>; enumerations are <c>enum</c>s with PascalCase members.
/// </summary>
internal sealed class TypeScriptWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    /// <summary>Reserved words, which cannot name parameters.</summary>
    private static readonly HashSet<string> Keywords =
    [
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "enum",
        "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null",
        "return", "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "implements",
        "interface", "let", "package", "private", "protected", "public", "static", "yield", "await",
    ];

    /// <summary>The types of the diagram the current file uses: true when it needs their value, not only their type.</summary>
    private readonly SortedDictionary<string, bool> _imports = new(StringComparer.Ordinal);

    private TypeModel _current = null!;

    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        _current = type;
        _imports.Clear();
        var body = new CodeWriter();
        WriteType(body, type);

        var file = new CodeWriter();
        file.Line("// " + GeneratedBy).Blank();
        foreach (var (name, isValue) in _imports)
        {
            file.Line($"import {(isValue ? string.Empty : "type ")}{{ {name} }} from './{name}';");
        }

        var folders = Naming.NamespaceParts(Options.Namespace).Select(p => Naming.Snake(p).Replace('_', '-')).ToList();
        var folder = folders.Count > 0 ? string.Join('/', folders) + "/" : string.Empty;
        yield return new GeneratedFile($"{folder}{type.Name}.ts", file.ToString().TrimEnd('\n') + "\n\n" + body);
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
            header = $"export interface {type.Name}" + List(" extends ", type.Interfaces, value: false);
        }
        else
        {
            header = $"export {(type.IsAbstract ? "abstract " : string.Empty)}class {type.Name}" +
                     List(" extends ", type.BaseClasses, value: true) + List(" implements ", type.Interfaces, value: false);
        }

        using (w.Block(header))
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

            var written = new HashSet<string>();
            foreach (var operation in ConstructorsFirst(type))
            {
                // TypeScript has one implementation per name: overloads would be duplicate functions.
                var name = operation.IsConstructor ? "constructor" : MemberName(operation.Operation.Name);
                if (!written.Add(name))
                {
                    Warnings.Add($"{type.Name}.{operation.Operation.Name}: TypeScript methods cannot be overloaded; only the first one is generated.");
                    continue;
                }

                w.Blank();
                WriteOperation(w, type, operation);
            }
        }

        string List(string keyword, List<TypeModel> types, bool value) =>
            types.Count == 0 ? string.Empty : keyword + string.Join(", ", types.Select(t => Use(t, value)));
    }

    private void WriteEnum(CodeWriter w, TypeModel type)
    {
        var literals = type.Class.Attributes;
        var values = literals.Select(l => EnumValue(type, l)).ToList();

        // After a string member, every member needs an initializer: give the others their own name.
        var hasStrings = values.Any(v => v?.StartsWith('\'') == true);
        using (w.Block($"export enum {type.Name}"))
        {
            for (var i = 0; i < literals.Count; i++)
            {
                var name = LiteralName(literals[i].Name);
                var value = values[i] ?? (hasStrings ? $"'{name}'" : null);
                w.Line(name + (value is null ? string.Empty : " = " + value) + ",");
            }
        }

        if (type.Operations.Count > 0)
        {
            Warnings.Add($"{type.Name}: TypeScript enumerations cannot have operations; {string.Join(", ", type.Operations.Select(o => o.Operation.Name))} not generated.");
        }
    }

    /// <summary>The value of an enumeration literal: a number or a string, as TypeScript allows; null for none.</summary>
    private string? EnumValue(TypeModel type, UmlAttribute literal)
    {
        var value = literal.DefaultValue?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number))
        {
            return value;
        }

        if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
        {
            return "'" + value[1..^1].Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        }

        Warnings.Add($"{type.Name}.{literal.Name}: TypeScript enumeration members are numbers or strings; the value {value} is not generated.");
        return null;
    }

    private void WriteAttribute(CodeWriter w, TypeModel owner, AttributeModel model)
    {
        var attribute = model.Attribute;
        var name = MemberName(attribute.Name);
        var type = TypeName(attribute.Type);
        if (owner.IsInterface)
        {
            if (attribute.IsStatic)
            {
                Warnings.Add($"{owner.Name}.{attribute.Name}: TypeScript interfaces cannot have static members; the attribute is not generated.");
                return;
            }

            if (attribute.Visibility != UmlVisibility.Public)
            {
                Warnings.Add($"{owner.Name}.{attribute.Name}: TypeScript interface members are public; it is generated as public.");
            }

            if (!string.IsNullOrWhiteSpace(attribute.DefaultValue))
            {
                Warnings.Add($"{owner.Name}.{attribute.Name}: TypeScript interfaces cannot have initial values; {attribute.DefaultValue.Trim()} is not generated.");
            }

            w.Line($"{name}: {type};");
            return;
        }

        // An attribute of an implemented interface is public; the others keep their visibility.
        var visibility = model.IsImplicit ? "public" : Visibility(owner, attribute.Name, attribute.Visibility);
        var modifiers = visibility + (attribute.IsStatic ? " static" : string.Empty);
        if (!string.IsNullOrWhiteSpace(attribute.DefaultValue))
        {
            w.Line($"{modifiers} {name}: {type} = {Default(attribute)};");
        }
        else
        {
            // Without a value the property is assigned elsewhere (e.g. by a constructor): "!" says so to strict mode.
            w.Line($"{modifiers} {name}{(attribute.IsStatic ? string.Empty : "!")}: {type};");
        }
    }

    private void WriteEnd(CodeWriter w, TypeModel owner, AssociationEnd end)
    {
        if (end.Label is { } label)
        {
            w.Line($"/** {Comment(label)} */");
        }

        var name = MemberName(end.Name);
        var target = Use(end.Target, value: false);
        if (owner.IsInterface)
        {
            w.Line(end.IsMany ? $"{name}: {target}[];" : end.IsOptional ? $"{name}?: {target};" : $"{name}: {target};");
        }
        else if (end.IsMany)
        {
            w.Line($"private {name}: {target}[] = [];");
        }
        else if (end.IsOptional)
        {
            w.Line($"private {name}: {target} | null = null;");
        }
        else
        {
            w.Line($"private {name}!: {target};");
        }
    }

    private void WriteOperation(CodeWriter w, TypeModel owner, OperationModel model)
    {
        var operation = model.Operation;
        var parameters = string.Join(", ", operation.Parameters.Select(p => $"{ParameterName(p.Name)}: {TypeName(p.Type)}"));
        if (model.IsConstructor)
        {
            if (owner.IsInterface)
            {
                return;
            }

            using (w.Block($"{Visibility(owner, operation.Name, operation.Visibility)} constructor({parameters})"))
            {
                if (owner.BaseClasses.Count > 0)
                {
                    w.Line("super();");
                }
            }

            return;
        }

        var returnType = string.IsNullOrWhiteSpace(operation.ReturnType) ? "void" : TypeName(operation.ReturnType);
        var name = MemberName(operation.Name);
        if (owner.IsInterface)
        {
            if (model.IsStatic)
            {
                Warnings.Add($"{owner.Name}.{operation.Name}: TypeScript interfaces cannot have static members; the operation is not generated.");
                return;
            }

            w.Line($"{name}({parameters}): {returnType};");
            return;
        }

        var visibility = model.ImplementsInterface ? UmlVisibility.Public : operation.Visibility;
        if (model.IsAbstract && visibility == UmlVisibility.Private)
        {
            Warnings.Add($"{owner.Name}.{operation.Name}: TypeScript abstract methods cannot be private; it is generated as protected.");
            visibility = UmlVisibility.Protected;
        }

        // TypeScript wants the modifiers in this order.
        var modifiers = Visibility(owner, operation.Name, visibility) +
                        (model.IsStatic ? " static" : string.Empty) +
                        (model.IsAbstract ? " abstract" : string.Empty) +
                        (model.IsOverride ? " override" : string.Empty);
        var signature = $"{modifiers} {name}({parameters}): {returnType}";
        if (model.IsAbstract)
        {
            w.Line(signature + ";");
            return;
        }

        using (w.Block(signature))
        {
            w.Line("throw new Error('Not implemented');");
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

    /// <summary>The TypeScript spelling of a type; types of the diagram are imported.</summary>
    private string TypeName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "unknown";
        }

        return TypeExpression.Parse(text) is { } type ? TypeName(type) : text.Trim();
    }

    private string TypeName(TypeExpression type)
    {
        var arrays = string.Concat(Enumerable.Repeat("[]", type.ArrayRank));
        if (Model.Find(type.Name) is { } own)
        {
            return Use(own, value: false) + Arguments(type) + arrays;
        }

        switch (BuiltIn(type.Name))
        {
            case "list":
                // An array of the element, in parentheses when it is a union or function type.
                var element = type.Arguments.Count > 0 ? TypeName(type.Arguments[^1]) : "unknown";
                return (element.Contains(' ') ? $"({element})" : element) + "[]" + arrays;
            case "set":
                return "Set" + Arguments(type, "unknown") + arrays;
            case "map":
                return "Map" + (type.Arguments.Count == 2 ? Arguments(type) : "<string, unknown>") + arrays;
        }

        var name = BuiltIn(type.Name) switch
        {
            "string" or "char" or "guid" => "string",
            "int" or "long" or "short" or "byte" or "double" or "float" or "decimal" => "number",
            "bool" => "boolean",
            "void" => "void",
            "object" => "unknown",
            "datetime" => "Date",
            _ => type.Name,
        };
        return name + Arguments(type) + arrays;
    }

    private string Arguments(TypeExpression type, string? empty = null) =>
        type.Arguments.Count > 0 ? "<" + string.Join(", ", type.Arguments.Select(TypeName)) + ">"
        : empty is null ? string.Empty : $"<{empty}>";

    /// <summary>The name of a type of the diagram, imported unless it is the current one.</summary>
    private string Use(TypeModel type, bool value)
    {
        if (type != _current)
        {
            _imports[type.Name] = value || _imports.GetValueOrDefault(type.Name);
        }

        return type.Name;
    }

    private string Default(UmlAttribute attribute) =>
        AttributeDefault(attribute, "null", (enumeration, literal) => $"{Use(enumeration, value: true)}.{LiteralName(literal.Name)}");

    private string Visibility(TypeModel owner, string member, UmlVisibility visibility)
    {
        switch (visibility)
        {
            case UmlVisibility.Private:
                return "private";
            case UmlVisibility.Protected:
                return "protected";
            case UmlVisibility.Package:
                Warnings.Add($"{owner.Name}.{member}: TypeScript has no package visibility; it is generated as public.");
                return "public";
            default:
                return "public";
        }
    }

    private static string MemberName(string name) => Naming.Cased(name, Naming.Camel);

    private static string ParameterName(string name) => AvoidKeyword(Naming.Cased(name, Naming.Camel), Keywords);

    private static string LiteralName(string name) => Naming.Cased(name, Naming.Pascal);

    private static string Comment(string text) => text.Replace("*/", "*&#47;");
}
