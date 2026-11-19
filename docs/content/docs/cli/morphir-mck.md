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
usage or local input error. This command replays the frozen synthetic case.
Collect execution and resource evidence separately.

Use `wasm-host run --help` and `wasm-host report-check --help` for the existing
crafted worker commands.
