using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration.Ruby;

/// <summary>
/// Ruby: one file per type (snake_case), in folders that follow the modules. Attributes and association ends become
/// accessors set up in <c>initialize</c>; types are documented with YARD tags; interfaces are modules that are included;
/// enumerations are modules of constants; operations raise <c>NotImplementedError</c>.
/// </summary>
internal sealed class RubyWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    private static readonly HashSet<string> Keywords =
    [
        "BEGIN", "END", "alias", "and", "begin", "break", "case", "class", "def", "defined?", "do", "else", "elsif", "end",
        "ensure", "false", "for", "if", "in", "module", "next", "nil", "not", "or", "redo", "rescue", "retry", "return", "self",
        "super", "then", "true", "undef", "unless", "until", "when", "while", "yield",
    ];

    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        var modules = Naming.NamespaceParts(Options.Namespace).Select(ConstantName).ToList();
        var w = new CodeWriter("  ");
        w.Line("# frozen_string_literal: true").Blank().Line("# " + GeneratedBy).Blank();
        // Base classes and modules must be loaded first; enumerations are needed for default values.
        var enumerations = type.Attributes
            .Where(a => !string.IsNullOrWhiteSpace(a.Attribute.DefaultValue) && a.Attribute.Type is { } t && Model.Find(t.Trim()) is { IsEnum: true })
            .Select(a => Model.Find(a.Attribute.Type!.Trim())!);
        foreach (var required in type.BaseClasses.Concat(type.Interfaces).Concat(enumerations).Where(t => t != type)
                     .Select(FileName).Distinct().Order(StringComparer.Ordinal))
        {
            w.Line($"require_relative '{required}'");
        }

        w.Blank();
        var blocks = modules.Select(m => w.Block($"module {m}", open: string.Empty, close: "end")).ToList();
        WriteType(w, type);
        foreach (var block in Enumerable.Reverse(blocks))
        {
            block.Dispose();
        }

        var folder = modules.Count > 0 ? string.Join('/', modules.Select(Naming.Snake)) + "/" : string.Empty;
        yield return new GeneratedFile($"{folder}{FileName(type)}.rb", w.ToString());
    }

    private void WriteType(CodeWriter w, TypeModel type)
    {
        WriteDocumentation(w, type.Documentation);
        var name = ConstantName(type.Name);
        if (type.IsEnum)
        {
            using (w.Block($"module {name}", open: string.Empty, close: "end"))
            {
                WriteEnum(w, type);
            }

            return;
        }

        var header = type.IsInterface
            ? $"module {name}"
            : $"class {name}" + (type.BaseClasses.Count > 0 ? " < " + ConstantName(type.BaseClasses[0].Name) : string.Empty);
        using (w.Block(header, open: string.Empty, close: "end"))
        {
            foreach (var module in type.Interfaces)
            {
                w.Line($"include {ConstantName(module.Name)}");
            }

            WriteStaticAttributes(w, type);
            var operations = Unique(type);
            foreach (var visibility in new[] { UmlVisibility.Public, UmlVisibility.Protected, UmlVisibility.Private })
            {
                WriteSection(w, type, operations, visibility);
            }
        }
    }

    private void WriteEnum(CodeWriter w, TypeModel type)
    {
        var names = new List<string>();
        foreach (var literal in type.Class.Attributes)
        {
            var constant = Naming.Cased(literal.Name, Naming.UpperSnake);
            var value = string.IsNullOrWhiteSpace(literal.DefaultValue) ? ":" + Naming.Cased(literal.Name, Naming.Snake) : literal.DefaultValue.Trim();
            w.Line($"{constant} = {value}");
            names.Add(constant);
        }

        if (names.Count > 0)
        {
            w.Blank().Line($"ALL = [{string.Join(", ", names)}].freeze");
        }

        foreach (var operation in type.Operations)
        {
            w.Blank();
            WriteMethod(w, type, operation, forceModuleFunction: true);
        }
    }

    /// <summary>Class-level attributes: accessors on the class itself, with their default values.</summary>
    private void WriteStaticAttributes(CodeWriter w, TypeModel type)
    {
        var statics = type.Attributes.Where(a => !a.IsImplicit && a.Attribute.IsStatic).Select(a => a.Attribute).ToList();
        if (statics.Count == 0)
        {
            return;
        }

        w.Blank();
        using (w.Block("class << self", open: string.Empty, close: "end"))
        {
            foreach (var visibility in new[] { UmlVisibility.Public, UmlVisibility.Protected, UmlVisibility.Private })
            {
                var group = statics.Where(a => Level(a.Visibility) == visibility).ToList();
                if (group.Count == 0)
                {
                    continue;
                }

                if (visibility != UmlVisibility.Public)
                {
                    w.Blank().Line(visibility.ToString().ToLowerInvariant()).Blank();
                }

                foreach (var attribute in group)
                {
                    WriteTypeTag(w, "@return", attribute.Type);
                    w.Line($"attr_accessor :{MethodName(attribute.Name)}");
                }
            }
        }

        w.Blank();
        foreach (var attribute in statics.Where(a => !string.IsNullOrWhiteSpace(a.DefaultValue)))
        {
            w.Line($"@{MethodName(attribute.Name)} = {Default(attribute)}");
        }
    }

    /// <summary>The accessors and methods of one visibility; public ones include <c>initialize</c>.</summary>
    private void WriteSection(CodeWriter w, TypeModel type, List<OperationModel> operations, UmlVisibility visibility)
    {
        var attributes = type.Attributes
            .Where(a => !a.IsImplicit && !a.Attribute.IsStatic && Level(type.IsInterface ? UmlVisibility.Public : a.Attribute.Visibility) == visibility)
            .Select(a => a.Attribute).ToList();
        var ends = visibility == UmlVisibility.Public ? type.Ends : [];
        var methods = operations.Where(o => !o.IsConstructor && Level(o.ImplementsInterface ? UmlVisibility.Public : o.Operation.Visibility) == visibility).ToList();
        var initialize = visibility == UmlVisibility.Public && !type.IsInterface && NeedsInitialize(type, operations);
        if (attributes.Count == 0 && ends.Count == 0 && methods.Count == 0 && !initialize)
        {
            return;
        }

        if (visibility != UmlVisibility.Public)
        {
            w.Blank().Line(visibility.ToString().ToLowerInvariant());
        }

        foreach (var attribute in attributes)
        {
            w.Blank();
            WriteTypeTag(w, "@return", attribute.Type);
            w.Line($"attr_accessor :{MethodName(attribute.Name)}");
        }

        foreach (var end in ends)
        {
            w.Blank();
            if (end.Label is { } label)
            {
                w.Line("# " + label);
            }

            var target = ConstantName(end.Target.Name);
            w.Line($"# @return [{(end.IsMany ? $"Array<{target}>" : end.IsOptional ? $"{target}, nil" : target)}]");
            w.Line($"attr_accessor :{MethodName(end.Name)}");
        }

        if (initialize)
        {
            w.Blank();
            WriteInitialize(w, type, operations.FirstOrDefault(o => o.IsConstructor));
        }

        foreach (var method in methods)
        {
            w.Blank();
            WriteMethod(w, type, method, forceModuleFunction: false);
        }
    }

    /// <summary>
    /// <c>initialize</c> comes from a constructor in the diagram, or is added to give attributes their default values
    /// and collections an empty array.
    /// </summary>
    private static bool NeedsInitialize(TypeModel type, List<OperationModel> operations) =>
        operations.Any(o => o.IsConstructor) || type.Ends.Any(e => e.IsMany) ||
        type.Attributes.Any(a => !a.IsImplicit && !a.Attribute.IsStatic && !string.IsNullOrWhiteSpace(a.Attribute.DefaultValue));

    private void WriteInitialize(CodeWriter w, TypeModel type, OperationModel? constructor)
    {
        var parameters = constructor?.Operation.Parameters ?? [];
        foreach (var parameter in parameters)
        {
            WriteTypeTag(w, $"@param {MethodName(parameter.Name)}", parameter.Type);
        }

        var header = parameters.Count > 0 ? $"def initialize({string.Join(", ", parameters.Select(p => MethodName(p.Name)))})" : "def initialize";
        using (w.Block(header, open: string.Empty, close: "end"))
        {
            if (type.BaseClasses.Count > 0)
            {
                w.Line("super()");
            }

            foreach (var attribute in type.Attributes.Select(a => a.Attribute)
                         .Where(a => !a.IsStatic && !string.IsNullOrWhiteSpace(a.DefaultValue) && type.Class.Attributes.Contains(a)))
            {
                w.Line($"@{MethodName(attribute.Name)} = {Default(attribute)}");
            }

            foreach (var end in type.Ends.Where(e => e.IsMany))
            {
                w.Line($"@{MethodName(end.Name)} = []");
            }
        }

        // initialize is always private in Ruby; a private constructor means that only the class itself creates instances.
        if (constructor?.Operation.Visibility == UmlVisibility.Private)
        {
            w.Line("private_class_method :new");
        }
    }

    private void WriteMethod(CodeWriter w, TypeModel type, OperationModel model, bool forceModuleFunction)
    {
        var operation = model.Operation;
        foreach (var parameter in operation.Parameters)
        {
            WriteTypeTag(w, $"@param {MethodName(parameter.Name)}", parameter.Type);
        }

        if (!string.IsNullOrWhiteSpace(operation.ReturnType) && BuiltIn(operation.ReturnType) != "void")
        {
            WriteTypeTag(w, "@return", operation.ReturnType);
        }

        var name = MethodName(operation.Name);
        var isClassMethod = model.IsStatic || forceModuleFunction;
        var parameters = operation.Parameters.Count > 0 ? $"({string.Join(", ", operation.Parameters.Select(p => MethodName(p.Name)))})" : string.Empty;
        using (w.Block($"def {(isClassMethod ? "self." : string.Empty)}{name}{parameters}", open: string.Empty, close: "end"))
        {
            var message = model.IsAbstract
                ? $"#{{self.class}} must implement {name}"
                : $"{ConstantName(type.Name)}{(isClassMethod ? "." : "#")}{name} is not implemented";
            w.Line($"raise NotImplementedError, \"{message}\"");
        }

        if (model.IsStatic && Level(operation.Visibility) == UmlVisibility.Private)
        {
            w.Line($"private_class_method :{name}");
        }
    }

    /// <summary>The operations, without the ones Ruby cannot tell apart: methods are found by name only.</summary>
    private List<OperationModel> Unique(TypeModel type)
    {
        var result = new List<OperationModel>();
        var names = new HashSet<string>();
        foreach (var operation in type.Operations)
        {
            var key = (operation.IsConstructor ? "initialize" : MethodName(operation.Operation.Name)) + (operation.IsStatic ? "/static" : string.Empty);
            if (names.Add(key))
            {
                result.Add(operation);
            }
            else if (!operation.IsImplicit)
            {
                Warnings.Add($"{type.Name}.{operation.Operation.Name}: Ruby methods cannot be overloaded; only the first one is generated.");
            }
        }

        return result;
    }

    private string Default(UmlAttribute attribute) =>
        AttributeDefault(attribute, "nil", (enumeration, literal) =>
            $"{ConstantName(enumeration.Name)}::{Naming.Cased(literal.Name, Naming.UpperSnake)}");

    private void WriteTypeTag(CodeWriter w, string tag, string? type)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            w.Line($"# {tag} [{TypeName(type)}]");
        }
    }

    /// <summary>The YARD spelling of a type, e.g. <c>Array&lt;Order&gt;</c> or <c>Hash{String =&gt; Integer}</c>.</summary>
    private string TypeName(string text) => TypeExpression.Parse(text) is { } type ? TypeName(type) : text.Trim();

    private string TypeName(TypeExpression type)
    {
        var arguments = type.Arguments.Select(TypeName).ToList();
        var name = Model.Find(type.Name) is { } diagramType
            ? ConstantName(diagramType.Name)
            : BuiltIn(type.Name) switch
            {
                "string" or "char" or "guid" => "String",
                "int" or "long" or "short" or "byte" => "Integer",
                "double" or "float" => "Float",
                "decimal" => "BigDecimal",
                "bool" => "Boolean",
                "object" => "Object",
                "datetime" => "Time",
                "list" => arguments.Count > 0 ? $"Array<{string.Join(", ", arguments)}>" : "Array",
                "set" => arguments.Count > 0 ? $"Set<{string.Join(", ", arguments)}>" : "Set",
                "map" => arguments.Count == 2 ? $"Hash{{{arguments[0]} => {arguments[1]}}}" : "Hash",
                _ => arguments.Count > 0 ? $"{type.Name}<{string.Join(", ", arguments)}>" : type.Name,
            };

        for (var i = 0; i < type.ArrayRank; i++)
        {
            name = $"Array<{name}>";
        }

        return name;
    }

    private static void WriteDocumentation(CodeWriter w, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            w.Line(line.Length == 0 ? "#" : "# " + line);
        }
    }

    /// <summary>Ruby has public, protected and private; package visibility is public.</summary>
    private static UmlVisibility Level(UmlVisibility visibility) =>
        visibility == UmlVisibility.Package ? UmlVisibility.Public : visibility;

    private static string FileName(TypeModel type) => Naming.Cased(type.Name, Naming.Snake);

    private static string MethodName(string name) => AvoidKeyword(Naming.Cased(name, Naming.Snake), Keywords);

    /// <summary>Class and module names are constants: they start with a capital.</summary>
    private static string ConstantName(string name)
    {
        var id = Naming.Identifier(name);
        return char.IsUpper(id[0]) ? id : Naming.Cased(id, Naming.Pascal);
    }
}
