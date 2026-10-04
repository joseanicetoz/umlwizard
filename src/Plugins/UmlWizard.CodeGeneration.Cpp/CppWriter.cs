using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration.Cpp;

/// <summary>
/// C++17: a header per type, and a source file with the operation stubs when there are any. Classes of the diagram are
/// referred to by pointer (forward-declared), compositions own their parts through <c>std::unique_ptr</c>, enumerations
/// are <c>enum class</c>, interfaces are classes with pure virtual functions only.
/// </summary>
internal sealed class CppWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    private static readonly HashSet<string> Keywords =
    [
        "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char",
        "class", "compl", "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "decltype",
        "default", "delete", "do", "double", "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float",
        "for", "friend", "goto", "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
        "nullptr", "operator", "or", "or_eq", "private", "protected", "public", "register", "reinterpret_cast", "requires",
        "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast", "struct", "switch", "template", "this",
        "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using", "virtual", "void",
        "volatile", "wchar_t", "while", "xor", "xor_eq",
    ];

    private readonly SortedSet<string> _systemIncludes = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _includes = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _forwardDeclarations = new(StringComparer.Ordinal);

    private TypeModel _type = null!;

    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        _type = type;
        _systemIncludes.Clear();
        _includes.Clear();
        _forwardDeclarations.Clear();

        var body = new CodeWriter();
        WriteDocumentation(body, type.Documentation);
        if (type.IsEnum)
        {
            WriteEnum(body, type);
        }
        else
        {
            WriteClass(body, type);
        }

        yield return new GeneratedFile(type.Name + ".h", Header(body.ToString()));

        var definitions = type.Operations.Where(o => !o.IsAbstract && !(type.IsInterface && !o.IsStatic) && !type.IsEnum).ToList();
        if (definitions.Count > 0)
        {
            yield return new GeneratedFile(type.Name + ".cpp", Source(type, definitions));
        }
    }

    private string Header(string body)
    {
        var file = new CodeWriter();
        file.Line("// " + GeneratedBy).Line("#pragma once").Blank();
        foreach (var include in _systemIncludes)
        {
            file.Line($"#include <{include}>");
        }

        file.Blank();
        foreach (var include in _includes.Where(i => i != _type.Name))
        {
            file.Line($"#include \"{include}.h\"");
        }

        var parts = Namespace();
        if (parts.Length > 0)
        {
            file.Blank().Line($"namespace {parts} {{");
        }

        file.Blank();
        foreach (var name in _forwardDeclarations.Where(n => n != _type.Name && !_includes.Contains(n)))
        {
            file.Line($"class {name};");
        }

        var text = file + "\n" + body;
        return parts.Length > 0 ? text + $"\n}}  // namespace {parts}\n" : text;
    }

    private string Source(TypeModel type, List<OperationModel> definitions)
    {
        var w = new CodeWriter();
        w.Line("// " + GeneratedBy).Line($"#include \"{type.Name}.h\"").Blank();
        if (definitions.Any(d => !d.IsConstructor))
        {
            w.Line("#include <stdexcept>");
        }

        w.Blank();
        foreach (var dependency in type.Dependencies.Select(d => d.Name).Distinct().Where(d => d != type.Name).Order(StringComparer.Ordinal))
        {
            w.Line($"#include \"{dependency}.h\"");
        }

        var parts = Namespace();
        if (parts.Length > 0)
        {
            w.Blank().Line($"namespace {parts} {{");
        }

        foreach (var model in definitions)
        {
            var operation = model.Operation;
            var parameters = Parameters(operation);
            w.Blank();
            if (model.IsConstructor)
            {
                using (w.Block($"{type.Name}::{type.Name}({parameters})"))
                {
                }

                continue;
            }

            using (w.Block($"{ReturnType(operation)} {type.Name}::{MemberName(operation.Name)}({parameters})"))
            {
                w.Line("throw std::logic_error(\"Not implemented\");");
            }
        }

        if (parts.Length > 0)
        {
            w.Blank().Line($"}}  // namespace {parts}");
        }

        return w.ToString();
    }

    private void WriteEnum(CodeWriter w, TypeModel type)
    {
        using (w.Block($"enum class {type.Name}", close: "};"))
        {
            foreach (var literal in type.Class.Attributes)
            {
                var value = string.IsNullOrWhiteSpace(literal.DefaultValue) ? string.Empty : " = " + literal.DefaultValue.Trim();
                w.Line($"{MemberName(literal.Name)}{value},");
            }
        }

        if (type.Operations.Count > 0)
        {
            Warnings.Add($"{type.Name}: C++ enumerations cannot have operations; {string.Join(", ", type.Operations.Select(o => o.Operation.Name))} not generated.");
        }
    }

    private void WriteClass(CodeWriter w, TypeModel type)
    {
        var bases = type.BaseClasses.Concat(type.Interfaces).ToList();
        foreach (var basis in bases)
        {
            _includes.Add(basis.Name);
        }

        var inheritance = bases.Count > 0 ? " : " + string.Join(", ", bases.Select(b => "public " + b.Name)) : string.Empty;
        using (w.Block($"class {type.Name}{inheritance}", close: "};"))
        {
            foreach (var visibility in new[] { UmlVisibility.Public, UmlVisibility.Protected, UmlVisibility.Private })
            {
                WriteSection(w, type, visibility);
            }
        }
    }

    /// <summary>
    /// The members with one access level: constructors, the destructor, operations, then data. Package visibility, which
    /// C++ does not have, is public; association ends are private.
    /// </summary>
    private void WriteSection(CodeWriter w, TypeModel type, UmlVisibility visibility)
    {
        bool Has(UmlVisibility v) => (v == UmlVisibility.Package ? UmlVisibility.Public : v) == visibility;

        var operations = ConstructorsFirst(type).Where(o => !type.IsInterface || !o.IsConstructor)
            .Where(o => Has(o.ImplementsInterface || type.IsInterface ? UmlVisibility.Public : o.Operation.Visibility)).ToList();
        var attributes = type.Attributes.Where(a => !a.IsImplicit && Has(type.IsInterface ? UmlVisibility.Public : a.Attribute.Visibility)).ToList();
        var ends = visibility == UmlVisibility.Private ? type.Ends : [];

        // A virtual destructor where deleting through a pointer to this type must reach the derived class's.
        var destructor = visibility == UmlVisibility.Public && (type.IsBase || type.IsInterface || type.IsAbstract) &&
                         type.BaseClasses.Count == 0 && type.Interfaces.Count == 0;
        if (operations.Count == 0 && attributes.Count == 0 && ends.Count == 0 && !destructor)
        {
            return;
        }

        w.Blank().Label(visibility.ToString().ToLowerInvariant() + ":");
        foreach (var operation in operations.Where(o => o.IsConstructor))
        {
            w.Line($"{(operation.Operation.Parameters.Count == 1 ? "explicit " : string.Empty)}{type.Name}({Parameters(operation.Operation)});");
        }

        if (destructor)
        {
            w.Line($"virtual ~{type.Name}() = default;");
        }

        w.Blank();
        foreach (var operation in operations.Where(o => !o.IsConstructor))
        {
            w.Line(Declaration(operation));
        }

        w.Blank();
        foreach (var attribute in attributes.Select(a => a.Attribute))
        {
            var initializer = string.IsNullOrWhiteSpace(attribute.DefaultValue) ? string.Empty : " = " + Default(attribute);
            w.Line($"{(attribute.IsStatic ? "inline static " : string.Empty)}{TypeName(attribute.Type, "std::any")} {MemberName(attribute.Name)}{initializer};");
        }

        foreach (var end in ends)
        {
            if (end.Label is { } label)
            {
                w.Line("/// " + label);
            }

            w.Line($"{EndType(end)} {MemberName(end.Name)};");
        }
    }

    private string Declaration(OperationModel model)
    {
        var operation = model.Operation;
        var signature = $"{ReturnType(operation)} {MemberName(operation.Name)}({Parameters(operation)})";
        if (model.IsStatic)
        {
            return $"static {signature};";
        }

        var overrides = model.IsOverride || model.ImplementsInterface;
        if (model.IsAbstract)
        {
            return overrides ? $"{signature} override = 0;" : $"virtual {signature} = 0;";
        }

        return overrides ? $"{signature} override;" : model.IsVirtual ? $"virtual {signature};" : $"{signature};";
    }

    private string EndType(AssociationEnd end)
    {
        string element;
        if (end.Target.IsEnum)
        {
            _includes.Add(end.Target.Name);
            element = end.Target.Name;
        }
        else if (end.IsComposition)
        {
            // The whole owns its parts; unique_ptr needs the complete type where the owner is destroyed.
            _systemIncludes.Add("memory");
            _includes.Add(end.Target.Name);
            element = $"std::unique_ptr<{end.Target.Name}>";
        }
        else
        {
            _forwardDeclarations.Add(end.Target.Name);
            element = end.Target.Name + "*";
        }

        if (!end.IsMany)
        {
            return element;
        }

        _systemIncludes.Add("vector");
        return $"std::vector<{element}>";
    }

    private string Default(UmlAttribute attribute) =>
        AttributeDefault(attribute, "nullptr", (enumeration, literal) => $"{enumeration.Name}::{MemberName(literal.Name)}");

    private string Parameters(UmlOperation operation) =>
        string.Join(", ", operation.Parameters.Select(p => $"{TypeName(p.Type, "std::any")} {MemberName(p.Name)}"));

    private string ReturnType(UmlOperation operation) => TypeName(operation.ReturnType, "void");

    /// <summary>The C++ spelling of a type; <paramref name="missing"/> when the diagram gives none.</summary>
    private string TypeName(string? text, string missing)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (missing == "std::any")
            {
                _systemIncludes.Add("any");
            }

            return missing;
        }

        return TypeExpression.Parse(text) is { } type ? TypeName(type) : text.Trim();
    }

    private string TypeName(TypeExpression type)
    {
        string name;
        var arguments = type.Arguments.Select(TypeName).ToList();
        if (Model.Find(type.Name) is { } diagramType)
        {
            // Enumerations are values; classes and interfaces are referred to, and only need to be declared.
            if (diagramType.IsEnum)
            {
                _includes.Add(diagramType.Name);
                name = diagramType.Name;
            }
            else
            {
                _forwardDeclarations.Add(diagramType.Name);
                name = diagramType.Name + "*";
            }
        }
        else
        {
            name = BuiltIn(type.Name) switch
            {
                "string" => System("string", "std::string"),
                "int" or "long" or "short" or "double" or "float" or "bool" or "char" or "void" => BuiltIn(type.Name)!,
                "byte" => System("cstdint", "std::uint8_t"),
                "decimal" => "double",
                "object" => System("any", "std::any"),
                "datetime" => System("chrono", "std::chrono::system_clock::time_point"),
                "guid" => System("string", "std::string"),
                "list" => System("vector", "std::vector"),
                "set" => System("set", "std::set"),
                "map" => System("map", "std::map"),
                _ => type.Name,
            };
        }

        if (arguments.Count > 0)
        {
            name += "<" + string.Join(", ", arguments) + ">";
        }

        // Arrays of unknown size cannot be members: they become vectors.
        for (var i = 0; i < type.ArrayRank; i++)
        {
            name = $"{System("vector", "std::vector")}<{name}>";
        }

        return name;
    }

    private string System(string header, string name)
    {
        _systemIncludes.Add(header);
        return name;
    }

    private string Namespace() => string.Join("::", Naming.NamespaceParts(Options.Namespace).Select(p => AvoidKeyword(p, Keywords)));

    private static void WriteDocumentation(CodeWriter w, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            w.Line(line.Length == 0 ? "///" : "/// " + line);
        }
    }

    private static string MemberName(string name) => AvoidKeyword(Naming.Identifier(name), Keywords);
}
