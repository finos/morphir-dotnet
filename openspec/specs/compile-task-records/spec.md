# compile-task-records Specification

## Purpose
Keeps the Morphir ecosystem's out-directory and task-record contract for compilation. Compile output can then be found and reused by `morphir generate` and by other Morphir tools that read the same records.

## Requirements

### Requirement: Compile output uses the workspace out root
Compile SHALL write its IR to `<out>/[<member path>/]compile.dest/morphir-ir.<ext>` (`json`, `yaml`, `ion`) or `compile.dest/morphir-ir` (`document-tree`), with `compile.json` beside it. `<out>` resolves from `--out-dir`, `MORPHIR_OUT_DIR`, `workspace.out_dir`, then `<workspace root>/.morphir/out`; `<member path>` is relative to the workspace root.

#### Scenario: Workspace member
- **WHEN** member `packages/orders` of a workspace is compiled with default settings
- **THEN** the IR is at `<workspace>/.morphir/out/packages/orders/compile.dest/morphir-ir.json`, and the record is at `<workspace>/.morphir/out/packages/orders/compile.json`

### Requirement: Compile output is published whole
The IR SHALL be staged beside its place in `compile.dest` and moved into place whole before the record is written. A write failure, a cancellation or a record failure before the record is complete SHALL remove the staged and moved IR from `compile.dest`, retrying a bounded number of times while another handle holds a file. After an earlier successful compile the tombstone of the run SHALL stay; a first compile SHALL leave no `compile.json`.

#### Scenario: Second move onto an existing IR
- **WHEN** a publication cannot move its staged IR because an IR is already at the target
- **THEN** it fails with `compile.publish_failed` and removes only its staged IR, and the IR at the target stays

#### Scenario: Write failure of a document tree
- **WHEN** an earlier compile of the project succeeded, and writing the staged tree of a later compile fails
- **THEN** the later compile fails with `compile.publish_failed` and exit code 2, `compile.dest` is empty, and `compile.json` is the tombstone of the later run

### Requirement: Cleanup refusals keep the outcome and are reported
When a failed or cancelled publication cannot remove a path after the bounded retries, the run SHALL keep its outcome (exit code 130 for a cancellation, its failure otherwise), SHALL complete no record, and SHALL write a warning on stderr that names each path that remains with the file system's reason and says what `compile.json` holds while the run holds `compile.lock`. Every removal SHALL run before the warnings, and a warning that stderr refuses SHALL NOT change the outcome.

#### Scenario: Rollback refused after a cancellation
- **WHEN** an earlier compile of the project succeeded, and a later compile is cancelled after its IR moved into place, and every attempt to remove that IR is refused
- **THEN** the exit code is 130, `compile.json` is the tombstone of the later run, and stderr has a warning that names the IR that remains and says so

#### Scenario: Rollback refused after a completed record
- **WHEN** a publication of a run completed the record, and a later publication of the same run fails and its staged IR cannot be removed
- **THEN** the warning says that `compile.json` is a completed record, not a tombstone

#### Scenario: Rollback refused on a first compile
- **WHEN** the first compile of a project is cancelled after its IR moved into place, and every attempt to remove that IR is refused
- **THEN** the exit code is 130, there is no `compile.json`, and the warning says that there is none

### Requirement: A compile clears compile.dest when it starts
A compile SHALL clear `compile.dest` with the same bounded retries when it starts, and SHALL remove the staged records (`.compile.json.<id>.tmp`) that earlier runs left. A staged record that it cannot remove SHALL be a warning of the start, and the compile SHALL go on and can succeed. It SHALL fail with `compile.prepare_failed` and exit code 2, naming `compile.dest`, when it cannot, and exit with code 130 when it is cancelled during the retries. Either way it SHALL release `compile.lock`.

#### Scenario: Staged record that the start cannot remove
- **WHEN** a staged record that an earlier run left cannot be removed when a compile starts
- **THEN** stderr has a warning that names it with the file system's reason, and the compile goes on and can complete its record

#### Scenario: Stale stage of a crashed run
- **WHEN** a staged IR that a crashed run left is in `compile.dest`, and another handle holds a file below it for longer than the retries
- **THEN** the compile fails before it compiles with `compile.prepare_failed` and exit code 2, naming `compile.dest` and the file system's reason

### Requirement: Records follow the ecosystem schema
A successful compile SHALL write a schema-1 `compile.json` with `task: "compile"`, `module`, `language`, `inputs`, `value` (paths relative to `.dest`), `ir {path, layout, format, version}`, `installed` and `completedAt`, preserving unknown fields except on tombstones. `ir.path` and `ir.format` SHALL match the written file or `morphir-ir` directory and format.

#### Scenario: Document-tree record
- **WHEN** a compile with `ir.layout = "document-tree"` succeeds
- **THEN** the record has `"value": ["morphir-ir"]` and `ir` with `path` `morphir-ir`, `layout` `document-tree`, `format` matching the selected `ir.format` (`json`, `yaml`, or `ion`), and `version` `v4` for IR 4 or `v3.1.0` for IR 3 JSON/YAML trees (or `v3` / `v3.1.0` for Ion trees)

#### Scenario: Record of an Ion compile
- **WHEN** `morphir compile --ir-format ion` succeeds
- **THEN** `value` is `["morphir-ir.ion"]`, and `ir` is `{ "path": "morphir-ir.ion", "layout": "single-file", "format": "ion", "version": "v4" }`

#### Scenario: Ecosystem reader accepts the record
- **WHEN** a record written by `morphir compile` is compared with the pinned upstream out-directory specification and its example record (`tests/Fixtures/MorphirOut/`)
- **THEN** it has every required field with the upstream spelling, and its `ir.version` names the produced IR version

### Requirement: Failed runs leave tombstones
Starting a compile SHALL clear `compile.dest` and replace a readable earlier record with a tombstone. The tombstone sets `tombstone: true`, empties `value` and `inputs`, removes `language`, `ir` and unknown fields, and keeps `installed` and `completedAt`. A successful compile SHALL replace the tombstone with a full record.

#### Scenario: Cancellation during publication
- **WHEN** an earlier compile of the project succeeded, and a later compile is cancelled after its IR is staged and before its record is complete
- **THEN** `compile.dest` is empty, `compile.json` is the tombstone of the later run, and the exit code is 130

#### Scenario: Failure after success
- **WHEN** a compile succeeds, and a later compile of the same project fails with a source error
- **THEN** `compile.json` is a tombstone that keeps the earlier `installed` ledger, and `compile.dest` contains no IR

### Requirement: Runs of one task are serialized
A compile SHALL hold an exclusive lock on `compile.lock` from clearing `.dest` until its record and any install are written. A reader of the compile output SHALL hold a shared lock from reading the record until it has read the IR. A waiting run SHALL report one stderr line saying that it is waiting.

#### Scenario: Generate during compile
- **WHEN** `generate` without `--input` starts while a compile of the same project holds the exclusive lock
- **THEN** `generate` waits, then reads the completed record and the IR from that compile

### Requirement: Generate reads the most recent compile
Without `--input`, `morphir generate` SHALL resolve project and out root like compile and read the IR via the compile record's `ir` descriptor in its recorded format (`json`, `yaml`, or `ion`; single-file or document-tree) and version. A missing record or tombstone SHALL fail with exit code 2 directing the user to run `compile`. Explicit `--input` SHALL never read a record.

#### Scenario: Compile then generate
- **WHEN** `morphir compile` succeeds, and then `morphir generate --target fsharp --output gen` runs in the same project
- **THEN** generation uses the compiled IR, and the report names the record's IR path and version as its input

#### Scenario: Bounded generation from a recorded document tree
- **WHEN** `morphir compile` with `ir.layout = "document-tree"` (`json` or `yaml`) succeeds, and then `morphir generate --target <id> --extension <backend>` runs without `--input`
- **THEN** generate reads the tree through the record in a bounded session under MEP `0.2.0-draft.2`, offering the recorded IR version and codec, and holds the compile read lease until the backend has stopped

#### Scenario: Generate after a YAML compile
- **WHEN** `morphir compile --ir-format yaml` succeeds, and then `morphir generate --target fsharp --output gen` runs in the same project
- **THEN** generation uses the compiled YAML IR, and the report names the record's IR path and version as its input

#### Scenario: No prior compile
- **WHEN** `morphir generate --target fsharp --output gen` runs in a project with no compile record
- **THEN** it fails with exit code 2, says to run `morphir compile` first, and publishes nothing
