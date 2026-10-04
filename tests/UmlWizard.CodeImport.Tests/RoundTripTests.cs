using UmlWizard.CodeGeneration;
using UmlWizard.CodeImport.Cpp;
using UmlWizard.CodeImport.CSharp;
using UmlWizard.CodeImport.Java;
using UmlWizard.CodeImport.Kotlin;
using UmlWizard.CodeImport.Ruby;
using UmlWizard.CodeImport.TypeScript;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.CodeImport.Tests;

/// <summary>
/// Generates the code of every class diagram of the samples in each language, imports it back with that language's
/// importer, and checks that the diagram comes back: the same classes of the same kinds, the same base classes and
/// interfaces, and (for the languages that declare types) the same associations.
/// </summary>
public sealed class RoundTripTests
{
    public static TheoryData<string> Languages => ["CSharp", "Java", "Cpp", "Ruby", "TypeScript", "Kotlin"];

    private static (ICodeGenerator Generator, ICodeImporter Importer) Language(string id) => id switch
    {
        "CSharp" => (new UmlWizard.CodeGeneration.CSharp.CSharpCodeGenerator(), new CSharpCodeImporter()),
        "Java" => (new UmlWizard.CodeGeneration.Java.JavaCodeGenerator(), new JavaCodeImporter()),
        "Cpp" => (new UmlWizard.CodeGeneration.Cpp.CppCodeGenerator(), new CppCodeImporter()),
        "TypeScript" => (new UmlWizard.CodeGeneration.TypeScript.TypeScriptCodeGenerator(), new TypeScriptCodeImporter()),
        "Kotlin" => (new UmlWizard.CodeGeneration.Kotlin.KotlinCodeGenerator(), new KotlinCodeImporter()),
        _ => (new UmlWizard.CodeGeneration.Ruby.RubyCodeGenerator(), new RubyCodeImporter()),
    };

    private static IEnumerable<DiagramDocument> SampleClassDiagrams() =>
        new[] { "OnlineShop/OnlineShop.umlwiz", "GangOfFour/GangOfFour.umlwiz" }
            .SelectMany(s => ProjectSerializer.Load(Path.Combine(AppContext.BaseDirectory, "samples", s)).Diagrams)
            .Where(d => d.DiagramType == DiagramType.Class);

    [Theory]
    [MemberData(nameof(Languages))]
    public void GeneratedCode_ImportsBackAsTheSameDiagram(string language)
    {
        var (generator, importer) = Language(language);
        var problems = new List<string>();
        var diagrams = 0;
        foreach (var document in SampleClassDiagrams())
        {
            diagrams++;
            var diagram = (ClassDiagramModel)document.Model;
            var folder = Directory.CreateTempSubdirectory("umlwizard-roundtrip-").FullName;
            try
            {
                foreach (var file in CodeGenerator.Generate(diagram, generator, new CodeGenerationOptions("Shop.Domain", document.Name)).Files)
                {
                    var path = Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, file.Content);
                }

                var result = CodeImporter.Import(SourceFolder.Read(folder, importer.FileExtensions), importer);
                Compare(document.Name, diagram, CodeModel.Build(diagram, generator.MultipleInheritance, []), result, language, problems);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        Assert.True(diagrams > 20);
        Assert.True(problems.Count == 0, $"{language}:\n" + string.Join("\n", problems));
    }

    private static void Compare(string name, ClassDiagramModel original, CodeModel code, CodeImportResult result, string language, List<string> problems)
    {
        if (result.Diagram is null)
        {
            problems.Add($"{name}: nothing was imported ({string.Join(" ", result.Notes)})");
            return;
        }

        var imported = (ClassDiagramModel)result.Diagram.Model;
        var names = imported.Classes.ToDictionary(c => c.Id, c => c.Name);
        var classes = imported.Classes.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());

        foreach (var type in code.Types)
        {
            if (!classes.TryGetValue(type.Name, out var cls))
            {
                problems.Add($"{name}: {type.Name} is missing (imported: {string.Join(", ", classes.Keys)})");
                continue;
            }

            // Ruby writes enumerations as modules of constants, and has no abstract classes but abstract methods.
            var expected = type.Class.Kind;
            if (language == "Ruby" && expected == ClassifierKind.Enumeration)
            {
                expected = ClassifierKind.Interface;
            }

            // Ruby and C++ have no abstract classes without abstract methods, and in C++ an abstract class whose
            // members are all pure virtual functions is written like an interface.
            var abstractOperations = type.Operations.Count(o => o.IsAbstract);
            var kindMatches = cls.Kind == expected ||
                (language is "Ruby" or "Cpp" && expected == ClassifierKind.AbstractClass && abstractOperations == 0 && cls.Kind == ClassifierKind.Class) ||
                (language == "Cpp" && expected == ClassifierKind.AbstractClass && cls.Kind == ClassifierKind.Interface &&
                    type.Attributes.Count == 0 && type.Ends.Count == 0 && abstractOperations == type.Operations.Count);
            if (!kindMatches)
            {
                problems.Add($"{name}: {type.Name} is a {cls.Kind}, not a {expected}");
            }
        }

        foreach (var extra in classes.Keys.Except(code.Types.Select(t => t.Name)))
        {
            problems.Add($"{name}: {extra} was not in the diagram");
        }

        var expectedBases = code.Types
            .SelectMany(t => t.BaseClasses.Concat(t.Interfaces).Select(b => $"{t.Name} -> {b.Name}"))
            .ToHashSet();
        var importedBases = imported.Relationships
            .Where(r => r.Kind is RelationshipKind.Inheritance or RelationshipKind.Realization)
            .Select(r => $"{names[r.SourceId]} -> {names[r.TargetId]}")
            .ToHashSet();
        Report(name, "base types", expectedBases, importedBases, problems);

        if (language != "Ruby")
        {
            // Association ends, and attributes whose type is another class of the diagram: both are fields in code.
            var expectedEnds = code.Types.SelectMany(t => t.Ends.Where(e => !e.Target.IsEnum).Select(e => $"{t.Name} -> {e.Target.Name}"))
                .Concat(code.Types.SelectMany(t => t.Attributes
                    .Select(a => ClassOf(code, a.Attribute.Type))
                    .Where(target => target is { IsEnum: false } && target != t)
                    .Select(target => $"{t.Name} -> {target!.Name}")))
                .ToHashSet();
            var importedEnds = imported.Relationships
                .Where(r => r.Kind == RelationshipKind.DirectedAssociation)
                .Select(r => $"{names[r.SourceId]} -> {names[r.TargetId]}")
                .ToHashSet();
            Report(name, "associations", expectedEnds, importedEnds, problems);
        }
    }

    /// <summary>The class of the diagram a type refers to, also as the element of a list or array.</summary>
    private static TypeModel? ClassOf(CodeModel code, string? type) =>
        TypeExpression.Parse(type) is { } expression
            ? code.Find(expression.Name) ?? expression.Arguments.Select(a => code.Find(a.Name)).LastOrDefault(t => t is not null)
            : null;

    private static void Report(string name, string what, HashSet<string> expected, HashSet<string> imported, List<string> problems)
    {
        foreach (var missing in expected.Except(imported))
        {
            problems.Add($"{name}: {what}: {missing} is missing");
        }

        foreach (var extra in imported.Except(expected))
        {
            problems.Add($"{name}: {what}: {extra} was not in the diagram");
        }
    }
}
