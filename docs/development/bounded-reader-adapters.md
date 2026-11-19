# Development bounded reader adapters

This is the existing development MCK's small native reader lane. It does not qualify a backend, MEP session,
WASM guest, or resource budget. `bounded-generation report-check` and its frozen receipt remain unchanged.

Run the development `morphir-mck` executable with explicit selection:

```text
morphir-mck bounded-generation run --fixture /absolute/parent/spec/mck/bounded-generation/fixtures/acme-orders --adapter /absolute/dotnet --adapter-arg exec --adapter-arg /absolute/morphir-mck.dll --adapter-arg bounded-generation --adapter-arg observe --adapter-binary /absolute/morphir-mck.dll --adapter-revision <exact-dotnet-source-commit>
```

For MoonBit, select its absolute executable and arguments instead. `--adapter-binary` names the executable itself,
or an absolute artifact that appears verbatim in `--adapter-arg`, such as a WASM module passed to an explicitly
selected runtime. There is no default adapter, PATH lookup, shell command string, or fallback to .NET.
Arguments are individual argv entries; use `--adapter-arg=<value>` when an argument starts with `--`.

The runner pins the exact parent `cases.json` SHA-256
`fd72b801461873db04911ade8d4c5b9194da793a0c89cafc55be791290d1f8e4` and independent `expected.json` SHA-256
`a5e585531daa19b5343130b5cd065eb8ac7ba5a7655d8ea21dc1d605fe134c58`.
It expands the existing two version rows and four named tree profiles into eight invocations. No observer or IR
codec generates expectations. The final selected module and attribute queries come from the parent grammar;
the adapter receives locations to observe and receives no expected values.

## MoonBit adapter exchange

This is a private, experimental development exchange, not a new published Morphir schema or product route.
Each invocation receives one UTF-8 JSON document on stdin followed by EOF and writes exactly one UTF-8 JSON
response to stdout. Diagnostics belong on stderr. A successful exchange requires a complete response and
root exit with bounded EOF on both output pipes. Current native ownership APIs observe Unix exit without
exposing its exit status, so a complete bound response, rather than an inferred Unix status, is the success signal.
An adapter that fails must omit the successful response and exit nonzero.

The request has these members:

| Member | Meaning |
| --- | --- |
| `scope` | `native-reader-adapter` |
| `caseId` | `acme-orders/<irVersion>/<profile>` |
| `binding` | Exact object to echo; parent byte hashes, executable path/hash, entry binary path/hash, argv and caller-declared adapter source revision |
| `treeRoot` | Absolute selected physical tree root |
| `snapshot` | SHA-256 of the sorted relative file paths and file-byte hashes, described below |
| `irVersion` | `3.1.0` or `4.0.0` |
| `profile` | `plainJson`, `plainYaml`, `contextualJson`, or `contextualYaml` |
| `selection` | Logical module document selected for paged enumeration |
| `pageSize` | Requested production page size |
| `documentLimit` | At most 8192 source/expanded document bytes |
| `attributes` | Ordered `{document, pointer}` queries; empty for v4 |

The response is `{scope: "native-reader-adapter", caseId, binding, facts}`. Echo `caseId` and `binding` unchanged.
The `facts` object contains `scope: "native-reader"`, `revision: <snapshot>`, `irVersion`, and these observed fields:

- `distribution`, `package`, `pathBudget`, `dependencies`, and `modules`, in the parent expected grammar.
- `pages`, the arrays of logical member document IDs returned by paging the selected module, types before values.
- `finalPageBody`, in the parent's grammar, from the final selected value definition. Follow its typed value
  reference through the real reader to observe `referencedIntegerLiteral`. Do not substitute the same-named type.
- `classicAttributes`, the ordered `{document, pointer, value}` observations for the requested v3.1 attribute slots.
  Use expanded document values, including contextual classic attributes. Empty is sufficient for v4.

Module/member array order is significant. Object property order and JSON whitespace are not significant.
The runner normalizes only this mapping into the parent shape: `pages` and `finalPageBody` become the members of
`domainInventory`, with the requested `pageSize`. It compares `classicAttributes` only where applicable, v3.1.
It does not discard inventory members, private modules, roles, access, names, or escaped document IDs.

For the snapshot, sort all nine tree file paths by ordinal relative path using `/`. SHA-256 each file's exact bytes.
Concatenate `path + LF + lowercase-file-sha256 + LF` for each file, UTF-8 encode that text, and SHA-256 it.
There are no semantic IR hashes. The runner records each snapshot and verifies inputs and selected artifacts
again after observation. The adapter's source revision is declared by the caller; binary digests bind the actual
selected files, not a verified source-to-binary build attestation.

The .NET adapter uses an on-demand physical `IFileProvider`, production snapshot admission, paging, selected
typed-member/document APIs, and the reader observer. Its revision callback fingerprints the small tree again.
That extra whole-tree byte hashing is intentional for this small correctness lane and is not bounded-residency
evidence for a large model. No full IR distribution is materialized.

## Bounds and results

One adapter invocation has a 10-second deadline spanning input, root exit, stdout and stderr drains, including pipes
inherited by descendants. Requests are capped at 16 KiB, stdout at 64 KiB and stderr at 8 KiB. JSON parsing allows
depth 32, 8192 nodes, 64 properties per object, 256 entries per array and strings up to 4096 characters; duplicate
properties are refused. Trees have exactly nine files, at most 64 storage entries and 8192 bytes per file.
Creation-time Windows job or Linux/Darwin process-group ownership settles descendants on success or failure.
Native cleanup has its existing ownership semantics; it is separate from the request deadline and reports cleanup
failure instead of granting a resource pass. Unsupported owned-launch platforms fail, without legacy fallback.

Stdout is one development report with the binding, eight case results, `correctness`, and `resources`.
Correctness passes only when every normalized case matches. Resources always say `not-run` with
`resource.small_case`. Exit 0 means all eight small correctness cases passed; 1 means refusal or failed evidence;
2 means missing/invalid explicit selection. Exit 0 is not a resource qualification.

The report is deliberately not a fabricated receipt accepted by `report-check`. The prior qualified synthetic
receipt remains the independent checker control. A later qualified resource run must gather the evidence that
the existing receipt contract requires; this command does not fill missing measurements with zeroes or passes.

## Focused verification

Build the Models and MCK test projects, then run these selectors:

```text
dotnet exec tests/Morphir.Models.Tests/bin/Release/net11.0/Morphir.Models.Tests.dll --filter-test-list "Bounded reader observations"
dotnet exec tests/Morphir.Mck.Tests/bin/Release/net11.0/Morphir.Mck.Tests.dll --treenode-filter "/*/*/BoundedGenerationRunTests/*"
dotnet exec tests/Morphir.Mck.Tests/bin/Release/net11.0/Morphir.Mck.Tests.dll --treenode-filter "/*/*/BoundedGenerationReportCheckCommandTests/*"
```

Run the documented native adapter command against all eight parent cases. For the Linux file-kind control,
put a FIFO at `cases.json` in a disposable fixture directory. The runner must refuse it before launching the
adapter, with exit 1 and no case results.
