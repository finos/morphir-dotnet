# Spec Delta

## Purpose

Makes a coding agent that adds, moves or changes a test give it the correct class, location and pattern, with one
skill and one reviewer that each supported agent can use.

## ADDED Requirements

### Requirement: The repository has one test-manager skill
The repository SHALL have one skill named `test-manager` that holds the rules for tests. Its canonical copy is with
the other project-owned skills, and each coding agent that the repository supports SHALL be able to load it. The
copies MUST equal the canonical copy.

#### Scenario: A copy that differs fails the check
- **WHEN** a copy of the skill in an agent folder differs from the canonical copy
- **THEN** the skill copy check fails and names the file

#### Scenario: Each supported agent finds the skill
- **WHEN** a supported coding agent lists the project skills
- **THEN** `test-manager` is in the list with a description that says when to use it

### Requirement: An agent uses the skill before it adds or changes a test
The agent instructions of the repository SHALL tell an agent to use the `test-manager` skill before it adds, moves,
renames or deletes a test, a feature file, a step, a driver or a test project.

#### Scenario: The main agent instructions name the skill
- **WHEN** an agent reads the testing section of the main agent instructions
- **THEN** the section names the `test-manager` skill and the cases in which the agent must use it

### Requirement: The skill classifies a test in a fixed order
The skill SHALL give one procedure that puts a test in exactly one class: conformance, build, property, unit,
integration, journey, smoke or end-to-end. The procedure SHALL ask its questions in a fixed order: the kit, the
build, the form, the reach, and then the purpose. The target on which a test runs MUST NOT decide its class.

#### Scenario: The order of the questions
- **WHEN** an agent reads the procedure
- **THEN** it asks first if the test is a kit feature or needs the kit library, then if its subject is the build of the repository, then if it is a corpus loop, a property test or a time-budget test, then if it reaches the product only through a built artifact, and then what it proves

#### Scenario: A test that starts the CLI for one capability
- **WHEN** a new test starts the CLI as a process to check the `ir verify` command on a model file
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

### Requirement: The skill gives the location and the pattern of each class
For each class, the skill SHALL state the project and folder, the form of the test (Gherkin scenario, framework
test, kit feature), the tags or markers that it must have, and the rules that keep it safe to run in parallel.

#### Scenario: The rules for a black-box scenario
- **WHEN** an agent reads the rules for an integration scenario
- **THEN** they state the folder, the group tag, the capability tags, that steps use driver interfaces only, and that an existing step is reused before a new step is written

#### Scenario: The rules for a lane
- **WHEN** an agent adds a black-box test that needs the packed packages, a Native AOT publish or a published executable
- **THEN** the rules state the one lane tag for it, and that a test with no lane must run on the development build

#### Scenario: The rules for a host
- **WHEN** an agent adds a black-box test that needs the F# frontend in the host
- **THEN** the rules state the host tag for the managed host or the tool host, and that a test with no host tag runs on the default host

#### Scenario: The rules for a test with a time budget
- **WHEN** an agent adds a test that asserts an elapsed time
- **THEN** the rules state how to declare that its assembly or group runs alone

#### Scenario: The rules for a unit test that touches the operating system
- **WHEN** an agent adds a unit test that uses the real file system
- **THEN** the rules state the `PlatformSpecific` marker for the framework of that project

#### Scenario: The rules for a test that needs a product assembly and an artifact
- **WHEN** an agent adds a test that compares the output of the CLI with the result of a product function
- **THEN** the rules state that the comparison is a test of the unit test project and that the artifact behavior is a black-box test

### Requirement: The skill has a review mode and a reviewer agent
The skill SHALL have a review mode with a check list, and the repository SHALL have a read-only reviewer agent for
each supported coding agent that applies it to a diff. A finding SHALL have the file, the line, the rule and the
correction.

#### Scenario: The reviewer finds a process start in a unit test project
- **WHEN** a diff adds a test that starts the CLI as a process in a unit test project
- **THEN** the reviewer reports the file and line, the rule, and the black-box group to which the test must go

#### Scenario: The reviewer changes no file
- **WHEN** the reviewer agent completes
- **THEN** the working tree has no change from it

### Requirement: Rules that a program can check are tests
A rule of the skill that a program can check SHALL also be a guard test or a build check, and the skill SHALL name
it. The skill SHALL say which rules have only a partial check, and those rules are in the review mode.

#### Scenario: The skill names the guard for the group tag
- **WHEN** an agent reads the rule that a black-box test has one group
- **THEN** the rule names the guard test that fails without the group

#### Scenario: A rule with a partial check is in the review mode
- **WHEN** an agent reads the rule that a step uses a driver interface
- **THEN** the rule names its guard test, says that the guard finds only the listed calls, and the review check list has the rule

#### Scenario: The skill and the contributor document agree
- **WHEN** the rules for markers, scopes and parallel runs change
- **THEN** the skill and the contributor document for CI change in the same pull request
