# Morphir.Ion

`Morphir.Ion` reads Morphir's Amazon Ion formats into immutable F# values. It reads Ion 1.0 text and binary, the Ion IR format as a single file or as a document tree, and the Morphir Extension Protocol (MEP) `morphir_model` document. It does not write Ion.

## Reading Ion

`IonReader.read` takes bytes and `IonReadLimits` and returns every top-level value as an `IonValue`. A value keeps its annotations in order, integers and decimals exactly (a decimal keeps its scale, so `1.50` and `1.5` differ), symbols, and the repeated field names of a struct. A read fails with an `IonReadError` when the input is not Ion, is not Ion 1.0, or goes past a limit on bytes, depth or nodes. It never raises for bad input.

## The Ion IR format

`IonIr.read limits bytes` reads a single-file distribution in Ion text or binary, as a datagram or as a collapsed `morphir::` record. It builds the same IR value the JSON readers build for the corresponding document. The result is an `IrDocument`: `ClassicIr` for `formatVersion` `3.0.0` and `3.1.0`, and `V4Ir` for `4.0.0`. Each case keeps the version the header declares.

### Ion document trees

A distribution can also be stored as a tree of Ion files, where each file's path supplies the package, module and name that its elements leave out. `IonIrTree.read limits tree` reads a `VFileTree` and returns the same `IrDocument` that the single file for that distribution gives. The reader does not touch the file system. Load the directory first, with `VFileTree.loadFromDisk`, or build the tree in memory.

The root of the tree holds `manifest.ion`. A `manifest.json`, `manifest.yaml` or `manifest.yml` beside it is `ambiguous_manifest`. A tree with no `manifest.ion` is `missing_manifest`, even when it holds one of the others, because this reader does not read JSON or YAML trees.

| Location | Holds |
| --- | --- |
| `manifest.ion` | The `morphir::` header, then one `package::spec` (an application: `package::def`) for each dependency, in order. |
| `pkg/<package>/<module>/module.ion` | The module element, then any of its types and values. |
| `pkg/<package>/<module>/<stem>.type.ion` | One type. |
| `pkg/<package>/<module>/<stem>.value.ion` | One value. |
| `deps/<package>/@/<module>/...` | The same three kinds of file for a dependency. The bare `@` segment ends the package path. The manifest must name the dependency. |

Under `pkg/` and `deps/`, every file must be one of those three kinds. Any other file there, such as `notes.md`, `x.json` or `y.10n`, is `invalid_tree_path`. A file outside `pkg/` and `deps/`, such as a `README.md` at the root, is not part of the tree and is ignored. A path segment that is empty, `.` or `..` is `invalid_tree_path`, and only `/` separates segments. A dependency directory that the manifest does not name is `invalid_tree_path`, and so is an `@` segment that carries a version, such as `@1.0.0`, since the current layout has only the bare `@`. A manifest package may also hold inline `modules`, which merge with the modules under `deps/`.

Each directory and file stem is an escaped name: an initialism segment is lowercase with a `_` prefix, so `morphir/SDK` is `morphir/_sdk` and `value-in-USD` is `value-in-_usd`, and a Windows device name takes a `_` suffix, so the type `con` is `con_.type.ion`. A file may leave out `package`, `module` and `name`. A name it states must match its path, or the read fails with `path_name_mismatch`. When the path budget cut a stem, the stem ends in `__` and the first eight hex digits of the SHA-256 of the whole escaped stem. That file must state its name, and the reader checks the name against the prefix and the digest.

The reader opens one file at a time, the manifest first, so a source that is never needed is never opened. One set of limits covers the whole tree, so many small files cannot get past a limit that one large file would reach. The first problem ends the read, and no partial distribution is returned. On an error, `Path` is the location of the file in the tree, and `Location` is the place in that file, counting its top-level values from `$[0]`. Dependencies are in manifest order. Modules and members are in path order, which is not the order of the single file, and the two read as equal values.

A file that is deep for the caller's stack is read as a single file is: if the stack runs short, the reader reads the whole tree again on a thread with a larger stack, with a new set of limits. It opens each source again, so a source must be able to open more than once. An error that stops the first attempt, such as an unreadable source, is returned without a second attempt.

The tree reader does not enforce the header's `pathBudget`. It accepts the member and reads cut stems when it meets them, but it does not check the length of any path against the budget.

`IonIrTree.read` returns a file that cannot be opened or read as `file_read_failed`. `IonIrTree.readDetailed` returns it as `SourceFailed(path, cause)` with the original exception.

### Reading a tree in a pipeline

`IonIrParser.parser limits` is a `Parser<IrDocument>` for `MorphirProcessor`, and `IonIrParser.defaultParser` uses `IonReadLimits.defaults`. It reads a file as Ion when the name ends in `.ion` or `.10n`, or the bytes start with the Ion binary marker, and it reads a directory as a tree when its root has `manifest.ion`. Anything else is `ParseError.UnsupportedInput`, so the next parser in the pipeline gets its turn. That includes a directory whose only manifest is JSON or YAML.

An Ion error becomes `ParseError.InvalidInput`, with `RuleId` set to the error's `Code`. For a tree, `Path` is the file's location and `Location` is the place in it. For a single file, `Path` is empty, because the file is the input, and `Location` is the place in it. A file that cannot be read becomes `ParseError.Failed` with the file's path and the exception.

Pipeline has its own `Error` case for message severity, so the sample spells out `Result.Error`. The sample is compiled by the tests, which also check that this text matches it.

```fsharp
open Morphir.IR.Pipeline
open Morphir.Ion

/// Reads a directory that holds an Ion document tree, and says what it holds.
let describeTree (directory: string) : string =
    match VFileTree.loadFromDisk directory with
    | Result.Error error -> TreeLoadError.describe error
    | Ok tree ->
        match IonIrTree.read IonReadLimits.defaults tree with
        | Ok(V4Ir(version, _)) -> $"IR {version}"
        | Ok(ClassicIr(version, _)) -> $"classic IR {version}"
        | Result.Error error ->
            let where = Option.defaultValue "the tree" error.Path
            $"{error.Code} in {where}"

/// Puts the Ion parser first in a pipeline. Input that is not Ion goes on to the other parsers.
let withIon (others: Parser<IrDocument> list) : MorphirProcessor<IrDocument> =
    let start =
        MorphirProcessor.empty<IrDocument>
        |> MorphirProcessor.parse IonIrParser.defaultParser

    others
    |> List.fold (fun processor parser -> MorphirProcessor.parse parser processor) start
```

### Error codes

A failed read returns an `IonIrError`. `Code` is one of the codes below, and `Path` says where the problem is, as in `$[1].values[0].body`, where `$[1]` is the second top-level value. Syntax and limit errors come from `IonReader` and have no path.

Codes the Morphir Ion draft, its compatibility kit, the format version contract, or the MEP corpus name (the MEP corpus names the three limit codes):

| Code | Meaning |
| --- | --- |
| `specs_before_3_1` | A v3 `specs` header says a `formatVersion` below `3.1.0`. |
| `definition_in_specs` | A `specs` document holds a definition. |
| `duplicate_name` | A name is defined twice: a module, a type or value after merging, a dependency's module, a parameter, a record field, a constructor, an argument of a v4 constructor, a `letrec` binding, an entry point, or a target platform. A v3 constructor lists its arguments, and the JSON reader does not check them for repeats, so neither does this reader. Two spellings of one name, such as `USD` and `u-s-d`, count as a repeat. |
| `unsupported_node` | The document's version or kind does not allow this node, such as `public::def::native::value` in v3, or a collapsed v3 `specs` record. |
| `invalid_name` | A string is not a canonical name, path or FQName. |
| `invalid_type` | A value has the wrong Ion type for its position. |
| `unknown_member` | A struct other than the header has a member its node does not define. |
| `missing_format_version` | The header has no `formatVersion`. |
| `duplicate_format_version` | The header repeats `formatVersion`. |
| `invalid_format_version_type` | `formatVersion` is not a string. |
| `invalid_format_version_syntax` | `formatVersion` is not an exact release. |
| `unsupported_format_version_major` | `formatVersion` has a major version other than 3 or 4. |
| `unsupported_format_version_minor` | `formatVersion` is a 3.x or 4.x release other than `3.0.0`, `3.1.0` and `4.0.0`. |
| `decoded_limit_exceeded` | The input has more bytes than the limit. |
| `depth_limit_exceeded` | The input nests deeper than the limit, or too deeply for the stack the decoder has. |
| `node_limit_exceeded` | The input holds more values than the limit. |

Local draft codes, which this reader defines because the upstream documents leave them unnamed. They may change when the draft names them:

| Code | Meaning |
| --- | --- |
| `invalid_document` | The layout is wrong: no header first, a missing or non-empty footer, an unknown top-level value, a header without `kind` or `packageName`, a misplaced fragment, or a v3 `package::spec` naming the header's package. In a tree it also means a footer in a tree file, a `.type.ion` or `.value.ion` file that does not hold exactly one type or value, a manifest that holds more than the header and the dependencies, or a module directory with no `module.ion`. |
| `missing_manifest` | A tree has no `manifest.ion` at its root. A root with only `manifest.json`, `manifest.yaml` or `manifest.yml` is not an Ion tree. |
| `ambiguous_manifest` | A tree's root has `manifest.ion` and also `manifest.json`, `manifest.yaml` or `manifest.yml`. The message names both. |
| `path_name_mismatch` | A name a tree file states does not match its path, or a file with a cut stem states no name, or states one whose prefix or digest does not match the stem. The same goes for a stated `package` or `module`. |
| `invalid_tree_path` | A file's place in a tree is wrong: a stem or directory the escape never writes, a file under `pkg/` or `deps/` that is not `module.ion`, `<stem>.type.ion` or `<stem>.value.ion`, a path segment that is empty, `.` or `..`, a file outside the package's directory, a dependency path with no `@` segment or an `@` segment with a version, a dependency directory the manifest does not name, or a file directly under the package directory or the `@` segment, where a module path is missing. |
| `file_read_failed` | A file in a tree could not be opened or read. `IonIrTree.readDetailed` keeps the exception. |
| `invalid_node` | A node is malformed: an unknown head or annotation, the wrong number of operands, a missing or repeated member, `annotations` on a definition, a document in a pattern, or a missing `outputType` on a body that needs one. |
| `unsupported_ion_version` | `ionVersion` is not `0.1.0-draft.1`, or a binary input is not Ion 1.0. A missing `ionVersion` means `0.1.0-draft.1`. |
| `unknown_critical_member` | The header's `critical` list names a member that is present and that this reader does not understand. A name no member answers to is accepted. |
| `literal_out_of_range` | A literal does not fit where it goes: a v3 whole number beyond 64 bits, a v3 decimal `System.Decimal` would round or cannot hold, a v3 character beyond one UTF-16 unit, or a float that is not finite. |
| `invalid_document_payload` | A document payload, `constraints` or `extensions` holds Ion with no JSON form: a float, timestamp, symbol, blob, clob, S-expression, typed null, repeated member, or an annotation other than `number::` on a number string. |
| `malformed_ion` | The input is not valid Ion. |
| `ion_ir_decode_failed` | The decoder failed in a way it did not expect, on the larger stack it falls back to. This is a defect in the reader, reported as an error rather than ending the process. |

## The `morphir_model` document

A MEP `morphir-model` payload decodes to one Ion struct annotated `morphir_model`, with exactly one each of `ir`, `enrichments` and `provenance`. `ModelDocument` reads that document and keeps all of it. It does not decode the IR until you ask.

```fsharp
open System
open Morphir.Ion

let describe (payload: ReadOnlyMemory<byte>) : string =
    match ModelDocument.readPayload IonReadLimits.defaults payload with
    | Error error -> $"{ModelReadError.code error}: {ModelReadError.describe error}"
    | Ok model ->
        // model.Bytes is the payload, unchanged. Enrichments and provenance are Ion values.
        let retained = $"{model.Bytes.Length} bytes, {model.Enrichments.Length} enrichments"

        match ModelDocument.decodeIr "4.0.0" model with
        | Ok(V4Ir(version, _)) -> $"{retained}, IR {version}"
        | Ok(ClassicIr(version, _)) -> $"{retained}, classic IR {version}"
        | Error error -> $"{retained}, IR not decoded ({error.Code})"
```

### Entry points

- `ModelDocument.readPayload limits bytes` reads a decoded `ion-binary` payload. The bytes must start with the Ion 1.0 binary marker `E0 01 00 EA` and hold one top-level value. Text gives `invalid_model_payload`.
- `ModelDocument.readFile limits bytes` reads a local file in Ion text or binary. Use it for files, not for MEP payloads.
- `ModelDocument.decodeIr expectedVersion model` decodes `ir`. A failure here does not change the model you already hold.

Base64 decoding, the declared length and the SHA-256 digest are checks on the MEP envelope. Do them before you call `readPayload`, and pass it the decoded bytes.

### What a read keeps

- `Bytes` is the input as it was read. Hash it to compare with the envelope's `sha256`.
- `Ir` is the `ir` member as an `IonValue`.
- `Enrichments` is a list of `{ Annotations; Owner; Data }`. `Annotations` are those on the enrichment struct. `Owner` is a nonempty string that contains `:`, such as a URN. `Data` is any Ion value, with its annotations, numeric exactness and repeated field names intact.
- `Provenance` is a list of Ion values.
- Members this version does not know stay in `Bytes` and in the values above.

### Shape errors

A document that is not a struct annotated `morphir_model`, that repeats or lacks `ir`, `enrichments` or `provenance`, or that has an enrichment without a namespaced `owner` or a `data` member, fails with `invalid_model_document`. Repeated field names inside `data` or `provenance` are not an error.

### Limits

`IonReadLimits` sets the most bytes, nesting depth and nodes a read accepts. A read that passes one fails with `decoded_limit_exceeded`, `depth_limit_exceeded` or `node_limit_exceeded`. A MEP caller passes the limits it negotiated. `IonReadLimits.defaults` suits local files.

### Decoding the IR

`decodeIr` takes the release you expect, such as `"4.0.0"` or `"3.0.0"`.

1. It compares that string with the `formatVersion` in `ir`. An integer `N` there means `N.0.0`. A missing `formatVersion` gives `missing_format_version`, and a different version gives `ir_version_mismatch`.
2. It writes `ir` as JSON. Structs become objects, lists become arrays, and integers and decimals become number text with every digit kept. A decimal is written as its Ion text with `d` replaced by `e`, so `123.4500` stays `123.4500` and `12d3` becomes `12e3`. A decimal with scale zero, which Ion writes as `12.`, becomes `12e0`. A decimal that would need more than 64 padding zeros, such as `5d-100`, is written as `5e-100`.
3. It passes the JSON to the reader for that version: `V4Json` for 4.x, `ClassicJson` otherwise. A reader error comes back with its rule as `Code` and its JSON location as `Location`.

Step 2 refuses what JSON has no form for, with `ir_not_json_compatible` and the path of the value in `Path`: annotations, repeated field names, symbols, S-expressions, floats, timestamps, blobs, clobs, typed nulls, and symbol zero. The refusal applies to the IR view only. The `ModelDocument` stays valid.

The result is an `IrDocument`: `V4Ir` holds a `Morphir.IR.V4.Distribution`, and `ClassicIr` holds a classic `Distribution` whose attributes are JSON values.
