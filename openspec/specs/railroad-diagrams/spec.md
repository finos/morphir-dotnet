# railroad-diagrams Specification

## Purpose
Represents regular expressions and EBNF grammars as immutable railroad models and draws them as railroad diagrams. Supplied match and failure traces from an external matcher are shown as highlights; the library itself never matches or parses input against the pattern.

## Requirements

### Requirement: Pattern and grammar models
The library SHALL represent patterns and grammars independently of PlantUML, keeping consumed atoms, choices, groups, repetitions with bounds, zero-width assertions, rule references, special sequences and exclusions, each with a stable occurrence identity. Equal text SHALL NOT merge occurrences.

#### Scenario: Two equal literals
- **WHEN** a pattern is `ab|ab`
- **THEN** the model has two alternatives with distinct occurrence IDs

### Requirement: Railroad layout
The renderer SHALL measure labels and place the entry, exit, ordered fragments, alternatives, optional bypasses and repeat loops, drawing literals, classes, references, assertions and exclusions distinctly. Repetition bounds SHALL be shown, and references SHALL NOT be inlined.

#### Scenario: Optional part
- **WHEN** an EBNF rule contains `[ sign ]`
- **THEN** the diagram draws a bypass track around `sign`

### Requirement: Trace evidence values
The library SHALL accept immutable trace values from a caller-supplied matching or parsing provider, with ordered visits, outcomes (matched, non-matched, unsupported, exhausted, error), optional input spans in UTF-16 and failure points. Traces that name a different model or invalid occurrences SHALL be rejected.

#### Scenario: Stale trace
- **WHEN** a trace built for one pattern is applied to another
- **THEN** rendering rejects it with a diagnostic and does not guess from labels

### Requirement: Highlight channels
Static rendering SHALL draw supplied matched paths, non-matched paths, taken prefixes and failure points as independent, theme-aware layers without changing layout. Rendering with no trace SHALL give the same bytes as before traces existed.

#### Scenario: Failure point only
- **WHEN** a trace marks a failure at the third terminal and no taken prefix
- **THEN** the SVG highlights only that terminal
