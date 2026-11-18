# Spec Delta

## Purpose

Gives users one command, `morphir model convert`, that changes how a Morphir model is stored (a single file or a document tree, plain or compacted) without changing the model, its IR version or its meaning.

```console
$ morphir model convert --input morphir-ir.json --output tree --layout document-tree
$ morphir model convert --input tree --output morphir-ir.json --layout single-file
$ morphir model convert --input morphir-ir.json --output compacted.json --compact
$ morphir model convert --input compacted-tree --output plain-tree
```

## ADDED Requirements

### Requirement: The input's layout and encoding are detected
The command SHALL read a file as a single-file model and a directory holding a manifest as a document tree. It SHALL detect from each file's envelope whether the input is compacted. It SHALL take the IR version from the input and never change it.

#### Scenario: A directory is read as a tree
- **WHEN** `--input` names a directory holding `manifest.json`
- **THEN** the command reads it as a document tree and reports the layout `document-tree`

#### Scenario: The IR version is kept
- **WHEN** a v3 `Library` single file is converted to a tree and back
- **THEN** the report names IR version 3 for both runs and the final file says `"formatVersion": 3`

### Requirement: The output is chosen by layout and encoding
`--layout document-tree|single-file` SHALL choose the output layout and default to the input's layout. `--compact` SHALL choose compacted output and its absence plain output. Every combination SHALL work for IR 3.1.0 and IR 4. A request that changes neither the layout nor the encoding SHALL fail with exit code 2 and a message naming the input's layout and encoding.

#### Scenario: A single file is compacted and stays one file
- **WHEN** a v4 single file is converted with `--compact` and no `--layout`
- **THEN** the output is one compacted file

#### Scenario: A compacted tree becomes a plain single file
- **WHEN** a compacted v4 tree is converted with `--layout single-file`
- **THEN** the output is one plain file equal to the expanded model

#### Scenario: Nothing to do
- **WHEN** a plain single file is converted with no `--layout` and no `--compact`
- **THEN** the command exits 2 and names the input as a plain single file

### Requirement: Compaction takes an optional mode
`--compact` SHALL take an optional mode that names which compactions to apply. The mode `names` SHALL apply name interning under the parent's writer policy, and a bare `--compact` SHALL mean `names`. An unknown mode SHALL fail with exit code 2 and list the known modes. The report SHALL name the mode and the policy.

#### Scenario: Bare compact means names
- **WHEN** a model is converted with a bare `--compact`
- **THEN** the report names mode `names` and the policy that wrote the files

#### Scenario: Unknown mode
- **WHEN** a model is converted with `--compact=bogus`
- **THEN** the command exits 2 and lists `names` as the known mode

#### Scenario: A compaction that no alias pays for is written plain, with a notice
- **WHEN** a model none of whose names is worth an alias under the policy is converted with `--compact`
- **THEN** the written file is the plain document, the report states `outputEncoding` `plain`, and its notices hold `model.compaction_declined`

### Requirement: An option value is given only with an equals sign
`--compact` SHALL take its mode only in the form `--compact=<mode>`. A bare `--compact` SHALL never take the next argument as its mode. `--compact names` SHALL fail with exit code 2 and suggest `--compact=names`.

#### Scenario: A separated mode is refused with a hint
- **WHEN** a model is converted with `--compact names`
- **THEN** the command exits 2 and the message suggests `--compact=names`

### Requirement: A refused conversion leaves no output
When the input is refused, the command SHALL exit 1 with the refusal's named diagnostic and SHALL leave nothing at `--output`. An `--output` that already holds files SHALL be refused before anything is written.

#### Scenario: A refused tree writes nothing
- **WHEN** a tree with a missing definition file is converted
- **THEN** the command exits 1, names the missing file, and `--output` does not exist afterwards

#### Scenario: A non-empty output is refused
- **WHEN** `--output` names a directory that holds a file
- **THEN** the command exits 1 with a diagnostic naming the directory, and the file is unchanged

### Requirement: The report states what was converted
The command's report SHALL hold success, exit code, input and output paths, input and output layouts and encodings, the IR version, the compaction mode and policy when compacting, every file written, and all diagnostics and notices. Its machine output SHALL follow the CLI JSON output convention, with a `fileWritten` event per file. Diagnostics in human mode SHALL go to stderr.

#### Scenario: Every written file is reported
- **WHEN** a model with several modules is converted to a tree with JSON output
- **THEN** the report lists every file written, and the stream has one `fileWritten` event for each

#### Scenario: Notices are in the report
- **WHEN** a v4 tree with `version` and `created` in its manifest is converted to a single file
- **THEN** the report holds a notice naming each member, and the command exits 0
