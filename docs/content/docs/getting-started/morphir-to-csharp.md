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

The program prints `5000`. Don't edit the generated files: call their public API from your own code, and generate
again when the model changes.

Without the `namespace` option, the generated code uses the model's package name, here `Car.Rental`.

Keep the generated output in its own folder, apart from the input. Leave the `.morphir-generated.json` file in it:
the next `generate` uses it to update only the files it owns and to keep yours. The
[generate reference]({{< relref "/docs/cli/generate" >}}) covers the options, including `language-version`, and what
happens when generation fails.

See [C# troubleshooting]({{< relref "/docs/cli/troubleshooting#c-generation" >}}) for invalid options and model
errors, and [Generate F# from Morphir]({{< relref "/docs/getting-started/morphir-to-fsharp" >}}) for F#.