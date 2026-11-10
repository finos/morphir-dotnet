# The CLI build interface for finos/morphir

finos/morphir pins this repository as `ecosystem/morphir-dotnet` and builds, qualifies and publishes the official
Morphir CLI from that commit. This repository keeps the CLI source and the build and release-profile logic. This page
lists what the parent can call and what it gets back. The requirements are in the OpenSpec change
`add-parent-cli-delivery-and-mck-dispatch` (spec `cli-build-interface`).

## What this repository still publishes

The deployment workflow (`.github/workflows/deployment.yml`), on a `v*` tag or a manual run:

- always publishes the library packages (`PublishLibs`) and the F# extension tools `Morphir.Frontend.FSharp.Extension`
  and `Morphir.CodeGeneration.FSharp.Extension` (`PublishExtensionTools`), and creates the GitHub release with the
  notes from `CHANGELOG.md`;
- builds and publishes the CLI (the legacy `morphir-<rid>` executables and archives, the `morphirup-<rid>` assets, and
  the `Morphir.Tool` package through `PublishCliTool`) only when the repository variable
  `MORPHIR_DOTNET_CLI_PUBLICATION` is `enabled`.

Leaving the variable unset is the default and stops this repository from competing with the parent. Setting it to
`enabled` restores the earlier publication without a code change, as a rollback while the parent's channel is not
live.

The `Morphir` library package (`src/Morphir`, the `Morphir.Program` API) is still one of the library packages.

## The reusable workflow

`.github/workflows/cli-offering.yml` builds one offering for one platform at one commit.

```yaml
jobs:
  native-aot-linux-x64:
    uses: finos/morphir-dotnet/.github/workflows/cli-offering.yml@<commit>
    with:
      ref: <commit>
      deployment: native-aot
      rid: linux-x64
      os: ubuntu-latest
```

Call it at the commit that `ecosystem/morphir-dotnet` pins, and pass the same commit as `ref`, so the workflow
definition and the source come from one commit.

| Input | Required | Meaning |
| --- | --- | --- |
| `repository` | no | The repository to check out. Default `finos/morphir-dotnet`. |
| `ref` | yes | The commit to build. A full SHA. |
| `deployment` | yes | `native-aot`, `self-contained`, `framework-dependent` or `dotnet-tool`. |
| `rid` | for all but `dotnet-tool` | `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64` or `linux-arm64`. Empty for `dotnet-tool`. |
| `os` | yes | The runner label. `native-aot` needs a runner of the RID's operating system. |
| `configuration` | no | Default `Release`. |

| Output | Meaning |
| --- | --- |
| `artifact-name` | The uploaded workflow artifact: `morphir-dotnet-<deployment>-<rid>`, or `morphir-dotnet-dotnet-tool-any`. |
| `artifact` | The file name: `morphir-dotnet-<release>-<deployment>-<rid>.<zip\|tar.gz>`, or `Morphir.Tool.<release>.nupkg`. |
| `sha256` | The file's SHA-256, lower-case hex. |
| `release` | The release version, the latest released version in that commit's `CHANGELOG.md`. |

The artifact holds the file, `<file>.build-launch.json`, `<file>.build-contents.json` and `<file>.sha256` (one line in
`sha256sum -c` format). It is kept for seven days. The workflow publishes nothing.

### What the workflow checks

1. The inputs: one of the four offerings, and a RID for every offering except `dotnet-tool`.
2. `./build.sh --target PackRelease --deployment <deployment> [--rid <rid>]` builds and packs the offering. It fails
   when the publish folder contradicts the launch metadata, as described in the README.
3. The file's SHA-256 matches its launch metadata.
4. The produced executable starts. The archive is extracted into a folder whose path has a space, and its launch path
   (`launchPath` in the launch metadata) runs with `--version`. For `dotnet-tool`, the package is installed with
   `dotnet tool install Morphir.Tool --tool-path` and the installed `dotnet-morphir` runs. The output must be the
   release version. The workflow never runs `dotnet exec` on an assembly, so the apphost or Native AOT file that users
   run is what starts.

This is a start check, not platform qualification. It runs on the runner's own machine with its .NET installation,
so it does not show that `native-aot` or `self-contained` runs without .NET, and it does not check a glibc floor.
[Checking the release profiles from packaged artifacts](release-profile-checks.md) has the fuller checks, and no
platform is qualified by them yet. A packaged-artifact failure seen on macOS, where a temporary input path under `/var`
resolves to `/private/var`, is unqualified evidence: it is not a reason to weaken the link checks.

## Calling the targets directly

A parent job that has the submodule checked out can run the same targets itself, from the submodule folder:

```bash
./build.sh --target PackRelease --deployment native-aot --rid linux-x64
./build.sh --target PackRelease --deployment self-contained --rid osx-arm64
./build.sh --target PackRelease --deployment framework-dependent --rid linux-arm64
./build.sh --target PackRelease --deployment dotnet-tool
```

The output is in `artifacts/release/`. The `Morphir.Tool` package id and its `dotnet-morphir` command, which
`dotnet morphir` runs, are unchanged.

## The legacy assets

Releases of this repository have attached `morphir-<rid>` (a managed, trimmed single file of the Native AOT
composition) and `morphir-<rid>-v<version>.tar.gz`. The adopted design keeps those names and bytes as an explicit
alias (REL-1). `cli-offering.yml` builds only the four offerings. If the parent keeps the alias, it can build it with
`./build.sh --target PublishSingleFile --rid <rid> --single-file-dir <dir>`, as `deployment.yml` does. Whether the
parent keeps the alias is the parent's and the maintainer's decision.

## The compatibility kit

`morphir-mck` is a separate, development-only tool from finos/morphir. The CLI does not include it. `morphir mck <args>`
runs it; see [mck](../content/docs/cli/mck.md).
