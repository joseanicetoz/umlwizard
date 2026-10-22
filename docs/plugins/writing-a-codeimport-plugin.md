# Writing a code import plug-in

UML Wizard creates class diagrams from existing source code (**File → Import Source Folder…**). Each source language
is a **plug-in**: a .NET class library that UML Wizard loads from its plug-ins folders when it starts. C#, C++, Java, Kotlin,
Ruby and TypeScript are plug-ins like any other. To add a language, you build a DLL, drop it into `plugins`, and the Import Source
Folder dialog offers it for every folder that has files of that language.

This guide adds **PHP** step by step. The complete, working plug-in is in [`php-import-sample/`](php-import-sample/).
It is the counterpart of the PHP code generator in [Writing a code generator plug-in](writing-a-codegeneration-plugin.md).

- [How plug-ins are loaded](#how-plug-ins-are-loaded)
- [What the plug-in does, and what UML Wizard does](#what-the-plug-in-does-and-what-uml-wizard-does)
- [Step 1 — Create the project](#step-1--create-the-project)
- [Step 2 — Describe the language](#step-2--describe-the-language)
- [Step 3 — Split the source into tokens](#step-3--split-the-source-into-tokens)
- [Step 4 — Read the declarations](#step-4--read-the-declarations)
- [Step 5 — Refer to types](#step-5--refer-to-types)
- [Step 6 — Report what the diagram cannot show](#step-6--report-what-the-diagram-cannot-show)
- [Step 7 — Build and drop it in](#step-7--build-and-drop-it-in)
- [Step 8 — Test it](#step-8--test-it)
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
| `plugins/Acme.UmlWizard.PhpImport.dll` | the plug-in is a single DLL |
| `plugins/Acme.UmlWizard.PhpImport/Acme.UmlWizard.PhpImport.dll` | the plug-in brings its own dependencies: put them in the same folder (the folder and the DLL have the same name) |

File names are case-sensitive on Linux: there, the folder and the DLL must have *exactly* the same name.

Every **public, non-abstract class implementing `ICodeImporter` with a public parameterless constructor** becomes an
importable language. One DLL may contain several.

- Each plug-in is loaded in its own `AssemblyLoadContext`, so plug-ins can use different versions of the same
  library without clashing.
- `UmlWizard.CodeImport` (the API below), `UmlWizard.Core` (the diagram model it uses) and the .NET framework are
  **shared** with UML Wizard. Never ship your own copies of `UmlWizard.CodeImport.dll` or `UmlWizard.Core.dll`.
- Code generators and code importers are found in the same loaded DLLs. One DLL can hold a language's generator
  **and** its importer; this guide keeps them apart so that each can be followed on its own.
- Plug-ins are loaded once per run, so **restart UML Wizard** after adding, updating or removing one.

> **Trust.** A plug-in runs inside UML Wizard with your permissions, like any program. Only install plug-ins you trust.

The built-in languages are plug-ins too; their sources are good examples:
[C#](../../src/Plugins/UmlWizard.CodeImport.CSharp/) (reads code with Roslyn),
[Java](../../src/Plugins/UmlWizard.CodeImport.Java/) and [C++](../../src/Plugins/UmlWizard.CodeImport.Cpp/)
(declaration parsers over `CLikeTokenizer`),
[Kotlin](../../src/Plugins/UmlWizard.CodeImport.Kotlin/) and [TypeScript](../../src/Plugins/UmlWizard.CodeImport.TypeScript/)
(declaration parsers over tokenizers of their own, like the PHP sample's), and [Ruby](../../src/Plugins/UmlWizard.CodeImport.Ruby/) (a line reader
for a language without declared types).

## What the plug-in does, and what UML Wizard does

A plug-in only **reads declarations**. It turns the text of the source files into a list of `ImportedType`s: the
classes, interfaces and enumerations it found, with their attributes, operations and literals, and the base types and
member types *as written*, plus [tagged values](#tagged-values) for what the diagram cannot show. It doesn't need to
know whether `Customer` is a class of the import or a library class.

UML Wizard does the rest, the same way for every language:

1. finds the files: every file with one of the plug-in's extensions under the folder, leaving out hidden folders
   (`.git`, …), build output and dependencies (`bin`, `obj`, `build`, `out`, `target`, `dist`, `packages`,
   `node_modules`, `vendor`), and counts the other files;
2. reads them and calls the plug-in's `Parse` with all of them at once, on a background thread;
3. merges the parts of a type (the same namespace and name declared in several places, like C# partial classes), with
   their tagged values;
4. links the types: a base type that is a type of the import becomes inheritance (realization for an interface of a
   class), and an attribute whose type is another class of the import becomes a directed association;
5. lays out the diagram, and shows a summary with the notes: its own (e.g. files skipped, base types not part of the
   import, initial values too long to show) followed by the plug-in's.

So the plug-in is only as big as the language requires. It never builds a diagram, names relationships or lays anything
out.

## Step 1 — Create the project

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a UML Wizard installation (or a build of this
repository). Create a class library:

```sh
dotnet new classlib -n Acme.UmlWizard.PhpImport -f net10.0
```

Replace `Acme.UmlWizard.PhpImport.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Built to be loaded by UML Wizard at run time -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <!-- Shown as the provider on the Languages tab of Help → About -->
    <Company>Acme</Company>
    <!-- The folder of your UML Wizard installation: the one with UmlWizard.CodeImport.dll. On macOS and Linux,
         pass yours when building: dotnet build -p:UmlWizardDir=/path/to/it -->
    <UmlWizardDir Condition="'$(UmlWizardDir)' == ''">C:\Program Files\UML Wizard</UmlWizardDir>
  </PropertyGroup>

  <ItemGroup>
    <!-- Compile against UML Wizard's code import API and diagram model; UML Wizard provides them at run time,
         so do not copy them. -->
    <Reference Include="UmlWizard.CodeImport">
      <HintPath>$(UmlWizardDir)\UmlWizard.CodeImport.dll</HintPath>
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

- **`<Private>false</Private>`** keeps `UmlWizard.CodeImport.dll` and `UmlWizard.Core.dll` out of your build output.
  UML Wizard shares its own copies, which is what makes your class an `ICodeImporter` that UML Wizard recognises.
- **`EnableDynamicLoading`** prepares the output for loading as a plug-in, including your own dependencies if you add
  NuGet packages (for example a ready-made parser for your language).

Delete the `Class1.cs` the template created.

> **Working in this repository?** Put the project in `src/Plugins/UmlWizard.CodeImport.<Language>/` and give it only
> a `RootNamespace`. `src/Plugins/Directory.Build.props` adds the settings above, and the application build copies
> every `src/Plugins/UmlWizard.CodeImport.*` project to `plugins/` for you. Add it to `UmlWizard.slnx` (in the
> `/src/Plugins/` folder) and to the references of `tests/UmlWizard.CodeImport.Tests` too.

## Step 2 — Describe the language

Add `PhpCodeImporter.cs`. This class is what UML Wizard finds and offers in the dialog:

```csharp
using UmlWizard.CodeImport;

namespace Acme.UmlWizard.PhpImport;

/// <summary>PHP 8: classes, interfaces, traits and enumerations with their members, in their namespaces.</summary>
public sealed class PhpCodeImporter : ICodeImporter
{
    public string Id => "Php";                                         // unique among code importers

    public string DisplayName => "PHP";                                // shown in the Import Source Folder dialog

    public IReadOnlyList<string> FileExtensions { get; } = [".php"];  // the files it reads, with the dot

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var loss = new Losses();
        var types = new List<ImportedType>();
        foreach (var file in files)
        {
            try
            {
                new PhpParser(PhpTokenizer.Tokenize(file.Text), types, loss).ParseFile();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Code the parser does not expect: keep what was read, and go on with the next file.
                loss.UnreadableFiles++;
            }
        }

        return new CodeImportParseResult(types, loss.Notes());
    }
}
```

- **`Id`** must differ from every other code importer's (the built-in ones are `CSharp`, `Cpp`, `Java`, `Kotlin`, `Ruby` and `TypeScript`).
  If two importers use the same `Id`, the first one loaded wins. Importers and generators are separate lists, so the
  PHP importer and the PHP generator may both use `Php`.
- **`FileExtensions`** decides which files are read, and which languages the dialog offers for a folder: it counts
  the files of each importer's extensions and lists the languages that have any, the most files first. Extensions are
  compared ignoring case. Only list extensions your parser can read: `.phtml` templates, for example, are mostly HTML.
- **`Parse`** receives **all** the files at once as `SourceFile(Path, Text)`, so types can be declared in one file and
  used in another. `Path` is only for your messages; don't read the disk yourself.
- Parse files independently, and **don't let one bad file stop the import**. If `Parse` throws, UML Wizard shows
  "The PHP importer failed: …" and imports nothing. Here, a file that trips up the parser keeps the types read so far
  and is counted in a note.

## Step 3 — Split the source into tokens

Reading declarations is much easier over tokens (words, strings, punctuation) than over characters. UML Wizard
provides **`CLikeTokenizer`** for C-like languages. It leaves out `//` and `/* */` comments, makes each string one
token, and can skip C preprocessor lines. Java and C++ use it directly:

```csharp
var tokens = CLikeTokenizer.Tokenize(file.Text);                        // Java
var tokens = CLikeTokenizer.Tokenize(file.Text, preprocessor: true);    // C and C++
```

PHP is not C-like enough for that: code sits between `<?php` and `?>` in HTML; `#` starts a comment (but `#[` an
attribute); strings may span lines; heredocs (`<<<EOT`) run to a line that starts with their label; `$name` is a
variable, and `\App\Models\Order` is one qualified name. So the sample has its own `PhpTokenizer`, but it produces
UML Wizard's **`Token`** records, so the parser can still use the helpers that work on them:

- **`CLikeTokenizer.Matching(tokens, i)`** finds the bracket that closes the one at `i` (`(`, `[`, `{` or `<`). This
  is how method bodies are skipped without reading them.
- **`CLikeTokenizer.Join(tokens)`** joins tokens back into readable code, e.g. for initial values.

An extract of `PhpTokenizer.Tokenize`:

```csharp
else if ((c == '/' && next == '/') || (c == '#' && next != '['))
{
    // A line comment ends at the end of the line, or at ?>.
    while (i < text.Length && text[i] != '\n' && string.CompareOrdinal(text, i, "?>", 0, 2) != 0)
    {
        i++;
    }
}
else if (c is '\'' or '"' or '`' || string.CompareOrdinal(text, i, "<<<", 0, 3) == 0)
{
    var end = c == '<' ? SkipHeredoc(text, i) : SkipString(text, i);
    tokens.Add(new Token(TokenKind.String, text[i..end], line));
    line += Count(text, i, end, '\n');
    i = end;
}
else if (IsNameStart(c) || (c is '$' or '\\' && IsNameStart(next)))
{
    // $order, Order, \App\Models\Order: one identifier each
    ...
}
```

Two things matter in a tokenizer: **everything that can contain a brace must be a single token** (strings, heredocs,
comments), or `Matching` will skip too little or too much; and every token must move forward, so a character the
tokenizer doesn't know becomes a one-character `Symbol`.

> If a real parser exists for your language on NuGet (as Roslyn does for C#), use it instead: add the package, and
> ship its DLLs in the plug-in's folder (`plugins/<Name>/`). Map its syntax tree to `ImportedType`s as in Step 4.

## Step 4 — Read the declarations

The sample's `PhpParser` walks the tokens of one file. It only needs to **recognise declarations and skip everything
else**: `namespace`, then `class`, `interface`, `trait` and `enum` at the top level, then members inside their braces.
Method bodies are skipped with `Matching`.

### Types

Create one `ImportedType` per declaration and add it to the list:

```csharp
var type = new ImportedType
{
    Namespace = _namespace,                   // App\Models, as the language writes it; empty for none
    Name = Current.Text,                      // Order: the simple name
    Kind = keyword switch
    {
        "interface" or "trait" => ImportedTypeKind.Interface,
        "enum" => ImportedTypeKind.Enumeration,
        _ when modifiers.Contains("abstract") => ImportedTypeKind.AbstractClass,
        _ => ImportedTypeKind.Class,
    },
    Stereotype = keyword == "trait" ? "trait" : null,
};
```

- **`Kind`** is what UML can show: class, abstract class, interface or enumeration. For anything else, pick the closest
  kind and say what it really is with a **`Stereotype`**. PHP traits are shown as interfaces «trait», which the classes
  that `use` them realize, just as the Ruby importer shows modules as interfaces «module».
- **`Namespace`** is kept on the class as the tagged value `namespace`. Classes are shown by their simple name; only
  when two namespaces declare the same name are they shown as `Namespace.Name`.
- **`DeclaringTypes`** lists the types a nested type is declared in (outermost first); it is shown as `Outer.Inner`.
  PHP has no nested types, so the sample leaves it empty.
- **`TypeParameters`** lists the type parameters of a generic type (`T`); it is shown as `Repository<T>`.
- Skip what only *looks* like a declaration: `new class { … }` (an anonymous class) and `Order::class`.

### Base types

Add every base class, implemented interface and used trait to **`BaseTypes`**, as written. You don't need to know
which are types of the import: UML Wizard links those that are, and lists the others in a note ("Base types that are
not part of the import are not shown: \Countable, \JsonSerializable."):

```csharp
if (IsKeyword("extends") || IsKeyword("implements"))
{
    _i++;
    ReadNames(type.BaseTypes);      // Payable, \JsonSerializable → one TypeRef each
}
```

### Attributes

Fields, properties and constants are **`ImportedAttribute`s**. Use the language's rules for what isn't written:
PHP members are public unless declared otherwise, and constants are static. `DefaultValue` is the initial value as
written. UML Wizard leaves out values longer than 40 characters and counts them in a note:

```csharp
type.Attributes.Add(new ImportedAttribute
{
    Name = name,                         // customer: without the $
    Type = propertyType,                 // a TypeRef (Step 5), or null when the code declares no type
    Visibility = visibility,
    IsStatic = isStatic,
    DefaultValue = value,                // CLikeTokenizer.Join of the tokens after =, e.g. Status::New
});
```

Look for attributes wherever the language declares them. PHP 8 **constructor promotion**
(`public function __construct(private string $number)`) declares a property in a parameter list, so `ReadParameters`
adds an attribute for each parameter that has a visibility or `readonly`.

### Operations

Methods and constructors are **`ImportedOperation`s**. A constructor is **named like its type**, whatever the language
calls it (`__construct` in PHP, `initialize` in Ruby), and has no return type:

```csharp
var isConstructor = name.Equals("__construct", StringComparison.OrdinalIgnoreCase);
var operation = new ImportedOperation
{
    Name = isConstructor ? type.Name : name,
    Visibility = visibility,
    IsStatic = isStatic,
    IsAbstract = isAbstract,
};
operation.Parameters.AddRange(ReadParameters(type, isConstructor));   // UmlParameter { Name, Type }
if (Current.Is(":") && !isConstructor)
{
    _i++;
    operation.ReturnType = ReadType().Text;                            // float, ?OrderLine, static
}

SkipMethodRest();                                                     // the body, or ; for an abstract method
```

Parameter and return types are shown as written, so `ReturnType` and `UmlParameter.Type` are plain strings.

### Enumerations

The literals of an enumeration are **`ImportedLiteral(Name, Value)`**; the value is the one the code gives, if any
(`case Paid = 'paid';`). Enumerations may also have attributes (constants) and operations.

### Tagged values

A tagged value is a name and a value kept on a class, attribute or operation, for what the UML notation has no place
for (see *Tagged values* in the user manual). It is how an import keeps what the language says but the diagram cannot
show: PHP `final` and `readonly`, C# `sealed`, Java annotations, and so on. Add them to the **`TaggedValues`** of an
`ImportedType`, `ImportedAttribute`, `ImportedOperation` or `ImportedLiteral`; UML Wizard keeps them on the class,
attribute or operation the element becomes. The sample keeps `final` and `readonly`:

```csharp
// PhpCodeImporter
public const string FinalTag = "php.final";
public const string ReadonlyTag = "php.readonly";

// PhpParser: final class, final function, readonly class, readonly property (also when promoted in the constructor)
private static List<TaggedValue> TagsFor(HashSet<string> modifiers) =>
[
    .. modifiers.Contains("final") ? [new TaggedValue { Name = PhpCodeImporter.FinalTag, Value = "true" }] : Array.Empty<TaggedValue>(),
    .. modifiers.Contains("readonly") ? [new TaggedValue { Name = PhpCodeImporter.ReadonlyTag, Value = "true" }] : Array.Empty<TaggedValue>(),
];

var type = new ImportedType
{
    // Namespace, Name, Kind, Stereotype as above
    TaggedValues = TagsFor(modifiers),
};
```

`final class Order { public readonly float $total; final public function pay(): void {} }` imports as the class
`Order` with `php.final = true`, its attribute `total` with `php.readonly = true` and its operation `pay` with
`php.final = true`.

How UML Wizard keeps them:

- **As given, in order.** Values are text; give yes/no values as `true`, and structured ones as JSON if you need to.
  `TaggedValue` is in `UmlWizard.Core.Model` (`Name`, `Value`).
- **Without a name, dropped.** Like rows without a name in the app's tagged value editor.
- **The first of each name wins.** When parts of a type are merged (partial classes, reopened Ruby classes), the class
  gets the tagged values of every part; if two parts give the same name, the first part's value is kept. The same goes
  for an attribute declared in several parts.
- **`namespace` is UML Wizard's.** It is set on every class whose `Namespace` is not empty, from the `Namespace` you gave
  the `ImportedType`, as the language writes it (e.g. `App\Models`); a `namespace` you add yourself is ignored then. So
  give every type its `Namespace` even though the diagram shows classes by their simple name: the user sees it in the
  class's tagged values, and a code generator can read it to put the class back in its namespace.
- **Not on associations.** An attribute whose type is another imported class is shown as an association, and
  relationships have no tagged values: its tagged values are left out, and a note says how many attributes lost theirs.

**Prefix the names** with your language (`php.final`, not `final`), so they don't clash with another plug-in's, and
don't use UML Wizard's own: `namespace`, `required` and those starting with `openapi.`. Use the names a code generator
for the same language reads, so a diagram imported from code generates the same code again: the PHP code generator in
[Writing a code generator plug-in](writing-a-codegeneration-plugin.md#tagged-values) reads `php.final` and
`php.readonly`. **List the tagged values your plug-in sets** wherever you tell users how to install it.

What has a better place in the diagram goes there instead: what **kind of type** something is goes in the `Stereotype`
(traits are interfaces «trait»). And what is neither shown nor kept, leave out and **say so** in the notes
([Step 6](#step-6--report-what-the-diagram-cannot-show)).

### Robust parsing

Real code will contain things your parser doesn't expect. Two habits keep a declaration parser from derailing:

- **Always move forward.** In a type body, if nothing was read, count it and skip the statement (up to `;` or past a
  `{ … }` block):

  ```csharp
  if (_i == start)
  {
      // Not a declaration we know: skip it.
      loss.Unreadable++;
      SkipStatement();
  }
  ```

- **Skip nested brackets as a whole** with `CLikeTokenizer.Matching`, e.g. default values like `new Money(0, 'EUR')`
  or `[1, 2]`, which contain commas that would otherwise end a parameter.

## Step 5 — Refer to types

An attribute's type is a **`TypeRef`**. It carries what UML Wizard needs to turn the attribute into an association:

| Member | Meaning | PHP example |
|---|---|---|
| `Text` | The type as written, shown in the diagram. | `?Customer` |
| `Name` | The simple name of the type it refers to, used to find it among the imported types; `null` for built-in types. | `Customer` |
| `Arity` | The number of type arguments of `Name`, for generic types. | `0` |
| `IsMany` | An array or collection of `Name`: the association gets multiplicity `*`. | — |
| `IsOptional` | A nullable type or pointer: the association gets multiplicity `0..1`. | `true` |

So `protected ?Customer $customer = null;` in `Order` becomes a directed association `Order → Customer` named
`customer` with multiplicity `0..1`, instead of an attribute, as long as `Customer` is a class of the import.

Look *through* wrappers to find `Name`. Java and C# report `List<OrderLine>` as `Name = "OrderLine", IsMany = true`,
and C++ does the same for `std::vector<OrderLine*>`. For PHP, the sample strips the namespace and the leading `\`,
treats `null` in a union as optional, and gives built-in types no name:

```csharp
private static TypeRef Reference(string text, string name, bool optional)
{
    var simple = name[(name.LastIndexOf('\\') + 1)..];                  // \App\Models\Order → Order
    return BuiltInTypes.Contains(simple)                                 // int, string, array, self, …
        ? new TypeRef(text, IsOptional: optional)
        : new TypeRef(text, simple, IsOptional: optional);
}
```

A union of several classes (`Order|Invoice`) has no single `Name`, so it stays an attribute. Base types are `TypeRef`s
too; only `Name` and `Arity` matter for them.

## Step 6 — Report what the diagram cannot show

Whenever the code has something the diagram cannot show, leave it out and **say so**. Return notes in
`CodeImportParseResult.Notes`. They are listed in the summary after the import, so the user knows the diagram isn't
the whole story. Count while reading, and report each kind of loss once, with **`CodeImporter.Add`**, which writes
"1 … was" or "3 … were" and nothing when the count is zero:

```csharp
internal sealed class Losses
{
    public int UnreadableFiles, Unreadable, Functions, Attributes, TraitRules, Hooks, ParameterDetails, ArrayProperties;

    public List<string> Notes()
    {
        var notes = new List<string>();
        CodeImporter.Add(notes, Functions, "function outside a class was left out", "functions outside classes were left out");
        CodeImporter.Add(notes, Attributes, "PHP attribute (e.g. #[ORM\\Entity]) was left out", "PHP attributes (e.g. #[ORM\\Entity]) were left out");
        CodeImporter.Add(notes, ArrayProperties,
            "array property is shown as an attribute, not an association: PHP does not declare what an array holds",
            "array properties are shown as attributes, not associations: PHP does not declare what an array holds");
        // … trait conflict rules, property hooks, parameter details, unreadable declarations and files
        return notes;
    }
}
```

Don't repeat what UML Wizard already reports: that method bodies, comments and documentation are not imported, other
files, unreadable files, base types that are not part of the import, and initial values too long to show.

Here is the summary of an import of a small PHP project:

```
6 classes, 3 interfaces and 2 enumerations from the folder shop (2 files).
• Only PHP source files (.php) are read, and without building the code: method bodies, comments and documentation are not imported.
• 1 function outside a class was left out.
• 2 PHP attributes (e.g. #[ORM\Entity]) were left out.
• 1 trait conflict rule (insteadof, as) was left out.
• 5 parameters lost their default values, & (by reference) or ... (variadic).
• 1 array property is shown as an attribute, not an association: PHP does not declare what an array holds.
• Base types that are not part of the import are not shown: \Countable, \JsonSerializable.
• 1 initial value was too long to show and was left out.
```

## Step 7 — Build and drop it in

Build it against your installation: the folder with `UmlWizard.CodeImport.dll`.

```sh
# Windows
dotnet build -c Release -p:UmlWizardDir="C:\Program Files\UML Wizard"

# macOS and Linux
dotnet build -c Release -p:UmlWizardDir="/path/to/UML Wizard"
```

Copy `bin/Release/net10.0/Acme.UmlWizard.PhpImport.dll` into a folder of the same name in **your** plug-ins folder
(see [How plug-ins are loaded](#how-plug-ins-are-loaded)). If you added NuGet packages, copy their DLLs into the same
folder too:

```powershell
# Windows (PowerShell)
$dir = "$env:APPDATA\UmlWizard\plugins\Acme.UmlWizard.PhpImport"
New-Item -ItemType Directory -Force $dir
Copy-Item bin\Release\net10.0\Acme.UmlWizard.PhpImport.dll $dir
```

```sh
# macOS and Linux
mkdir -p ~/.config/UmlWizard/plugins/Acme.UmlWizard.PhpImport
cp bin/Release/net10.0/Acme.UmlWizard.PhpImport.dll ~/.config/UmlWizard/plugins/Acme.UmlWizard.PhpImport/
```

```
%APPDATA%\UmlWizard\plugins/  or  ~/.config/UmlWizard/plugins/
└── Acme.UmlWizard.PhpImport/
    └── Acme.UmlWizard.PhpImport.dll    ← your plug-in
```

Start UML Wizard and choose **File → Import Source Folder…** (or **Import Source Folder…** on the start screen). Pick
a folder of PHP code: **PHP** is now offered with the number of `.php` files, and the new project opens with the
diagram and the summary. To remove a language, delete its folder and restart.

To try the sample from this repository: build the application (`dotnet build` at the root), then
`dotnet build docs/plugins/php-import-sample`. Its `UmlWizardDir` defaults to the application's build output of the same
configuration (Debug or Release), on every platform; building the whole solution builds the application first. Copy the
DLL into your plug-ins folder as above.

To check that UML Wizard found your plug-in, open **Help → About** and the **Languages** tab: it lists PHP as a code
import plug-in, with the version of your DLL (`<Version>` in the project file, 1.0.0 by default) and its provider
(`<Company>`). Plug-ins that could not be loaded are listed there too, with the reason.

## Step 8 — Test it

Test the importer without the application. In a test project (xUnit here) that references `UmlWizard.CodeImport`,
`UmlWizard.Core` and your plug-in, test two levels:

- **`Parse`** directly, with source text in memory, to check what the parser reads;
- **`CodeImporter.Import`** on a folder, to check the diagram the user will get: the same call the dialog makes.

```csharp
using Acme.UmlWizard.PhpImport;
using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

public sealed class PhpCodeImporterTests
{
    private const string Order = """
        <?php
        namespace App\Models;

        class Order extends Entity
        {
            private ?Customer $customer = null;

            public function __construct(private string $number) {}

            public function total(): float { return 0.0; }
        }
        """;

    [Fact]
    public void Parse_ReadsTheDeclarations()
    {
        var result = new PhpCodeImporter().Parse([new SourceFile("Order.php", Order)]);

        var order = Assert.Single(result.Types);
        Assert.Equal(@"App\Models", order.Namespace);
        Assert.Equal("Entity", Assert.Single(order.BaseTypes).Name);
        Assert.Equal(["customer", "number"], order.Attributes.Select(a => a.Name));   // number: a promoted property
        Assert.True(order.Attributes[0].Type!.IsOptional);
        Assert.Equal(["Order", "total"], order.Operations.Select(o => o.Name));       // __construct is named Order
        Assert.Equal("float", order.Operations[1].ReturnType);
    }

    [Fact]
    public void Import_LinksTheClasses()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(folder, "Order.php"), Order);
        File.WriteAllText(Path.Combine(folder, "Customer.php"), @"<?php namespace App\Models; class Customer {}");

        var importer = new PhpCodeImporter();
        var result = CodeImporter.Import(SourceFolder.Read(folder, importer.FileExtensions), importer);

        var model = (ClassDiagramModel)result.Diagram!.Model;
        var association = Assert.Single(model.Relationships);
        Assert.Equal(RelationshipKind.DirectedAssociation, association.Kind);
        Assert.Equal("customer", association.Label);
        Assert.Equal("0..1", association.TargetMultiplicity);
        Assert.Contains("Base types that are not part of the import are not shown: Entity.", result.Notes);
    }
}
```

In the test project, reference `UmlWizard.CodeImport.dll` and `UmlWizard.Core.dll` **without**
`<Private>false</Private>`, so the tests have them at run time.

Good test input:

- **Idiomatic code of real projects**, with the features that trip parsers up: for PHP, heredocs and strings that
  contain braces, HTML around the code, attributes, anonymous classes, `Order::class`, union types.
- **A round trip**, if UML Wizard has a generator for your language: generate the code of the sample diagrams in
  [`samples/`](../../samples/) (see [Step 7 of the generator guide](writing-a-codegeneration-plugin.md#step-7--test-it)),
  import it back and compare the classes, as `tests/UmlWizard.CodeImport.Tests` does for the built-in languages.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| "… has no source files of a language that can be imported (C#, C++, Java, Kotlin, Ruby, TypeScript)", or the dialog doesn't offer the language | The plug-in wasn't loaded. Restart UML Wizard: plug-ins are loaded once per run. Check it's in one of the [two plug-ins folders](#how-plug-ins-are-loaded), laid out as `plugins/X.dll` or `plugins/X/X.dll` (the folder and the DLL have the same name, with the same case on Linux). Check the class is `public`, not `abstract`, and has a public parameterless constructor, and that `FileExtensions` include the dot (`.php`, not `php`). |
| Why a plug-in could not be loaded | The reasons are listed under *Notes on the generated code* in **Diagram → Generate Code…**, because generators and importers come from the same loaded DLLs, and in the message shown when no code importer at all could be loaded. The DLL may not be a .NET assembly, may target a newer .NET than UML Wizard, or may miss a dependency in its folder. A plug-in "is ignored" when a DLL of the same name in the application's `plugins` folder "was loaded first": rename yours, or remove the other copy. |
| Files are missing from the import | Files under hidden folders and under `bin`, `obj`, `build`, `out`, `target`, `dist`, `packages`, `node_modules` and `vendor` are never read. |
| "The PHP importer failed: …" and nothing is imported | `Parse` threw an exception. Catch errors per file (see [Step 2](#step-2--describe-the-language)) and report them as notes. |
| "No classes, interfaces, enumerations, structs or records were found in …" | `Parse` returned no types. Test the parser on one of the files ([Step 8](#step-8--test-it)). |
| Types are missing after a certain point in a file | A string, comment or heredoc wasn't made a single token, so a brace in it threw `Matching` off. Fix the tokenizer. |
| An attribute should be an association but isn't | Its `TypeRef.Name` is `null` or doesn't match the class's `Name` (compare without namespace), or the type is an enumeration (enumerations stay attributes). |
| `MissingMethodException` / `TypeLoadException` | The plug-in was built against a different version of `UmlWizard.CodeImport` or `UmlWizard.Core`. Rebuild it against the installed ones. |
| The class is found but isn't an `ICodeImporter` | A copy of `UmlWizard.CodeImport.dll` or `UmlWizard.Core.dll` was shipped next to the plug-in and loaded twice. Delete it and set `<Private>false</Private>`. |

## Reference

All types are in `UmlWizard.CodeImport.dll`, namespace `UmlWizard.CodeImport` (the diagram types, such as
`UmlVisibility` and `UmlParameter`, are in `UmlWizard.Core.dll`, namespace `UmlWizard.Core.Model`).

### `ICodeImporter`

| Member | Meaning |
|---|---|
| `string Id` | Unique identifier among code importers, e.g. `Php`. |
| `string DisplayName` | The language as shown in the dialog and the notes, e.g. `PHP`. The list is sorted by it. |
| `IReadOnlyList<string> FileExtensions` | The extensions of the files it reads, with the dot, e.g. `.php`. |
| `CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)` | Reads the declarations of all the files. Code that cannot be read is skipped and reported in the notes. |

`SourceFile` is `(string Path, string Text)`. `CodeImportParseResult` is
`(IReadOnlyList<ImportedType> Types, IReadOnlyList<string> Notes)`.

### Imported types

| Type | Members |
|---|---|
| `ImportedType` | `Namespace` (as the language writes it; empty for none); `Name` (simple, without type parameters); `DeclaringTypes` (outermost first; empty for a top-level type); `TypeParameters`; `Kind` (`Class`, `AbstractClass`, `Interface`, `Enumeration`); `Stereotype`; `BaseTypes` (`TypeRef`s, as written); `Attributes`; `Operations`; `Literals`; `TaggedValues`. |
| `TypeRef` | `(Text, Name = null, Arity = 0, IsMany = false, IsOptional = false)`: see [Step 5](#step-5--refer-to-types). |
| `ImportedAttribute` | `Name`; `Type` (a `TypeRef`, or `null` when the language declares none); `Visibility` (default `Private`); `IsStatic`; `DefaultValue` (as written); `TaggedValues`. |
| `ImportedOperation` | `Name` (a constructor is named like its type); `ReturnType` (as written; `null` for constructors and when not declared); `Parameters` (`UmlParameter { Name, Type }`); `Visibility` (default `Public`); `IsStatic`; `IsAbstract`; `TaggedValues`. |
| `ImportedLiteral` | `(Name, Value = null)`: a literal of an enumeration; `TaggedValues` (set with `{ TaggedValues = [...] }`). |
| `TaggedValue` (in `UmlWizard.Core.Model`) | `Name`, `Value`: see [Tagged values](#tagged-values). |

Types with the same `Namespace`, `DeclaringTypes`, `Name` and number of `TypeParameters` are merged into one class.
Their attributes are merged by name.

Their `TaggedValues` are kept on the class, attribute or operation they become, the first of each name winning; see
[Tagged values](#tagged-values).

### Helpers

| Helper | What it does |
|---|---|
| `CLikeTokenizer` | `Tokenize(text, preprocessor = false)`: the `Token`s of C-like code, without comments (and preprocessor lines); `Matching(tokens, open)`: the index of the bracket that closes the one at `open` (`(`, `[`, `{`, `<`); `Join(tokens)`: tokens as readable code. |
| `Token` | `(TokenKind Kind, string Text, int Line)`; `Is(text)`: the token is that text and not a string. `TokenKind` is `Identifier`, `Number`, `String` or `Symbol`. |
| `CodeImporter` | `Add(notes, count, one, many)`: adds "1 *one*." or "*count* *many*." unless the count is zero; `Count(count, one, many)`: "1 file", "2 files"; `Import(source, importer)`: the whole import, as the dialog runs it; `NamespaceTag`: `namespace`, the name of the tagged value that keeps a type's namespace. |
| `SourceFolder` | `Read(folder, extensions)`: the `CodeSource` of a folder (its files of those extensions, and a count of the others), as the dialog reads it. |
