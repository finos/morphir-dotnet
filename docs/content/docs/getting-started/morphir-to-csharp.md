---
title: "Generate and run C#"
description: "Generate a typed Morphir library and execute it in a fresh C# consumer"
---

# Generate and run C#

Use a repository checkout, the Morphir CLI built from it, and SDK 10.0.403.
Generation writes source files. Compilation is a separate step and requires the
consumer's compiler and `Morphir.SDK.CSharp` package.

The fixture exports a daily rate of 5,000 cents. These PowerShell commands create
a fresh consumer and a local runtime feed without publishing a package:

```powershell
dotnet pack src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj --configuration Release --output artifacts/csharp-feed -p:Version=0.0.0-local
dotnet new console --framework net10.0 --output artifacts/csharp-pricing
dotnet add artifacts/csharp-pricing package Morphir.SDK.CSharp --version 0.0.0-local --source "$PWD/artifacts/csharp-feed"
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target csharp --output artifacts/csharp-pricing/Generated --option language-version=14 --option namespace=Pricing.Generated --json
```

Replace `artifacts/csharp-pricing/Program.cs` with:

```csharp
using Pricing.Generated;

var cents = InventoryModule.DailyRate();
if (cents != 5000) throw new System.Exception("Unexpected daily rate");
System.Console.WriteLine(cents);
```

Then compile and execute:

```powershell
dotnet build artifacts/csharp-pricing --configuration Release -p:LangVersion=14
dotnet run --project artifacts/csharp-pricing --configuration Release --no-build
```

The program prints `5000`. Generated records and variants use get-only payloads
and the SDK's `IMorphirValue` equality/hash protocol. Keep the generated files
unchanged and use their public API from your consumer code.

Omitting `namespace` uses the model package, here `Car.Rental`. Use that namespace
in the consumer when generating with default options. `language-version=15`
requires a separately selected matching .NET 11 compiler for compilation; it
does not install or invoke that compiler during generation. Keep toolchain
selection separate from the numeric language option.

Input and output must be separate. The output includes
`.morphir-generated.json`. Preserve this manifest so reruns can retain unchanged
sources and remove stale owned files while preserving your own files. Semantic
failures and cancellation before publication preserve prior output. A filesystem
failure during final commit can leave mixed output, as described in the
[command reference](../cli/generate/).

See [C# troubleshooting](../cli/troubleshooting/#c-generation) for invalid options
and located semantic refusals, and the [F# walkthrough](morphir-to-fsharp/) for
the existing F# workflow.
