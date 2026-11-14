---
title: "Publish an extension as a NuGet package"
linkTitle: "Publish to NuGet"
weight: 40
description: "Pack a Morphir extension as a NuGet package, so users install it with morphir extension install nuget:"
---

A .NET team already publishes its tools to NuGet feeds: nuget.org, a private V3 feed such as Azure Artifacts, or a
folder feed. This guide packs a Morphir extension for those feeds, so that a user installs it with:

```bash
morphir extension install nuget:Acme.Morphir.Python@1.2.3 --home "$HOME/.morphir" --no-probe
```

The command reads the user's feeds, credentials and signature policy from `nuget.config`. The full rules are the
[NuGet package convention](https://morphir.finos.org/docs/design/draft/extensions/distribution-and-acquisition#nuget-package-convention)
of the extension family `0.1.0-preview.3`; this page shows how to follow them with the .NET SDK.

## The parts of a NuGet release

| Part | Where | What it is |
| --- | --- | --- |
| The main package | `Acme.Morphir.Python` 1.2.3 | Holds `morphir/release.json` and has the package type `MorphirExtension` |
| The release descriptor | `morphir/release.json` in the main package | The extension family descriptor: id, version, artifacts and their claims |
| A process artifact | a folder (`root`) in the main package or in another package | The files of the extension for one platform; the descriptor pins them by `treeSha256` |
| A WASM artifact | the entry `morphir/<filename>` of the main package | One module; the descriptor pins it by `sha256` |

The installer reads `release.json` only from the main package. It installs only the files under the `root` of the
selected artifact: `.nuspec`, `_rels/`, `[Content_Types].xml`, `package/`, `.signature.p7s` and other folders stay
out. Digests cover the package contents, not the `.nupkg` file, because nuget.org signs each package after you push
it, and that changes the file.

## Choose a layout

**Layout A: one package.** Each platform is a folder of the main package, such as `morphir/linux-x64`. Use it for a
small extension, or for a WASM extension, whose module is the entry `morphir/<filename>` of the main package.

```text
Acme.Morphir.Python 1.2.3
  morphir/release.json
  morphir/linux-x64/acme-python
  morphir/osx-arm64/acme-python
  morphir/win-x64/acme-python.exe
```

**Layout B: one package for each platform.** The main package holds only `morphir/release.json`. Each platform has
its own package whose id is the main id, a `.`, and more, at the same version and in the same feed. A user downloads
only the package of their platform.

```text
Acme.Morphir.Python 1.2.3                  morphir/release.json
Acme.Morphir.Python.osx-arm64 1.2.3        tools/any/osx-arm64/acme-python, ...
Acme.Morphir.Python.linux-x64 1.2.3        tools/any/linux-x64/acme-python, ...
```

An artifact package must extend the main id: `Acme.Morphir.Python.linux-x64` extends `Acme.Morphir.Python`, but
`Acme.Morphir.Pythonx` and `Contoso.Tools` do not, and the installer refuses them with
`extension.invalid_descriptor`. On nuget.org, reserve the id prefix so that nobody else can push such a package.

## A .NET tool that is also an extension

A RID-specific .NET tool is layout B. `dotnet pack` (.NET SDK 11) writes a main package that lists the RID packages,
and one package for each runtime identifier. A Native AOT tool is under `tools/any/<rid>` in its RID package, with its
`DotnetToolSettings.xml`; other tools are under `tools/<tfm>/<rid>`. Check with `unzip -l` and use that folder as the
`root`. Give the tool both package types, and publish it self-contained or as Native AOT:

```xml
<PropertyGroup>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>acme-python</ToolCommandName>
  <PackageId>Acme.Morphir.Python</PackageId>
  <PackageType>DotnetTool;MorphirExtension</PackageType>
  <ToolPackageRuntimeIdentifiers>linux-x64;osx-arm64;win-x64</ToolPackageRuntimeIdentifiers>
  <RuntimeIdentifiers>linux-x64;osx-arm64;win-x64</RuntimeIdentifiers>
  <SelfContained>true</SelfContained>
  <PublishAot>true</PublishAot>
</PropertyGroup>
<ItemGroup>
  <None Include="morphir/release.json" Pack="true" PackagePath="morphir/release.json" />
</ItemGroup>
```

`dotnet tool install Acme.Morphir.Python` still installs the same packages. The installer refuses a framework-dependent
tool (an `entryPoint` that ends in `.dll`, or an `<entry point>.runtimeconfig.json` that names a shared framework) with
`extension.unsupported_package`, because it needs an installed .NET runtime to start.

`dotnet pack` copies each `Pack="true"` item into every package of the tool, so each RID package also has a
`morphir/release.json`. That is harmless: it is outside the `root`, and the installer reads the descriptor only from
the main package.

## Write the descriptor

Each process artifact has the package format `nupkg`, a `root`, an `entryPoint` relative to the root, and a
`treeSha256`. Its `filename` is the lower-case `<id>.<version>.nupkg` of the package that holds it. A `nupkg` artifact
has no `sha256`. Mark `artifacts.package` critical.

```json
{
  "$schema": "https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json",
  "extensionId": "com.acme.python",
  "shortId": "acme-python",
  "version": "1.2.3",
  "platformDifferences": "declared",
  "artifacts": [
    {
      "runtime": "process",
      "platform": "aarch64-apple-darwin",
      "filename": "acme.morphir.python.osx-arm64.1.2.3.nupkg",
      "package": {
        "format": "nupkg",
        "root": "tools/any/osx-arm64",
        "entryPoint": "acme-python",
        "treeSha256": "<tree digest of tools/any/osx-arm64>"
      },
      "claims": { "...": "the claims of the extension" }
    }
  ],
  "critical": ["artifacts.package"]
}
```

The descriptor `version` must equal the package version, compared as NuGet versions. The installer selects the
`process` artifact for the user's platform, else a `wasm` artifact. A release may offer both.

## Compute a tree digest

The tree digest is the SHA-256 of the canonical package manifest of the files under the root: the line
`morphir-package-tree 0.1.0-preview.1`, then one line for each file in ordinal order of its path, `x` for the entry
point and each declared executable and `f` for another file, its length in bytes, its SHA-256 and its path relative to
the root. Each line ends with LF. This shell function computes it for a folder, with the executable paths after it:

```bash
tree_digest() {
  root="$1"; shift
  {
    printf 'morphir-package-tree 0.1.0-preview.1\n'
    (cd "$root" && find . -type f | sed 's#^\./##' | LC_ALL=C sort) | while IFS= read -r path; do
      kind=f
      for executable in "$@"; do [ "$executable" = "$path" ] && kind=x; done
      size=$(wc -c < "$root/$path" | tr -d ' ')
      sum=$(shasum -a 256 "$root/$path" | cut -d ' ' -f 1)
      printf '%s %s %s %s\n' "$kind" "$size" "$sum" "$path"
    done
  } | shasum -a 256 | cut -d ' ' -f 1
}

# The root folder of a packed RID package, unzipped
unzip -q artifacts/nupkg/Acme.Morphir.Python.osx-arm64.1.2.3.nupkg -d /tmp/osx-arm64
tree_digest /tmp/osx-arm64/tools/any/osx-arm64 acme-python
```

Compute it from the files in the packed package, not from the publish folder: the package is what users install.
On Linux, use `sha256sum` in place of `shasum -a 256`.

## Publish in two passes

A RID package's tree digest exists only after it is packed, and the main package must carry the finished
`release.json`. So pack in this order:

1. Pack the RID packages (or, for layout A, build the platform folders).
2. Compute the tree digest of each root, and write the finished `morphir/release.json`.
3. Pack the main package with that `release.json`.
4. Push the RID packages, then the main package, to the same feed with the same version. A user who finds the main
   package then finds every package it names.

For a .NET tool, `dotnet pack` writes the main and the RID packages in one run. Run it once to get the RID packages,
write `release.json`, and run it again. The RID packages of the second run have the same files under their roots, so
their tree digests do not change.

## Check the release locally

A folder of `.nupkg` files is a NuGet feed. Install from it before you push:

```bash
morphir extension install nuget:Acme.Morphir.Python@1.2.3 --source ./artifacts/nupkg \
  --home "$PWD/.morphir-test" --no-probe --dry-run
morphir extension install nuget:Acme.Morphir.Python@1.2.3 --source ./artifacts/nupkg \
  --home "$PWD/.morphir-test" --no-probe
```

`package.tree_digest_mismatch` means that a `treeSha256` does not match the files under its root: compute it again
from the packed package. `extension.release_descriptor_missing` means that the main package has no
`morphir/release.json`.

The repository has no command that computes a tree digest for an author yet
([#657](https://github.com/finos/morphir-dotnet/issues/657)); the shell function above follows the same rule as the
installer. `tests/Fixtures/NuGetExtension/build.sh` runs these steps on a Native AOT tool: it packs in two passes,
computes the tree digest with this function, checks that the second pass keeps the tree, and checks that
`dotnet tool install` installs the same packages. The installer tests read the packages it wrote.
