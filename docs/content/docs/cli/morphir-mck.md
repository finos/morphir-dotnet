# Development compatibility runner

`morphir-mck` is a development executable. Build it with the SDK pinned in
`global.json`.

```text
dotnet build src/Morphir.Mck/Morphir.Mck.csproj -c Release
dotnet exec src/Morphir.Mck/bin/Release/net11.0/morphir-mck.dll --help
```

Keep the complete build output together. The product CLI can forward to the
development apphost when `MORPHIR_MCK` names its absolute path. Use the `.exe`
on Windows and the executable without that suffix on macOS or Linux.

## Bounded-generation receipt replay

Supply the receipt schema and control file from the parent Morphir repository.
The runner checks their exact digests and takes the case expectation from the
control file.

```powershell
$runner = "./src/Morphir.Mck/bin/Release/net11.0/morphir-mck.dll"
$parent = "C:/code/morphir"
dotnet exec $runner bounded-generation report-check `
  --schema "$parent/website/static/schemas/bounded-generation-kit/0.1.0-draft.1/receipt.schema.json" `
  --controls "$parent/spec/mck/bounded-generation/receipt.controls.json" `
  --receipt "./receipt.json"
```

The command prints one JSON document with separate correctness and resource
decisions. Exit `0` means both decisions passed. Exit `1` means the receipt
was refused or one of the decisions failed or did not run. Exit `2` means a
usage or local input error. `--controls` replays the frozen synthetic case.
Collect execution and resource evidence separately.

Use `wasm-host run --help` and `wasm-host report-check --help` for the existing
crafted worker commands.

### Real orders scale A/B receipts

Replace `--controls` with `--scale-cases` to replay a real scale receipt:

```powershell
dotnet exec $runner bounded-generation report-check `
  --schema "$parent/website/static/schemas/bounded-generation-kit/0.1.0-draft.1/receipt.schema.json" `
  --scale-cases "./orders-scale.cases.json" `
  --receipt "./orders-scale-a.receipt.json"
```

Supply exactly one of `--controls` and `--scale-cases`. The scale case source is
pinned to SHA-256 `cde3dc58b49c229bac45af57126c2aa8b2f62c6e06766c84dd7f99bb8b0c4910`
from finos/morphir PR #1198, head `305623ec52a96868a5a2e9cac7e80951b6030f73`.
Whitespace or other byte changes are refused. The receipt's `kit.caseId` selects
`orders-scale/a` or `orders-scale/b`; `kit.suiteSha256` must equal that case-file
hash, and `kit.contractSha256` must match the case source.

Admission checks the case's fixture scale, expanded input bytes, largest document,
WASM execution, complete tuple, target, guest digest and comparison kind.
Ordinary optional `fixture.modelSha256` and `fixture.documentCount` must match
when emitted. They do not change the published receipt schema. Output expectations
come from the pinned case source, never from a receipt's expected digest.

The existing receipt checker validates shape, contradictions and claimed outcomes,
then replays correctness and resources separately. Correct output with missing
measurements or unenforced limits can pass correctness while resources stay
`not-run`, yielding exit `1`. Replaying a pilot receipt does not collect evidence
or qualify resource bounds. The schema and synthetic controls remain unchanged.