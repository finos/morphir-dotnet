# TUF client spike

An evaluation of the NuGet package `Tuf` 1.1.1 as morphirup's TUF client. No product project references it. The findings and the recommendation are in [docs/content/contributing/design/spikes/tuf-client.md](../../docs/content/contributing/design/spikes/tuf-client.md); `results/` holds the generated case table.

`TufProbe` is a console program with an in-memory transport over the finos/morphir fixtures, so nothing goes to the network except the package restore from nuget.org. The project turns off central package management and lock files.

```powershell
$v1 = "<finos/morphir checkout>/docs/spec/tool-release-metadata/fixtures/v1/conformance.json"
$signed = "<directory holding signed/repository.json and signed/cases.json>"   # tests/Fixtures/Toolchain/fixtures/0.1.0-draft.1/signed

dotnet run --project spikes/tuf-client/TufProbe -c Release -- refresh $v1
dotnet run --project spikes/tuf-client/TufProbe -c Release -- conformance $v1 $signed results.md

# Native AOT (on Windows the Visual Studio Installer folder must be on PATH so vswhere.exe is found)
dotnet publish spikes/tuf-client/TufProbe -c Release -r win-arm64 -p:ProbeAot=true -o out/tuf
dotnet publish spikes/tuf-client/TufProbe -c Release -r win-arm64 -p:ProbeAot=true -p:ProbeVariant=refresh  -o out/refresh
dotnet publish spikes/tuf-client/TufProbe -c Release -r win-arm64 -p:ProbeAot=true -p:ProbeVariant=baseline -o out/baseline
dotnet publish spikes/tuf-client/TufProbe -c Release -r win-arm64 -p:ProbeAot=true -p:ProbeVariant=ed25519  -o out/ed25519
```

| Variant | What it builds |
| --- | --- |
| default | The library, `refresh` and the `conformance` case runner |
| `refresh` | The library and `refresh` only, to size the library without the case runner |
| `baseline` | No library: parses the fixture, encodes canonically and hashes the targets |
| `ed25519` | The baseline plus BouncyCastle's Ed25519 used directly to verify the fixture roots: the primitive a direct implementation needs |

Delete `TufProbe/obj` between variants: they share one intermediate folder.

The fixtures' keys are TEST-ONLY. The signed fixture's seeds are public labels, which is how the harness re-signs mutated metadata.
