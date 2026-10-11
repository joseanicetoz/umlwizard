# Architecture

This document describes the solution layout, core architecture, rendering model, and plug-in system of UML Wizard.

## Solution layout

| Project | Purpose |
|---|---|
| `src/UmlWizard.Core` | UI-independent: model, JSON serialization, UML text notation, SkiaSharp renderers (drawing, hit testing, PNG export, the automatic layout of imported class diagrams) and the `DiagramEditor` (commands, undo/redo, clipboard, in-line edit sessions). |
| `src/UmlWizard.CodeGeneration` | Code generation from class diagrams: the language-neutral code model, the helpers languages share (naming, type expressions, the code writer), and the plug-in API (`ICodeGenerator`) with the plug-in loader. The OpenAPI export builds on its code model too. |
| `src/UmlWizard.App` | Avalonia application: main window, toolbars, Project Explorer, `DiagramCanvas` control, dialogs. |
| `src/UmlWizard.OpenApi` | OpenAPI import and export of class diagrams (see [OpenAPI](../help/user-manual-en.html#openapi) in the User Manual); reads and writes YAML with YamlDotNet. |
| `src/UmlWizard.Ai` | The AI assistant ([design](ai-assistant.md)): the services (Anthropic Messages API, OpenAI-compatible chat completions; HttpClient only, no SDK) with streaming; Create with AI, which asks for PlantUML and imports it; and the chat session, with the editing tools of each diagram type, the placement of what they add, and the whole-diagram merge for models without tool calling. API keys are kept in the OS credential store by the app. |
| `src/UmlWizard.PlantUml` | PlantUML import and export of every diagram type (see [PlantUML](../help/user-manual-en.html#plantuml) in the User Manual), without a dependency: its own readers, one per kind of diagram. |
| `src/UmlWizard.CodeImport` | Import of source code as class diagrams (see [Importing code](../help/user-manual-en.html#import-code) in the User Manual, and [Plug-ins](#plug-ins)): the language-neutral model of imported types, the plug-in API (`ICodeImporter`) and catalog, and the builder that links, draws and lays out the diagram and reports what was left out. |
| `src/Plugins/UmlWizard.CodeImport.*` | The C#, C++, Java, Kotlin, Ruby and TypeScript code importers, each a plug-in DLL that the build copies to `plugins/`. The C# importer reads code with Roslyn, which the application ships and shares with it. |
| `src/Plugins/UmlWizard.CodeGeneration.*` | The C#, Java, Kotlin, TypeScript, Ruby and C++ code generators, each a plug-in DLL that the build copies to `plugins/` (see [Plug-ins](#plug-ins)). |
| `tests/UmlWizard.Core.Tests` | Model, notation, editor, rendering and sample tests. |
| `tests/UmlWizard.CodeGeneration.Tests` | The built-in code generators (the generated C# of the shop and of every sample is compiled) and loading plug-ins from a folder. |
| `tests/UmlWizard.CodeImport.Tests` | The C#, C++, Java, Kotlin, Ruby and TypeScript importers on idiomatic code (for C#, the mapping to UML and the report of what is left out), round trips (the code each generator writes for every sample diagram is imported back and compared), the catalog and the source folder. |
| `tests/UmlWizard.OpenApi.Tests` | OpenAPI import, export and round trips, with OpenAPI 3.0 and Swagger 2.0 fixtures. |
| `tests/UmlWizard.Ai.Tests` | The AI services against a fake HTTP handler (requests, replies, streamed replies, errors, time limit), Create with AI and the chat against a scripted service (tools of every diagram type, placement, whole-diagram merge), including a check that the PlantUML the AI is told to write is what the importer reads. |
| `tests/UmlWizard.PlantUml.Tests` | PlantUML round trips of every sample diagram, hand-written PlantUML of every kind, and what the export reports. |
| `tests/UmlWizard.App.Tests` | Headless Avalonia UI tests that drive the window and canvas with real mouse/keyboard input. |
| `samples/OnlineShop` | An online shop with one diagram of every type. |
| `samples/GangOfFour` | The 23 Gang of Four design patterns as class diagrams, grouped into creational, structural and behavioral patterns. |
| `docs/help` | The help pages installed with the application and opened from the **Help** menu, named `<page>-<language>.html` (e.g. `user-manual-en.html`). The menu opens the page in the display language, or the English one when the page has no translation. |
| `docs/plugins` | How to write a [code generator plug-in](../plugins/writing-a-codegeneration-plugin.md) and a [code import plug-in](../plugins/writing-a-codeimport-plugin.md), with a working PHP sample of each (`php-generation-sample`, `php-import-sample`). |

## Rendering and UI

`DiagramCanvas` records each frame into an `SKPicture` on the UI thread and draws it through Avalonia's
`ISkiaSharpApiLeaseFeature`, so the render thread never reads the mutable model.

Class and sequence diagrams have their own models and renderers. State machine, activity, use case, component and
deployment diagrams share one node-and-edge model (`GraphDiagramModel`) and renderer; `GraphRules` lists which node and edge
kinds belong to each of them, their sizes and which connections are valid.

## Localization

The text of the user interface is in `src/UmlWizard.App/Resources/Strings.resx`; its `Strings` class is in
`Strings.Designer.cs` next to it, which Rider and Visual Studio regenerate when you save the `.resx` (after editing the
`.resx` in another editor, open and save it in one of them; a test fails while the two differ).
Views use `{x:Static res:Strings.Key}`, code uses `Strings.Key`, and texts with placeholders go through
`Loc.Format`. Names of UML element kinds shown to the user (e.g. in combo boxes) are the `Enum_<type>_<value>` entries,
looked up by `Loc.Of`. The app is English only so far; a translation would be a `Strings.<culture>.resx` next to it.
Text from `UmlWizard.Core` (default names of new elements such as `Class1`, error details from loading files) and
UML keywords such as «include» are not in the resources.

## Plug-ins

Code generators and code importers are plug-ins: .NET DLLs loaded at start-up from two `plugins` folders, the
application's (next to the executable, where the build copies the built-in `UmlWizard.CodeGeneration.<Language>.dll`
and `UmlWizard.CodeImport.<Language>.dll`) and the user's (`%APPDATA%\UmlWizard\plugins` on Windows,
`~/.config/UmlWizard/plugins` on macOS and Linux), which can always be written to, unlike the application's inside a
macOS app bundle or a Linux installation under `/usr`. When both have a DLL with the same name, the application's wins;
a plug-in that cannot be loaded is skipped and reported (in the Generate Code dialog, and on the Languages tab of
**Help → About**, which lists every plug-in with its version and provider, its `<Company>`).

- **Code generators** implement `ICodeGenerator` from `UmlWizard.CodeGeneration` and receive a language-neutral
  `CodeModel` of the diagram. [Writing a code generator plug-in](../plugins/writing-a-codegeneration-plugin.md)
  builds one for PHP; the finished sample is in [`docs/plugins/php-generation-sample`](../plugins/php-generation-sample/).
- **Code importers** implement `ICodeImporter` from `UmlWizard.CodeImport`: an `Id`, a `DisplayName`, the
  `FileExtensions` it reads, and `Parse`, which reads the declarations of the files into language-neutral
  `ImportedType`s (kind, stereotype, base types, attributes, operations, literals). `CodeImporter` does the rest: it
  merges the parts of each type, links the types, draws and lays out the diagram, and reports what was left out, adding
  the notes the plug-in returns. `CLikeTokenizer` splits the source of C-like languages into tokens for a declaration
  parser. [Writing a code import plug-in](../plugins/writing-a-codeimport-plugin.md) builds one for PHP; the finished
  sample is in [`docs/plugins/php-import-sample`](../plugins/php-import-sample/).
