# Writing a code generator plug-in

UML Wizard generates source code from class diagrams (**Diagram → Generate Code…**). Each target language is a
**plug-in**: a .NET class library that UML Wizard loads from its plug-ins folders when it starts. C#, Java, Kotlin,
TypeScript, Ruby and C++ are plug-ins like any other. To add a language, you build a DLL, drop it into `plugins`, and it appears in the
**Language** box of the Generate Code dialog.

This guide adds **PHP** step by step. The complete, working plug-in is in [`php-generation-sample/`](php-generation-sample/). To read PHP
code into class diagrams, see [Writing a code import plug-in](writing-a-codeimport-plugin.md).

- [How plug-ins are loaded](#how-plug-ins-are-loaded)
- [Step 1 — Create the project](#step-1--create-the-project)
- [Step 2 — Describe the language](#step-2--describe-the-language)
- [Step 3 — Write the files](#step-3--write-the-files)
- [Step 4 — Map the types](#step-4--map-the-types)
- [Step 5 — Report what the language cannot express](#step-5--report-what-the-language-cannot-express)
- [Step 6 — Build and drop it in](#step-6--build-and-drop-it-in)
- [Step 7 — Test it](#step-7--test-it)
- [Troubleshooting](#troubleshooting)
- [Reference](#reference)

## How plug-ins are loaded

When it starts, UML Wizard loads the plug-ins of two folders, in this order:

| Folder | Windows | macOS and Linux |
|---|---|---|
| The application's: the built-in languages | `plugins` next to `UmlWizard.exe` | `plugins` next to the `UmlWizard` executable |
| **Yours: install your plug-ins here** | `%APPDATA%\UmlWizard\plugins` | `~/.config/UmlWizard/plugins` |

Install plug-ins in **your** folder, and create it if it doesn't exist yet. You can always write to it. The application's
folder may be read-only: on macOS it is inside the app bundle, and changing a bundle breaks its signature; on Linux it
may be under `/usr` or `/opt`. If both folders have a DLL with the same name, the one in the application's folder is
loaded and the other is reported.

In either folder, a plug-in is laid out as:

| Layout | Use it when |
|---|---|
| `plugins/Acme.UmlWizard.PhpGeneration.dll` | the plug-in is a single DLL |
| `plugins/Acme.UmlWizard.PhpGeneration/Acme.UmlWizard.PhpGeneration.dll` | the plug-in brings its own dependencies: put them in the same folder (the folder and the DLL have the same name) |

File names are case-sensitive on Linux: there, the folder and the DLL must have *exactly* the same name.

Every **public, non-abstract class implementing `ICodeGenerator` with a public parameterless constructor** becomes a
language. One DLL may contain several.

- Each plug-in is loaded in its own `AssemblyLoadContext`, so plug-ins can use different versions of the same
  library without clashing.
- `UmlWizard.CodeGeneration` (the API below), `UmlWizard.Core` (the diagram model it uses) and the .NET framework are
  **shared** with UML Wizard. Never ship your own copies of `UmlWizard.CodeGeneration.dll` or `UmlWizard.Core.dll`.
- A plug-in that cannot be loaded does not stop UML Wizard: the reason is listed under *Notes on the generated code*
  in the dialog.
- Plug-ins are loaded once per run, so **restart UML Wizard** after adding, updating or removing one.

> **Trust.** A plug-in runs inside UML Wizard with your permissions, like any program. Only install plug-ins you trust.

The built-in languages are plug-ins too; their sources are good examples:
[C#](../../src/Plugins/UmlWizard.CodeGeneration.CSharp/), [Java](../../src/Plugins/UmlWizard.CodeGeneration.Java/),
[Kotlin](../../src/Plugins/UmlWizard.CodeGeneration.Kotlin/), [TypeScript](../../src/Plugins/UmlWizard.CodeGeneration.TypeScript/),
[Ruby](../../src/Plugins/UmlWizard.CodeGeneration.Ruby/) and [C++](../../src/Plugins/UmlWizard.CodeGeneration.Cpp/).

## Step 1 — Create the project

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download) and a UML Wizard installation (or a build of this
repository). Create a class library:

```sh
dotnet new classlib -n Acme.UmlWizard.PhpGeneration -f net9.0
```

Replace `Acme.UmlWizard.PhpGeneration.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Built to be loaded by UML Wizard at run time -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <!-- Shown as the provider on the Languages tab of Help → About -->
    <Company>Acme</Company>
    <!-- The folder of your UML Wizard installation: the one with UmlWizard.CodeGeneration.dll. On macOS and Linux,
         pass yours when building: dotnet build -p:UmlWizardDir=/path/to/it -->
    <UmlWizardDir Condition="'$(UmlWizardDir)' == ''">C:\Program Files\UML Wizard</UmlWizardDir>
  </PropertyGroup>

  <ItemGroup>
    <!-- Compile against UML Wizard's code generation API and diagram model; UML Wizard provides them at run time,
         so do not copy them. -->
    <Reference Include="UmlWizard.CodeGeneration">
      <HintPath>$(UmlWizardDir)\UmlWizard.CodeGeneration.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UmlWizard.Core">
      <HintPath>$(UmlWizardDir)\UmlWizard.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
```

The two settings that matter:

- **`<Private>false</Private>`** keeps `UmlWizard.CodeGeneration.dll` and `UmlWizard.Core.dll` out of your build output.
  UML Wizard shares its own copies, which is what makes your class an `ICodeGenerator` that UML Wizard recognises.
- **`EnableDynamicLoading`** prepares the output for loading as a plug-in, including your own dependencies if you add
  NuGet packages.

Delete the `Class1.cs` the template created.

> **Working in this repository?** Put the project in `src/Plugins/UmlWizard.CodeGeneration.<Language>/` and give it
> only a `RootNamespace`. `src/Plugins/Directory.Build.props` adds the settings above, and the application build copies
> every `src/Plugins/UmlWizard.CodeGeneration.*` project to `plugins/` for you. Add it to `UmlWizard.slnx` too.

## Step 2 — Describe the language

Add `PhpCodeGenerator.cs`. This class is what UML Wizard finds and shows in the dialog:

```csharp
using UmlWizard.CodeGeneration;

namespace Acme.UmlWizard.PhpGeneration;

/// <summary>PHP 8.1+: one PSR-4 file per class, interface and enumeration.</summary>
public sealed class PhpCodeGenerator : ICodeGenerator
{
    public string Id => "Php";                  // unique; also the suggested folder Generated/Php

    public string DisplayName => "PHP";         // shown in the Language box

    public NamespaceKind NamespaceKind => NamespaceKind.Namespace;   // labels the namespace box

    public bool MultipleInheritance => false;   // PHP classes have one parent

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier;

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new PhpWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
```

- **`Id`** must differ from every other plug-in's (the built-in ones are `CSharp`, `Java`, `Kotlin`, `TypeScript`, `Ruby` and `Cpp`). If two
  plug-ins use the same `Id`, the first one loaded wins and the other is reported.
- **`MultipleInheritance`**: when `false`, UML Wizard keeps the first base class of a class that has several, and adds
  a warning. Set it to `true` only if the language supports several base classes, as C++ does.
- **`DefaultNamespace`** receives the project name as an identifier (e.g. `OnlineShop`). Java, for example, returns
  it in lower case.
- **`Generate`** returns the files. Paths are relative to the output folder the user picks, and use `/`.

## Step 3 — Write the files

`Generate` receives a **`CodeModel`**: the diagram's classes with the relationships already resolved into what code
needs (base classes, implemented interfaces, fields for association ends, inherited abstract operations to implement).
You don't need to look at the diagram's relationships yourself.

The easiest way to write a language is to derive from **`LanguageGenerator`**. It holds the model, the options and the
warnings, and provides helpers that the built-in languages share (see [Reference](#reference)). You write one
type at a time. Add `PhpWriter.cs`:

```csharp
using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace Acme.UmlWizard.PhpGeneration;

internal sealed class PhpWriter(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    : LanguageGenerator(model, options, warnings)
{
    public override IEnumerable<GeneratedFile> Generate(TypeModel type)
    {
        var namespaceParts = Naming.NamespaceParts(Options.Namespace);   // "Shop.Domain" or "Shop::Domain" → [Shop, Domain]
        var w = new CodeWriter(braceOnOwnLine: true);                     // PSR-12: braces on their own line
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

    // WriteClass, WriteEnum, WriteAttribute, WriteEnd, WriteOperation, ... below
}
```

A type can produce more than one file. C++, for example, writes a header and a source file.

### Classes and interfaces

A `TypeModel` tells you what kind of type it is (`IsInterface`, `IsEnum`, `IsAbstract`) and what it derives from
(`BaseClasses`, `Interfaces`). Its members are `Attributes`, `Ends` (fields for associations) and `Operations`.
**`CodeWriter.Block`** writes a header, indents until disposed, then closes the block:

```csharp
private void WriteClass(CodeWriter w, TypeModel type)
{
    var header = (type.IsAbstract ? "abstract class " : "class ") + type.Name;
    if (type.BaseClasses.Count > 0)
    {
        header += " extends " + type.BaseClasses[0].Name;
    }

    if (type.Interfaces.Count > 0)
    {
        header += " implements " + string.Join(", ", type.Interfaces.Select(t => t.Name));
    }

    using (w.Block(header))
    {
        foreach (var attribute in type.Attributes) WriteAttribute(w, type, attribute);
        foreach (var end in type.Ends) WriteEnd(w, type, end);
        foreach (var operation in ConstructorsFirst(type))
        {
            w.Blank();                       // one blank line between members, never at the start of a block
            WriteOperation(w, type, operation);
        }
    }
}
```

(The sample also handles interfaces, which `extend` their parents.)

### Attributes

Each `AttributeModel` wraps the diagram's `UmlAttribute` (`Name`, `Type`, `Visibility`, `IsStatic`, `DefaultValue`).
Use **`Naming`** to follow the language's conventions, and **`AttributeDefault`** to write default values. It turns
`New` or `OrderStatus.New` into the language's way of naming an enumeration literal:

```csharp
private void WriteAttribute(CodeWriter w, TypeModel owner, AttributeModel model)
{
    var attribute = model.Attribute;
    var modifiers = Visibility(owner, attribute.Name, attribute.Visibility) + (attribute.IsStatic ? " static" : "");
    var initializer = string.IsNullOrWhiteSpace(attribute.DefaultValue)
        ? ""
        : " = " + AttributeDefault(attribute, "null", (enumeration, literal) => $"{enumeration.Name}::{Naming.Cased(literal.Name, Naming.Pascal)}");
    w.Line($"{modifiers} {TypeName(attribute.Type)} ${Naming.Cased(attribute.Name, Naming.Camel)}{initializer};");
}
```

### Association ends

An `AssociationEnd` is a field the owner needs because of an association, aggregation or composition. It has a
`Target` type, a `Name` (e.g. `orderLines`), and `IsMany` / `IsOptional` from the multiplicity. `Label` is the
relationship's label:

```csharp
if (end.IsMany)
{
    w.Line($"/** @var {end.Target.Name}[] */");
    w.Line($"private array ${Naming.Cased(end.Name, Naming.Camel)} = [];");
}
else
{
    w.Line($"private ?{end.Target.Name} ${Naming.Cased(end.Name, Naming.Camel)} = null;");
}
```

### Operations

Each `OperationModel` wraps the diagram's `UmlOperation` (`Name`, `ReturnType`, `Parameters`, `Visibility`) and adds
what the model worked out:

- **`IsConstructor`**: named like its class.
- **`IsAbstract`**: abstract, or declared by an interface.
- **`IsOverride`** / **`IsVirtual`**: overrides a base class operation / is overridden by a derived class.
- **`ImplementsInterface`**: implements an interface operation, so it must be public in most languages.
- **`IsImplicit`**: not in the diagram. The model adds inherited abstract and interface operations that a concrete
  class must implement, so the generated code compiles.

```csharp
var signature = $"{visibility} function {name}({parameters}): {returnType}";
if (owner.IsInterface) { w.Line(signature + ";"); }
else if (model.IsAbstract) { w.Line("abstract " + signature + ";"); }
else
{
    using (w.Block(signature))
    {
        w.Line("throw new \\LogicException('Not implemented.');");
    }
}
```

### Documentation

`TypeModel.Documentation` holds the texts of the notes attached to the class, then its stereotype, one line per
entry. Write them as the language's doc comment:

```csharp
private static void WriteDocumentation(CodeWriter w, IReadOnlyList<string> lines)
{
    if (lines.Count == 0) return;
    w.Line("/**");
    foreach (var line in lines) w.Line(line.Length == 0 ? " *" : " * " + line.Replace("*/", "* /"));
    w.Line(" */");
}
```

### Enumerations

An enumeration's literals are its class's attributes (`type.Class.Attributes`). Their `DefaultValue` is the literal's
value, if any. See `WriteEnum` in the sample, which writes a backed `enum` when every literal has an integer value.

### Tagged values

A tagged value is a name and a value, such as `php.final = true`, that the user adds to a class, attribute or
operation in its **Edit…** dialog (see *Tagged values* in the user manual). They are how a diagram says something the
UML notation has no place for, which makes them the natural way to let users steer your generator: mark a class
`final`, an attribute `readonly`, choose a base class from a framework, and so on.

Read them from the diagram's elements behind the model, with the **`Get`** extension method of
`UmlWizard.Core.Model.TaggedValues`. It returns the value of the first tagged value with that name, or `null` when
there is none:

| On | Read from |
|---|---|
| A class, interface or enumeration | `type.Class.TaggedValues` |
| An attribute | `model.Attribute.TaggedValues` |
| An operation | `model.Operation.TaggedValues` |

For example, PHP `final` classes and `readonly` properties:

```csharp
using UmlWizard.Core.Model;   // TaggedValues.Get

private const string FinalTag = "php.final";
private const string ReadonlyTag = "php.readonly";

private void WriteClass(CodeWriter w, TypeModel type)
{
    var final = IsTrue(type.Class.TaggedValues.Get(FinalTag), type.Name, FinalTag);
    var header = (type.IsAbstract ? "abstract class " : final ? "final class " : "class ") + type.Name;
    // ...
}

private void WriteAttribute(CodeWriter w, TypeModel owner, AttributeModel model)
{
    var attribute = model.Attribute;
    var modifiers = Visibility(owner, attribute.Name, attribute.Visibility)
        + (attribute.IsStatic ? " static" : "")
        + (IsTrue(attribute.TaggedValues.Get(ReadonlyTag), $"{owner.Name}.{attribute.Name}", ReadonlyTag) ? " readonly" : "");
    // ...
}

/// <summary>A yes/no tagged value: true for "true", false when it is missing; anything else is reported.</summary>
private bool IsTrue(string? value, string element, string tag)
{
    if (value is null || value.Equals("false", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    Warnings.Add($"{element}: {tag} is \"{value}\"; it should be true or false, and is ignored.");
    return false;
}
```

The sample plug-in doesn't read tagged values; add the code above to its `PhpWriter` to try it. The PHP code import
sample sets `php.final` and `php.readonly` from `final` and `readonly`, so a diagram imported from PHP then generates
them again (see *Tagged values* in [Writing a code import plug-in](writing-a-codeimport-plugin.md#tagged-values)).

Some things to keep in mind:

- **Values are text**, exactly as the user typed them, and names are compared exactly (`php.final`, not `PHP.Final`).
  Parse a value the way you would parse user input: accept the obvious spellings, and when a value makes no sense,
  report it with a warning ([Step 5](#step-5--report-what-the-language-cannot-express)) instead of throwing or
  guessing. For structured values, JSON in the value works; the OpenAPI import keeps whole schemas that way.
- **Prefix your names** with your language, e.g. `php.final`, so they don't clash with another plug-in's. A few names
  belong to UML Wizard: `namespace` (set by Import Source Folder on every imported class: the type's namespace, as its
  language wrote it), `required` and everything starting with `openapi.` (set by the OpenAPI import, read by the
  export). You may read them, e.g. `namespace` to place each class in the namespace it was imported from, but don't
  give them another meaning.
- **Implicit members** (`IsImplicit`), which the model adds for inherited abstract and interface operations and
  interface attributes, wrap the interface's or base class's own `UmlOperation` or `UmlAttribute`. Their tagged values
  are the ones set on that declaration.
- **Not everything has tagged values.** Parameters, relationships (and so association ends) and notes have none. The
  diagram's own tagged values (**Diagram → Properties…**) are not passed to generators: `Generate` receives the class
  diagram's model, not the diagram document.
- **Don't change them.** The model is read-only, and the `UmlClass`, `UmlAttribute` and `UmlOperation` behind it are
  the diagram's own.
- **Document the tagged values your plug-in reads**, with their names and values, wherever you tell users how to install
  it: nothing in UML Wizard lists them, and the user types the names by hand.

## Step 4 — Map the types

Types in a diagram are written freely: `int`, `Integer`, `String`, `List<OrderLine>`, `Map<string, int>`, `Order[]`.
Two helpers turn them into something you can map:

- **`TypeExpression.Parse(text)`** gives the `Name`, the generic `Arguments` and the `ArrayRank`. It returns `null`
  for an empty or invalid type.
- **`BuiltIn(name)`** (from `BuiltInTypes.Key`) maps the many spellings of built-in types to one key.
- **`Model.Find(name)`** returns the diagram's own type of that name, or `null`.

```csharp
private string TypeName(string? text)
{
    if (TypeExpression.Parse(text) is not { } type) return "mixed";
    if (type.ArrayRank > 0) return "array";

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
        _ => type.Name,                               // unknown: used as written
    };
}
```

| Key | Spellings it covers |
|---|---|
| `string` | string, str, text |
| `int` | int, integer, int32, UnlimitedNatural |
| `long`, `short`, `byte` | long, int64 · short, int16 · byte |
| `double`, `float`, `decimal` | double, real, number · float, single · decimal, money, BigDecimal |
| `bool`, `char` | bool, boolean · char, character |
| `void`, `object` | void · object, any |
| `datetime`, `guid` | date, datetime, time, timestamp, LocalDateTime · guid, uuid |
| `list`, `set`, `map` | list, IList, ArrayList, collection, IEnumerable, sequence, array, vector · set, HashSet, ISet · map, dictionary, HashMap, IDictionary |

The lookups are case-insensitive. Types the diagram uses but doesn't define are already reported by UML Wizard
("… is not a class of the diagram; it is used as written").

## Step 5 — Report what the language cannot express

When the diagram asks for something the language lacks, generate the closest thing and **add a warning**. Warnings are
listed under *Notes on the generated code* in the dialog, so the user knows what changed. Start each one with the type
or member it concerns:

```csharp
case UmlVisibility.Package:
    Warnings.Add($"{owner.Name}.{member}: PHP has no package visibility; it is generated as public.");
    return "public";
```

Examples in the PHP sample: package visibility, properties in interfaces, private abstract methods, enumeration values
that aren't integers.

If `Generate` throws, UML Wizard doesn't crash. It shows "The PHP code generator failed: …" as a note and writes
nothing. Still, prefer warnings to exceptions.

## Step 6 — Build and drop it in

Build it against your installation: the folder with `UmlWizard.CodeGeneration.dll`.

```sh
# Windows
dotnet build -c Release -p:UmlWizardDir="C:\Program Files\UML Wizard"

# macOS and Linux
dotnet build -c Release -p:UmlWizardDir="/path/to/UML Wizard"
```

Copy `bin/Release/net9.0/Acme.UmlWizard.PhpGeneration.dll` into a folder of the same name in **your** plug-ins
folder (see [How plug-ins are loaded](#how-plug-ins-are-loaded)). If you added NuGet packages, copy their DLLs into
the same folder too:

```powershell
# Windows (PowerShell)
$dir = "$env:APPDATA\UmlWizard\plugins\Acme.UmlWizard.PhpGeneration"
New-Item -ItemType Directory -Force $dir
Copy-Item bin\Release\net9.0\Acme.UmlWizard.PhpGeneration.dll $dir
```

```sh
# macOS and Linux
mkdir -p ~/.config/UmlWizard/plugins/Acme.UmlWizard.PhpGeneration
cp bin/Release/net9.0/Acme.UmlWizard.PhpGeneration.dll ~/.config/UmlWizard/plugins/Acme.UmlWizard.PhpGeneration/
```

```
%APPDATA%\UmlWizard\plugins/  or  ~/.config/UmlWizard/plugins/
└── Acme.UmlWizard.PhpGeneration/
    └── Acme.UmlWizard.PhpGeneration.dll        ← your plug-in
```

Start UML Wizard, select a class diagram and choose **Diagram → Generate Code…**. **PHP** is now in the Language box,
and the suggested output folder is `Generated/Php` next to the project. To remove a language, delete its folder and
restart.

To try the sample from this repository: build the application (`dotnet build` at the root), then
`dotnet build docs/plugins/php-generation-sample`. Its `UmlWizardDir` defaults to the application's build output of the
same configuration (Debug or Release), on every platform; building the whole solution builds the application first. Copy
the DLL into your plug-ins folder as above.

To check that UML Wizard found your plug-in, open **Help → About** and the **Languages** tab: it lists PHP as a code
generation plug-in, with the version of your DLL (`<Version>` in the project file, 1.0.0 by default) and its provider
(`<Company>`). Plug-ins that could not be loaded are listed there too, with the reason.

## Step 7 — Test it

Test the generator without the application: build a `ClassDiagramModel` in code and call **`CodeGenerator.Generate`**.
This builds the code model exactly as the dialog does. In a test project (xUnit here) that references `UmlWizard.CodeGeneration`, `UmlWizard.Core`
and your plug-in:

```csharp
using Acme.UmlWizard.PhpGeneration;
using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

public sealed class PhpCodeGeneratorTests
{
    [Fact]
    public void AClass_BecomesAPsr4File()
    {
        var order = new UmlClass
        {
            Name = "Order",
            Attributes = { new UmlAttribute { Name = "number", Type = "String" } },
            Operations = { new UmlOperation { Name = "total", ReturnType = "decimal" } },
        };
        var diagram = new ClassDiagramModel { Classes = { order } };

        var result = CodeGenerator.Generate(diagram, new PhpCodeGenerator(), new CodeGenerationOptions("Shop.Domain", "Shop"));

        var file = Assert.Single(result.Files);
        Assert.Equal("Shop/Domain/Order.php", file.Path);
        Assert.Contains("namespace Shop\\Domain;", file.Content);
        Assert.Contains("private string $number;", file.Content);
        Assert.Contains("public function total(): float", file.Content);
        Assert.Empty(result.Warnings);
    }
}
```

In the test project, reference `UmlWizard.CodeGeneration.dll` and `UmlWizard.Core.dll` **without**
`<Private>false</Private>`, so the tests have them at run time. The sample diagrams in [`samples/`](../../samples/) are good test input: load one with
`ProjectSerializer.Load(path)` and generate every class diagram in it.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| The language isn't in the Language box | Restart UML Wizard: plug-ins are loaded once per run. Check it's in one of the [two plug-ins folders](#how-plug-ins-are-loaded), laid out as `plugins/X.dll` or `plugins/X/X.dll` (the folder and the DLL have the same name, with the same case on Linux). Check the class is `public`, not `abstract`, and has a public parameterless constructor. |
| A note says the plug-in "is ignored" because another "has the same name and was loaded first" | A DLL of the same name is in the application's `plugins` folder, which wins. Rename yours, or remove the other copy. |
| A note says the plug-in "could not be loaded" | The DLL isn't a .NET assembly, targets a newer .NET than UML Wizard, or a dependency is missing from its folder. |
| A note says another generator "already uses the Id" | Two plug-ins have the same `Id`. Choose a unique one. |
| `MissingMethodException` / `TypeLoadException` | The plug-in was built against a different version of `UmlWizard.CodeGeneration` or `UmlWizard.Core`, or against a version from before the API moved out of `UmlWizard.Core.dll` (namespace `UmlWizard.Core.CodeGeneration`). Rebuild it against the installed ones. |
| The class is found but isn't an `ICodeGenerator` | A copy of `UmlWizard.CodeGeneration.dll` or `UmlWizard.Core.dll` was shipped next to the plug-in and loaded twice. Delete it and set `<Private>false</Private>`. |
| "No code generator plug-ins were found" | Neither plug-ins folder has one; the message lists both. Reinstall UML Wizard to get the built-in languages back, or put a plug-in in your folder. |

## Reference

All types are in `UmlWizard.CodeGeneration.dll`, namespace `UmlWizard.CodeGeneration` (the diagram types, such as
`UmlAttribute`, are in `UmlWizard.Core.dll`, namespace `UmlWizard.Core.Model`).

### `ICodeGenerator`

| Member | Meaning |
|---|---|
| `string Id` | Unique identifier, e.g. `Php`; names the suggested output folder `Generated/<Id>`. |
| `string DisplayName` | Shown in the Language box, e.g. `PHP`. The list is sorted by it. |
| `NamespaceKind NamespaceKind` | `Namespace`, `Package` or `Module`: the label of the namespace box. |
| `bool MultipleInheritance` | The language allows several base classes. |
| `string DefaultNamespace(string projectIdentifier)` | The suggested namespace for a project. |
| `IEnumerable<GeneratedFile> Generate(CodeModel, CodeGenerationOptions, IList<string> warnings)` | The files, with paths relative to the output folder using `/`. |

`CodeGenerationOptions` has `Namespace` (as typed by the user, parts separated by `.` or `::`; empty for none) and
`DiagramName`. `GeneratedFile` is `(string Path, string Content)`.

### Code model

| Type | Members |
|---|---|
| `CodeModel` | `Types` (every class, interface and enumeration, in diagram order); `Find(name)` (a type of the diagram by name, or `null`). |
| `TypeModel` | `Name` (a unique identifier); `Class` (the diagram's `UmlClass`, with its `TaggedValues`); `IsInterface`, `IsEnum`, `IsAbstract`; `BaseClasses`, `Interfaces`; `Attributes`, `Ends`, `Operations`; `Dependencies` (types it uses); `Documentation` (note texts, then the stereotype); `IsBase` (another type derives from it); `Ancestors()` (base classes, nearest first); `AllInterfaces()` (also through base classes). |
| `AttributeModel` | `Attribute` (the diagram's `UmlAttribute`: `Name`, `Type`, `Visibility`, `IsStatic`, `DefaultValue`, `TaggedValues`); `IsImplicit` (comes from an implemented interface). |
| `AssociationEnd` | `Target`, `Name`, `IsMany`, `IsOptional`, `IsComposition`, `Label`. |
| `OperationModel` | `Operation` (the diagram's `UmlOperation`: `Name`, `ReturnType`, `Parameters`, `Visibility`, `IsStatic`, `TaggedValues`); `IsConstructor`, `IsAbstract`, `IsStatic`, `IsVirtual`, `IsOverride`, `ImplementsInterface`, `IsImplicit`; `Key` (name and parameter count, to match operations across types). |

Treat the model as read-only: it's built fresh for each generation.

### Helpers

| Helper | What it does |
|---|---|
| `LanguageGenerator` | Optional base class: `Model`, `Options`, `Warnings`; `GeneratedBy` (the header comment text); `AttributeDefault(...)`, `DefaultValue(value, nullLiteral)`; `AvoidKeyword(name, keywords, suffix)`; `BuiltIn(name)`; `ConstructorsFirst(type)`. Override `Generate(TypeModel)`. |
| `CodeWriter(indentUnit = "    ", braceOnOwnLine = false)` | `Line(text)`, `Blank()` (at most one blank line, never at a block's start or end), `Block(header, open = "{", close = "}")` and `Indent()` (both indent until disposed), `Label(text)` (one level out, e.g. `public:`); `ToString()`. Lines end with `\n`. |
| `Naming` | `Pascal`, `Camel`, `Snake`, `UpperSnake` (`"order lines"` → `OrderLines`, `orderLines`, `order_lines`, `ORDER_LINES`); `Cased(name, casing)` (also fixes a leading digit); `Identifier(name)`; `Plural(word)`; `NamespaceParts(ns)`. |
| `TypeExpression` | `Parse(text)` → `Name`, `Arguments`, `ArrayRank`; `Names()`. |
| `BuiltInTypes` | `Key(name)`: see [Step 4](#step-4--map-the-types). |
| `Multiplicity` | `Parse("0..*")` → `Lower`, `Upper`, `IsMany`, `IsOptional`. |
| `TaggedValues` (in `UmlWizard.Core.Model`) | `Get(name)`: the value of the first tagged value named `name`, or `null`; see [Tagged values](#tagged-values). A `TaggedValue` is `Name` and `Value`, both text. |
