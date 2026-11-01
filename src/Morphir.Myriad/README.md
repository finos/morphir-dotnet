# Morphir.Myriad

Generate one F# file per module from a classic Morphir library JSON model during an ordinary .NET 10 F# build.

```xml
<ItemGroup>
  <PackageReference Include="Morphir.Myriad" Version="YOUR_VERSION" PrivateAssets="all" />
  <MorphirModel Include="models/morphir-ir.json" />
</ItemGroup>
```

The package includes the Myriad 0.85.0 build tool, loads `morphir-fsharp`, and invokes it once for each module using its `MorphirModule` selector. The host runs with .NET major-version roll forward so the .NET 10 plugin can load. Consumers need only this package reference; the host, plugin, backend and MSBuild task dependencies are packaged together, including for the first build with an empty NuGet package cache.

Upstream Myriad 0.85.0 bundles FSharp.Core 9, while the backend targets FSharp.Core 11. This package's host uses FSharp.Core 11 to keep host, plugin and Fantomas async types compatible. The coordinator runs a private copy inside its staging directory and removes it after generation. The NuGet cache remains unchanged.

For a direct host invocation, use this package's `tools/host/Myriad.dll` with `dotnet --roll-forward LatestMajor`, and pass `--plugin <package>/tools/plugin/Morphir.Myriad.dll --generator-filter MorphirFSharpGenerator --additionalparams MorphirModule=Inventory` along with input/output file arguments. Myriad 0.85.0 filters by CLR class name, even though the plugin's generator attribute is named `morphir-fsharp`. A separately installed upstream host needs the same FSharp.Core alignment.

Generated files and their ordered `manifest.json` live under `obj/<configuration>/<target-framework>/morphir/<model-identity>/`. They precede handwritten F# compilation inputs. Relative JSON paths resolve from the project directory. The optional `OutputDirectory` metadata on `MorphirModel` selects a model-owned output directory.

Input must declare integer `formatVersion` 1, 2 or 3 and contain a classic Library distribution. IR 4+, document trees and a Morphir CLI command are outside this delivery. Open extensible records, cross-module dependency cycles, unmapped SDK/external references and definite type mismatches such as a string literal under a declared `Int` produce build errors.

Generation folds SDK operations on literals where the result matches run-time behaviour, so `add 2 3` appears as `5` in generated source. It does not change which models are accepted.

A failed generation blocks compilation, including when old generated files exist. Successful generation removes files owned by the previous manifest when their modules disappear. Identical source files are not rewritten on repeat builds. Ordinary builds generate; design-time builds can read the last successful manifest.

`ThirdPartyNotices.txt` records bundled component versions, authors, license metadata and available original notices. The upstream Myriad README is preserved under `tools/host/UPSTREAM-README.md`.

See the repository's [getting-started article](https://finos.github.io/morphir-dotnet/docs/getting-started/morphir-to-fsharp/), [plugin reference](https://finos.github.io/morphir-dotnet/docs/guides/myriad-fsharp/) and [car rental tutorial](https://finos.github.io/morphir-dotnet/docs/guides/car-rental-tutorial/). The runnable example lives in `examples/car-rental/` in the repository.
