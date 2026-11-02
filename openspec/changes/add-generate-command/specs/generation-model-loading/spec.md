# Spec Delta

## Purpose

Read supported Morphir model representations into versioned typed IR for generation while retaining the original input and reporting format-specific failures.

## ADDED Requirements

### Requirement: Supported generation input representations

The loader SHALL accept versioned classic JSON files, v4 JSON files, supported single-file Ion IR in text or binary, and Ion document trees. It SHALL return one model per input. This delivery SHALL reject YAML model input, JSON/YAML document trees and MEP model-envelope documents with an unsupported-format or unsupported-profile diagnostic.

#### Scenario: Classic JSON versions

- **WHEN** a nonempty valid classic Library declares integer formatVersion 1, 2 or 3
- **THEN** loading returns the corresponding typed classic model and its source version

#### Scenario: Ion forms

- **WHEN** equivalent supported classic Ion IR is supplied as text, binary or an Ion document tree
- **THEN** loading returns equivalent typed distributions with the declared semantic version preserved

#### Scenario: V4 loading is independent of backend selection

- **WHEN** valid v4 JSON or Ion is loaded
- **THEN** loading succeeds independently of whether the selected generator supports v4

#### Scenario: Envelope is not bare IR

- **WHEN** input is a `morphir_model` wrapper or a MEP model-envelope JSON object
- **THEN** loading reports that model-envelope admission is unavailable and does not treat it as bare IR

### Requirement: Deterministic format selection

An explicit format SHALL constrain parser selection. Automatic detection SHALL recognize the Ion binary marker before textual hints and use supported names, manifests and content to select a codec. Recognized malformed input SHALL retain its codec's diagnostic rather than fall through to an unrelated codec and succeed.

#### Scenario: Binary Ion without a conventional suffix

- **WHEN** a file begins with the Ion 1.0 binary marker and format selection is automatic
- **THEN** the loader selects the Ion reader without decoding the bytes as UTF-8 first

#### Scenario: Explicit format contradicts input

- **WHEN** JSON is explicitly selected for an Ion input
- **THEN** loading fails as JSON input and does not silently switch to Ion

#### Scenario: Malformed Ion

- **WHEN** a recognized Ion file is malformed
- **THEN** the loader reports the Ion reader's error rather than masking it with a JSON error

### Requirement: Version and distribution identity remain explicit

The loaded model SHALL distinguish classic and v4 IR and retain the declared source version and distribution kind. Missing, malformed and unsupported versions SHALL fail without assuming v3. Loading SHALL not migrate versions or change Library, Specs or Application identity to satisfy a generator.

#### Scenario: Semantic version is retained

- **WHEN** a supported Ion document declares formatVersion `3.1.0`
- **THEN** the result retains `3.1.0` even though its semantic model belongs to the classic family

#### Scenario: No inferred version

- **WHEN** input omits its required version or declares an unsupported version
- **THEN** loading fails with a version diagnostic and returns no successful model

### Requirement: Original input survives semantic decoding

The loader SHALL retain an owned snapshot of file bytes or tree entries alongside typed IR. It SHALL preserve source format and available path context. Retention SHALL not depend on caller mutation or reopening changed files. A projection that cannot preserve required data SHALL refuse rather than silently discard it.

#### Scenario: Input changes after loading

- **WHEN** the original file or caller-owned byte array changes after a successful load
- **THEN** the loaded snapshot and decoded model remain unchanged

#### Scenario: Unknown permitted representation data

- **WHEN** a supported codec accepts annotations or optional representation facts that the typed view does not expose
- **THEN** the original snapshot retains those bytes and the loader does not claim the typed view is a lossless serialization

### Requirement: Typed input is independent of its serialization

Equivalent supported inputs SHALL present equivalent typed model views to generation without JSON write-and-reparse conversion. A generator SHALL not need to reference the Ion codec to consume a classic model. Exact source-format details SHALL remain available separately from the semantic view.

#### Scenario: Generation parity

- **WHEN** equivalent supported JSON, Ion text, Ion binary and Ion-tree libraries are loaded and generated with the same settings
- **THEN** F# source and ordered module manifests are equivalent

### Requirement: Bounded input capture

Loading SHALL enforce finite byte, depth and node limits and finite tree-entry and path limits before unbounded capture or decoding. Tree byte limits SHALL cover the aggregate input. Symlinks and special files SHALL be refused. Exceeding a limit or cancellation SHALL return a structured error without publishing output.

#### Scenario: Aggregate tree limit

- **WHEN** individually small Ion files together exceed the configured total byte limit
- **THEN** loading fails with a limit diagnostic instead of accepting the tree

#### Scenario: Unreadable or linked input

- **WHEN** an input entry is unreadable, is a symbolic link or is a special file
- **THEN** loading reports the affected path and no backend receives a partial model

### Requirement: Loading diagnostics preserve useful context

Failures SHALL distinguish unsupported input, malformed models, limit violations and operational read failures. Diagnostics SHALL preserve the codec rule, input path and available logical location; tree failures SHALL name the member file. The public result SHALL not contain raw exceptions or arbitrary model payloads.

#### Scenario: Invalid tree member

- **WHEN** an Ion type file contains an invalid node
- **THEN** the diagnostic identifies that tree-relative file and the reader's rule and logical location
