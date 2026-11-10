# Qualifying the release profiles from packaged artifacts

Task 2.4 of `add-deployment-and-acquisition` qualifies the four .NET offerings from the archives that users get,
not from build output folders. This page says how to run that qualification, what it checks, what each platform's
status is, and what is not established yet.

## Run it

Pack the profile for this machine's platform, then run the E2E suite with the offering as the executable type:

```bash
./build.sh --target PackRelease --deployment native-aot --rid linux-x64
./build.sh --target TestE2E --executable-type native-aot

# all four offerings: TestE2E packs any missing profile first
./build.sh --target TestE2E --executable-type offerings
```

On Windows use `build.cmd`. The Native AOT link on Windows needs `vswhere.exe` on `PATH`
(`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`).

`MORPHIR_EXECUTABLE_TYPE` selects the offering for a direct run of the test assembly, and `MORPHIR_RELEASE_DIR`
points at a release folder other than `artifacts/release`. The run checks the archive's SHA-256 against its launch
metadata, installs it into a new temporary folder whose path contains a space (`dotnet tool install --tool-path` for
`dotnet-tool`), runs every non-manual scenario against it, and removes the folder afterwards.

On demand per platform, without a pull request job:

- **Manual E2E Testing** (`.github/workflows/manual-e2e-test.yml`): choose the platforms and an executable type
  (`offerings` or one offering). It uploads the size report and the launch and contents files.
- **Manual AOT Testing** (`.github/workflows/manual-aot-test.yml`): the `packaged` suite packs and qualifies
  `native-aot` for one platform.

## What the scenarios check

`tests/Morphir.E2E.Tests/Features/AOT/PackagedArtifacts.feature` takes each expectation from the artifact's own
launch metadata (`<artifact>.build-launch.json`), so it also checks that the metadata is true. The other E2E features
(basic commands, error handling, extension list, IR verification, validate, management) run against the installed
artifact too.

| Scenario | Checks |
|---|---|
| The installed artifact is the one its launch metadata names | Archive SHA-256 and launch path |
| The installed layout is what the offering claims | native-aot: exactly one file, no .NET runtime or assembly. self-contained: a folder with `hostfxr`, `coreclr`, `System.Private.CoreLib.dll` and FCS. framework-dependent: a folder with FCS and a runtimeconfig that names `Microsoft.NETCore.App`, no runtime. dotnet-tool: the tool installation with the managed composition and FCS, no `morphir.dll` |
| Startup from a clean directory | `--version` from a new empty folder prints the artifact's release |
| Core commands | `extension list --json` names the host and the shared built-ins; `--help` |
| An F# compile follows the artifact's frontend | Managed offerings compile in process (`morphir-dotnet.fsharp (in-process)`); native-aot refuses with `compile.frontend_unavailable`, and the message names `dotnet-morphir` and `--extension` |
| Reading an input file | `validate --input`. On Linux and macOS this reads the file kind through `libSystem.Native`, the fix in #556. On Windows the code path returns before that call, so a Windows run does not exercise it |
| No installed .NET runtime | See below |
| The declared runtime | framework-dependent and dotnet-tool run on the machine's runtime: the host loads `hostfxr` from a machine runtime root and chooses a `Microsoft.NETCore.App` 10.x. native-aot and self-contained never load the machine's runtime, even when it is reachable |

### How "no installed .NET runtime" is shown

The run removes every `DOTNET_*` variable, sets `DOTNET_MULTILEVEL_LOOKUP=0`, drops every `PATH` entry that holds a
`dotnet` executable, and records a .NET host trace (`COREHOST_TRACE=1`, `COREHOST_TRACEFILE`). The assertion reads
what the process loaded, not only whether it started:

- **native-aot**: no host trace exists, because a Native AOT executable runs no .NET host, and the layout scenario
  shows the installation holds no runtime files.
- **self-contained**: the trace says "Using app-local location", and every `hostfxr`, CoreCLR and loaded-library path
  in it is inside the installation. None is under a machine runtime root: `DOTNET_ROOT*`, the host that runs the
  tests, any `dotnet` on `PATH`, and the default and registered locations.
- **framework-dependent** and **dotnet-tool**: the apphost exits with `0x80008083` and prints "You must install .NET
  to run this application" with a download link. The installation guide repeats what to do
  ([installation](../content/docs/getting-started/installation.md)).

Environment variables cannot hide a runtime at the apphost's default or registered location (`C:\Program Files\dotnet`
on Windows, `/usr/share/dotnet` or `/etc/dotnet/install_location` on Linux, `/usr/local/share/dotnet` on macOS).
CI runners have one. There the framework-dependent check accepts a start only if the trace shows `hostfxr` came from
that location, and the bundled offerings still must not load anything from it. On a machine without such a
location, the framework-dependent failure is shown directly.

### Single-file claims

No offering claims a .NET single-file bundle, so there is no bundle extraction arrangement to qualify. native-aot
is one native executable that needs no extraction; the layout scenario checks that it holds no managed runtime or
assembly. The managed offerings are folders, because the in-process F# frontend reads compiler references from
assembly locations, which a single-file bundle does not have. They stay untrimmed. The legacy release asset
(`LegacyAsset.TrimmedSingleFile`) is a managed trimmed single-file build; this qualification does not cover it.

## Shipped sizes

`PackRelease` writes `<artifact>.build-contents.json` (every shipped file with its size, read from the artifact) and
regenerates `artifacts/release/size-report.md` over every artifact packed there. The report also counts each artifact's
satellite resource folders: the managed offerings ship 13 (cs, de, es, fr, it, ja, ko, pl, pt-BR, ru, tr, zh-Hans and
zh-Hant), which hold the translated messages of the F# compiler and FSharp.Core. These are build outputs, not public
contracts, and there is no size target.

## Platform status

| Platform | native-aot | self-contained | framework-dependent | dotnet-tool |
|---|---|---|---|---|
| win-arm64 | Qualified on a developer machine | Qualified on a developer machine | Qualified on a developer machine | Qualified on a developer machine |
| win-x64 | Not yet qualified | Not yet qualified | Not yet qualified | Not yet qualified |
| osx-arm64 | Not yet qualified | Not yet qualified | Not yet qualified | Not yet qualified |
| osx-x64 | Not yet qualified | Not yet qualified | Not yet qualified | Not yet qualified |
| linux-x64 | Not yet qualified | Not yet qualified | Not yet qualified | Not yet qualified |
| linux-arm64 | Not yet qualified | Not yet qualified | Not yet qualified | Not yet qualified |

win-arm64 means Windows 11 Home 10.0.26300 on ARM64, release `0.4.0-alpha.3`, with no .NET at the default or
registered location (the machine's .NET 10.0.13 and 10.0.14 runtimes are in a folder reached only through
`DOTNET_ROOT`). For each offering, all 8 packaged scenarios pass, and 37 of the 38 other non-manual E2E scenarios
pass; the 38th, "Verify with quiet mode", is tagged `@Ignore` in the existing suite. This is task 2.4 evidence, not release qualification: the release lanes of task
2.5 and the acquired-artifact checks of group 6 still apply. The `dotnet-tool` package is portable; only its win-arm64
installation has been run.

Unsupported, and refused by `PackRelease`:

- Any platform outside the six above, for example `linux-musl-x64`, `win-x86` or `linux-arm`.
- `native-aot` built on a machine of another operating system than the RID: Native AOT does not cross-compile
  between operating systems.

Issue #264 says Native AOT is not supported on Linux ARM64 and Windows ARM64. The win-arm64 Native AOT publish and the
scenarios above contradict that for Windows ARM64. For Linux ARM64 it is neither confirmed nor refuted until a real
run on a Linux ARM64 runner.

## Operating-system prerequisites

Established on win-arm64:

- `native-aot` and `self-contained` start and work with no .NET runtime, SDK or `DOTNET_*` variable reachable.
- `framework-dependent` needs `Microsoft.NETCore.App` 10.0 for the machine's architecture. Its runtimeconfig asks for
  `10.0.0` with the default roll-forward, so any 10.0.x patch serves; the host chose 10.0.14 here.
- `dotnet-tool` needs the .NET SDK 10.0 to install and the .NET 10 runtime to run.

Not established yet, to be recorded from runner evidence:

- Linux: the minimum glibc version, and whether ICU and OpenSSL libraries are needed (the projects do not set
  `InvariantGlobalization`).
- macOS: the minimum macOS version, and what Gatekeeper does with a downloaded, unsigned executable.
- Windows x64: anything beyond the operating system itself.
