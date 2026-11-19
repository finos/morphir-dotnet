# black-box-test-suite Specification

## Purpose

Keeps the tests that use only a built artifact in one project, grouped by purpose, so that the build can select,
order and split them, and so that each operating system runs them.

## Requirements

### Requirement: Black-box tests use built artifacts only
A black-box test SHALL reach the product only through a built artifact: the command line of an executable, a
package, a published folder or an archive. The black-box test project MUST NOT make an assembly from `src/`
visible to its code, directly or through another reference. A reference that only orders the build is permitted.
A test that needs the compatibility kit library in the test process is a conformance test and is not in the
black-box project.

#### Scenario: A reference that exposes a product assembly fails the build tests
- **WHEN** the black-box test project gets a project reference below `src/` that makes the assembly visible to its code
- **THEN** a guard test fails and names the reference

#### Scenario: An assembly that comes through another reference fails the build tests
- **WHEN** a project that the black-box test project references makes an assembly from `src/` visible to it
- **THEN** a guard test fails and names the assembly and the reference that brings it

#### Scenario: A build-order reference is permitted
- **WHEN** the black-box test project names the CLI project only so that a build of the test project builds the CLI first
- **THEN** the guard test passes, and no type of the CLI is visible to a step

#### Scenario: A scenario runs the CLI through its command line
- **WHEN** a scenario compiles a model
- **THEN** the scenario gives a command line to a built executable and reads its exit code, standard output, standard error and the files that it wrote

### Requirement: Each black-box test has one purpose group
Each test of the black-box project SHALL be in exactly one of four groups: integration, end-to-end, smoke or
journey. The group is a tag on a scenario or its feature, or a category on a framework test or its class. The
folder of the file SHALL agree with the group.

#### Scenario: A scenario with no group fails the build tests
- **WHEN** a feature file has a scenario with no group tag on the scenario or the feature
- **THEN** a guard test fails and names the feature file and the scenario

#### Scenario: A scenario with two groups fails the build tests
- **WHEN** a scenario has the integration tag and its feature has the journey tag
- **THEN** a guard test fails and names the scenario and both tags

#### Scenario: A framework test with no group fails the build tests
- **WHEN** a framework test of the black-box project has no group category on the test or its class
- **THEN** a guard test fails and names the test

#### Scenario: The folder and the group disagree
- **WHEN** a feature file in the `Smoke` folder has the end-to-end tag
- **THEN** a guard test fails and names the feature file

### Requirement: The group states the purpose, not the target
The group of a test SHALL state what the test proves. The form of the product on which a test runs is the target,
and the target MUST NOT change the group.

#### Scenario: A journey on a published target is a journey
- **WHEN** a journey scenario runs against the Native AOT executable
- **THEN** its group is journey, and its result is in the journey group

### Requirement: The four groups have distinct purposes
An integration test SHALL prove that the components behind one capability work together. A journey test SHALL
prove one user goal that needs two or more capabilities. A smoke test SHALL prove only that an artifact starts and
does one basic function. An end-to-end test SHALL prove the delivered form itself: installation, layout, start,
and behavior that only a published or packaged form has.

#### Scenario: A journey uses more than one capability
- **WHEN** a scenario has the journey tag
- **THEN** its steps use two or more capabilities, and the last check is against the user goal

#### Scenario: A command that runs two times is one capability
- **WHEN** a scenario runs the generate command, changes the model, and runs the generate command again
- **THEN** its group is integration

#### Scenario: An end-to-end test of a build output
- **WHEN** a test reads a built assembly of the development build and checks a property that the delivered form must have
- **THEN** its group is end-to-end, and it runs on the development build

### Requirement: Gherkin is the default form of a black-box test
A black-box test SHALL be a Gherkin scenario in a feature file, with three exceptions: a loop over a corpus, a
property test, and a test with a time budget. An exception that reaches the product through a built artifact
SHALL be a framework test in the black-box project, with a group and with drivers. An exception that reaches code
in the test process stays in the test project of its library.

#### Scenario: A corpus loop that starts an executable
- **WHEN** a test runs each case of a corpus against a published executable
- **THEN** it is a framework test in the black-box project, with a group category, and it uses the drivers

#### Scenario: A property test of a library function
- **WHEN** a test states a law over generated inputs and calls a function in the test process
- **THEN** it stays in the test project of that library

### Requirement: The kit vocabulary stays separate
The step vocabulary of the black-box project SHALL be separate from the step vocabulary of the compatibility kit.
A kit feature file MUST NOT use a step of the black-box project.

#### Scenario: A kit feature uses only kit steps
- **WHEN** a feature file below `conformance/suites/` is read
- **THEN** each of its steps is in the step vocabulary of the kit

### Requirement: The build selects a group as one unit
The test build SHALL run one group of the black-box project as one unit: a developer can ask for one group, the
build can run two groups at the same time, and the report has the time and the result of each group.

#### Scenario: A developer runs one group
- **WHEN** a developer runs the test build with the option that names the integration group
- **THEN** only the integration tests of the black-box project run, and no test of another group, no test of another project and no other check of the test build runs

#### Scenario: The group and the scope give one selection
- **WHEN** the test build runs the integration group in the scope that runs only platform-specific tests
- **THEN** the run selects each integration test and no test of another group

#### Scenario: A group that selects no test fails
- **WHEN** the test build runs a group and no test has it
- **THEN** the run of that group fails

#### Scenario: The report has one row for each group
- **WHEN** the test build completes
- **THEN** the log and the job summary show the time and the result of each group that ran, and the count of skipped tests with their reasons

### Requirement: Each operating system runs the black-box project
In a pull request, the test jobs on Linux, Windows and macOS SHALL run each group of the black-box project on the
development build, for the tests that have no lane. The release and manual workflows, which publish the other
targets, SHALL run each group against them, for each test whose capabilities the target supports. A test in a
unit test project keeps its `PlatformSpecific` marker until a black-box test replaces it.

#### Scenario: Windows runs the integration group
- **WHEN** the test job of a pull request runs on Windows
- **THEN** the integration group of the black-box project runs with no marker on its tests

#### Scenario: An end-to-end test with no lane runs in a pull request
- **WHEN** an end-to-end test needs only the build output of the development build
- **THEN** the test job of a pull request runs it on each operating system

#### Scenario: An integration test runs on a published target in the release workflow
- **WHEN** the release workflow runs the black-box project against the trimmed executable
- **THEN** the integration tests that the target supports run against it, in the integration group

### Requirement: A lane states where a test with other needs runs
A black-box test that needs more than the development build SHALL have exactly one lane: `packages` (the packed
packages), `native-aot` (a Native AOT publish) or `published` (a published or packaged executable). A run MUST NOT
execute a test of another lane: the run either does not select it, or skips it before its first step with the
two lanes as the reason. The lane, not the group, decides where such a test runs.

#### Scenario: The packages lane
- **WHEN** the test job of a pull request completes the pack step
- **THEN** the tests of the packages lane run against the packed packages, on each operating system

#### Scenario: The Native AOT lane
- **WHEN** the test job of a pull request runs on Linux
- **THEN** the tests of the Native AOT lane run in that job

#### Scenario: The published lane
- **WHEN** the test job of a pull request runs
- **THEN** no test of the published lane runs in it, and the release and manual workflows run that lane against the published and packaged targets

#### Scenario: A test with a lane does not execute in the run of its group
- **WHEN** the test build runs the end-to-end group on the development build
- **THEN** no test of the packages, Native AOT or published lane executes a step

#### Scenario: A run that cannot exclude by tag still does not execute the test
- **WHEN** a run selects a test of the packages lane and the run is not the packages run
- **THEN** the test is skipped before its first step, the reason names its lane and the lane of the run, and the report counts the skip

#### Scenario: A test with two lanes fails the build tests
- **WHEN** a test has the packages lane and the published lane
- **THEN** a guard test fails and names the test

### Requirement: A check that a pull request runs keeps a pull request run
The package checks and the Native AOT checks that a pull request runs now SHALL run in a pull request after they
move, on the same operating systems. The old check of each stays until its replacement ran and passed in CI.

#### Scenario: The package tests run on each operating system
- **WHEN** the package tests moved to the packages lane
- **THEN** the test job of a pull request runs them after the pack step on Linux, Windows and macOS

#### Scenario: The Linux test job publishes with Native AOT
- **WHEN** the Native AOT probes moved to the Native AOT lane
- **THEN** the test job of a pull request on Linux runs them

#### Scenario: The old target stays until the replacement is proved
- **WHEN** a pull request adds the Native AOT test of a probe
- **THEN** the old build target of that probe is removed only in a change after a CI run in which the new test passed

### Requirement: The migration has one inventory
The change SHALL keep one inventory of the test files that start a process, run a build or read a built artifact,
with a destination for each: the pull request that moves it, or the reason that it stays. The migration is
complete when each row is done.

#### Scenario: A black-box test outside the named projects
- **WHEN** a test file in a unit test project starts a built host and uses no product type
- **THEN** the inventory has the file with the pull request that moves it

#### Scenario: A test of a packed package moves
- **WHEN** a test in the build test project reads the layout of a packed package and uses no product type
- **THEN** the inventory has the file with the pull request that moves it

#### Scenario: A test of the build stays
- **WHEN** a test checks a build target, a workflow file or a helper of the build
- **THEN** the inventory has the file as a build test that stays

#### Scenario: A file that stays has a reason
- **WHEN** a test file starts a helper process and tests a library in the test process
- **THEN** the inventory has the file as one that stays, with that reason

### Requirement: A test of a delivered package is a black-box test
A test of a packed package or an installed tool SHALL be a black-box test, also when it starts no process and is
in the build test project. A test of a build target, a workflow file or a helper of the build is a build test and
stays in the build test project.

#### Scenario: A package layout test in the build test project
- **WHEN** a test in the build test project reads the layout of a packed package and uses no product type
- **THEN** it is a black-box test, and the inventory has the pull request that moves it

### Requirement: An old setting is removed only when nothing reads it
A reader of an old environment variable that stays SHALL be changed in the pull request that the inventory names,
before the variable is removed.

#### Scenario: A variable with a reader is not removed
- **WHEN** the last pull request removes the old environment variables
- **THEN** a search finds no code, script or workflow that reads them, and no document that tells a reader to set them

### Requirement: A moved test keeps its coverage
A change that moves tests into the black-box project SHALL account for each test that the test runner discovers
in the source: its new black-box test, or the reason and the place in which it stays. A test that also needs a
product assembly SHALL be split: the part that needs the assembly stays in the unit test project. A project is
removed only when each of its tests is accounted for.

#### Scenario: A slice records what it moved
- **WHEN** a slice moves tests from a project
- **THEN** its pull request has a table with each discovered test of the source and its new test or the reason that it stays

#### Scenario: A test that compares with a product function is split
- **WHEN** a test runs the CLI and compares its output with the result of a product function in the test process
- **THEN** the comparison stays in the unit test project, and a black-box scenario checks the behavior that needs only the artifact

#### Scenario: A project with framework tests is removed
- **WHEN** a slice removes a test project that has feature files and framework tests
- **THEN** the table of the slice has each framework test, not only the feature files

#### Scenario: The source project keeps its unit tests
- **WHEN** a slice moves the black-box tests of a project
- **THEN** the unit tests of that project stay and still run
