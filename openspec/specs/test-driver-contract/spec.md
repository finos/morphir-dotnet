# test-driver-contract Specification

## Purpose

Gives black-box tests one way to reach the product: driver interfaces that own the orchestration of a test, so
that the same test runs against each form of the product and each test is safe to run in parallel.

## Requirements

### Requirement: A step uses a driver interface
A step or a framework test of the black-box project SHALL reach the product, the file system and other processes
only through a driver interface. It MUST NOT start a process, make a temporary directory or read an environment
variable of the test machine directly.

#### Scenario: A step that starts a process fails the build tests
- **WHEN** a step class of the black-box project starts a process without a driver
- **THEN** a guard test fails and names the step class

#### Scenario: A step states an intent
- **WHEN** a step compiles a project
- **THEN** it calls the compile capability of the driver with the project and the options, and gets a command result

#### Scenario: A test that inspects a built file
- **WHEN** a test checks the metadata of a built assembly or a configuration file of the build output
- **THEN** it asks a driver for that artifact, and the driver gives the file of the selected target

### Requirement: A driver owns the orchestration of a test
A driver SHALL own the resources of its test: the workspace folder, the temporary directories, the fixture files,
the processes that it starts, and the artifacts that it publishes or installs. When the test ends, the driver
SHALL stop its processes and remove its resources.

#### Scenario: A test leaves no process
- **WHEN** a test ends, with a pass or a failure
- **THEN** no process that its driver started is alive

#### Scenario: A failed test keeps its evidence
- **WHEN** a test fails and the run asks to keep evidence
- **THEN** the workspace of the test stays and the failure message gives its path

### Requirement: Each test has its own contained workspace
A driver SHALL give each test its own workspace: a home directory, machine configuration locations, a work area
and a temporary directory, on paths that no other test is given. The path of the workspace MUST have no link in
it. A product process of the test SHALL get the workspace as its home, its machine locations and its working
directory, and MUST NOT get those of the developer.

#### Scenario: Two tests get different paths
- **WHEN** two tests of one group run at the same time
- **THEN** no path of one workspace is in the other, and each product process has only the paths of its own workspace

#### Scenario: The machine of the developer does not change
- **WHEN** a run of the black-box project completes
- **THEN** the home directory and the machine configuration locations of the developer are as they were before the run

#### Scenario: The temporary directory is physical
- **WHEN** the temporary directory of the operating system is below a link, as on macOS
- **THEN** the workspace uses the path with the link replaced by its target

### Requirement: The target of a run is one closed value
The target of a black-box run SHALL be one of a closed set of forms of the product: the development build, the
trimmed executable, the untrimmed executable, the Native AOT executable, a packaged offering (`native-aot`,
`self-contained`, `framework-dependent`, `dotnet-tool`), or one executable at a given path. One setting selects
the target. A value outside the set SHALL stop the run with a message that lists the set.

#### Scenario: The default target is the development build
- **WHEN** a run gives no target
- **THEN** the drivers run the product from the build output of the solution

#### Scenario: An unknown target stops the run
- **WHEN** the target setting is `fast`
- **THEN** the run fails before the first test, and the message lists the valid targets

#### Scenario: An executable at a path
- **WHEN** the target is one executable and the run gives its path
- **THEN** the drivers run that file, and a path with no file stops the run before the first test

### Requirement: A test runs on the host that it names
The host SHALL be one of a closed set of entry points: the native `morphir` host, the managed `morphir` host and
the `dotnet-morphir` tool host. A target states the hosts that it has and its default host, which is one of them.
A test that names a host SHALL run on that host. A test that names no host SHALL run on the default host of the
run. A test MUST NOT run on another host than the one that it names.

#### Scenario: The development build has the three hosts
- **WHEN** a run on the development build has a test that names the managed host and a test that names no host
- **THEN** the first runs the managed `morphir` executable and the second runs the default host, in the same run

#### Scenario: A managed host test in a pull request
- **WHEN** the test job of a pull request runs a test that compiles F# with the frontend in the host
- **THEN** the test runs the managed host of the development build, and not the native host

#### Scenario: A target without the host
- **WHEN** a test names the managed host and the target is the Native AOT executable
- **THEN** the test is skipped, and the reason names the host and the target

#### Scenario: The default host of a run
- **WHEN** a run on the development build sets the default host to the tool host
- **THEN** each test that names no host runs `dotnet-morphir`

#### Scenario: A packaged target with one host
- **WHEN** a run selects the `self-contained` offering, the `framework-dependent` offering or the `dotnet-tool` offering, and gives no host
- **THEN** each test that names no host runs on the one host of that offering, and is not skipped for its host

#### Scenario: A default host that the target does not have
- **WHEN** a run selects the Native AOT executable and sets the default host to the managed host
- **THEN** the run fails before the first test, and the message names the host and the hosts of the target

### Requirement: A target resolves to exactly its own artifact
A target SHALL resolve to the artifact of its own form. The development build target SHALL resolve to the output
of the same build configuration as the test project. A target with no artifact SHALL stop the run before the
first test, with the command that makes the artifact. A target MUST NOT use the artifact of another form or
another configuration instead.

#### Scenario: A target with no artifact stops the run
- **WHEN** the target is the Native AOT executable and no such executable is published
- **THEN** the run fails before the first test, and the message gives the build command that publishes it

#### Scenario: No other form is used instead
- **WHEN** the target is the trimmed executable, it is not published, and an untrimmed executable is published
- **THEN** the run fails and does not test the untrimmed executable

#### Scenario: A Debug test build runs the Debug CLI
- **WHEN** the test project is built in the Debug configuration and the target is the development build
- **THEN** the drivers run each host from the Debug build output

### Requirement: The packed packages resolve from the folder that the build gives
The driver of the packages lane SHALL read the packed packages from the one folder that the test build gives it,
which is the output folder of the pack step. A packages run with no package in that folder SHALL stop before the
first test, with the command that packs them. It MUST NOT pack, and MUST NOT use a package from another place.

#### Scenario: The packages run gets the output of the pack step
- **WHEN** the test build runs the packages lane after the pack step
- **THEN** each test of the lane reads the packages that the step wrote

#### Scenario: A packages run with no package stops
- **WHEN** the packages lane runs and the folder has no package
- **THEN** the run fails before the first test, and the message gives the pack command

### Requirement: One decision says if a test runs
Before the first step of a test, the testing library SHALL make one decision from the lane, the host and the
capabilities of the test and of the run, in that order. A test that the decision rejects SHALL be skipped with
one reason, the first check that failed. The decision SHALL be the same for a Gherkin scenario and for a
framework test.

#### Scenario: A test with no lane runs in a development run and in a published run
- **WHEN** a test has no lane
- **THEN** the decision lets it run in the run of its group on the development build and in a run on a published target, if the host and the capabilities are there

#### Scenario: Two checks fail
- **WHEN** a test of the packages lane names a host that the target does not have, and the run is not the packages run
- **THEN** the test is skipped with the lane as its one reason

#### Scenario: A framework test gets the same decision
- **WHEN** a framework test and a Gherkin scenario have the same lane, host and capabilities
- **THEN** both run, or both are skipped with the same reason

#### Scenario: The report counts the skips
- **WHEN** a run completes
- **THEN** the report shows the count of skipped tests for each kind of reason: lane, host and capability

### Requirement: A driver states its capabilities
A driver SHALL state the capabilities that it supports for the selected target. A test SHALL state the
capabilities that it needs. A test that needs a capability that the driver does not support SHALL NOT run, and its
result SHALL be skipped with the capability and the target as the reason. It MUST NOT pass and MUST NOT fail.

#### Scenario: The tool target has no self-contained frontend
- **WHEN** a test needs the self-contained F# frontend process and the target is the `dotnet-morphir` tool
- **THEN** the test is skipped, and the reason names the capability and the target

#### Scenario: A pull request run has no hidden test
- **WHEN** the build tests run
- **THEN** for each test with no lane, with the packages lane or with the Native AOT lane, the pull request run of that lane selects the test, has the host that it names, and supports all the capabilities that it needs, on an operating system of that run

#### Scenario: Two capabilities that no one run supports together
- **WHEN** a test needs two capabilities, one run supports only the first and another run supports only the second
- **THEN** the guard test fails and names the test

#### Scenario: A package test is not hidden by a skip
- **WHEN** a test of the packages lane needs a capability that the packages run does not support
- **THEN** the guard test fails and names the test

### Requirement: A command result has one form
A driver SHALL return the result of a product command in one form: the command line, the exit code, standard
output, standard error, the duration and the files that the command wrote. Each failure message of an assertion on
it SHALL have the command line, the exit code and both streams.

#### Scenario: A failure message shows the command
- **WHEN** an assertion on a command result fails
- **THEN** the message has the command line, the exit code, standard output and standard error

### Requirement: Standard output holds only the output of the command
The testing library SHALL give one assertion that standard output of a command with a machine-readable format is
exactly one document of that format, with no other text. Logs are on standard error.

#### Scenario: A JSON command writes one document
- **WHEN** a test runs a command with `--json` and asserts one JSON document
- **THEN** the assertion fails if standard output is empty, is not JSON, or has text before or after the document

#### Scenario: A log line on standard output fails
- **WHEN** a command with `--json` writes a log line to standard output
- **THEN** the assertion fails and shows the line

### Requirement: The status of a report belongs to its command
The meaning of a report (which field states success, which fields hold the diagnostics) SHALL belong to the
capability of its command, not to the general assertion on a command result. A capability SHALL give the checks
for its own report.

#### Scenario: A verification report
- **WHEN** a test asserts that a model is valid after `ir verify --json`
- **THEN** the check reads the `IsValid` field of the verification report

#### Scenario: A generation report
- **WHEN** a test asserts that `generate --json` succeeded
- **THEN** the check reads the `success` field of the generation report and compares it with the exit code

### Requirement: A driver bounds each process
A driver SHALL give each process that it starts a time limit. When a process exceeds its limit, the driver SHALL
stop the process and its child processes, and the test SHALL fail with the command line and the output that the
process wrote.

#### Scenario: A process that does not exit
- **WHEN** a product command does not exit within its time limit
- **THEN** the driver stops the process tree and the test fails with a time-out message

### Requirement: The process limit holds for the complete test run
A test process SHALL limit the number of product processes that it runs at the same time. The test build SHALL
give each test process that it starts a fixed share of one limit for the machine, so that at each moment the sum
of the limits of the test processes that run is not more than the limit for the machine. The share does not
depend on the moment at which a test process starts.

#### Scenario: More tests than the limit in one process
- **WHEN** more tests of one test process start a product process than its limit permits
- **THEN** the extra product processes wait for a free place and do not fail

#### Scenario: Two lanes share the machine limit
- **WHEN** the test build runs with two test processes at the same time and a machine limit of 8
- **THEN** each test process gets a limit of 4, also one that starts while another runs

#### Scenario: A limit that does not divide
- **WHEN** the test build runs with three test processes at the same time and a machine limit of 8
- **THEN** each test process gets a limit of 2

#### Scenario: A machine limit lower than the lanes
- **WHEN** the machine limit is 1 and the test build is asked for two test processes at the same time
- **THEN** the test build runs one test process at a time, with a limit of 1

#### Scenario: A suite that runs alone
- **WHEN** a suite that must run alone runs with a machine limit of 8
- **THEN** its test process gets a limit of 8, and no other test process runs

### Requirement: A driver can hold a session with a product process
For a product process that stays alive, a driver SHALL give a session: send input to it, read its output as it
comes, send it an interrupt, and get its exit. The time limit and the stop of the process tree apply to a session
as to a command.

#### Scenario: A worker gets a request and gives a reply
- **WHEN** a test starts a worker process, sends one framed request and reads the reply
- **THEN** the test gets the reply while the process is alive

#### Scenario: An interrupt stops a worker
- **WHEN** a test sends an interrupt to a worker session (Ctrl+Break on Windows)
- **THEN** the session reports the exit of the process and the time from the interrupt to the exit

### Requirement: A driver does not replace the solution build outputs
A driver that builds, packs or publishes a project of the repository SHALL write the outputs to its workspace or
to a folder that the build gave it, and MUST NOT replace an assembly of the solution build.

#### Scenario: A solution build after the black-box tests compiles nothing
- **WHEN** the black-box project runs after a solution build, and then the solution builds again
- **THEN** the second build compiles no project

### Requirement: A library host is a subject that a driver runs
For a shipped library that no product executable uses for the behavior under test, a host program MAY call the
library. A driver SHALL publish the host in the form that the test asks for (managed, trimmed or Native AOT), run
it, and return its output and the warnings of the publish. The checks of the test are in the black-box project,
not in the host.

#### Scenario: The Native AOT host gives the same output as the managed host
- **WHEN** a smoke test runs a library host as Native AOT
- **THEN** the test passes only if its output equals the output of the managed run of the same host

#### Scenario: An unexpected trim or AOT warning fails
- **WHEN** the Native AOT publish of a host gives a trim or AOT warning that is not in the allowed list of that host
- **THEN** the test fails and shows the warning

#### Scenario: A host is necessary only when the CLI does not reach the library
- **WHEN** the CLI uses a library for the behavior under test
- **THEN** the behavior is a test against the CLI target, and no host is added for it
