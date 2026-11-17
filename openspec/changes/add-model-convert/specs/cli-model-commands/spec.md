# Spec Delta

## Purpose

Groups the commands that act on a Morphir model, the IR together with its decorators, metadata and later additions, under `morphir model`, which replaces the former `morphir ir` group, and makes `model verify` check every form a model is stored in: a single file or a document tree, plain or compacted, for every supported IR version.

## ADDED Requirements

### Requirement: Model commands are under one model group
The CLI SHALL offer a `model` command group holding `verify` and `convert`. `morphir model --help` SHALL list both.

#### Scenario: The group lists its commands
- **WHEN** a user runs `morphir model --help`
- **THEN** the help lists `verify` and `convert` with a one-line description each

### Requirement: Model verify keeps the behavior of the former IR verify
For single files at classic versions 1, 2 and 3, `morphir model verify` SHALL accept the same inputs and options, write the same report, and exit with the same codes as `morphir ir verify` did, with two changes: the JSON report SHALL state the exit code of the run in a member `exitCode` (0 valid, 1 not valid, 2 a command line that does not parse) beside `IsValid`, which keeps its name, and a JSON mode SHALL write its report also with `--quiet`, which then suppresses only the text and the log.

#### Scenario: A valid v3 model is valid
- **WHEN** a user runs `morphir model verify` on a valid IR v3 file
- **THEN** the command exits 0 and reports the file as valid with schema version v3

#### Scenario: A file without a format version is not valid
- **WHEN** a user runs `morphir model verify` on a file with no `formatVersion`
- **THEN** the command exits 1 and names the missing member

#### Scenario: The report states its exit code
- **WHEN** a user runs `morphir model verify --json=document` on a file that is not valid, and on a command line with an unknown option
- **THEN** the reports have `exitCode` 1 and 2, the exit codes of the runs, and `IsValid` false

#### Scenario: JSON output is written in quiet mode
- **WHEN** a user runs `morphir model verify --quiet --json` on a file that is not valid
- **THEN** standard output is the JSON report, and the command exits 1

### Requirement: Model verify checks an IR v4 single file
`morphir model verify` SHALL validate a single-file IR v4 model against the parent's published IR v4 JSON Schema, detecting version 4 from `formatVersion`, and `--schema-version` SHALL accept `4`.

#### Scenario: A valid v4 model is valid
- **WHEN** a user runs `morphir model verify` on a valid single-file IR v4 model
- **THEN** the command exits 0 and reports schema version v4

#### Scenario: An invalid v4 model names its error
- **WHEN** a v4 model has a type node with an unknown tag
- **THEN** the command exits 1 and the error names the JSON path of that node

### Requirement: Model verify checks a document tree
When the input is a directory holding a tree manifest, `morphir model verify` SHALL check the whole tree: every file against the parent's published schema for its file kind where one exists, every tree rule of `model-document-trees`, and every document's content. The version SHALL come from the manifest. Each error SHALL name the file and the place in it. The report SHALL state the layout `document-tree` and the number of files checked.

#### Scenario: A valid v4 tree is valid
- **WHEN** a user runs `morphir model verify` on a v4 tree that `model convert` wrote
- **THEN** the command exits 0, reports schema version v4 and layout `document-tree`, and counts every file

#### Scenario: A valid v3 tree is valid
- **WHEN** a user runs `morphir model verify` on a v3 tree whose files say `"3.1.0"`
- **THEN** the command exits 0 and reports schema version v3 and layout `document-tree`

#### Scenario: An error inside one tree file names the file
- **WHEN** one type file of a v4 tree holds an invalid type node
- **THEN** the command exits 1, and the error names that file's path in the tree and the JSON path in the file

#### Scenario: A broken tree rule is an error
- **WHEN** a tree's module file lists a value that has no file
- **THEN** the command exits 1 with the `missing_member` diagnostic at the missing file's path, naming the module file and the value

#### Scenario: A schema version that contradicts the manifest
- **WHEN** a user runs `morphir model verify --schema-version=3` on a v4 tree
- **THEN** the command exits 2 and names both versions

### Requirement: Model verify reads compacted files
`morphir model verify` SHALL accept compacted single files and compacted tree files. It SHALL apply the context refusals of `model-name-compaction`, then check the expanded documents as it checks plain ones. The report SHALL list the files that were compacted.

#### Scenario: A compacted tree is verified after expansion
- **WHEN** a user verifies a compacted v4 tree that is valid once expanded
- **THEN** the command exits 0 and the report lists the compacted files

#### Scenario: An invalid context fails verification
- **WHEN** a compacted file references an alias no table defines
- **THEN** the command exits 1 with the `context.unknown_alias` diagnostic naming the file and the alias

### Requirement: The IR command group is removed
The CLI SHALL NOT offer an `ir` command group or an alias for it. `ir` SHALL be treated as any other unknown command: it fails with a non-zero exit code and names `ir`, as an unknown command does today (only the commands that route to an external tool, such as `mck`, start one). The changelog SHALL state the rename.

#### Scenario: The former command is unknown
- **WHEN** a user runs `morphir ir verify model.json`
- **THEN** the command fails with a non-zero exit code, names `ir` as unknown, and verifies nothing
