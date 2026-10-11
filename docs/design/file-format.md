# Design: File format

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

## User state

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

## Diagram models

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

## Layout and styling

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

## Schema versions

Schema 1 projects stored each diagram in a separate `*.diagram.json` file and listed its `path`. Opening
one embeds the referenced files, and the next save writes the current schema. The old files are left untouched.
Schema 2 projects have no `folders`; all their diagrams appear at the top level.
