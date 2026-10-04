# UML Wizard

A UML designer built with **Avalonia UI** (application shell) and **SkiaSharp** (diagram rendering). It runs on
Windows, macOS and Linux.

- **Seven diagram types:** class, sequence, state machine, activity, use case, component and deployment diagrams,
  organised in projects and folders.
- **Editing:** in-place editing in UML notation, undo/redo, clipboard, notes and text boxes, fonts, colours and line
  styles, light and dark themes, and **Arrange Diagram** to lay a diagram out automatically.
- **Code generation** from class diagrams to C#, Java, Kotlin, TypeScript, Ruby or C++.
- **Code import:** a class diagram from a folder of C#, C++, Java, Kotlin, Ruby or TypeScript code.
- **OpenAPI** import and export of class diagrams, and **PlantUML** import and export of every diagram type.
- **PNG export**, with a white or transparent background.
- **AI assistant** (optional): Diagram → Create with AI draws a diagram from a description, and View → AI Chat talks
  about the active diagram and proposes changes to it, of any type, which you apply as one undoable step. With Claude, an
  OpenAI-compatible service or a local model (Ollama, LM Studio); see the [design](docs/design/ai-assistant.md).
- **Preferences** (File → Preferences, Ctrl+,): projects folder, display language, theme, the font of the user
  interface, and the default fills, border and thickness of the shapes of each diagram type.
- **Plug-ins:** every code generation and code import language is a plug-in DLL, so more can be added.

## Documentation

- **[User Manual](docs/help/user-manual.html):** how to use the application. It is installed with it and opens from
  **Help → User Manual** (F1).
- **How-to pages** in [`docs/help`](docs/help/), also on the **Help** menu: [Learn UML](docs/help/learn-uml.html),
  [OpenAPI](docs/help/openapi-how-to.html), [PlantUML](docs/help/plantuml-how-to.html) and
  [generating code](docs/help/generating-code-how-to.html).
- **Design documents** in [`docs/design`](docs/design/): [AI assistant](docs/design/ai-assistant.md).
- **Plug-in guides:** [writing a code generator plug-in](docs/plugins/writing-a-codegeneration-plugin.md) and
  [writing a code import plug-in](docs/plugins/writing-a-codeimport-plugin.md).

## Build and run

Requires the .NET 9 SDK (or later).

```bash
dotnet build
dotnet run --project src/UmlWizard.App -- samples/OnlineShop/OnlineShop.umlwiz
dotnet test
```

A project file passed on the command line is opened at start-up.

On Linux, SkiaSharp needs fontconfig and Avalonia the X11 libraries; minimal installs may need `libfontconfig1`,
`libx11-6`, `libice6` and `libsm6`. Wayland sessions run through XWayland.

## Solution layout

| Project | Purpose |
|---|---|
| `src/UmlWizard.Core` | UI-independent: model, JSON serialization, UML text notation, SkiaSharp renderers (drawing, hit testing, PNG export, the automatic layout of imported class diagrams) and the `DiagramEditor` (commands, undo/redo, clipboard, in-line edit sessions). |
| `src/UmlWizard.CodeGeneration` | Code generation from class diagrams: the language-neutral code model, the helpers languages share (naming, type expressions, the code writer), and the plug-in API (`ICodeGenerator`) with the plug-in loader. The OpenAPI export builds on its code model too. |
| `src/UmlWizard.App` | Avalonia application: main window, toolbars, Project Explorer, `DiagramCanvas` control, dialogs. |
| `src/UmlWizard.OpenApi` | OpenAPI import and export of class diagrams (see [OpenAPI](docs/help/user-manual.html#openapi) in the User Manual); reads and writes YAML with YamlDotNet. |
| `src/UmlWizard.Ai` | The AI assistant ([design](docs/design/ai-assistant.md)): the services (Anthropic Messages API, OpenAI-compatible chat completions; HttpClient only, no SDK) with streaming; Create with AI, which asks for PlantUML and imports it; and the chat session, with the editing tools of each diagram type, the placement of what they add, and the whole-diagram merge for models without tool calling. API keys are kept in the OS credential store by the app. |
| `src/UmlWizard.PlantUml` | PlantUML import and export of every diagram type (see [PlantUML](docs/help/user-manual.html#plantuml) in the User Manual), without a dependency: its own readers, one per kind of diagram. |
| `src/UmlWizard.CodeImport` | Import of source code as class diagrams (see [Importing code](docs/help/user-manual.html#import-code) in the User Manual, and [Plug-ins](#plug-ins)): the language-neutral model of imported types, the plug-in API (`ICodeImporter`) and catalog, and the builder that links, draws and lays out the diagram and reports what was left out. |
| `src/Plugins/UmlWizard.CodeImport.*` | The C#, C++, Java, Kotlin, Ruby and TypeScript code importers, each a plug-in DLL that the build copies to `plugins/`. The C# importer reads code with Roslyn, which the application ships and shares with it. |
| `src/Plugins/UmlWizard.CodeGeneration.*` | The C#, Java, Kotlin, TypeScript, Ruby and C++ code generators, each a plug-in DLL that the build copies to `plugins/` (see [Plug-ins](#plug-ins)). |
| `tests/UmlWizard.Core.Tests` | Model, notation, editor, rendering and sample tests. |
| `tests/UmlWizard.CodeGeneration.Tests` | The built-in code generators (the generated C# of the shop and of every sample is compiled) and loading plug-ins from a folder. |
| `tests/UmlWizard.CodeImport.Tests` | The C#, C++, Java, Kotlin, Ruby and TypeScript importers on idiomatic code (for C#, the mapping to UML and the report of what is left out), round trips (the code each generator writes for every sample diagram is imported back and compared), the catalog and the source folder. |
| `tests/UmlWizard.OpenApi.Tests` | OpenAPI import, export and round trips, with OpenAPI 3.0 and Swagger 2.0 fixtures. |
| `tests/UmlWizard.Ai.Tests` | The AI services against a fake HTTP handler (requests, replies, streamed replies, errors, time limit), Create with AI and the chat against a scripted service (tools of every diagram type, placement, whole-diagram merge), including a check that the PlantUML the AI is told to write is what the importer reads. |
| `tests/UmlWizard.PlantUml.Tests` | PlantUML round trips of every sample diagram, hand-written PlantUML of every kind, and what the export reports. |
| `tests/UmlWizard.App.Tests` | Headless Avalonia UI tests that drive the window and canvas with real mouse/keyboard input. |
| `samples/OnlineShop` | An online shop with one diagram of every type (see [Samples](#samples)). |
| `samples/GangOfFour` | The 23 Gang of Four design patterns as class diagrams, grouped into creational, structural and behavioral patterns. |
| `docs/help` | The help pages installed with the application and opened from the **Help** menu. |
| `docs/plugins` | How to write a [code generator plug-in](docs/plugins/writing-a-codegeneration-plugin.md) and a [code import plug-in](docs/plugins/writing-a-codeimport-plugin.md), with a working PHP sample of each (`php-generation-sample`, `php-import-sample`). |

`DiagramCanvas` records each frame into an `SKPicture` on the UI thread and draws it through Avalonia's
`ISkiaSharpApiLeaseFeature`, so the render thread never reads the mutable model.

Class and sequence diagrams have their own models and renderers. State machine, activity, use case, component and
deployment diagrams share one node-and-edge model (`GraphDiagramModel`) and renderer; `GraphRules` lists which node and edge
kinds belong to each of them, their sizes and which connections are valid.

The text of the user interface is in `src/UmlWizard.App/Resources/Strings.resx`; its `Strings` class is in
`Strings.Designer.cs` next to it, which Rider and Visual Studio regenerate when you save the `.resx` (after editing the
`.resx` in another editor, open and save it in one of them; a test fails while the two differ).
Views use `{x:Static res:Strings.Key}`, code uses `Strings.Key`, and texts with placeholders go through
`Loc.Format`. Names of UML element kinds shown to the user (e.g. in combo boxes) are the `Enum_<type>_<value>` entries,
looked up by `Loc.Of`. The app is English only so far; a translation would be a `Strings.<culture>.resx` next to it.
Text from `UmlWizard.Core` (default names of new elements such as `Class1`, error details from loading files) and
UML keywords such as «include» are not in the resources.

## Samples

**OnlineShop** (`samples/OnlineShop/OnlineShop.umlwiz`) shows every diagram type on one subject:

| Folder | Diagram | Type | Shows |
|---|---|---|---|
| Ordering | Domain Model | Class | Classes, an abstract class, an interface and an enumeration; inheritance, realization, association and composition with multiplicities |
| Ordering | Checkout Sequence | Sequence | An actor and object lifelines; synchronous calls, replies and an asynchronous message |
| Ordering | Order Lifecycle | State machine | Initial and final states, a choice, guards and effects, a self-transition, a note attached to a state |
| Ordering | Checkout Process | Activity | Decision and merge, fork and join, guards |
| Requirements | Shop Use Cases | Use case | Actors, a system boundary, «include», «extend», actor generalization |
| Architecture | Shop Components | Component | Components, provided interfaces (realizations), dependencies |
| Architecture | Shop Deployment | Deployment | Devices and a node with nested execution environments and artifacts, communication paths labelled with protocols, «manifest», a note attached to an environment |

**GangOfFour** (`samples/GangOfFour/GangOfFour.umlwiz`) has one class diagram per design pattern of *Design
Patterns* (Gamma, Helm, Johnson, Vlissides), each with the pattern's intent as its description.

## Licence

UML Wizard is licensed under the [Apache License 2.0](LICENSE); its copyright notice is in [NOTICE](NOTICE). The build
copies both next to the executable, as `LICENSE.txt` and `NOTICE.txt`, and **Help → About** names the licence.

## Third-party notices

`THIRD-PARTY-NOTICES.txt` lists every third-party component the application ships with — Material Design Icons,
Avalonia, SkiaSharp, the Inter font and the rest — with its version, source and full license text. The build copies
it next to the executable, and **Help → About** acknowledges the main components and opens the file.

When you add or upgrade a NuGet package, add or update its entry: a test compares the file with the application's
dependency manifest (`UmlWizard.deps.json`) and fails when a shipped package or its version is missing.

## UML support

UML Wizard draws a subset of **UML 2.x** (UML 2.5.1) notation; it does not claim full conformance to the OMG
specification and has no XMI import or export. The [User Manual](docs/help/user-manual.html#diagram-types) lists, per
diagram type, the constructs that are supported and those that are not.

## Plug-ins

Code generators and code importers are plug-ins: .NET DLLs loaded at start-up from two `plugins` folders, the
application's (next to the executable, where the build copies the built-in `UmlWizard.CodeGeneration.<Language>.dll`
and `UmlWizard.CodeImport.<Language>.dll`) and the user's (`%APPDATA%\UmlWizard\plugins` on Windows,
`~/.config/UmlWizard/plugins` on macOS and Linux), which can always be written to, unlike the application's inside a
macOS app bundle or a Linux installation under `/usr`. When both have a DLL with the same name, the application's wins;
a plug-in that cannot be loaded is skipped and reported (in the Generate Code dialog, and on the Languages tab of
**Help → About**, which lists every plug-in with its version and provider, its `<Company>`).

- **Code generators** implement `ICodeGenerator` from `UmlWizard.CodeGeneration` and receive a language-neutral
  `CodeModel` of the diagram. [Writing a code generator plug-in](docs/plugins/writing-a-codegeneration-plugin.md)
  builds one for PHP; the finished sample is in [`docs/plugins/php-generation-sample`](docs/plugins/php-generation-sample/).
- **Code importers** implement `ICodeImporter` from `UmlWizard.CodeImport`: an `Id`, a `DisplayName`, the
  `FileExtensions` it reads, and `Parse`, which reads the declarations of the files into language-neutral
  `ImportedType`s (kind, stereotype, base types, attributes, operations, literals). `CodeImporter` does the rest: it
  merges the parts of each type, links the types, draws and lays out the diagram, and reports what was left out, adding
  the notes the plug-in returns. `CLikeTokenizer` splits the source of C-like languages into tokens for a declaration
  parser. [Writing a code import plug-in](docs/plugins/writing-a-codeimport-plugin.md) builds one for PHP; the finished
  sample is in [`docs/plugins/php-import-sample`](docs/plugins/php-import-sample/).

## File format

A project is a single JSON file (`*.umlwiz`) that contains all of its diagrams in the `diagrams` array.
Projects saved by earlier versions with the `.umlproj` extension still open; they are saved under their existing name.
Each diagram keeps the **UML model** and the **rendering information** in separate sections:

```json
{
  "schemaVersion": 3,
  "name": "Online Shop",
  "description": "…",
  "version": "1.2.0",
  "author": "…",
  "created": "2026-09-25T09:00:00+00:00",
  "modified": "2026-09-25T09:00:00+00:00",
  "diagrams": [
    {
      "id": "d0main000001",
      "name": "Domain Model",
      "description": "…",
      "author": "…",
      "diagramType": "class",
      "model": {
        "kind": "class",
        "classes": [
          { "id": "c1", "name": "Order", "kind": "class",
            "attributes": [ { "name": "total", "type": "decimal", "visibility": "private" } ],
            "operations": [ { "name": "pay", "returnType": "bool", "visibility": "public",
                              "parameters": [ { "name": "amount", "type": "decimal" } ] } ] }
        ],
        "relationships": [ { "id": "r1", "kind": "composition", "sourceId": "c1", "targetId": "c2" } ],
        "notes": [ { "id": "n1", "text": "Totals include tax", "horizontalAlignment": "left", "verticalAlignment": "top" } ],
        "noteAttachments": [ { "id": "a1", "noteId": "n1", "targetId": "c1" } ],
        "textBoxes": []
      },
      "layout": {
        "view": { "zoom": 1, "offsetX": 0, "offsetY": 0 },
        "font": { "family": "Segoe UI", "size": 12, "bold": false, "italic": false },
        "elements": [
          { "elementId": "c1", "x": 400, "y": 250, "width": 240, "height": 150,
            "font": { "family": "Consolas", "size": 16, "bold": true, "italic": false } }
        ]
      }
    }
  ],
  "folders": [
    { "id": "f1", "name": "Ordering", "diagramIds": [ "d0main000001" ],
      "folders": [ { "id": "f2", "name": "Drafts", "diagramIds": [], "folders": [] } ] }
  ]
}
```

`folders` is the hierarchy shown in the Project Explorer. Each folder lists its subfolders and the ids of its
diagrams in display order; diagrams that no folder lists appear at the top level, in the order of `diagrams`.
Note attachments and edges whose ends no longer exist are dropped when the project is loaded.

Each user's open tabs are kept in their application data folder, never in the project file, so opening and arranging
tabs never changes a project that is shared. Every project has a file of its own in `%AppData%\UmlWizard\projects`
(`~/.config/UmlWizard/projects` on Linux and macOS), named after a hash of the project file's path:

```json
{
  "projectPath": "C:\\Work\\Shop.umlwiz",
  "openDiagramIds": [ "d0main000001" ],
  "activeDiagramId": "d0main000001",
  "expandedFolderIds": [ "$project", "f1" ]
}
```

`openDiagramIds` are the tabs to reopen, in order, and `activeDiagramId` the active one. `expandedFolderIds` are the
folders expanded in the Project Explorer, with `$project` for the project itself; without it everything is expanded,
and the folder of the active tab is always expanded so its diagram stays visible. Unknown or repeated ids are dropped. The state follows the project's path, so a project that is moved or renamed opens without its tabs. Projects
saved by earlier versions kept these two properties in the project file; they are read from there until the user has
a state of their own for the project.

Diagrams, classes, attributes and operations may have `taggedValues` (`[{ "name": "required", "value": "true" }]`;
left out when empty), and operations an `httpMethod` and `path` when they are web API endpoints. Older versions
ignore these fields.

Open diagrams are edited on a copy. **Save Project** copies every edited diagram into the project and rewrites the
project file. Creating, renaming, moving and deleting diagrams and folders, and changing properties, save the project
immediately. The open tabs and expanded folders are kept for the user whenever the project is saved, and when the project or the
application is closed.

Every model may have `notes`, `noteAttachments` and `textBoxes`. Notes and text boxes have `text`,
`horizontalAlignment` (`left`/`center`/`right`) and `verticalAlignment` (`top`/`middle`/`bottom`); their position,
size and font are in the layout.

Sequence diagrams use `"kind": "sequence"` with `lifelines` (`name`, `type`, `kind`: `object`/`actor`) and
`messages` (`sourceId`, `targetId`, `text`, `kind`: `synchronous`/`asynchronous`/`reply`/`create`). A
message's layout entry only uses `y`; message order follows the vertical position.

State machine, activity, use case, component and deployment diagrams use `"kind": "stateMachine"`, `"activity"`,
`"useCase"`, `"component"` or `"deployment"` with `nodes` (`id`, `kind`, `name`) and `edges` (`id`, `kind`, `sourceId`, `targetId`, `label`):

```json
"model": {
  "kind": "stateMachine",
  "nodes": [
    { "id": "s0", "kind": "initialState" },
    { "id": "s1", "kind": "state", "name": "Created" },
    { "id": "s2", "kind": "state", "name": "Paid" }
  ],
  "edges": [
    { "id": "t1", "kind": "transition", "sourceId": "s0", "targetId": "s1" },
    { "id": "t2", "kind": "transition", "sourceId": "s1", "targetId": "s2", "label": "pay / capture()" }
  ]
}
```

Node kinds: `state`, `initialState`, `finalState`, `choice` (state machine); `action`, `initialNode`,
`activityFinal`, `decision`, `forkJoin` (activity); `actor`, `useCase`, `systemBoundary` (use case); `component`,
`interface` (component); `node`, `device`, `executionEnvironment`, `artifact`, `component` (deployment). Edge kinds:
`transition`, `controlFlow`, `association`, `include`, `extend`, `generalization`, `dependency`, `realization`,
`communicationPath`, `deploy`, `manifest`.

Fonts are part of the layout: `layout.font` is the diagram font and an element's `font` overrides it. Both are
optional; a missing `family` means the platform default font, and `size` is in points at 100 % zoom (6–72).
Relationships and edges get a layout entry only when they have their own font or line style.

An element's `style` sets the fill and border of its shape: `{ "fill": "#FDE7E7", "border": "#C62828", "borderWidth": 3 }`.
Each part is optional and falls back to the theme's colour for the kind of element (thickness 1.2, allowed 0.5–8);
the whole `style` is omitted while everything is the default.

A connector's `line` sets its colour and thickness: `{ "color": "#1A73E8", "width": 3 }`. Both parts are optional
(thickness 1.3 by default, allowed 0.5–8), and `line` is omitted while both are the default.

Class heights are recomputed from their content; widths are the user's preferred width but never smaller than
the content. Named graph nodes grow to fit their names.

Schema 1 projects stored each diagram in a separate `*.diagram.json` file and listed its `path`. Opening
one embeds the referenced files, and the next save writes the current schema. The old files are left untouched.
Schema 2 projects have no `folders`; all their diagrams appear at the top level.
