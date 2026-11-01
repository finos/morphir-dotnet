# Spec Delta

## Purpose

Defines how a pipeline turns input files into a tree of output files using strongly typed stages, and how it reports what happened along the way.

## ADDED Requirements

### Requirement: Strongly typed stage contracts

Parsers, plugins, compilers and processors SHALL be generic over the parsed model type. No public pipeline contract SHALL expose `obj` as a parameter, return value or field. A mismatch between stages SHALL be a compile error.

#### Scenario: Plugin receives the parsed type

- **WHEN** a processor combines a parser of a model type with a plugin of that type
- **THEN** the plugin receives the parsed model without a cast

#### Scenario: Mismatched stages

- **WHEN** code adds a plugin of one model type to a processor of another model type
- **THEN** the code does not compile

### Requirement: One parsed model per run

A run SHALL accept either a file or a directory as input and SHALL produce one parsed model from that input. Parsers SHALL be tried in order until one succeeds.

#### Scenario: Single file input

- **WHEN** a run starts from one file and a parser succeeds
- **THEN** plugins and compilers each operate on that one parsed model

#### Scenario: Directory input

- **WHEN** a run starts from a directory and a parser accepts directories
- **THEN** that parser receives the whole directory and produces one parsed model

#### Scenario: File-only parser given a directory

- **WHEN** a parser written for single files is given a directory
- **THEN** it reports unsupported input and the next parser is tried

### Requirement: Per-file runs

The pipeline SHALL offer an entry point that runs a processor once for each file in a tree and merges the results into one output tree. A failure in one file SHALL NOT stop the other files from being processed.

#### Scenario: Each file produces its own output

- **WHEN** a tree of three files is processed per file by a one-output compiler
- **THEN** the output tree contains three files

#### Scenario: One file fails

- **WHEN** one file of three fails to parse in a per-file run
- **THEN** the other two still produce output and the output tree reports an error naming the failed file

### Requirement: Runs produce a tree

Every run SHALL return a file tree, and a compiler SHALL be able to produce any number of output files from one parsed model. Output from multiple compilers SHALL be merged in the order the compilers were added.

#### Scenario: One input, many outputs

- **WHEN** a compiler produces three files from one parsed model
- **THEN** the run returns a tree containing those three files at their locations

#### Scenario: Multiple compilers

- **WHEN** two compilers each produce one file at different locations
- **THEN** the run returns a tree containing both files

### Requirement: Failed runs report through the output tree

A run that cannot produce output SHALL return a tree with no output files and its diagnostics at tree level. Parser failures SHALL be warnings while another parser can still succeed. When every parser fails, malformed input and parser failures SHALL become errors that keep their own rule and details.

#### Scenario: Malformed input with no other parser

- **WHEN** every parser fails and one reports malformed input with a rule and location
- **THEN** the tree has no files, an error with that rule and location, and no plugin or compiler ran

#### Scenario: No parser recognizes the input

- **WHEN** every parser reports unsupported input
- **THEN** the tree has no files, a warning for each parser and an error saying no parser succeeded

#### Scenario: A later parser succeeds

- **WHEN** one parser fails and the next succeeds
- **THEN** the first failure remains a warning and the run continues

### Requirement: Stages stop by severity

Plugins SHALL keep running after an error message so that one run can report several problems. A plugin rejecting the model, or a fatal message, SHALL stop the remaining plugins. Compilers SHALL NOT run when the file has any error or fatal message.

#### Scenario: Two plugins report errors

- **WHEN** two plugins each add an error and keep the model
- **THEN** both run, both errors are reported and no compiler runs

#### Scenario: Plugin stops the run

- **WHEN** a plugin rejects the model
- **THEN** later plugins and compilers do not run and the plugin's diagnostics appear on the output tree

#### Scenario: Warnings do not block output

- **WHEN** a plugin adds only a warning
- **THEN** compilers run and the warning appears on the output tree

### Requirement: Input data reaches the output

Data a plugin stores on the input file SHALL be readable from the output when the run ends. In a single run it SHALL appear in the output tree's data, with compiler values winning. In a per-file run it SHALL appear on that run's output files, with each file's own values winning.

#### Scenario: Plugin stores data

- **WHEN** a plugin sets a property on the file and the run finishes
- **THEN** the property can be read from the output tree

#### Scenario: Per-file data does not collide

- **WHEN** a per-file run sets one property to a different value for each input file
- **THEN** each output file holds the value from its own input

### Requirement: Diagnostics identify their origin

A diagnostic SHALL carry its severity and message, and SHALL be able to carry a file path, a logical location such as a module or JSON path, a source position, the stage that produced it and a rule identifier. Tree queries for errors, messages and statistics SHALL include tree-level diagnostics.

#### Scenario: Error with no output file

- **WHEN** a model fails validation before any output exists
- **THEN** the diagnostic names the input path, logical location and rule, and the tree reports that it has errors

#### Scenario: Parser details reach the diagnostic

- **WHEN** a parser reports malformed input with a path, rule, location and source position
- **THEN** the diagnostic carries all four

#### Scenario: Readable description

- **WHEN** a diagnostic with a path, rule, location and message is described for a person
- **THEN** the text contains all four

### Requirement: Immutable reuse of processors

Processors SHALL remain immutable values that can be shared and extended without changing the original, and SHALL be buildable with the pipeline computation expression.

#### Scenario: Extending a frozen processor

- **WHEN** a plugin is added to a frozen processor
- **THEN** the result includes the plugin and the original processor is unchanged

#### Scenario: Computation expression

- **WHEN** a processor is declared with the pipeline computation expression using a typed parser, plugin and compiler
- **THEN** its model type is inferred from those stages
