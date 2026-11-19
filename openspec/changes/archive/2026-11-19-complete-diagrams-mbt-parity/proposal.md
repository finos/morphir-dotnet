# Proposal

## Why

`Morphir.Diagrams` is a migration of [diagrams.mbt at 60a8dd1](https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07), but it reads only two of the diagram families upstream supports: Mermaid flowcharts and Mermaid sequences. An author who keeps a state machine, an architecture view, a BPMN process or an EBNF grammar next to a Morphir model gets a parse error today, and cannot annotate, link or play that diagram with the knowledge features that flowcharts and sequences now have. The migration PRD lists the other families as planned. This change delivers all of them, with the rest of upstream's rendering features, so that the .NET library is a full replacement for diagrams.mbt.

For example, this source fails today and renders after this change, with the same themes, element references, comments and offline player as a flowchart:

```text
stateDiagram-v2
  [*] --> Draft
  Draft --> Review : submit
  Review --> Approved : accept
  Review --> Draft : reject
  Approved --> [*]
```

## What Changes

- **Mermaid families:** state diagrams (`stateDiagram`, `stateDiagram-v2`), architecture diagrams (`architecture-beta`), swimlanes, and ZenUML, which reads into the existing sequence model.
- **PlantUML:** a shared `@startuml` envelope and reader, with activity diagrams, state diagrams, regular expressions and EBNF (drawn as railroad diagrams), and JSON and YAML (drawn as data trees).
- **BPMN 2.0 XML:** reading, validating and writing process and collaboration documents, and rendering them. The XML reader prohibits DTDs and uses no resolver.
- **Tree queries:** the upstream query language over BPMN and other diagram trees.
- **Remaining Mermaid syntax:** flowchart styles, classes, click and link statements, configuration and direction overrides, subgraph endpoints; sequence participant metadata, boxes, creation and destruction, central connections and links. Every Mermaid and PlantUML family gains a canonical printer.
- **Rendering features:** rounded and bridged connectors, playback route exploration, trace evidence for railroad diagrams, and raster export to PNG, JPEG and animated GIF, deterministic and without native dependencies.
- **Icons:** participant and architecture symbols for every key in upstream's catalogue. AWS, Azure and Google Cloud symbols are copied unchanged under each provider's icon terms, which ask that shapes and colours be kept. The UML symbols are copied from ZenUML under MIT. Technology logos are copied where their SVG Repo licence allows and drawn originally otherwise. The one share-alike symbol, `externalsystem`, is redrawn so rendered diagrams never carry share-alike terms. Third-party and trademark notices name every source and owner.
- **Knowledge, themes and player for every family:** element references, selections, annotations and links, colour families in every built-in theme, and the offline player with its notes panel.
- Nothing existing is removed. The public API grows. Existing flowchart and sequence SVG changes only where the shared layout and connector work improves it, and every such change is reviewed through snapshot diffs.

## Capabilities

### New Capabilities

- `diagram-source-reading`: one entry point that detects the family of Mermaid, PlantUML and BPMN input and reports typed diagnostics, and canonical printing of every text family.
- `mermaid-diagram-syntax`: the Mermaid flowchart and sequence syntax still missing, and the state, architecture, swimlane and ZenUML grammars.
- `plantuml-diagram-syntax`: the `@startuml` envelope and the activity, state, regex, EBNF, JSON and YAML grammars.
- `state-diagrams`: the state model and its SVG rendering, shared by Mermaid and PlantUML.
- `architecture-diagrams`: the architecture model and its SVG rendering.
- `swimlane-diagrams`: the swimlane model, shared by Mermaid swimlanes and PlantUML activity diagrams, and its SVG rendering.
- `railroad-diagrams`: the railroad model for regular expressions and EBNF, its SVG rendering and trace evidence.
- `structured-data-diagrams`: the data-tree model for JSON and YAML and its SVG rendering.
- `bpmn-documents`: BPMN 2.0 XML reading, validation, writing and traversal.
- `bpmn-diagrams`: BPMN process and collaboration rendering.
- `diagram-tree-queries`: the query language over diagram trees.
- `diagram-icons`: the symbol catalogue, its provenance and its rendering.
- `diagram-connectors`: rounded and bridged connector styling shared by every family.
- `diagram-playback-exploration`: route exploration in the offline player.
- `diagram-raster-export`: PNG, JPEG and animated GIF output.
- `diagram-family-knowledge`: element references, selections, annotations, themes and player support for each new family.

### Modified Capabilities

None. The flowchart, sequence and linked-metadata behaviour has no main spec yet; `add-diagram-linked-metadata` is still an open change on the same branch.

## Tracking

Delivered on the same branch and pull request as the diagrams migration, [finos/morphir-dotnet#692](https://github.com/finos/morphir-dotnet/pull/692). Related: the migration PRD at `docs/content/contributing/design/prds/migrate-diagrams.md` and the open change `add-diagram-linked-metadata` (#689).

## Impact

- `Morphir.Diagrams`: new public model types, readers and render entry points for each family; new options for icons, connector style and raster export. Existing members keep their behaviour.
- `Morphir.Svg`: any scene features the new layouts need (paths with arcs, symbol definitions, text runs).
- Player scripts: route exploration and support for the new families' elements.
- New embedded resources: the symbol catalogue (about 1.6 MB of SVG before normalisation) and the raster fonts (Noto Sans Mono and Noto Sans Symbols 2, SIL OFL 1.1), each with provenance and licence notices. The library stays trimmable and Native AOT compatible, with no native dependencies.
- Tests: pinned upstream fixtures with `PROVENANCE.md` rows in the fixture catalog, per-family unit tests in C# and F#, and Chromium checks of every family in every theme.
- Size: upstream's sources for these families total about 1.5 MB of MoonBit, so the pull request grows substantially.
