# morphirup F# example

A small F# model to try `morphirup` on: one type and one rule. The walkthrough
[Your first model with morphirup](../../docs/content/docs/getting-started/first-model.md) explains each step and
shows the output. This file has the same commands, so the folder works on its own.

| File | What it is |
| --- | --- |
| `morphir.toml` | The project: package `acme/demo`, F# source in `src`, Morphir IR format 4 |
| `src/Pricing.fs` | The model: a `Car` with a plate and a number of seats, and the rule `seats` |

It works on Windows (x64, ARM64) and on Macs with Apple silicon. On Linux, `morphirup` installs, but `setup` doesn't
support Linux yet. You don't need .NET.

## Install morphirup

Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex
```

macOS:

```sh
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh
```

Open a new terminal and check it with `morphirup --version`. For the script's options and other ways to install,
see [Installation](../../docs/content/docs/getting-started/installation.md).

## Set up, compile and generate

Run these in this folder:

```shell
morphirup setup --yes
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
```

`setup --yes` downloads the Morphir CLI, the F# frontend and the F# backend, checks them against the release's
checksums and installs them. It is the only step that needs the network. `compile` writes the IR to
`.morphir/out/compile.dest/morphir-ir.json`, and `generate` writes `generated/Pricing.fs`:

```fsharp
module Acme.Demo.Pricing

type Car = { plate: string; seats: int }

let seats (car: Car) : int = (car).seats
```

## Change the rule

Keep the current output, change the last line of `src/Pricing.fs` to
`let seats (car: Car) : int = car.Seats - 1`, then compile, generate and compare.

Windows:

```powershell
Copy-Item generated generated-before -Recurse
# edit src\Pricing.fs now
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
Compare-Object (Get-Content generated-before\Pricing.fs) (Get-Content generated\Pricing.fs)
```

macOS:

```sh
cp -R generated generated-before
# edit src/Pricing.fs now
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
diff generated-before/Pricing.fs generated/Pricing.fs
```

The new generated line is `let seats (car: Car) : int = ((-) ((car).seats) (1))`.

If something goes wrong, see
[Troubleshooting](../../docs/content/docs/getting-started/troubleshooting.md).

Leave the model as it is in the repository: the `Windows released morphirup journey` workflow
(`.github/workflows/morphirup-release-journey.yml`) compiles this folder and compares the IR with a fixture and the
generated F# with a fixed SHA-256.
