# Spec Delta

## Purpose

Defines the PlantUML 1.2026.9 syntax that `Morphir.Diagrams` reads without a PlantUML, Java or Graphviz runtime: one block envelope, activity diagrams in the modern and legacy dialects, state diagrams, regular expressions, EBNF, JSON and YAML.

## ADDED Requirements

### Requirement: Block envelope and dispatch
The reader SHALL read exactly one block delimited by `@startuml`, `@startregex`, `@startebnf`, `@startjson` or `@startyaml` and its matching end keyword, and SHALL return the typed kind. A wrong closer, multiple blocks or trailing non-trivia SHALL produce diagnostics. Reading a raw body SHALL require the caller to name the kind.

#### Scenario: Wrong closer
- **WHEN** a block opens with `@startjson` and closes with `@enduml`
- **THEN** reading reports a closer diagnostic at `@enduml`

### Requirement: Activity dialects
The reader SHALL accept the modern activity profile (actions, `start`/`stop`/`end`, `if`/`elseif`/`else`/`endif`, `switch`/`case`, `repeat`/`backward`/`break`, `while`/`endwhile`, `fork`/`fork again`/`end fork`/`end merge`, `split`, `detach`, `kill`, connectors, `label`/`goto`, `partition`, `group`, `|Lane|`, notes) and the legacy profile (`(*)`, `(*top)`, aliases with `as`, `===bar===` synchronisation bars). Mixed or ambiguous dialects SHALL produce a diagnostic.

#### Scenario: Legacy start
- **WHEN** a `@startuml` block body is `(*) --> "Check" --> (*)`
- **THEN** it is read as a legacy activity diagram with one action

#### Scenario: Break leaves the loop
- **WHEN** a `repeat` body contains `break`
- **THEN** the converted flowchart has an edge from the break to the loop exit and no fall-through edge

### Requirement: State syntax
The reader SHALL accept PlantUML state declarations, quoted aliases, descriptions, transitions with arrow hints, `[*]`, composites, `--` and `||` concurrent separators, `<<choice>>`, `<<fork>>`, `<<join>>`, `[H]` and `[H*]` history, `<<entryPoint>>`, `<<exitPoint>>`, pins, expansion symbols and notes. Entry, do, exit and internal lines SHALL stay descriptions and SHALL NOT become transitions.

#### Scenario: Deep history
- **WHEN** a transition targets `Active[H*]`
- **THEN** the model has a deep-history pseudostate scoped to `Active` and no restored state is inferred

### Requirement: Regular expression syntax
The reader SHALL represent literals, escapes, character classes, concatenation, alternation, groups with names, anchors, lookarounds and quantifiers with bounds and modifiers. Unsupported syntax SHALL stay in the syntax tree with a diagnostic.

#### Scenario: Large bound is not expanded
- **WHEN** a pattern contains `a{1000000}`
- **THEN** the model stores the bound once and reading stays within its work limit

### Requirement: EBNF syntax
The reader SHALL represent ordered rules with Unicode names, terminals, references, choices, concatenation, grouping, optional and repeated parts, counts, special sequences, exclusions, empty expressions and notes. Recursion SHALL remain a reference.

#### Scenario: Recursive rule
- **WHEN** a rule `list = item, [",", list];` is read
- **THEN** the model keeps `list` as a reference inside its own definition

### Requirement: JSON and YAML values
The JSON reader SHALL keep member order, duplicate members and exact number lexemes, and SHALL diagnose invalid escapes and numbers. The YAML reader SHALL follow the PlantUML profile, keeping scalars such as `enabled: true` as text, and SHALL NOT expand aliases, load resources or construct objects.

#### Scenario: Exact number
- **WHEN** a JSON block contains `123.4567890123456789`
- **THEN** the model keeps that lexeme unchanged

### Requirement: Highlights and presentation
Titles, notes, highlight selectors and style commands SHALL be kept apart from the data. Highlight paths SHALL keep quoted segments and array indices. An unresolved selector SHALL produce a diagnostic.

#### Scenario: Quoted path segment
- **WHEN** a JSON block has `#highlight "a/b" / 0`
- **THEN** the highlight targets the first element of member `a/b`
