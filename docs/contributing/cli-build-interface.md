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
| `os` | yes | The runner label: a native runner of the RID's operating system and architecture, for example `windows-11-arm` for `win-arm64`. |
| `configuration` | no | Default `Release`. |

| Output | Meaning |
| --- | --- |
| `artifact-name` | The uploaded workflow artifact: `morphir-dotnet-<deployment>-<rid>`, or `morphir-dotnet-dotnet-tool-any`. |
| `artifact` | The file name: `morphir-dotnet-<release>-<deployment>-<rid>.<zip\|tar.gz>`, or `Morphir.Tool.<release>.nupkg`. |
| `sha256` | The file's SHA-256, lower-case hex. |
| `release` | The release version, the latest released version in that commit's `CHANGELOG.md`. |

The named release profile stamps the offering identity at build time. The registry's `host` still names the command;
it is not inferred from the offering or process execution mode. Builds outside a named profile leave the deployment
unspecified. The parent owns official `v<version>` tags and publishes all four offerings from the selected source.

The artifact holds the file, `<file>.build-launch.json`, `<file>.build-contents.json` and `<file>.sha256` (one line in
`sha256sum -c` format). It is kept for seven days. The workflow publishes nothing.

### What the workflow checks

1. The inputs: one of the four offerings, and a RID for every offering except `dotnet-tool`.
2. For an offering with a RID, `./build.sh --target CheckBuildMachine --rid <rid>` fails unless the runner's operating
   system and architecture are the RID's and the .NET SDK runs natively there, not as x64 under emulation on ARM64. It
   logs the machine, so the job's log records what built the file. No offering is cross-built for another architecture.
3. `./build.sh --target PackRelease --deployment <deployment> [--rid <rid>]` builds and packs the offering. It fails
   when the publish folder contradicts the launch metadata, as described in the README.
4. The file's SHA-256 matches its launch metadata.
5. The produced executable starts. The archive is extracted into a folder whose path has a space, and its launch path
   (`launchPath` in the launch metadata) runs with `--version`. For `dotnet-tool`,
   `.github/scripts/install-tool-package.ps1` installs the package into a new tool path, and the installed
   `dotnet-morphir` runs. The install reads only a new `nuget.config` that lists a feed holding nothing but the built
   package, with new global-packages, HTTP and plugin caches, so the same id and version on nuget.org or in a machine
   cache cannot be chosen. It then checks that the package in the tool store
   (`.store/<id>/<version>/<id>/<version>/<id>.<version>.nupkg`) has the built package's SHA-256, and that every
   extracted `tools/` file equals the built package's entry, with no other file and no other package.

   The script has two optional switches for callers such as the parent's release qualification. `-Local` installs
   into a new tool manifest instead of a tool path. The SDK then extracts the package into the new global-packages
   folder and records the command's entry assembly in its resolver cache under a new `DOTNET_CLI_HOME`, and the same
   checks apply there. The tool then runs as `dotnet morphir` from the manifest folder, with `NUGET_PACKAGES` and
   `DOTNET_CLI_HOME` set as recorded. `-ResultFile <path>` writes what was run and checked as JSON (internal
   evidence, not a contract): the install command and its working directory, the package and cache folders, the built
   SHA-256, the verified archive paths with their SHA-256, and the invocation. The full rules are in the script's
   header. The output must be the
   release version, which the executable prints with the commit as build metadata (`0.4.0-alpha.4+<sha>`); the part
   before `+` must equal the `release` of the launch metadata. The workflow never runs `dotnet exec` on an assembly, so the apphost or Native AOT file that users
   run is what starts.

This is a start check, not platform qualification. It runs on the runner's own machine with its .NET installation,
so it does not show that `native-aot` or `self-contained` runs without .NET, and it does not check a glibc floor.
The reusable workflow itself has not run on GitHub. Its helper's default, `-Local` and `-ResultFile` paths have separate
tests in `Morphir.Build.Tests`; those tests do not establish that the workflow has run.
[Checking the release profiles from packaged artifacts](release-profile-checks.md) has the fuller checks, and no
platform is qualified by them yet. The packaged-test harness resolves its working directory to a physical path before
preparing absolute input paths. This avoids passing macOS's `/var` alias for `/private/var` to bounded input capture,
which deliberately refuses linked ancestors. A regression test supplies a linked temporary root. This harness fix
also resolves the discovery boundary to the same physical root. The first fresh macOS packaged run,
[43010471928](https://github.com/finos/morphir-dotnet/actions/runs/43010471928) at `f4fc12a2`, showed that resolving
only the working directory still produced a `discovery.boundary` refusal. The corrected harness has a failing-then-
passing linked-root regression on Windows. The corrected
[macOS packaged-artifact run 43014397958](https://github.com/finos/morphir-dotnet/actions/runs/43014397958)
at `1b9b6367` passed 72 tests with two skips and no failures. It built its artifact on that runner; acquired-release
and minimum-platform qualification remain outstanding.

Each packaged command run in a clean image gets a unique container name. The check force-removes that container
after the command, including after a client timeout or exception, with a separate one-minute cleanup limit. A failed
cleanup fails the check, since stopping the Docker client alone does not stop the container managed by the daemon.

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

Releases of this repository have attached `morphir-<rid>` and `morphir-<rid>-v<version>.tar.gz`, which held a
managed, trimmed single-file build of the Native AOT composition. The maintainer decided on 9 November 2026 (REL-1 of
`add-deployment-and-acquisition`, finos/morphir#1146) that future official files under these names carry the same
Native AOT bytes as the `native-aot` archive, and that the trimmed managed build is retired as a product build. The
assets of published releases stay as they are.

So the parent builds the `native-aot` offering with `cli-offering.yml` and publishes that executable under the legacy
names too; it does not run `PublishSingleFile`. The gated `build-executables` job of `deployment.yml` still runs
`PublishSingleFile`. That is the earlier release path kept as the rollback, and it is not the decided product
build.

`win-arm64` is a released platform (maintainer decision, 12 November 2026). The rollback `build-executables` and
`build-morphirup` matrices build it on the hosted `windows-11-arm` runner, so a rollback release attaches
`morphir-win-arm64.exe`, `morphir-win-arm64-v<version>.tar.gz`, `morphirup-win-arm64.exe` and
`morphirup-win-arm64-v<version>.tar.gz`, and the morphirup checksum file lists the morphirup ones. The pull request
workflow `morphirup.yml` builds and checks `morphirup` there too. The parent's official release calls `cli-offering.yml`
with `rid: win-arm64` and `os: windows-11-arm` for each platform offering, and builds `morphirup` for `win-arm64` with
`PackMorphirup` and `CheckMorphirup` on that runner. Published releases keep the assets they have.

## The compatibility kit

`morphir-mck` is a separate, development-only tool from finos/morphir. The CLI does not include it. `morphir mck <args>`
runs it; see [mck](../content/docs/cli/mck.md).

The helper and `mck` routing do not acquire or install extensions. The parent's merged extension-state and package
plans in finos/morphir#1150 and #1151 belong to their installer follow-up. Package leases do not change this routing
contract. Contract-test pins and CLI delivery source pins have separate purposes; updating this build interface does
not move either pin.
