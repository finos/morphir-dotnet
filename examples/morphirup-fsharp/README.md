# morphirup F# example

A small F# model to try `morphirup` on: one type and one rule. The walkthrough
[Your first model with morphirup](../../docs/content/docs/getting-started/first-model.md) explains each step and
shows the output. This file has the same commands, so the folder works on its own.

| File | What it is |
| --- | --- |
| `morphir.toml` | The project: package `acme/demo`, F# source in `src`, Morphir IR format 4 |
| `src/Pricing.fs` | The model: a `Car` with a plate and a number of seats, and the rule `seats` |

The commands are PowerShell and need Windows x64 or ARM64. On Linux and macOS, `morphirup setup` refuses with
`setup.platform_mismatch` until releases publish a toolchain for those platforms. No .NET install is needed.

## Install morphirup

```powershell
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex
```

Or download it and check its checksum yourself:

```powershell
$version = '0.4.0-alpha.5'
$rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$base = "https://github.com/finos/morphir-dotnet/releases/download/v$version"
$bin = "$HOME\.morphirup\bin"
New-Item -ItemType Directory -Force $bin | Out-Null
Invoke-WebRequest "$base/morphirup-$rid.exe" -OutFile "$bin\morphirup.exe"
Invoke-WebRequest "$base/morphirup-v$version.sha256" -OutFile "$bin\morphirup.sha256"
$want = ((Select-String -Path "$bin\morphirup.sha256" -Pattern " morphirup-$rid.exe$").Line -split ' ')[0]
$got = (Get-FileHash "$bin\morphirup.exe" -Algorithm SHA256).Hash.ToLowerInvariant()
if ($got -ne $want) { throw "Checksum mismatch: got $got, expected $want" }
$env:PATH = "$bin;$env:PATH"
morphirup --version
```

## Set up the toolchain

Run these in this folder:

```powershell
morphirup setup --dry-run
morphirup setup --yes
morphirup show
```

The dry run reports what setup would install and changes nothing. `setup --yes` downloads the Morphir CLI, the F#
frontend and the F# backend from the release, checks each file against the release's `SHA256SUMS`, installs them
into the Home (`$HOME\.morphir`, or `MORPHIR_HOME`) and selects them. It takes about 35 seconds. `show` names the
selected generation.

## Compile and generate

```powershell
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
Get-Content generated\Pricing.fs
```

`compile` writes the Morphir IR to `.morphir\out\compile.dest\morphir-ir.json`. `generate` reads it and writes
`generated\Pricing.fs`:

```fsharp
module Acme.Demo.Pricing

type Car = { plate: string; seats: int }

let seats (car: Car) : int = (car).seats
```

## Change the rule

Keep the current output, then change the last line of `src\Pricing.fs` to
`let seats (car: Car) : int = car.Seats - 1`, so the driver's seat no longer counts:

```powershell
Copy-Item generated generated-before -Recurse
# edit src\Pricing.fs now
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
Compare-Object (Get-Content generated-before\Pricing.fs) (Get-Content generated\Pricing.fs)
```

```text
InputObject                                          SideIndicator
-----------                                          -------------
let seats (car: Car) : int = ((-) ((car).seats) (1)) =>
let seats (car: Car) : int = (car).seats             <=
```

## Offline

After setup, `compile` and `generate` need no network. With the proxy pointed at a closed port, `setup --dry-run`
fails and the other two still succeed:

```powershell
$env:HTTPS_PROXY = 'http://127.0.0.1:9'
$env:HTTP_PROXY = 'http://127.0.0.1:9'
morphirup setup --dry-run
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
Remove-Item Env:HTTPS_PROXY, Env:HTTP_PROXY
```

Leave the model as it is in the repository: the `Windows released morphirup journey` workflow
(`.github/workflows/morphirup-release-journey.yml`) compiles this folder and compares the IR with a fixture and the generated F# with a fixed SHA-256.
