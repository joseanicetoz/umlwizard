using UmlWizard.CodeImport.Cpp;
using UmlWizard.CodeImport.CSharp;
using UmlWizard.CodeImport.Java;
using UmlWizard.CodeImport.Kotlin;
using UmlWizard.CodeImport.Plugins;
using UmlWizard.CodeImport.Ruby;
using UmlWizard.CodeImport.TypeScript;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.Tests;

public sealed class CodeImportTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("umlwizard-codeimport-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            // Plug-in DLLs stay loaded until the test run ends.
        }
    }

    [Fact]
    public void Catalog_LoadsTheBuiltInImporters_FromAPluginsFolder()
    {
        foreach (var type in new[] { typeof(CSharpCodeImporter), typeof(JavaCodeImporter), typeof(CppCodeImporter), typeof(RubyCodeImporter), typeof(TypeScriptCodeImporter), typeof(KotlinCodeImporter) })
        {
            File.Copy(type.Assembly.Location, Path.Combine(_folder, Path.GetFileName(type.Assembly.Location)));
        }

        var catalog = CodeImporterCatalog.Load(_folder);

        Assert.Empty(catalog.Problems);
        Assert.Equal(["C#", "C++", "Java", "Kotlin", "Ruby", "TypeScript"], catalog.Importers.Select(i => i.DisplayName));
        Assert.Equal("Cpp", catalog.ForExtension(".HPP")?.Id);
        Assert.Equal("Ruby", catalog.Find("ruby")?.Id);
        Assert.Equal("TypeScript", catalog.ForExtension(".tsx")?.Id);
        Assert.Equal("Kotlin", catalog.ForExtension(".kt")?.Id);
        Assert.Null(catalog.ForExtension(".py"));
        Assert.NotSame(typeof(CSharpCodeImporter).Assembly, catalog.Find("CSharp")!.GetType().Assembly);
    }

    [Fact]
    public void Catalog_ReportsImportersWhoseIdIsTaken()
    {
        var catalog = new CodeImporterCatalog([new JavaCodeImporter(), new JavaCodeImporter()]);

        Assert.Single(catalog.Importers);
        Assert.Contains("already uses the Id \"Java\"", Assert.Single(catalog.Problems));
    }

    [Fact]
    public void SourceFolder_SkipsBuildOutputAndHiddenFolders_AndCountsOtherFiles()
    {
        foreach (var file in new[] { "src/A.java", "src/b/B.java", "target/C.java", "build/D.java", ".git/E.java", "node_modules/x/F.java", "pom.xml", "README.md" })
        {
            var path = Path.Combine(_folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "class X {}");
        }

        var source = SourceFolder.Read(_folder, [".java"]);

        Assert.Equal(["A.java", "B.java"], source.Files.Select(Path.GetFileName));
        Assert.Equal(new Dictionary<string, int> { [".xml"] = 1, [".md"] = 1 }, source.SkippedFiles);
        Assert.Equal($"the folder {Path.GetFileName(_folder)}", source.Label);
        Assert.Equal(2, SourceFolder.Count(_folder, [new JavaCodeImporter(), new RubyCodeImporter()]).Values.Sum());
    }

    [Fact]
    public void AnImporterThatFails_IsReported_NotThrown()
    {
        File.WriteAllText(Path.Combine(_folder, "a.x"), "x");

        var result = CodeImporter.Import(SourceFolder.Read(_folder, [".x"]), new FailingImporter());

        Assert.Null(result.Diagram);
        Assert.Contains("The Broken importer failed: boom", result.Notes);
    }

    [Fact]
    public void AnImport_ReportsItsStages_AndTheFilesRead()
    {
        var source = ThreeClasses();
        var reports = new List<CodeImportProgress>();

        var result = CodeImporter.Import(source, new CSharpCodeImporter(), new Recorder(reports.Add));

        Assert.Equal(3, ((Core.Model.ClassDiagramModel)result.Diagram!.Model).Classes.Count);
        Assert.Equal(
            [
                new(CodeImportStage.Reading, 0, 3), new(CodeImportStage.Reading, 1, 3), new(CodeImportStage.Reading, 2, 3),
                new(CodeImportStage.Reading, 3, 3), new(CodeImportStage.Parsing, 0, 3), new(CodeImportStage.LayingOut, 0, 3),
            ],
            reports);
    }

    [Fact]
    public void ACancelledImport_StopsBetweenStages()
    {
        var source = ThreeClasses();
        using var cancel = new CancellationTokenSource();
        var importer = new CallbackImporter(() => cancel.Cancel());

        Assert.ThrowsAny<OperationCanceledException>(() => CodeImporter.Import(source, importer, null, cancel.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => CodeImporter.Import(source, new CSharpCodeImporter(), null, cancel.Token));
    }

    private static TaggedValue Tag(string name, string value) => new() { Name = name, Value = value };

    private static CodeImportResult ImportTypes(params ImportedType[] types) =>
        CodeImporter.Import(new CodeSource("Code", "the folder Code", [], new Dictionary<string, int>()), new FixedImporter(types));

    [Fact]
    public void TheTaggedValuesAPlugInReads_AreKeptOnTheClassesAttributesOperationsAndLiterals()
    {
        var type = new ImportedType
        {
            Namespace = @"App\Models",
            Name = "Order",
            TaggedValues = [Tag("php.final", "true"), Tag("namespace", "ignored"), Tag(" ", "no name")],
            Attributes = [new ImportedAttribute { Name = "total", Type = new TypeRef("float"), TaggedValues = [Tag("php.readonly", "true")] }],
            Operations = [new ImportedOperation { Name = "pay", TaggedValues = [Tag("php.attribute", "#[Route('/pay')]")] }],
        };
        var status = new ImportedType
        {
            Name = "Status",
            Kind = ImportedTypeKind.Enumeration,
            Literals = [new ImportedLiteral("Paid", "'paid'") { TaggedValues = [Tag("php.since", "8.1")] }],
        };

        var model = (ClassDiagramModel)ImportTypes(type, status).Diagram!.Model;

        var order = model.Classes.Single(c => c.Name == "Order");
        Assert.Equal([("namespace", @"App\Models"), ("php.final", "true")], order.TaggedValues.Select(t => (t.Name, t.Value)));
        Assert.Equal("true", order.Attributes.Single().TaggedValues.Get("php.readonly"));
        Assert.Equal("#[Route('/pay')]", order.Operations.Single().TaggedValues.Get("php.attribute"));
        Assert.Equal("8.1", model.Classes.Single(c => c.Name == "Status").Attributes.Single().TaggedValues.Get("php.since"));

        // Copies: the diagram does not share them with what the plug-in returned.
        Assert.DoesNotContain(order.TaggedValues, t => type.TaggedValues.Contains(t));
    }

    [Fact]
    public void TheTaggedValuesOfMergedParts_AreAllKept_TheFirstOfEachNameWinning()
    {
        var first = new ImportedType
        {
            Name = "Order",
            TaggedValues = [Tag("cs.partial", "true"), Tag("cs.sealed", "true")],
            Attributes = [new ImportedAttribute { Name = "total", TaggedValues = [Tag("cs.required", "true")] }],
        };
        var second = new ImportedType
        {
            Name = "Order",
            TaggedValues = [Tag("cs.sealed", "false"), Tag("cs.generated", "true")],
            Attributes = [new ImportedAttribute { Name = "total", TaggedValues = [Tag("cs.required", "false"), Tag("cs.key", "true")] }],
        };

        var order = ((ClassDiagramModel)ImportTypes(first, second).Diagram!.Model).Classes.Single();

        Assert.Equal(
            [("cs.partial", "true"), ("cs.sealed", "true"), ("cs.generated", "true")],
            order.TaggedValues.Select(t => (t.Name, t.Value)));
        var total = Assert.Single(order.Attributes);
        Assert.Equal([("cs.required", "true"), ("cs.key", "true")], total.TaggedValues.Select(t => (t.Name, t.Value)));
    }

    [Fact]
    public void TheTaggedValuesOfAnAttributeShownAsAnAssociation_AreReported()
    {
        var order = new ImportedType
        {
            Name = "Order",
            Attributes = [new ImportedAttribute { Name = "customer", Type = new TypeRef("Customer", "Customer"), TaggedValues = [Tag("php.readonly", "true")] }],
        };

        var result = ImportTypes(order, new ImportedType { Name = "Customer" });

        Assert.Single(((ClassDiagramModel)result.Diagram!.Model).Relationships);
        Assert.Contains("1 attribute shown as an association had tagged values, which were left out.", result.Notes);
    }

    /// <summary>Returns the types it was given, whatever the files.</summary>
    private sealed class FixedImporter(IReadOnlyList<ImportedType> types) : ICodeImporter
    {
        public string Id => "Fixed";

        public string DisplayName => "Fixed";

        public IReadOnlyList<string> FileExtensions { get; } = [".x"];

        public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files) => new(types, []);
    }

    /// <summary>Three C# files of one class each.</summary>
    private CodeSource ThreeClasses()
    {
        var files = Enumerable.Range(0, 3).Select(i =>
        {
            var path = Path.Combine(_folder, $"C{i}.cs");
            File.WriteAllText(path, $"public class C{i} {{ }}");
            return path;
        }).ToList();
        return new CodeSource("Code", "the folder Code", files, new Dictionary<string, int>());
    }

    /// <summary>Records reports as they are made, unlike <see cref="Progress{T}"/>, which posts them.</summary>
    private sealed class Recorder(Action<CodeImportProgress> report) : IProgress<CodeImportProgress>
    {
        public void Report(CodeImportProgress value) => report(value);
    }

    /// <summary>Calls back while parsing, and declares nothing.</summary>
    private sealed class CallbackImporter(Action parsing) : ICodeImporter
    {
        public string Id => "Callback";

        public string DisplayName => "Callback";

        public IReadOnlyList<string> FileExtensions { get; } = [".cs"];

        public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
        {
            parsing();
            return new CodeImportParseResult([], []);
        }
    }

    private sealed class FailingImporter : ICodeImporter
    {
        public string Id => "Broken";

        public string DisplayName => "Broken";

        public IReadOnlyList<string> FileExtensions { get; } = [".x"];

        public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files) => throw new InvalidOperationException("boom");
    }
}
