using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace Acme.UmlWizard.PhpGeneration;

/// <summary>Writes one type of the code model as a PHP file.</summary>
internal sealed class PhpWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        var namespaceParts = Naming.NamespaceParts(Options.Namespace);
        var w = new CodeWriter(braceOnOwnLine: true);
        w.Line("<?php").Blank()
            .Line("// " + GeneratedBy).Blank()
            .Line("declare(strict_types=1);");
        if (namespaceParts.Count > 0)
        {
            w.Blank().Line($"namespace {string.Join('\\', namespaceParts)};");
        }

        w.Blank();
        WriteDocumentation(w, type.Documentation);
        if (type.IsEnum)
        {
            WriteEnum(w, type);
        }
        else
        {
            WriteClass(w, type);
        }

        // PSR-4: the folders follow the namespace.
        var folder = namespaceParts.Count > 0 ? string.Join('/', namespaceParts) + "/" : string.Empty;
        yield return new GeneratedFile($"{folder}{type.Name}.php", w.ToString());
    }

    private void WriteClass(CodeWriter w, TypeModel type)
    {
        string header;
        if (type.IsInterface)
        {
            var parents = type.BaseClasses.Concat(type.Interfaces).Select(t => t.Name).ToList();
            header = $"interface {type.Name}" + (parents.Count > 0 ? " extends " + string.Join(", ", parents) : string.Empty);
        }
        else
        {
            header = (type.IsAbstract ? "abstract class " : "class ") + type.Name;
            if (type.BaseClasses.Count > 0)
            {
                header += " extends " + type.BaseClasses[0].Name;
            }

            if (type.Interfaces.Count > 0)
            {
                header += " implements " + string.Join(", ", type.Interfaces.Select(t => t.Name));
            }
        }

        using (w.Block(header))
        {
            foreach (var attribute in type.Attributes)
            {
                WriteAttribute(w, type, attribute);
            }

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
        // A backed enum when every literal has an integer value, e.g. "Paid = 2".
        var literals = type.Class.Attributes;
        var backed = literals.Count > 0 && literals.All(l => int.TryParse(l.DefaultValue, out _));
        using (w.Block($"enum {type.Name}" + (backed ? ": int" : string.Empty)))
        {
            foreach (var literal in literals)
            {
                w.Line($"case {CaseName(literal.Name)}" + (backed ? $" = {literal.DefaultValue!.Trim()}" : string.Empty) + ";");
            }

            if (!backed && literals.Any(l => !string.IsNullOrWhiteSpace(l.DefaultValue)))
            {
                Warnings.Add($"{type.Name}: only integer values of PHP enumeration cases are generated; the values are left out.");
            }

            foreach (var operation in type.Operations)
            {
                w.Blank();
                WriteOperation(w, type, operation);
            }
        }
    }

    private void WriteAttribute(CodeWriter w, TypeModel owner, AttributeModel model)
    {
        var attribute = model.Attribute;
        if (owner.IsInterface)
        {
            Warnings.Add($"{owner.Name}.{attribute.Name}: PHP interfaces cannot have properties; it is not generated.");
            return;
        }

        var visibility = model.IsImplicit ? "public" : Visibility(owner, attribute.Name, attribute.Visibility);
        var modifiers = visibility + (attribute.IsStatic ? " static" : string.Empty);
        var initializer = string.IsNullOrWhiteSpace(attribute.DefaultValue) ? string.Empty : " = " + Default(attribute);
        w.Line($"{modifiers} {TypeName(attribute.Type)} ${VariableName(attribute.Name)}{initializer};");
    }

    private void WriteEnd(CodeWriter w, TypeModel owner, AssociationEnd end)
    {
        if (owner.IsInterface)
        {
            Warnings.Add($"{owner.Name}.{end.Name}: PHP interfaces cannot have properties; it is not generated.");
            return;
        }

        if (end.IsMany)
        {
            // PHP arrays are untyped: say what they hold in a docblock.
            w.Line($"/** @var {end.Target.Name}[]{(end.Label is { } label ? " " + label : string.Empty)} */");
            w.Line($"private array ${VariableName(end.Name)} = [];");
        }
        else
        {
            if (end.Label is { } label)
            {
                w.Line($"/** {label} */");
            }

            w.Line($"private ?{end.Target.Name} ${VariableName(end.Name)} = null;");
        }
    }

    private void WriteOperation(CodeWriter w, TypeModel owner, OperationModel model)
    {
        var operation = model.Operation;
        var parameters = string.Join(", ", operation.Parameters.Select(p => $"{TypeName(p.Type)} ${VariableName(p.Name)}"));
        if (model.IsConstructor)
        {
            using (w.Block($"{Visibility(owner, operation.Name, operation.Visibility)} function __construct({parameters})"))
            {
            }

            return;
        }

        var name = Naming.Cased(operation.Name, Naming.Camel);
        var returnType = string.IsNullOrWhiteSpace(operation.ReturnType) ? "void" : TypeName(operation.ReturnType);
        var visibility = owner.IsInterface || model.ImplementsInterface ? "public" : Visibility(owner, operation.Name, operation.Visibility);
        if (model.IsAbstract && visibility == "private")
        {
            Warnings.Add($"{owner.Name}.{operation.Name}: PHP abstract methods cannot be private; it is generated as protected.");
            visibility = "protected";
        }

        var signature = model.IsStatic
            ? $"{visibility} static function {name}({parameters}): {returnType}"
            : $"{visibility} function {name}({parameters}): {returnType}";
        if (owner.IsInterface && !model.IsStatic)
        {
            w.Line(signature + ";");
        }
        else if (model.IsAbstract && !model.IsStatic)
        {
            w.Line("abstract " + signature + ";");
        }
        else
        {
            using (w.Block(signature))
            {
                w.Line("throw new \\LogicException('Not implemented.');");
            }
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
            w.Line(line.Length == 0 ? " *" : " * " + line.Replace("*/", "* /"));
        }

        w.Line(" */");
    }

    /// <summary>The PHP type declaration for a type from the diagram; <c>mixed</c> when it is missing.</summary>
    private string TypeName(string? text)
    {
        if (TypeExpression.Parse(text) is not { } type)
        {
            return "mixed";
        }

        if (type.ArrayRank > 0)
        {
            return "array";
        }

        return Model.Find(type.Name)?.Name ?? BuiltIn(type.Name) switch
        {
            "string" or "char" or "guid" => "string",
            "int" or "long" or "short" or "byte" => "int",
            "double" or "float" or "decimal" => "float",
            "bool" => "bool",
            "void" => "void",
            "object" => "mixed",
            "datetime" => "\\DateTimeImmutable",
            "list" or "set" or "map" => "array",
            _ => type.Name,
        };
    }

    private string Default(UmlAttribute attribute) =>
        AttributeDefault(attribute, "null", (enumeration, literal) => $"{enumeration.Name}::{CaseName(literal.Name)}");

    /// <summary>PHP has no package visibility: such members are public.</summary>
    private string Visibility(TypeModel owner, string member, UmlVisibility visibility)
    {
        switch (visibility)
        {
            case UmlVisibility.Public:
                return "public";
            case UmlVisibility.Protected:
                return "protected";
            case UmlVisibility.Package:
                Warnings.Add($"{owner.Name}.{member}: PHP has no package visibility; it is generated as public.");
                return "public";
            default:
                return "private";
        }
    }

    private static string VariableName(string name) => Naming.Cased(name, Naming.Camel);

    private static string CaseName(string name) => Naming.Cased(name, Naming.Pascal);
}
