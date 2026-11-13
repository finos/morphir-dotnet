---
title: "Your first model with morphirup"
linkTitle: "First model with morphirup"
weight: 1
description: "Install morphirup, set up the F# toolchain, compile a small model, generate F# from it, change a rule and see the result"
---

This walkthrough takes a newcomer from nothing to generated code. You install `morphirup`, let it set up the Morphir
toolchain, compile a small F# model to Morphir IR, generate F# from that IR, change one rule and see the change in
the output. You don't need .NET installed.

## Where this works today

The commands below were run against the published release `v0.4.0-alpha.5`.

| Platform | Install `morphirup` | `setup` and `run` |
| --- | --- | --- |
| Windows x64 | Yes | Yes. The whole walkthrough works |
| Windows ARM64 | Yes | Yes. The whole walkthrough works |
| Linux x64 and ARM64 (glibc) | Yes | No. `setup` refuses with `setup.platform_mismatch` |
| macOS ARM64 | Yes | No. `setup` refuses with `setup.platform_mismatch` |

On Linux and macOS, releases do not publish a toolchain for `setup` to install yet, so `setup` and `run` refuse:

```text
"code": "setup.platform_mismatch",
"message": "Release setup requires an actual Windows x64 or ARM64 manager process."
```

macOS support is in progress. Until it ships, follow the rest of this page on Windows. The commands are PowerShell.

## What morphirup is

`morphirup` is a manager for the Morphir toolchain. It downloads the Morphir CLI (`morphir`) together with the
providers that do the language work: an F# frontend, which reads F# and writes Morphir IR, and an F# backend, which
reads Morphir IR and writes F#. It installs them into one folder, called the Home, and runs them for you with
`morphirup run`. It is a single executable. The Morphir CLI it installs carries its own .NET runtime, so nothing
needs .NET on the machine.

## 1. Install morphirup

The quickest way is the install script. On Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex
```

On Linux or macOS:

```bash
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh
```

[Installation]({{< relref "/docs/getting-started/installation" >}}) says where the scripts put `morphirup` and how
to add it to your `PATH`.

### Or download it yourself

You can also download `morphirup` from the release and check it against the release's checksum file. This puts it
in `$HOME\.morphirup\bin`; use any folder you like:

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

```text
0.4.0-alpha.5+3abb0de0a9de5a321e109db5f2eef3afcdd429ea
```

The script picks the executable for your processor, downloads it with the release's `morphirup` checksum file, and
stops if the SHA-256 of the file you got differs from the published one. The `$env:PATH` line lasts only for this
PowerShell window. Add the folder to your user `PATH` to keep it.

On Linux, the same download and check is:

```bash
version=0.4.0-alpha.5
base=https://github.com/finos/morphir-dotnet/releases/download/v$version
curl -fsSLO "$base/morphirup-linux-arm64"
curl -fsSLO "$base/morphirup-v$version.sha256"
sha256sum -c --ignore-missing "morphirup-v$version.sha256"
chmod +x morphirup-linux-arm64
./morphirup-linux-arm64 --version
```

```text
morphirup-linux-arm64: OK
0.4.0-alpha.5+3abb0de0a9de5a321e109db5f2eef3afcdd429ea
```

Use `morphirup-linux-x64` on an x64 machine.

## 2. Get the example project

The model lives in [`examples/morphirup-fsharp`](https://github.com/finos/morphir-dotnet/tree/main/examples/morphirup-fsharp).
It has two files. Download them into a new folder:

```powershell
New-Item -ItemType Directory -Force morphirup-fsharp\src | Out-Null
Set-Location morphirup-fsharp
$raw = 'https://raw.githubusercontent.com/finos/morphir-dotnet/main/examples/morphirup-fsharp'
Invoke-WebRequest "$raw/morphir.toml" -OutFile morphir.toml
Invoke-WebRequest "$raw/src/Pricing.fs" -OutFile src\Pricing.fs
```

If you have cloned the repository, `Set-Location examples\morphirup-fsharp` does the same. Run every later command
in this folder.

## 3. Set up the toolchain

First ask `morphirup` what it would do, without doing it:

```powershell
morphirup setup --dry-run
```

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
{
  "$schema": "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/result.schema.json",
  "command": "setup",
  "status": "planned",
  "origin": "release-checksum",
  "candidateSha256": "9023ac37c208428ccff0f3eb7987d93b099ebdd6b698b5b417900b563c650242"
}
Run morphirup setup --yes to install and select this toolchain.
```

`morphirup` went to the release of its own version, `v0.4.0-alpha.5`. It downloaded two small files there:
`SHA256SUMS`, and the bundle file that lists the toolchain for your platform. It checked the bundle against
`SHA256SUMS`. `candidateSha256` names the toolchain it found. A dry run installs nothing and creates no Home.

A project can ask for another release with an exact version in `.morphir/toolchain.toml`. This example has no such
file, so setup uses the manager's own version.

Now install it:

```powershell
morphirup setup --yes
```

```text
{
  "$schema": "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/result.schema.json",
  "command": "setup",
  "status": "installed",
  "origin": "release-checksum",
  "candidateSha256": "9023ac37c208428ccff0f3eb7987d93b099ebdd6b698b5b417900b563c650242",
  "generationSha256": "7215f6002e17d0d64a06e8ca13abbcf6dc9f6046a95cea40047b5864ac645257",
  "receiptPath": "toolchains/generations/7215f6002e17d0d64a06e8ca13abbcf6dc9f6046a95cea40047b5864ac645257/receipt.json",
  "selectionPath": "toolchains/default.json"
}
morphirup: release-checksum delivery, NOT publisher-authenticated.
```

On the machine used for this page it took about 35 seconds. Setup downloaded three things from the same release: the Morphir CLI with its own .NET
runtime, the F# frontend and the F# backend. It checked the size and SHA-256 of each file against the release's
`SHA256SUMS`, installed them, ran a short readiness check with the installed tools, and only then selected them.
The installed set is a *generation*, named by `generationSha256`. Your value will differ from the one above.

The stderr line `release-checksum delivery, NOT publisher-authenticated` means what it says. HTTPS and matching
checksums show that you got the files the release published. They don't prove who published them. Signed release
metadata is later work.

Everything went into the Home. That is `.morphir` in your user folder, or the folder that the `MORPHIR_HOME`
environment variable names. Have a look:

```powershell
$morphirHome = if ($env:MORPHIR_HOME) { $env:MORPHIR_HOME } else { "$HOME\.morphir" }
Get-ChildItem $morphirHome -Name
```

```text
catalog
locks
store
tmp
toolchains
.morphir-dotnet-io.lock
```

`store` holds the installed files, each folder named by its SHA-256. `toolchains` holds the generations and
`default.json`, which records the selected one. `catalog` lists the installed providers.

## 4. See what is installed

```powershell
morphirup show
```

```text
{
  "$schema": "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/result.schema.json",
  "command": "show",
  "status": "shown",
  "origin": "release-checksum",
  "candidateSha256": "9023ac37c208428ccff0f3eb7987d93b099ebdd6b698b5b417900b563c650242",
  "generationSha256": "7215f6002e17d0d64a06e8ca13abbcf6dc9f6046a95cea40047b5864ac645257",
  "receiptPath": "toolchains/generations/7215f6002e17d0d64a06e8ca13abbcf6dc9f6046a95cea40047b5864ac645257/receipt.json",
  "selectionPath": "toolchains/default.json"
}
morphirup: release-checksum delivery, NOT publisher-authenticated.
```

`show` names the selected generation and its receipt. The receipt says which Morphir CLI is installed:

```powershell
$shown = morphirup show 2>$null | ConvertFrom-Json
Get-Content (Join-Path $morphirHome $shown.receiptPath)
```

```text
{
  "$schema": "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/receipt.schema.json",
  "origin": "release-checksum",
  "implementation": "dotnet",
  "deployment": "self-contained",
  "platform": "win-arm64",
  "version": "0.4.0-alpha.5+3abb0de0a9de5a321e109db5f2eef3afcdd429ea",
  ...
  "installationPath": "store/tools/sha256/8f11ff1e63c853a2830bdabf5b39e7785af4cb3edad6cb94e4df652e1efba5fe",
  "entryPoint": {
    "path": "morphir.exe",
    "sha256": "599362c3d81120240dbb153df8882e3baa13173b09347e77c4bd656bf99c916f"
  },
  "owner": "manager-owned",
  "readiness": "workflow-verified",
  "generationSha256": "7215f6002e17d0d64a06e8ca13abbcf6dc9f6046a95cea40047b5864ac645257"
}
```

`self-contained` means the CLI carries its own .NET runtime. `workflow-verified` means the readiness check passed.
This output comes from Windows ARM64. On Windows x64, `platform` is `win-x64` and the SHA-256 values differ.
The catalog lists the two providers:

```powershell
(Get-Content "$morphirHome\catalog\extensions.json" | ConvertFrom-Json).extensions | Select-Object extensionId, version, runtime
```

```text
extensionId                       version       runtime
-----------                       -------       -------
org.finos.morphir.fsharp-backend  0.4.0-alpha.5 process
org.finos.morphir.fsharp-frontend 0.4.0-alpha.5 process
```

`process` means each provider is a separate program that the CLI starts when it needs it.

### Choose a generation with select

Each successful `setup --yes` records a new generation and selects it. The older ones stay in the Home. Use
`morphirup select` to switch back to an earlier one, for example if a newer setup breaks your build. List the
generations, then check a switch with `--dry-run`:

```powershell
Get-ChildItem "$morphirHome\toolchains\generations" -Name
morphirup select --generation $shown.generationSha256 --dry-run
```

```text
7215f6002e17d0d64a06e8ca13abbcf6dc9f6046a95cea40047b5864ac645257
{
  "$schema": "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/result.schema.json",
  "command": "select",
  "status": "planned",
  ...
  "generationSha256": "7215f6002e17d0d64a06e8ca13abbcf6dc9f6046a95cea40047b5864ac645257",
  ...
}
morphirup: release-checksum delivery, NOT publisher-authenticated.
```

You have one generation, so there is nothing to switch to yet. Without `--dry-run`, `select` checks every installed
file of that generation and then makes it the default, and the status is `selected`. `morphirup use` is the same
command.

## 5. Read the model

`morphir.toml` describes the project:

```toml
[project]
name = "acme/demo"
source_directory = "src"

[frontend]
language = "fsharp"

[ir]
format_version = 4
```

The package is `acme/demo`. Its source is in `src`, written in F#, and the compiler writes Morphir IR format 4.

`src/Pricing.fs` is the model:

```fsharp
namespace Acme.Demo
module Pricing =
    type Car = { Plate: string; Seats: int }
    let seats (car: Car) : int = car.Seats
```

A model is ordinary F# that Morphir can read. This one has a type and a rule. A `Car` has a number plate and a
number of seats. The rule `seats` takes a car and gives back how many seats it has to sell.

## 6. Compile the model to Morphir IR

```powershell
morphirup run -- compile
```

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
[01:53:58 INF] Foundatio.Mediator registered 3 handler(s) and 0 middleware.
compile succeeded: 1 module(s), IR version 4, ...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json; no diagnostics
...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json
```

Everything after `--` goes to the installed Morphir CLI, so this is `morphir compile`. The CLI read `morphir.toml`,
handed `src/Pricing.fs` to the F# frontend, and wrote the IR to `.morphir\out\compile.dest\morphir-ir.json`. The last
line, the IR's path, is on stdout. The other lines are on stderr, and you can ignore the `[INF]` line.

Morphir IR is the model as data, independent of any language. Here is part of it:

```powershell
Get-Content .morphir\out\compile.dest\morphir-ir.json
```

```json
{
  "formatVersion": 4,
  "distribution": {
    "Library": {
      "packageName": "acme/demo",
      "dependencies": {},
      "def": {
        "modules": {
          "pricing": {
            "Public": {
              "types": {
                "car": {
                  "Public": {
                    "TypeAliasDefinition": {
                      "typeParams": [],
                      "typeExp": {
                        "Record": {
                          ...
                          "fields": {
                            "plate": {
                              "Reference": {
                                ...
                                "fqname": "morphir/SDK:string#string"
                              }
                            },
                            "seats": {
                              "Reference": {
                                ...
                                "fqname": "morphir/SDK:basics#int"
                              }
                            }
  ...
              "values": {
                "seats": {
                  "Public": {
                    "ExpressionBody": {
                      "inputTypes": {
                        "car": {
                          "Reference": {
                            ...
                            "fqname": "acme/demo:pricing#car"
                          }
                        }
                      },
                      "outputType": {
                        "Reference": {
                          ...
                          "fqname": "morphir/SDK:basics#int"
                        }
                      },
                      "body": {
                        "Field": {
                          ...
                          "target": {
                            "Variable": {
                              ...
                              "name": "car"
                            }
                          },
                          "name": "seats"
  ...
}
```

It holds one package, `acme/demo`, with one module, `pricing`. The module has the type `car`, a record of `plate`
(a string) and `seats` (an int), and the value `seats`, which takes a `car` and returns an int. Its body reads the
field `seats` of the variable `car`. Each `fqname` is a full name, `package:module#name`, and `morphir/SDK` is
Morphir's standard library. Morphir stores names as words, without the F# capitals. The `...` parts are
`attributes`, which record the source line and column of each piece and the type the compiler worked out for it.

## 7. Generate F# from the IR

```powershell
morphirup run -- generate --target fsharp --output generated
Get-Content generated\Pricing.fs
```

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
[01:54:00 INF] Foundatio.Mediator registered 3 handler(s) and 0 middleware.
Pricing.fs
module Acme.Demo.Pricing

type Car = { plate: string; seats: int }

let seats (car: Car) : int = (car).seats
```

`generate` read the IR that `compile` wrote, handed it to the F# backend, and wrote `generated\Pricing.fs`. This F#
comes from the IR, not from your source, so it spells names the way the backend writes them: `plate` and `seats` in
lower case. Any program that reads Morphir IR can work from the same file.

## 8. Change a rule

Say the business decides that the driver's seat is not for sale. First keep a copy of the current output, so you
can compare:

```powershell
Copy-Item generated generated-before -Recurse
```

Open `src\Pricing.fs` in an editor and change the last line, so that the file reads:

```fsharp
namespace Acme.Demo
module Pricing =
    type Car = { Plate: string; Seats: int }
    let seats (car: Car) : int = car.Seats - 1
```

Compile again, look for the change in the IR, generate again and compare:

```powershell
morphirup run -- compile
Select-String -Path .morphir\out\compile.dest\morphir-ir.json -Pattern 'subtract'
morphirup run -- generate --target fsharp --output generated
Compare-Object (Get-Content generated-before\Pricing.fs) (Get-Content generated\Pricing.fs)
```

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
[01:54:02 INF] Foundatio.Mediator registered 3 handler(s) and 0 middleware.
compile succeeded: 1 module(s), IR version 4, ...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json; no diagnostics
...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json

.morphir\out\compile.dest\morphir-ir.json:236:                                  "fqname": "morphir/SDK:basics#subtract"
morphirup: release-checksum delivery, NOT publisher-authenticated.
[01:54:04 INF] Foundatio.Mediator registered 3 handler(s) and 0 middleware.
Pricing.fs

InputObject                                          SideIndicator
-----------                                          -------------
let seats (car: Car) : int = ((-) ((car).seats) (1)) =>
let seats (car: Car) : int = (car).seats             <=
```

In the IR, the body of `seats` now calls `subtract` from Morphir's standard library, where it used to read the
field. In the generated F#, `<=` marks the old line and `=>` the new one. The backend writes the subtraction as a
call of the operator `(-)` with the two operands, `car.seats` and `1`. It means `car.seats - 1`. That is the whole
loop: change the F# model, compile, generate.

## 9. Work offline

`setup` needs the network. After it, `compile` and `generate` use only the files in the Home. To try it, point
PowerShell at a proxy that does not exist, so that any download fails:

```powershell
$env:HTTPS_PROXY = 'http://127.0.0.1:9'
$env:HTTP_PROXY = 'http://127.0.0.1:9'
morphirup setup --dry-run
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
Remove-Item Env:HTTPS_PROXY, Env:HTTP_PROXY
```

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
{
  "$schema": "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/result.schema.json",
  "command": "setup",
  "status": "refused",
  "origin": "release-checksum",
  "diagnostic": {
    "code": "acquisition.refused",
    "message": "The bounded release transfer was refused by transport or byte-integrity checks."
  }
}
morphirup: release-checksum delivery, NOT publisher-authenticated.
[01:54:07 INF] Foundatio.Mediator registered 3 handler(s) and 0 middleware.
compile succeeded: 1 module(s), IR version 4, ...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json; no diagnostics
...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json
morphirup: release-checksum delivery, NOT publisher-authenticated.
[01:54:10 INF] Foundatio.Mediator registered 3 handler(s) and 0 middleware.
Pricing.fs
```

The dry run fails because it cannot reach the release, which shows the network really is cut off. Compile and
generate still succeed. You can also unplug the network and run the same two commands.

## Next steps

- [Compile F# to Morphir IR]({{< relref "/docs/getting-started/compile-fsharp" >}}) writes a larger rule and
  generates C# from it.
- [Generate F# from a Morphir model]({{< relref "/docs/getting-started/generate-fsharp" >}}) covers the F# backend's
  options, such as `--option format=false`, which writes the same bytes on every operating system.
- [Configure a workspace]({{< relref "/docs/getting-started/configuration" >}}) explains `morphir.toml`.
- [morphirup]({{< relref "/docs/cli/morphirup" >}}) is the reference for every `morphirup` command.
