# Spec Delta

## Purpose

Shows the Morphir refusals of F# source while the user edits it and in CI, before any compile, with the same codes, messages and ranges as the F# frontend.

## ADDED Requirements

### Requirement: The analyzer uses the frontend's rule catalog
The analyzer SHALL refuse exactly the constructs that the F# frontend's admission catalog refuses. For the same document, it SHALL report the same codes, messages and ranges as the frontend's admission stage, after the same nested-refusal reduction. It SHALL NOT add or omit a rule.

#### Scenario: Catalog parity
- **WHEN** the analyzer and the frontend admission stage each check every example in the rule catalog
- **THEN** both report the same list of `fsharp.unsupported_*` codes and ranges for each example

#### Scenario: Several refusals in one document
- **WHEN** a document contains `let mutable total = 0` and a class declaration
- **THEN** the analyzer reports one `fsharp.unsupported_mutable` diagnostic at `mutable` and one `fsharp.unsupported_class` diagnostic at the class, and nothing for the members of the class

### Requirement: Analyzer diagnostics are errors at the source range
Each refusal SHALL be an analyzer diagnostic of severity error. Its code SHALL be the catalog code. Its message SHALL be the catalog message and suggestion. Its range SHALL be the refused construct's source range in the analyzed document.

#### Scenario: Mutable binding in the editor
- **WHEN** an editor that loads F# analyzers opens a Morphir project document with `let mutable total = 0` on its third line
- **THEN** the editor shows an error with code `fsharp.unsupported_mutable` on the word `mutable` of that line

### Requirement: The analyzer reports only for Morphir projects
The analyzer SHALL report refusals only for a document whose directory, or an ancestor directory, holds a `morphir.toml` file. For any other F# document, it SHALL report nothing.

#### Scenario: Ordinary F# project
- **WHEN** the analyzer checks a document with `let mutable count = 0` and no ancestor directory has `morphir.toml`
- **THEN** the analyzer reports no diagnostic

### Requirement: The analyzer does not report code that the compiler rejects
When the F# compiler reports an error for a document, the analyzer SHALL report no refusal for that document. This matches the frontend, which reports only compiler errors in that case.

#### Scenario: Type error and refusal in one document
- **WHEN** a document has an F# type error and also `let mutable total = 0`
- **THEN** the analyzer reports no `fsharp.unsupported_*` diagnostic for the document

### Requirement: CI runs the analyzer and writes SARIF
The analyzer package SHALL work with the FSharp.Analyzers.SDK command-line tool. A CI run over a Morphir project SHALL give a SARIF report that holds each refusal with its code, message, document and range. The CI step SHALL fail when the report holds a refusal.

#### Scenario: CI report
- **WHEN** CI runs the F# analyzers tool with the Morphir analyzer on a project whose `Pricing.fs` has `let mutable total = 0`
- **THEN** the SARIF report holds one result with rule `fsharp.unsupported_mutable` located in `Pricing.fs`, and the CI step fails
