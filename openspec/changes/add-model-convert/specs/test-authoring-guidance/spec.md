# Spec Delta

## MODIFIED Requirements

### Requirement: The skill classifies a test in a fixed order
The skill SHALL give one procedure that puts a test in exactly one class: conformance, build, property, unit,
integration, journey, smoke or end-to-end. The procedure SHALL ask its questions in a fixed order: the kit, the
build, the form, the reach, and then the purpose. The target on which a test runs MUST NOT decide its class.

#### Scenario: The order of the questions
- **WHEN** an agent reads the procedure
- **THEN** it asks first if the test is a kit feature or needs the kit library, then if its subject is the build of the repository, then if it is a corpus loop, a property test or a time-budget test, then if it reaches the product only through a built artifact, and then what it proves

#### Scenario: A test that starts the CLI for one capability
- **WHEN** a new test starts the CLI as a process to check the `model verify` command on a model file
- **THEN** the procedure gives the class integration and the black-box project as its location

#### Scenario: A test that only starts the CLI
- **WHEN** a new test starts the CLI as a process and checks only that `--version` gives a version
- **THEN** the procedure gives the class smoke

#### Scenario: A test that calls a function
- **WHEN** a new test calls a function of a library in the test process and uses no file or process
- **THEN** the procedure gives the class unit and the test project of that library as its location

#### Scenario: A test that follows a tutorial
- **WHEN** a new test runs compile, then generate, then a build of the generated code
- **THEN** the procedure gives the class journey

#### Scenario: A journey on a published executable
- **WHEN** a journey test must also run against the Native AOT executable
- **THEN** the procedure gives the class journey, and the published executable is a target of the run

#### Scenario: A test of a build target
- **WHEN** a new test checks that a build target refuses a wrong property, or reads a workflow file
- **THEN** the procedure gives the class build and the build test project as its location

#### Scenario: A test of a packed package
- **WHEN** a new test reads the layout of a packed package or installs the packed tool
- **THEN** the procedure gives the class end-to-end in the black-box project, with the packages lane, and not the build test project

#### Scenario: A test that runs the kit against an executable
- **WHEN** a new test uses the kit library in the test process to judge the output of a built executable
- **THEN** the procedure gives the class conformance, and the test is not in the black-box project

#### Scenario: A corpus loop against an executable
- **WHEN** a new test runs each case of a corpus against a built executable
- **THEN** the procedure gives a framework test in the black-box project, with the group that states what it proves

## ADDED Requirements

### Requirement: Test data comes from object mothers and one fixture catalog
Tests SHALL get models, trees and files from object mothers, named cases built on test-data builders, and SHALL reach files on disk only through one fixture catalog that gives each file one owner and a provenance entry. A feature file SHALL name a case by intent, never by a fixture path or inline JSON. Exact bytes SHALL appear only where a contract defines them, read from the catalog.

#### Scenario: A feature file names a case by intent
- **WHEN** a scenario needs a v3 Library model with a dependency
- **THEN** its step says so in words, and the step maps the phrase to an object mother case

#### Scenario: An unowned fixture fails a guard test
- **WHEN** a file is added under the test fixtures with no owner in the fixture catalog
- **THEN** a guard test fails and names the file

#### Scenario: The skill gives the pattern
- **WHEN** an agent adds a test that needs a model or a fixture file
- **THEN** the skill directs it to an existing object mother case or a new one, and to the fixture catalog for files
