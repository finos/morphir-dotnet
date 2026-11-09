# compile-task-records Specification

## Purpose
Keeps the Morphir ecosystem's out-directory and task-record contract for compilation. Compile output can then be found and reused by `morphir generate` and by other Morphir tools that read the same records.

## Requirements

### Requirement: Compile output uses the workspace out root
Compile SHALL write its IR to `<out>/[<member path>/]compile.dest/morphir-ir.json` and its record to `compile.json` beside that directory. `<out>` SHALL resolve in this order:

1. `--out-dir`;
2. `MORPHIR_OUT_DIR`;
3. the workspace `workspace.out_dir`;
4. `<workspace root>/.morphir/out`.

The member path SHALL be the member's declared path relative to the workspace root.

#### Scenario: Workspace member
- **WHEN** member `packages/orders` of a workspace is compiled with default settings
- **THEN** the IR is at `<workspace>/.morphir/out/packages/orders/compile.dest/morphir-ir.json`, and the record is at `<workspace>/.morphir/out/packages/orders/compile.json`

### Requirement: Records follow the ecosystem schema
A successful compile SHALL write a schema-1 record with these fields: `task` `compile`, `module`, `language`, `inputs`, `value`, `ir {path, layout, format, version}`, `installed` and `completedAt`. `value` SHALL list product paths relative to `.dest`. Unknown fields in an existing record SHALL be preserved on rewrite, except where a tombstone clears them.

#### Scenario: Ecosystem reader accepts the record
- **WHEN** a record written by `morphir compile` is compared with the pinned upstream out-directory specification and its example record (`tests/Fixtures/MorphirOut/`)
- **THEN** it has every required field with the upstream spelling, and its `ir.version` names the produced IR version

### Requirement: Failed runs leave tombstones
Starting a compile SHALL clear `compile.dest` and replace a readable earlier record with a tombstone. The tombstone sets `tombstone: true`, empties `value` and `inputs`, removes `language`, `ir` and unknown fields, and keeps `installed` and `completedAt`. A successful compile SHALL replace the tombstone with a full record.

#### Scenario: Failure after success
- **WHEN** a compile succeeds, and a later compile of the same project fails with a source error
- **THEN** `compile.json` is a tombstone that keeps the earlier `installed` ledger, and `compile.dest` contains no IR

### Requirement: Runs of one task are serialized
A compile SHALL hold an exclusive lock on `compile.lock` from clearing `.dest` until its record and any install are written. A reader of the compile output SHALL hold a shared lock from reading the record until it has read the IR. A waiting run SHALL report one stderr line saying that it is waiting.

#### Scenario: Generate during compile
- **WHEN** `generate` without `--input` starts while a compile of the same project holds the exclusive lock
- **THEN** `generate` waits, then reads the completed record and the IR from that compile

### Requirement: Generate reads the most recent compile
When `--input` is absent, `morphir generate` SHALL resolve the project and out root in the same way as compile. It SHALL read the IR through the compile record's `ir` descriptor and keep the recorded format version. A missing record or a tombstone SHALL fail with exit code 2 and a message telling the user to run `morphir compile` first. An explicit `--input` SHALL never read a record.

#### Scenario: Compile then generate
- **WHEN** `morphir compile` succeeds, and then `morphir generate --target fsharp --output gen` runs in the same project
- **THEN** generation uses the compiled IR, and the report names the record's IR path and version as its input

#### Scenario: No prior compile
- **WHEN** `morphir generate --target fsharp --output gen` runs in a project with no compile record
- **THEN** it fails with exit code 2, says to run `morphir compile` first, and publishes nothing
