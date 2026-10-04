using UmlWizard.CodeGeneration;
using UmlWizard.CodeGeneration.CSharp;
using UmlWizard.CodeGeneration.Java;
using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration.Tests;

public sealed class CodeGeneratorCatalogTests : IDisposable
{
    private static readonly string[] BuiltIn =
        [
            "UmlWizard.CodeGeneration.CSharp", "UmlWizard.CodeGeneration.Java", "UmlWizard.CodeGeneration.Ruby", "UmlWizard.CodeGeneration.Cpp",
            "UmlWizard.CodeGeneration.TypeScript", "UmlWizard.CodeGeneration.Kotlin",
        ];

    private readonly string _plugins = Directory.CreateTempSubdirectory("umlwizard-plugins-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_plugins, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            // A loaded plug-in DLL stays locked on Windows until the test process ends.
        }
    }

    /// <summary>Drops a built plug-in into the folder, in its own subfolder or as a single DLL.</summary>
    private void Drop(string plugin, bool inFolder = true)
    {
        var folder = inFolder ? Directory.CreateDirectory(Path.Combine(_plugins, plugin)).FullName : _plugins;
        File.Copy(Path.Combine(AppContext.BaseDirectory, plugin + ".dll"), Path.Combine(folder, plugin + ".dll"));
    }

    [Fact]
    public void DroppedPlugins_AreFound_InTheirFolders_AndAsSingleDlls_ByDisplayName()
    {
        Drop(BuiltIn[0]);
        Drop(BuiltIn[1]);
        Drop(BuiltIn[2]);
        Drop(BuiltIn[3], inFolder: false);
        Drop(BuiltIn[4]);
        Drop(BuiltIn[5], inFolder: false);

        var catalog = CodeGeneratorCatalog.Load(_plugins);

        Assert.Empty(catalog.Problems);
        Assert.Equal(["C#", "C++", "Java", "Kotlin", "Ruby", "TypeScript"], catalog.Generators.Select(g => g.DisplayName));
        Assert.Equal(NamespaceKind.Package, catalog.Find("java")!.NamespaceKind);
        Assert.True(catalog.Find("Cpp")!.MultipleInheritance);
        Assert.Null(catalog.Find("Php"));
    }

    [Fact]
    public void ALoadedPlugin_SharesTheContract_AndGenerates()
    {
        Drop(BuiltIn[0]);
        var generator = Assert.Single(CodeGeneratorCatalog.Load(_plugins).Generators);

        // Loaded from the plug-in folder, not the copy the tests reference.
        Assert.NotSame(typeof(CSharpCodeGenerator).Assembly, generator.GetType().Assembly);
        var diagram = new ClassDiagramModel { Classes = { new UmlClass { Name = "Order" } } };
        var result = CodeGenerator.Generate(diagram, generator, new CodeGenerationOptions("Shop", "Shop"));
        Assert.Contains("public class Order", Assert.Single(result.Files).Content);
    }

    [Fact]
    public void PluginsOfSeveralFolders_AreLoaded_AndTheEarlierFolderWinsASameNamedDll()
    {
        // The application's folder, then the user's; a missing folder is skipped.
        var application = Path.Combine(_plugins, "app");
        var user = Path.Combine(_plugins, "user");
        foreach (var (folder, plugin) in new[] { (application, BuiltIn[0]), (user, BuiltIn[1]), (user, BuiltIn[0]) })
        {
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(AppContext.BaseDirectory, plugin + ".dll"), Path.Combine(folder, plugin + ".dll"));
        }

        var catalog = CodeGeneratorCatalog.From(Core.Plugins.PluginAssemblies.Load([application, Path.Combine(_plugins, "none"), user]));

        Assert.Equal(["C#", "Java"], catalog.Generators.Select(g => g.DisplayName));
        var problem = Assert.Single(catalog.Problems);
        Assert.Contains(Path.Combine(user, BuiltIn[0] + ".dll"), problem);
        Assert.Contains("was loaded first", problem);
    }

    [Fact]
    public void AMissingFolder_OffersNothing()
    {
        var catalog = CodeGeneratorCatalog.Load(Path.Combine(_plugins, "none"));
        Assert.Empty(catalog.Generators);
        Assert.Empty(catalog.Problems);
    }

    [Fact]
    public void ABrokenDll_IsReported_AndADllWithoutGenerators_IsIgnored_WhileTheOthersLoad()
    {
        Drop(BuiltIn[0]);
        File.WriteAllText(Path.Combine(_plugins, "Broken.dll"), "not an assembly");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "xunit.abstractions.dll"), Path.Combine(_plugins, "xunit.abstractions.dll"));

        var catalog = CodeGeneratorCatalog.Load(_plugins);

        Assert.Equal("CSharp", Assert.Single(catalog.Generators).Id);
        Assert.Contains("Broken.dll", Assert.Single(catalog.Problems));
    }

    [Fact]
    public void ATakenId_IsReported_AndTheFirstGeneratorKept()
    {
        var first = new JavaCodeGenerator();
        var catalog = new CodeGeneratorCatalog([first, new JavaCodeGenerator(), new CSharpCodeGenerator()]);

        Assert.Equal(["C#", "Java"], catalog.Generators.Select(g => g.DisplayName));
        Assert.Same(first, catalog.Find("Java"));
        Assert.Contains("\"Java\"", Assert.Single(catalog.Problems));
    }
}
