# Spec delta

## Purpose

Let an F# project consume a Morphir model JSON file as build input through a discoverable Myriad plugin and compile the generated F# alongside handwritten application code.

## ADDED Requirements

### Requirement: Discoverable JSON generator

The plugin SHALL expose a Myriad generator named `morphir-fsharp` that accepts `.json` inputs and an explicit module selector. It SHALL read the selected model and return F# for that module through the real Myriad host without requiring an F# marker file or source attributes. Missing or unknown module selectors SHALL fail explicitly.

#### Scenario: Select the generator for a model

- **WHEN** Myriad loads the plugin and selects `morphir-fsharp` for a supported model JSON file and one of its modules
- **THEN** it discovers the generator and receives non-empty F# output for exactly that module

#### Scenario: Invalid module selection

- **WHEN** a generator invocation omits the module selector or selects a module absent from the model
- **THEN** it fails with a diagnostic naming the missing or unknown selector

#### Scenario: Existing generators remain separate

- **WHEN** the repository's development-only generators process their existing `.fs` inputs
- **THEN** their registered names and accepted extensions remain unchanged

### Requirement: Consumer build integration

The plugin SHALL provide package build assets that discover a model's modules, run Myriad once per module and compile the generated files in dependency order before handwritten consumers. A developer SHALL configure the model once without enumerating its modules. JSON input SHALL NOT be added as F# compilation input.

#### Scenario: Clean package consumer

- **WHEN** a clean .NET 10 F# project restores the locally packed plugin and the documented compatible Myriad dependencies and builds a supported model
- **THEN** the build loads all plugin dependencies, generates one file per module, compiles each once in dependency order and lets handwritten code call a generated function

#### Scenario: Multiple models with distinct outputs

- **WHEN** a project explicitly configures two supported model inputs with distinct package namespaces and model output directories
- **THEN** both are generated and compiled without overwriting each other's output

### Requirement: Project-relative paths and repeat builds

Relative input paths SHALL resolve from the consuming project directory. The integration SHALL support paths containing spaces, generate under the project's intermediate output directory by default, regenerate after input changes, and produce the same source on unchanged repeat builds.

#### Scenario: Build from another working directory

- **WHEN** a consuming project in a path containing spaces is built from a different current directory
- **THEN** generation reads its configured model and places the generated file in the project's intermediate directory

#### Scenario: Model changes after a successful build

- **WHEN** a model function's body changes and the project builds again
- **THEN** executing the rebuilt consumer observes the changed function result

#### Scenario: Unchanged repeat build

- **WHEN** a successful project is built again with unchanged model content and options
- **THEN** generated F# bytes remain identical and compilation contains no duplicate generated declarations

### Requirement: Generated module lifecycle

The integration SHALL use the current model's ordered manifest as the authoritative generated compilation set. Adding or removing modules SHALL update that set on the next build. The integration SHALL publish new output only after every module succeeds and SHALL remove obsolete files it previously generated within its own output directory.

#### Scenario: Module added and removed

- **WHEN** a successful model is changed to add a module and remove a previous module
- **THEN** the next build compiles the new module in dependency order and excludes and removes the obsolete generated file

#### Scenario: One module fails

- **WHEN** a model's modules include a translation error
- **THEN** the build fails without publishing a partial new set or compiling stale generated files

### Requirement: Build failure propagation

The integration SHALL fail the consuming build when the input cannot be read or transpilation fails. It SHALL report the model path and available translation details through build diagnostics. It MUST NOT permit an older generated file to make a failed generation appear successful. The plugin SHALL direct its own diagnostic logging to stderr.

#### Scenario: Missing model

- **WHEN** the configured input JSON file does not exist
- **THEN** the build fails with a diagnostic naming that path

#### Scenario: Invalid model after prior success

- **WHEN** a successful build is followed by replacing the model with invalid JSON or an unsupported version and rebuilding
- **THEN** the build fails before compiling consumers, even if an older generated file exists

### Requirement: Documented compatibility contract

The plugin documentation SHALL include a reference guide stating supported IR versions, constructs and SDK mappings, known rejections, runtime references, a tested host/plugin version combination, build configuration, output layout and troubleshooting. The package README and documentation navigation SHALL link the reference, getting-started article and car rental tutorial. They SHALL state that IR 4+, document trees and a Morphir CLI command are outside this delivery.

#### Scenario: Follow the example

- **WHEN** a developer follows the documented package references and build configuration using the supplied model
- **THEN** a clean build succeeds and the sample consumer returns the documented result

### Requirement: Getting-started article

The documentation SHALL provide a getting-started article using the car rental example. It SHALL include prerequisites, exact package setup, the supplied model JSON path, `MorphirModel` configuration, build/run commands, generated module locations and an expected result from a generated function. A fresh checkout SHALL suffice without a separate Morphir frontend or editing generated F#.

#### Scenario: First generated car rental call

- **WHEN** a developer follows the getting-started article from a fresh checkout and uses the documented local package feed
- **THEN** the model generates and compiles, the F# consumer calls a generated inventory or billing function, and its output matches the article

### Requirement: Runnable car rental example

The delivery SHALL include a simple car rental model with inventory, reservation, pickup, return and billing, checked-in equivalent v1/v2/v3 JSON fixtures and an F# consumer. Business rules SHALL execute in generated functions; handwritten F# SHALL only supply inputs and present results. The model SHALL use one active reservation per car, positive whole-day durations and integer-cent daily rates.

#### Scenario: Complete rental lifecycle

- **WHEN** an available car at 5000 cents per day is reserved, picked up, returned after three billable days and billed
- **THEN** its states progress through available, reserved, rented and available again, its reservation completes, and the generated billing function produces an invoice totaling 15000 cents

#### Scenario: Rejected transitions preserve state

- **WHEN** the consumer attempts to reserve an unavailable car, pick up without a confirmed reservation, return without an active rental, or bill before return or a second time
- **THEN** generated functions return typed business errors and leave inventory, reservation and billing state unchanged

#### Scenario: Invalid duration

- **WHEN** a reservation or return supplies zero or negative billable days
- **THEN** generated functions reject it with a typed business error without changing state

### Requirement: End-to-end car rental tutorial

The documentation SHALL provide a step-by-step tutorial for the full car rental lifecycle, explaining its model modules, rules, supplied JSON, Myriad setup, generated files and F# calls. It SHALL show expected state transitions, invoice calculation, rejected operations, a model rate edit followed by regeneration, and failure troubleshooting. Its commands and expected results SHALL be verified against the runnable example.

#### Scenario: Follow the complete workflow

- **WHEN** a developer follows the tutorial's build and run steps from a clean checkout
- **THEN** generated functions perform inventory lookup, reservation, pickup, return and billing with the documented state changes and 15000-cent invoice, and the documented rejected operations fail as described

#### Scenario: Change the model and regenerate

- **WHEN** the tutorial's daily-rate value is changed in the model from 5000 to 6000 cents, followed by a rebuild and a fresh three-day rental
- **THEN** the invoice becomes 18000 cents through regenerated F# without changes to the handwritten business rules or generated source

#### Scenario: Explain failures

- **WHEN** a reader encounters a documented business-rule rejection, unsupported IR version or generation failure
- **THEN** the tutorial distinguishes the error types and provides the expected diagnostic or result and the next corrective step
