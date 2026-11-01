---
title: "Myriad F# generation reference"
linkTitle: "Myriad F# reference"
weight: 35
description: "Configure classic Morphir JSON generation, module files and diagnostics"
---

`Morphir.Myriad` translates classic Morphir library JSON into one F# file per module during a .NET 10 F# build. See [getting started]({{< relref "/docs/getting-started/morphir-to-fsharp" >}}) for the first run and the [car rental tutorial]({{< relref "/docs/guides/car-rental-tutorial" >}}) for a complete business workflow.

## Configure a consumer

Reference the package and add each model once:

```xml
<ItemGroup>
  <PackageReference Include="Morphir.Myriad" Version="YOUR_VERSION" PrivateAssets="all" />
  <MorphirModel Include="models/business.json" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

Use the `VersionOverride` convention if your repository manages package versions centrally. The model path resolves from the consuming project directory. Paths containing spaces are supported. The package supplies its backend, plugin and build task dependencies, so these tools do not become runtime references in the application.

| Setting | Meaning |
|---------|---------|
| `MorphirModel` item | One classic library JSON input |
| `MorphirModel.OutputDirectory` | Optional model-owned output directory, relative to the project directory or absolute |
| `MorphirMyriadHostPath` | Optional path to the compatible Myriad host assembly |

Only assign an output directory to one model. The build rejects duplicate output ownership and generated filename collisions. Distinct input models with identical basenames receive distinct default output identities.

## Version and host contract

The plugin uses Myriad.Core 0.85.0, Fabulous.AST 1.10.0, Fantomas.Core 7.0.5 and the repository's FSharp.Core 11.0.100. The package bundles the real Myriad 0.85.0 host and launches it with .NET major-version roll forward. A cold first restore supplies the whole generation toolchain through the single package reference.

The host's FSharp.Core must match the plugin's runtime identity when formatting crosses their boundary. The bundled host is aligned with the plugin's FSharp.Core and the coordinator uses an isolated runtime; it does not change the restored NuGet cache. Package contents include upstream attribution and third-party notices. Use the package's normal build integration to obtain this setup automatically.

The generator attribute name is `morphir-fsharp`, its CLR type name is `MorphirFSharpGenerator`, and it accepts `.json`. It reads `GeneratorContext.InputFilename` and requires the `MorphirModule` additional parameter, a dot-separated canonical module path such as `Inventory` or `Domain.Types`. Each call returns only the selected module as `Output.Source`.

For direct host use with an appropriately aligned host runtime, the important arguments are:

```text
dotnet --roll-forward LatestMajor <Myriad.dll> \
  --inputfile <model.json> --outputfile <module.fs> \
  --plugin <Morphir.Myriad.dll> \
  --generator-filter MorphirFSharpGenerator \
  --additionalparams MorphirModule=Inventory
```

In Myriad 0.85.0, `--generator-filter` selects the CLR type name. Passing the attribute name there does not select this generator. The normal `MorphirModel` build integration supplies the correct selection and output ordering for every module.

## Supported inputs and declarations

Input must contain an explicit integer `formatVersion` of 1, 2 or 3 and a matching classic `Library` distribution. Missing versions, version strings, fractional versions, other distributions and IR 4+ fail explicitly. A document tree is not an input to this plugin.

The backend translates aliases, generic variables, closed records, custom unions, tuples, functions and unit, along with classic values and patterns. These include literals, curried application, lambdas, bindings, local recursion, destructuring, conditions, ordered matches, record construction/access/update and field functions. Open extensible records are rejected instead of losing their row information.

Package and module qualification is retained. Record fields and values use Morphir camelCase; types and cases use PascalCase. F# keywords are escaped. Unsupported collisions and cycles between separate modules produce diagnostics. Private declarations retain restricted visibility in generated code.

## Initial SDK mappings

| Morphir SDK | Generated representation |
|------------|--------------------------|
| `Basics.Int`, `Basics.Float`, `Basics.Bool` | `int`, `float`, `bool` |
| `String.String`, `Char.Char` | `string`, `char` |
| `List.List a` | `a list` |
| `Maybe.Maybe a`, `Just`, `Nothing` | `a option`, `Some`, `None` |
| `Result.Result error value`, `Ok`, `Err` | `Result<value, error>`, `Ok`, `Error` |
| `Basics.Order` and its cases | `Morphir.SDK.Order` |
| Addition, subtraction, multiplication, division | Corresponding curried F# operators |
| Equality and ordered comparisons | Corresponding F# comparisons |
| `List.map`, `Maybe.map`, `Result.map` | `List.map`, `Option.map`, `Result.map` |

Unmapped SDK names and specification-only external references fail with their qualified name. Generated code using SDK-specific types such as `Order` needs the `Morphir.SDK` runtime reference; the supplied car rental model uses built-in mappings only. `Int` uses the existing .NET SDK's 32-bit representation; an out-of-range whole-number literal is rejected rather than narrowed silently. The car rental sample uses whole days and integer cents within that range.

## Output, regeneration and failure behavior

By default output lives under `obj/<configuration>/<target-framework>/morphir/<model-identity>/`. Each module has a distinct relative `.fs` path, and `manifest.json` records dependency order. Generated compilation inputs precede handwritten files. Empty libraries produce an empty manifest; no JSON is compiled as F#.

Ordinary builds plan the current model and run generation. Identical source and manifests are not rewritten. New output is staged and published after the model succeeds, and obsolete files owned by the prior manifest are removed. A failed generation stops compilation even when an older successful output exists. Design-time builds can read the last successful manifest. Build once normally after a clean checkout to establish it.

The coordinator isolates output by model, configuration and target framework. Avoid sharing an explicit output directory across unrelated builds. Model module/declaration maps are ordered deterministically, while argument lists and match branches retain their semantic order. Source contains no timestamps or absolute input paths.

## Troubleshooting

| Symptom | Corrective step |
|---------|-----------------|
| Missing model path | Correct `MorphirModel.Include` relative to the consumer project |
| `unsupported-version` | Supply explicit classic integer version 1, 2 or 3 |
| Decode error | Check the reported location against that version's wire shape |
| Open-record diagnostic | Replace the unsupported open row with a closed model type |
| Module dependency cycle | Refactor the model's cross-module cycle; files remain separate |
| Unmapped SDK/external symbol | Use a supported mapping or provide an implementation in the model |
| `fsharp-generation` | Review the rendering reason and IR declaration; constructs without an F# representation, such as empty unions, fail without partial output |
| Duplicate output directory or path | Assign distinct model ownership and names |
| Host method/assembly mismatch | Use the documented pinned packages and automatic isolated host setup |
| Old plugin code after repacking | Use a new development package version and restore it |

Business errors such as `InvalidDuration` in the car rental example are results from generated functions. Generation diagnostics instead identify an input file and available JSON location or qualified symbol. Plugin diagnostic logging goes to stderr. IR 4+, document trees, generated codecs/lenses and a Morphir CLI generation command remain separate initiatives.
