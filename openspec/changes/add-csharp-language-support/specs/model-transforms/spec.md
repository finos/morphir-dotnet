# Spec Delta

## Purpose

Lets a host run passes that change a model from Morphir IR to Morphir IR: validation, transformation and rewriting. A pass is a registered extension, compiled code with a catalog entry, and the engine records each rewrite so a later stage can follow a node to where it went. Nodes are named with the semantic node address of `finos/morphir`.

Example: the rule "an identity lambda becomes its argument" runs on a value `net`. The body changes from `Apply (Lambda x x) price` to `price`. The log has one record, and the new body states its origin.

## ADDED Requirements

### Requirement: Passes are registered extensions with a stage

A transform pass SHALL be a built-in or installed extension of the type `transform`. Its claim SHALL name one stage: after a frontend, or before a backend of one target id. A host SHALL run the passes of a stage in a stable order.

#### Scenario: Target-side pass

- **WHEN** `morphir generate --target csharp` runs
- **THEN** the host runs the passes of the stage "before the backend of `csharp`" and no pass of another target

#### Scenario: Pass of another target

- **WHEN** a pass claims the stage "before the backend of `fsharp`"
- **THEN** a `csharp` generation does not run it

### Requirement: Passes read the current IR only

A pass SHALL read and write the current IR. When a command has a model of an older family and no upgrade applies, the host SHALL skip the passes and SHALL report one diagnostic that says so.

#### Scenario: Older family and no upgrade

- **WHEN** a command has a classic model for a backend that reads classic models
- **THEN** the passes do not run and the result has one `info` diagnostic that names the reason

### Requirement: A pass gives a checked result

A pass SHALL return the changed module and the list of applications, or an error diagnostic. A pass SHALL NOT write a file, start a process or read the clock.

#### Scenario: Error from a pass

- **WHEN** a pass returns an error diagnostic
- **THEN** the command stops before the backend, reports the diagnostic and publishes nothing

### Requirement: Rewrite rules are compiled rules with an example

A rewrite rule SHALL have an absolute IRI as its id, a title, an example with a model before and after, and the IR features that it removes and adds. A rule SHALL NOT be a script. A rule without an example SHALL NOT ship.

#### Scenario: Example runs as a test

- **WHEN** the test suite runs the rule catalog
- **THEN** each rule changes its example before-model to its example after-model

#### Scenario: Rule without an example

- **WHEN** a rule is registered without an example
- **THEN** the catalog check fails and names the rule

### Requirement: The engine records each application

For each rule application, the engine SHALL record the rule id, the node address that the rule matched, pinned to the input model of the pipeline, the node address of the replacement, and the source location when the node has one. A second pass that replaces a replacement SHALL keep the first origin. `--json` SHALL report the list.

#### Scenario: Two passes on one node

- **WHEN** one pass replaces a node and a later pass replaces that replacement
- **THEN** the second record names the original node in the input model as its origin

#### Scenario: Report

- **WHEN** a command runs with `--json` and a rule applies
- **THEN** the report has the application list with the rule id and both addresses

### Requirement: A replacement keeps its attributes

A replacement node SHALL take the attributes of the node that it replaces, so a later diagnostic still points to the source of the user.

#### Scenario: Diagnostic after a rewrite

- **WHEN** a pass replaces a node and a later check reports a diagnostic on the replacement
- **THEN** the diagnostic has the source location of the original node

### Requirement: Rules apply with a stated strategy

The engine SHALL apply a rule set bottom-up, top-down, or as a fixed point with a round limit. A fixed point that does not settle within its limit SHALL return an error diagnostic and no model.

#### Scenario: Non-terminating rule set

- **WHEN** two rules undo each other and the round limit is reached
- **THEN** the pass returns an error diagnostic that names the rules and returns no model

### Requirement: The host checks the order of passes

The host SHALL check that each IR feature that a backend cannot take is removed by a pass of the stage before it. When a feature remains, the host SHALL report an error that names the feature and the backend before generation.

#### Scenario: Feature that remains

- **WHEN** a backend cannot take a feature and no pass removes it
- **THEN** the command fails with an error that names the feature, and the backend does not run

### Requirement: Work is done one module at a time

The engine SHALL process one module at a time. A rule SHALL read other modules only through a package view, and the view SHALL record each lookup.

#### Scenario: Recorded reads

- **WHEN** a rule reads a value of another module
- **THEN** the pass result lists that lookup

### Requirement: Node addresses follow the upstream contract

The address of a node SHALL have an artifact selector, a format, a root, typed steps, a revision and an optional guard, in the form of the draft `finos/morphir` node address contract. The canonical writer SHALL give one spelling for one address.

#### Scenario: URI round trip

- **WHEN** a URI of the upstream draft fixture that needs no resolver is parsed and written
- **THEN** the written URI equals the canonical URI of the fixture

#### Scenario: Address for each node

- **WHEN** the walk visits a value definition
- **THEN** each node of its body has an address with the typed steps of its role, for example `body/apply/argument`

#### Scenario: Unknown role

- **WHEN** a URI has a role that the table does not define
- **THEN** parsing fails with `invalid_node_uri`
