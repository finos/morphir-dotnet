---
title: "Your first model with morphirup"
linkTitle: "First model with morphirup"
weight: 1
description: "Install morphirup, set up the toolchain, compile a small F# model, generate F# from it, then change a rule and see the result"
---

In this walkthrough you install `morphirup`, set up the Morphir toolchain, compile a small F# model, and generate F#
from it. Then you change one rule and see the change in the output. You don't need .NET installed.

| Platform | Supported |
| --- | --- |
| Windows x64 and ARM64 | Yes |
| macOS on Apple silicon | Yes |
| Linux x64 and ARM64 | `morphirup` installs, but `setup` doesn't support Linux yet |

Intel Macs are not supported. If something goes wrong, see [Troubleshooting]({{< relref "/docs/getting-started/troubleshooting" >}}).

## What morphirup is

`morphirup` installs the Morphir command line, `morphir`, together with the F# support it needs: a frontend that
turns F# into Morphir IR, and a backend that turns Morphir IR into F#. `morphirup run` runs them for you.

## 1. Install morphirup

{{< tabpane text=true persist=header >}}
{{% tab header="Windows" %}}
In PowerShell:

```powershell
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex
```
{{% /tab %}}
{{% tab header="macOS" %}}
In a terminal:

```sh
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh
```
{{% /tab %}}
{{< /tabpane >}}

The script downloads the newest `morphirup`, checks it against the release's checksums, and installs it:

```text
morphirup-install: finding the newest release with morphirup-win-arm64.exe
morphirup-install: downloading morphirup-win-arm64.exe 0.4.0-alpha.5
morphirup-install: SHA-256 verified: 6bb59736f765a9f2cbdf7262a1f07838c15c6025927f10f7642f4307a8d237f3
morphirup-install: installed ...\morphirup.exe
morphirup-install: morphirup --version: 0.4.0-alpha.5+3abb0de0a9de5a321e109db5f2eef3afcdd429ea
```

Your version may be newer. Open a new terminal, then check that `morphirup` is found:

```shell
morphirup --version
```

```text
0.4.0-alpha.5+3abb0de0a9de5a321e109db5f2eef3afcdd429ea
```

If the terminal says `morphirup` is not found, see
[morphirup is not found]({{< relref "/docs/getting-started/troubleshooting#morphirup-is-not-found" >}}).
[Installation]({{< relref "/docs/getting-started/installation" >}}) has the other ways to install it.

## 2. Get the example project

The example has two files. Download them into a new folder, and run every later command in that folder:

{{< tabpane text=true persist=header >}}
{{% tab header="Windows" %}}
```powershell
New-Item -ItemType Directory -Force morphirup-fsharp\src | Out-Null
Set-Location morphirup-fsharp
$raw = 'https://raw.githubusercontent.com/finos/morphir-dotnet/main/examples/morphirup-fsharp'
Invoke-WebRequest "$raw/morphir.toml" -OutFile morphir.toml
Invoke-WebRequest "$raw/src/Pricing.fs" -OutFile src\Pricing.fs
```
{{% /tab %}}
{{% tab header="macOS" %}}
```sh
mkdir -p morphirup-fsharp/src && cd morphirup-fsharp
raw=https://raw.githubusercontent.com/finos/morphir-dotnet/main/examples/morphirup-fsharp
curl -fsSL -o morphir.toml "$raw/morphir.toml"
curl -fsSL -o src/Pricing.fs "$raw/src/Pricing.fs"
```
{{% /tab %}}
{{< /tabpane >}}

If you have cloned the repository, the same files are in `examples/morphirup-fsharp`.

## 3. Set up the toolchain

See what setup would install, without installing anything:

```shell
morphirup setup --dry-run
```

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
{
  ...
  "command": "setup",
  "status": "planned",
  ...
}
Run morphirup setup --yes to install and select this toolchain.
```

Then install it:

```shell
morphirup setup --yes
```

```text
{
  ...
  "command": "setup",
  "status": "installed",
  ...
}
morphirup: release-checksum delivery, NOT publisher-authenticated.
```

Setup downloads the Morphir CLI, the F# frontend and the F# backend from the same release as `morphirup`. It checks
every file against the release's checksums, tries the tools out, and then makes them the ones `morphirup run` uses.
It took about 35 seconds here. This is the only step that needs the network.

`setup`, `show` and `run` all print the line `release-checksum delivery, NOT publisher-authenticated`. It means the files
matched the checksums the release published; it doesn't mean they were signed.
[Troubleshooting]({{< relref "/docs/getting-started/troubleshooting#the-release-checksum-note" >}}) says more.

Check what is installed:

```shell
morphirup show
```

```text
{
  ...
  "command": "show",
  "status": "shown",
  ...
}
morphirup: release-checksum delivery, NOT publisher-authenticated.
```

`"status": "shown"` means a toolchain is set up and selected. Each `setup --yes` installs a new copy and selects it;
`morphirup select` switches back to an earlier one. See the
[morphirup reference]({{< relref "/docs/cli/morphirup" >}}) for both.

## 4. Read the model

`morphir.toml` names the project:

```toml
[project]
name = "acme/demo"
source_directory = "src"

[frontend]
language = "fsharp"

[ir]
format_version = 4
```

The package is `acme/demo`, its source is F# in the `src` folder, and Morphir IR is written in format 4.

`src/Pricing.fs` is the model:

```fsharp
namespace Acme.Demo
module Pricing =
    type Car = { Plate: string; Seats: int }
    let seats (car: Car) : int = car.Seats
```

A model is ordinary F#. This one has a type and a rule. A `Car` has a number plate and a number of seats. The rule
`seats` says how many seats a car has to sell.

## 5. Compile the model

```shell
morphirup run -- compile
```

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
compile succeeded: 1 module(s), IR version 4, ...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json; no diagnostics
...\morphirup-fsharp\.morphir\out\compile.dest\morphir-ir.json
```

Everything after `--` goes to the Morphir CLI. It read `morphir.toml`, compiled `src/Pricing.fs`, and wrote the model
as Morphir IR to `.morphir/out/compile.dest/morphir-ir.json`.

Morphir IR is your model as data that any Morphir tool can read. Open the file and you'll find, among the details:

```json
{
  "formatVersion": 4,
  "distribution": {
    "Library": {
      "packageName": "acme/demo",
      ...
        "modules": {
          "pricing": {
            ...
              "types": {
                "car": {
                  ...
                          "fields": {
                            "plate": {
                              ...
                                "fqname": "morphir/SDK:string#string"
                            ...
                            "seats": {
                              ...
                                "fqname": "morphir/SDK:basics#int"
              ...
              "values": {
                "seats": {
                  ...
                      "body": {
                        "Field": {
                          ...
                          "name": "seats"
  ...
}
```

The package `acme/demo` has one module, `pricing`. It has the type `car`, a record with a string `plate` and an int
`seats`, and the rule `seats`, whose body reads the field `seats`. Morphir writes names without the F# capitals.

## 6. Generate F#

{{< tabpane text=true persist=header >}}
{{% tab header="Windows" %}}
```powershell
morphirup run -- generate --target fsharp --output generated
Get-Content generated\Pricing.fs
```
{{% /tab %}}
{{% tab header="macOS" %}}
```sh
morphirup run -- generate --target fsharp --output generated
cat generated/Pricing.fs
```
{{% /tab %}}
{{< /tabpane >}}

```text
morphirup: release-checksum delivery, NOT publisher-authenticated.
Pricing.fs
module Acme.Demo.Pricing

type Car = { plate: string; seats: int }

let seats (car: Car) : int = (car).seats
```

`generate` read the IR from the last compile and wrote `generated/Pricing.fs`. This F# comes from the IR, not from
your source file, which is why the field names are in lower case.

## 7. Change a rule

Say the driver's seat is not for sale. Keep a copy of the current output, so you can compare:

{{< tabpane text=true persist=header >}}
{{% tab header="Windows" %}}
```powershell
Copy-Item generated generated-before -Recurse
```
{{% /tab %}}
{{% tab header="macOS" %}}
```sh
cp -R generated generated-before
```
{{% /tab %}}
{{< /tabpane >}}

Open `src/Pricing.fs` in an editor and change the last line:

```fsharp
    let seats (car: Car) : int = car.Seats - 1
```

Compile, generate and compare:

{{< tabpane text=true persist=header >}}
{{% tab header="Windows" %}}
```powershell
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
Compare-Object (Get-Content generated-before\Pricing.fs) (Get-Content generated\Pricing.fs)
```
{{% /tab %}}
{{% tab header="macOS" %}}
```sh
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
diff generated-before/Pricing.fs generated/Pricing.fs
```
{{% /tab %}}
{{< /tabpane >}}

On Windows the comparison ends with:

```text
InputObject                                          SideIndicator
-----------                                          -------------
let seats (car: Car) : int = ((-) ((car).seats) (1)) =>
let seats (car: Car) : int = (car).seats             <=
```

`<=` marks the old line and `=>` the new one. The generated F# now subtracts 1 from the seats. It writes
`car.seats - 1` as a call of the operator `(-)`, which means the same. That's the whole loop: change the model,
compile, generate.

## 8. Work offline

Setup needed the network. Compile and generate don't. To see this, point the terminal at a proxy that doesn't exist,
so that every download fails:

{{< tabpane text=true persist=header >}}
{{% tab header="Windows" %}}
```powershell
$env:HTTPS_PROXY = 'http://127.0.0.1:9'
$env:HTTP_PROXY = 'http://127.0.0.1:9'
morphirup setup --dry-run
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
Remove-Item Env:HTTPS_PROXY, Env:HTTP_PROXY
```
{{% /tab %}}
{{% tab header="macOS" %}}
```sh
export HTTPS_PROXY=http://127.0.0.1:9 HTTP_PROXY=http://127.0.0.1:9
morphirup setup --dry-run
morphirup run -- compile
morphirup run -- generate --target fsharp --output generated
unset HTTPS_PROXY HTTP_PROXY
```
{{% /tab %}}
{{< /tabpane >}}

`setup --dry-run` fails because it can't reach the release:

```text
  "status": "refused",
  ...
    "code": "acquisition.refused",
    "message": "The bounded release transfer was refused by transport or byte-integrity checks."
```

Compile and generate still succeed, with the same output as before.

## Next steps

- [Troubleshooting]({{< relref "/docs/getting-started/troubleshooting" >}}) covers the problems you're most likely
  to meet.
- [Compile F# to Morphir IR]({{< relref "/docs/getting-started/compile-fsharp" >}}) writes a larger rule and
  generates C# from it.
- [Generate F# from a Morphir model]({{< relref "/docs/getting-started/generate-fsharp" >}}) shows more of
  `generate`.
- [Configure a workspace]({{< relref "/docs/getting-started/configuration" >}}) explains `morphir.toml`.
- The [morphirup reference]({{< relref "/docs/cli/morphirup" >}}) lists every `morphirup` command.
