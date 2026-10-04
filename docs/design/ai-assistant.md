# Design: AI assistant

Status: steps 1, 2 and 3 implemented (AI service in Preferences, Create with AI, the chat panel with tools for every
diagram type, streaming, Markdown, consent and the whole-diagram mode); step 4 proposed.

## Goal

Let a large language model help people build and understand diagrams, without UML Wizard depending on one: the
assistant is optional, configured by the user, and every change it makes goes through the same editor, rules and undo
as a change made by hand.

Inspired by SourceGit, which lets the user configure an OpenAI-compatible service in its preferences and uses it to chat
about a repository and to write commit messages from the staged changes. UML Wizard's equivalent of "the staged
changes" is the diagram, which it can already express as text (PlantUML, for every diagram type) and change through
`DiagramEditor`.

## Principles

- **Optional.** Nothing is sent anywhere until the user configures a service in Preferences. "None" is the default.
- **Provider-agnostic.** Claude (Anthropic Messages API), OpenAI-compatible services (OpenAI, Azure OpenAI, OpenRouter,
  …) and local models (Ollama, LM Studio, which speak the OpenAI API), behind one interface.
- **Private by choice.** The user sees which service is used; local models keep diagrams on the machine. API keys are
  kept in the operating system's credential store, never in `settings.json`.
- **The app executes, the AI suggests.** The model produces structured output (PlantUML now, editor tool calls later);
  UML Wizard parses, validates and applies it with its own code: `PlantUmlImporter`, `GraphRules`, `DiagramEditor`,
  `DiagramArranger`. Invalid output is rejected or repaired, never drawn as is.
- **Undoable and reviewable.** What the assistant adds is a new diagram (step 2) or one undo step with a preview
  (step 3).
- **Testable without a network.** Providers sit behind `IAiProvider`; tests use a fake that returns scripted replies,
  as `FakeServices` does for dialogs.

## Architecture

```
UmlWizard.App ──► UmlWizard.Ai ──► UmlWizard.PlantUml ──► UmlWizard.Core
   │                 │
   │                 ├─ IAiProvider            one request, one reply (text; tool calls in step 3)
   │                 ├─ AnthropicProvider       POST {endpoint}/v1/messages
   │                 ├─ OpenAiCompatibleProvider POST {endpoint}/chat/completions
   │                 ├─ AiServiceSettings / AiProviderKind / AiProviders (factory, defaults)
   │                 └─ AiDiagramGenerator      description → PlantUML → DiagramDocument(s)
   │
   ├─ ISecretStore: Windows Credential Manager, macOS Keychain, Linux Secret Service (secret-tool),
   │                with ANTHROPIC_API_KEY / OPENAI_API_KEY as fallbacks
   ├─ Preferences → AI tab
   └─ Diagram → Create with AI… (dialog, busy overlay with Cancel)
```

`UmlWizard.Ai` has no Avalonia dependency, like the other feature projects. It uses `HttpClient` and `System.Text.Json`
only: no SDK packages, so no new third-party notices.

### Providers

| Provider | Endpoint (default) | Authentication | Model (default) |
|---|---|---|---|
| Anthropic | `https://api.anthropic.com` | `x-api-key`, `anthropic-version: 2023-06-01` | `claude-sonnet-5-5` |
| OpenAI-compatible | `https://api.openai.com/v1`; e.g. `http://localhost:11434/v1` for Ollama | `Authorization: Bearer` when a key is set (local servers need none) | none: the user names it |

Both send a system prompt and one user message and return the text of the reply. Errors (HTTP status, the service's
error message, timeouts, no connection) become an `AiException` with a message fit for the user. Requests time out
after two minutes and can be cancelled.

Claude Sonnet 5.5 is the default: good PlantUML at moderate cost. Claude Opus 5.5 is the choice for large or subtle
diagrams, Claude Haiku 4.5 for quick assists (step 4).

### Settings and secrets

`AppSettings.Ai` holds the provider, endpoint and model. The API key is kept per provider in the credential store:

| Platform | Store | How |
|---|---|---|
| Windows | Credential Manager, target `UmlWizard:<name>` | `CredWriteW` / `CredReadW` / `CredDeleteW` (P/Invoke) |
| macOS | Keychain, service `UmlWizard` | `security add-generic-password -U` / `find-generic-password -w` / `delete-generic-password` |
| Linux | Secret Service (GNOME Keyring, KWallet) | `secret-tool store` / `lookup` / `clear` (libsecret-tools) |

When the store has no key, `ANTHROPIC_API_KEY` or `OPENAI_API_KEY` is used, which also suits CI and machines without a
keyring. The key is written when the Preferences window closes (or when Test Connection is clicked), not on every key
stroke.

## Step 1: AI service in Preferences (implemented)

A fourth tab, **AI**:

- **Service**: None, Anthropic (Claude), OpenAI-compatible (OpenAI, Ollama, LM Studio, …).
- **Endpoint**: empty for the provider's default, shown as the watermark.
- **Model**: empty for the default (Anthropic), shown as the watermark.
- **API key**: a password box; a caption says where it is kept.
- **Test Connection**: sends a one-line request and says whether the service answered, and with which model.
- A note on privacy: diagrams are sent to the chosen service when an AI command is used, and only then.

## Step 2: Create with AI (implemented)

**Diagram → Create with AI…** turns a description into a diagram:

1. A dialog asks for the description and the diagram type (or "Let the AI choose").
2. A busy overlay with **Cancel** covers the window while the service works.
3. `AiDiagramGenerator` sends a system prompt describing the PlantUML that UML Wizard reads (from the PlantUML how-to:
   the constructs of each diagram type), and asks for one `@startuml` … `@enduml` block with a `title`.
4. The reply goes through `PlantUmlImporter`, which lays the diagram out. If nothing could be read, the generator asks
   once more, with what went wrong; then it gives up with a message.
5. The diagram is added to the folder of the Project Explorer selection and opened, like a PlantUML import; its
   description records the request. Anything the import could not keep is listed.

Why PlantUML rather than JSON or tool calls for this step: models write PlantUML well (it is common in their training
data), it is compact, and the importer already validates and lays out every diagram type, so the step needs almost no
new modelling code.

## Step 3: Chat panel with editor tools

### 3a (implemented)

- **View → AI Chat** (Ctrl+Alt+I): a panel on the right, one conversation per diagram tab (`AiChatViewModel`, not saved).
- `IAiProvider.ChatAsync`: conversations with tool calls, in both APIs (Anthropic `tool_use` / `tool_result` blocks;
  OpenAI `tools`, `tool_calls` and `role: tool` messages). No streaming yet.
- `ClassDiagramTools`: add, set, rename and delete classes; add attributes and operations in UML notation (parsed by
  `UmlNotation`, as typed in the editor); remove members; add and remove relationships; add notes. Classes by name; bad
  calls change nothing and return an error the model can act on.
- `AiChatSession`: the loop (at most 10 rounds of tool calls per message) on a working copy; a failed or cancelled request
  leaves the conversation and the copy as they were. The model is told, with the next message, when its changes were
  applied or discarded. Every request carries the diagram (as PlantUML) in the system prompt, so the model always sees
  the current state.
- `ClassPlacement`: new classes next to a related class, in free space, without moving others; in an empty diagram,
  Arrange.
- `DiagramChanges`: the preview lines; **Apply** runs `DiagramEditor.ReplaceContent`, one undo step; **Discard**.
- Other diagram types can be discussed, not changed.

### 3b (implemented)

- **Tools for every diagram type.** `DiagramTools` is the base (dispatch, errors as results, notes, argument reading);
  `ClassDiagramTools`, `GraphDiagramTools` and `SequenceDiagramTools` derive from it. `DiagramTools.For(document)`
  picks them.
  - Graph diagrams (state machine, activity, use case, component, deployment): `add_node`, `rename_node`,
    `delete_node`, `move_node` (diagrams with containers), `add_edge`, `set_edge_label`, `remove_edge`, `add_note`.
    The schemas list the node and edge kinds of the diagram type (`GraphRules`), and edges are checked with
    `GraphRules.CheckEdge`, as in the editor.
  - Nodes without a name are referred to by kind and number (`decision#1`), and duplicate names get `#2`…
    (`ElementReferences`). The system prompt lists the references, the containment and the edges (`Outline`), since
    the PlantUML of activity diagrams has no names for decisions or bars.
  - Containment is geometric in UML Wizard (a use case is in a system when drawn inside it). `inside` puts a node in a
    container, and `GraphPlacement` places it in free space inside, growing the container (and its containers) when full.
  - Sequence diagrams: `add_lifeline`, `set_lifeline`, `delete_lifeline`, `add_message` (at a `position`),
    `set_message`, `remove_message`, `add_note`. Messages are numbered in time order (vertical position);
    `SequencePlacement` keeps each message where it was when there is room above it, else just below the one before, so
    an inserted message pushes the later ones down. New lifelines go to the right.
  - `DiagramChanges` describes node, edge, lifeline and message changes; `FreeSpace` is shared by the placements.
- **Streaming.** `IAiProvider.StreamChatAsync` reports the text so far; a default implementation reports the whole
  answer once. The HTTP providers read server-sent events: Anthropic `content_block_start` / `content_block_delta`
  (`text_delta`, `input_json_delta` for tool arguments), OpenAI `choices[0].delta` (`content`, `tool_calls` pieces by
  index). A service that answers with plain JSON is read whole; an `error` event is an `AiException`. Each round of tool
  calls starts its text afresh. A failed or stopped request removes the partial answer.
- **Markdown.** Replies are Markdown (the instructions ask for paragraphs, lists, bold and code, no tables).
  `MarkdownText` parses that subset without a package; `MarkdownView` shows it as selectable text blocks.
- **Consent per project.** The first message of a project asks whether its diagrams may be sent to the named service;
  allowed projects are kept by full path in `AppSettings.AiAllowedProjects` (a project without a file is allowed for the
  session). Cancel keeps the message.
- **Whole-diagram mode** (`AiServiceSettings.ChangeMode`, Preferences → AI → Changes). No tools are sent; the model is
  told to answer with the whole revised diagram in PlantUML, with the syntax of the type. `DiagramMerge` imports it and
  matches elements by name (classes; nodes by kind and name, nameless ones by kind in order; lifelines; messages by
  ends, text and kind in order; notes by text): matched elements keep id, layout, and what PlantUML does not carry
  (tagged values, unchanged members, styles); text boxes stay; the new ones are placed as above. The block is hidden from
  the answer, also while it streams.

### Original design

A dockable panel next to the canvas, bound to the active diagram:

- Each request carries the diagram as PlantUML (compact) plus its name, description and tagged values; optionally the
  project's other diagrams.
- The model may call tools that map to `DiagramEditor`: `add_class`, `add_attribute`, `add_operation`,
  `add_relationship`, `add_node`, `add_edge`, `rename`, `delete`, `set_note`, `arrange`. Element references are by name,
  resolved by the app.
- Tool calls run against a copy; the panel shows a preview ("+2 classes, +3 relationships, 1 renamed") with **Apply** and
  **Discard**. Apply runs them as one `DiagramEditor.Execute`, so one Undo takes them back. Rule violations
  (`GraphRules.CheckEdge`) are returned to the model as tool errors so it can correct itself.
- Replies stream; the conversation is per diagram and not saved with the project.

`IAiProvider` grows a `CompleteAsync` overload with tools and returns tool calls (Anthropic `tool_use` blocks, OpenAI
`tool_calls`).

## Step 4: One-click assists (proposed)

In the chat panel and the diagram's context menu:

| Assist | Output |
|---|---|
| Review | Missing multiplicities, unclear navigability, god classes, cycles, naming inconsistencies; states without exits, activities without a final node |
| Explain | A plain-language walkthrough; can fill in the diagram's description |
| Derive | A state machine from an enumeration's lifecycle, a sequence diagram from a use case and the class model |
| Focus an import | From a large code import, the core classes in a smaller diagram |
| Help | Answers "how do I…" grounded on the User Manual |

Later candidates: method bodies and routing attributes in generated code, «api» design from the domain model, naming
suggestions.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Confidential models leave the machine | Off by default; the service is named in the UI; local models supported |
| Wrong or invalid output | Parsed and validated by the app's own importer and rules; one repair attempt; nothing replaces existing work in step 2 |
| Cost and latency | PlantUML instead of JSON; only the active diagram by default; Sonnet as the default, Haiku for small tasks; Cancel |
| Keys leaking | OS credential store; never logged; not part of settings or projects |
| Provider churn | One interface; OpenAI-compatible covers most services; providers could become plug-ins like the languages |

## Testing

- Providers: a fake `HttpMessageHandler` checks the request (URL, headers, body) and feeds replies and errors.
- The generator: a fake `IAiProvider` returns scripted PlantUML, broken PlantUML (repair path) and nothing usable.
- The app: Preferences → AI and Create with AI run against a fake provider and an in-memory secret store; no test talks
  to a real service.
