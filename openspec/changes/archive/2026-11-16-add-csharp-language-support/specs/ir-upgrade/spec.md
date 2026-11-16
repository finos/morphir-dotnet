# Spec Delta

## Purpose

Brings a model of an older IR format to the current IR before the middle end and the backends run, with one small step for each pair of neighbouring formats and a record of each node that a step moves. Contracts that run later never name a format version: a new IR adds one step and moves "current".

Example: a classic `3.0.0` model goes to the current IR in one step. A step that removes an unreachable match case emits one record, so a sidecar key of the old model finds the node again:

```text
before: …?format=3.0.0&rev=sha256:bbbb…#/module/domain/value/evaluate/body/pattern-match/case/1/body
after:  …?format=4.0.0#/module/domain/value/evaluate/body/pattern-match/case/0/body
rule:   <IRI of the upgrade step>
```

## ADDED Requirements

### Requirement: Upgrade steps go from one format version to the next

An upgrade step SHALL declare the format version that it reads and the format version that it writes. A host SHALL join the registered steps from the format version of a model to the current IR. A new current IR SHALL add one step and SHALL NOT change an existing step.

#### Scenario: Classic model reaches the current IR

- **WHEN** a classic `3.0.0` model is upgraded
- **THEN** the host runs the registered steps in order and returns a model in the current IR

#### Scenario: Model already current

- **WHEN** the model is already in the current IR
- **THEN** the host runs no step and returns the model unchanged

#### Scenario: No path to the current IR

- **WHEN** no registered chain of steps reaches the current IR from the format version of the model
- **THEN** the upgrade fails with a diagnostic that names the format version, and returns no model

### Requirement: A step reports what it cannot express

A step SHALL return a checked result. When the model has a node that the next format cannot express, the step SHALL return an error diagnostic with the location of the node and SHALL NOT return a model that approximates it.

#### Scenario: Node without a target form

- **WHEN** a step reads a node that has no form in the next format
- **THEN** the result is an error diagnostic that names the node, and no model

### Requirement: A step emits an application list

Each step SHALL return, with its model, one record for each node that it moves or replaces. A record SHALL hold the id of the step as an absolute IRI, the semantic node address before, pinned to the input snapshot, and the semantic node address after. A node that is not in the list SHALL keep its path.

#### Scenario: Moved node

- **WHEN** a step removes an unreachable match case and the later cases move up by one
- **THEN** the list has one record for each later case, with the address before and the address after

#### Scenario: Unchanged node

- **WHEN** a step keeps a node at the same path
- **THEN** the list has no record for that node

### Requirement: The upgrade is deterministic and does not change its input

The same input model SHALL give the same output model and the same list, on every machine. The upgrade SHALL NOT change the input value, read a file or start a process.

#### Scenario: Repeated upgrade

- **WHEN** the same classic model is upgraded twice
- **THEN** the two models and the two lists are equal

### Requirement: The generate command upgrades for a current-IR backend

`morphir generate` SHALL upgrade a model when the selected backend claims the current IR only. A backend that reads the classic model directly SHALL receive the model unchanged, and an F# generation request SHALL behave as before this change.

#### Scenario: Classic model and the C# backend

- **WHEN** `morphir generate --target csharp` reads a classic model
- **THEN** the C# backend receives the upgraded model and the generation succeeds

#### Scenario: Classic model and the F# backend

- **WHEN** `morphir generate --target fsharp` reads a classic model
- **THEN** the F# backend receives the classic model and the output is the same as before this change
