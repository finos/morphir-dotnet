# Checking the release profiles from packaged artifacts

Task 2.4 of `add-deployment-and-acquisition` checks the four .NET offerings from packaged artifacts: the archives
that `PackRelease` writes, installed the way a user would install them, not build output folders. This page says how
to run the checks, what they check, what each platform's status is, and what is not established yet.

Nothing on this page qualifies a release or a platform. Release or platform qualification may be claimed only from
the exact artifacts of a release, acquired the way a user acquires them, never from a local build run on the build
machine. These checks run local builds on the build machine.

## Run them

Pack the profile for this machine's platform, then run the E2E suite with the offering as the executable type:

```bash
./build.sh --target PackRelease --deployment native-aot --rid linux-x64
./build.sh --target TestE2E --executable-type native-aot

# all four offerings
./build.sh --target TestE2E --executable-type offerings
```

On Windows use `build.cmd`. The Native AOT link on Windows needs `vswhere.exe` on `PATH`
(`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`).

`scripts/run-e2e-tests.cs` (which `TestE2E` runs) packs an offering only when the release folder holds no artifact
of it for this platform. The release folder is `artifacts/release`, or the folder that `MORPHIR_RELEASE_DIR` names;
the script packs into the same folder that it reads from, by passing it to `PackRelease` as `--release-dir`. To pack
into another folder by hand, pass `--release-dir <folder>` to `PackRelease` as well. An artifact that is already
there is used as it is, so before each run the script prints
the release, the commit it was packed from, its SHA-256 and its pack time. It refuses an artifact packed from another
commit than `HEAD`, or one whose pack commit is not recorded, unless `MORPHIR_REUSE_PACKAGED_ARTIFACTS=true` says to
test it anyway. It only warns about an artifact packed with uncommitted changes, which it cannot compare.

`MORPHIR_EXECUTABLE_TYPE` selects the offering for a direct run of the test assembly, and `MORPHIR_RELEASE_DIR`
points it at a release folder other than `artifacts/release`; a direct run neither packs nor checks the pack commit.
The run
checks the archive's SHA-256 against its launch metadata, installs it into a new temporary folder whose path contains
a space (`dotnet tool install --tool-path` for `dotnet-tool`, in a sandbox with its own NuGet packages folder), runs
every non-manual scenario against it from a working folder whose path contains a space, and removes the folder
afterwards. A packaged type never runs the `@manual-only` scenarios, even with `INCLUDE_MANUAL_TESTS=true`: they build
their own executables from the source tree.

On demand per platform, without a pull request job:

- **Manual E2E Testing** (`.github/workflows/manual-e2e-test.yml`): choose the platforms and an executable type
  (`offerings` or one offering). It uploads the size report and the launch and contents files. On Linux the
  "No installed .NET runtime" scenario runs in a clean container (see below).
- **Manual AOT Testing** (`.github/workflows/manual-aot-test.yml`): the `packaged` suite packs and checks
  `native-aot` for one platform. Its `trimming` and `aot-compilation` suites exclude `@packaged`.
- **morphir native probe** (`.github/workflows/morphir-native-probe.yml`): a manual six-platform Native AOT
  dependency probe, separate from PR CI. Run it from the source ref being examined. It publishes using the release
  profile, on Linux inside the pinned Ubuntu 22.04 image, and uploads `probe.md`, the publish log and command output.
  A failed build or infrastructure step fails the job. Command outcomes remain report-only, so a green job does not
  mean every command passed. It publishes no release and does not replace acquired-artifact qualification.

The two manual AOT suites ran no test before this change: their filters `*/AOT*` and `*/Trimming*` were not full
TUnit tree paths, so they selected nothing, and the run ended with "zero tests ran" (exit code 8). They now name
the feature classes (`/*/*/NativeAOTCompilationFeature/*`, `/*/*/AssemblyTrimmingFeature/*`), 10 scenarios each,
which have not been run in that lane yet.

## What the scenarios check

`tests/Morphir.E2E.Tests/Features/Packaged/PackagedArtifacts.feature` takes each expectation from the artifact's own
launch metadata (`<artifact>.build-launch.json`), so it also checks that the metadata is true. The other E2E features
(basic commands, error handling, extension list, IR verification, validate, management) run against the installed
artifact too, with one exception: the extension list scenario "Both hosts list the same shared built-ins" runs the
other host from a build folder (`src/Morphir/bin` or `src/Morphir.Tool/bin`), so that build must be of the same
commit as the artifact.

| Scenario | Checks |
|---|---|
| The installed artifact is the one its launch metadata names | Archive SHA-256 and launch path. For `dotnet-tool`, the package that the tool store keeps is byte for byte the packed file |
| The installed layout is what the offering claims | native-aot: exactly one file, no .NET runtime or assembly, and a Native AOT executable, not a single-file bundle. self-contained: a folder with `hostfxr`, `coreclr`, `System.Private.CoreLib.dll` and FCS. framework-dependent: a folder with FCS and a runtimeconfig that names `Microsoft.NETCore.App`, no runtime. dotnet-tool: the tool installation with the managed composition and FCS, no `morphir.dll` |
| Startup from a clean directory | `--version` from a new empty folder prints the artifact's release |
| Core commands | `extension list --json` names the host and the shared built-ins; `--help` |
| An F# compile follows the artifact's frontend | Managed offerings compile in process (`morphir-dotnet.fsharp (in-process)`), from a source path with a space; native-aot refuses with `compile.frontend_unavailable`, and the message names `dotnet-morphir` and `--extension` |
| Reading an input file | `validate --input` with an absolute path that contains spaces. On Linux and macOS this reads the file kind through `libSystem.Native`, the fix in #556. On Windows the code path returns before that call, so a Windows run does not exercise it |
| No installed .NET runtime | See below. Skipped, with the reason, where the runtime cannot be hidden |
| The declared runtime | framework-dependent and dotnet-tool run on the machine's runtime: the host loads `hostfxr` from a machine runtime root and chooses a `Microsoft.NETCore.App` 10.x, read from the first `Chose FX version` line of the host trace. native-aot and self-contained never load the machine's runtime, even when it is reachable |

The harness resolves its working directory to a physical path before preparing input files. On macOS, `/var` is an
alias for `/private/var`. Passing that alias in an absolute input path previously failed the input scenario because
bounded capture rejects linked ancestors. The product's link refusal remains in force. Regression tests cover a
linked temporary root and verify that the prepared working directory has no linked ancestor. A fresh macOS packaged
run is still needed to establish the scenario's result for a candidate artifact.

### How "no installed .NET runtime" is shown

The run removes every `DOTNET_*` variable, sets `DOTNET_MULTILEVEL_LOOKUP=0`, drops every `PATH` entry that holds a
`dotnet` executable, and records a .NET host trace (`COREHOST_TRACE=1`, `COREHOST_TRACEFILE`). The assertion reads
what the process loaded, not only whether it started:

- **native-aot**: no host trace file exists, standard error holds no host tracing (the host writes there when it
  cannot open the trace file), and the executable is a Native AOT executable: it has no .NET bundle signature, which
  every apphost and single-file bundle carries, and it exports `DotNetRuntimeDebugHeader`. In the same scenario the
  test host's own apphost runs through the same sandbox as a control and must write a trace, so the trace variables
  do work there. The structural rule was verified on win-arm64 PE files (a Native AOT `morphir.exe`, a single-file
  bundle and two apphosts); it is expected to hold for ELF and Mach-O files and is not verified there.
- **self-contained**: the trace says "Using app-local location", and every `hostfxr`, CoreCLR and loaded-library path
  in it is inside the installation. None is under a machine runtime root: `DOTNET_ROOT*`, the host that runs the
  tests, any `dotnet` on `PATH`, and the registered and default locations. Paths are compared in canonical form,
  with links and Windows short names resolved.
- **framework-dependent** and **dotnet-tool**: the apphost exits with `0x80008083` on Windows (131 is expected in a
  Linux or macOS shell) and prints "You must install .NET to run this application" with a download link. On Windows
  the trace must also show that the default location the host searched was the empty folder of the run. The
  installation guide repeats what to do ([installation](../content/docs/getting-started/installation.md)).

The apphost searches the `DOTNET_ROOT*` variables, then the registered location, then the default location. What a
run can hide:

- **Windows**: the run points `ProgramW6432` at an empty folder, which moves the default location. That the .NET 10
  apphost takes its default location from `ProgramW6432`, and not from `ProgramFiles`, was established on win-arm64:
  with `ProgramFiles` changed the host still searched `C:\Program Files\dotnet`; with `ProgramW6432` changed it
  searched the new folder and started from a runtime placed there. It is not verified on win-x64. This is a test
  technique, not product guidance. A registered location (`HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>`)
  cannot be hidden from the environment, so on a machine that has one the scenario is skipped with the reason.
  Hosted Windows runners may have one; that is not checked here.
- **Linux**: an environment cannot move the registered or default location (`/etc/dotnet/install_location`,
  `/usr/share/dotnet`, `/usr/lib/dotnet`), which `actions/setup-dotnet` installs into. When
  `MORPHIR_E2E_RUNTIME_CONTAINER` names an image, the hidden-runtime run executes in that container instead: no
  network, the installation mounted read-only and the sandbox writable at their own paths, and only the sandbox,
  Morphir and host-trace variables passed in. The manual workflows set it to `mcr.microsoft.com/dotnet/runtime-deps:10.0`,
  which has the native libraries .NET needs and no .NET. Without the variable, the scenario is skipped with the
  reason. The container path has not been run yet: the machine where this was written is Windows.
- **macOS**: nothing can hide a runtime at `/usr/local/share/dotnet`; there the scenario is skipped with the reason.

A skipped scenario is reported as skipped, never as passed. If the apphost still starts from a registered or
default location after the run hid everything it could, the scenario is skipped too; a start from anywhere else
fails.

### Single-file claims

No offering claims a .NET single-file bundle, so there is no bundle extraction arrangement to check. native-aot is
one native executable that needs no extraction; the layout scenario checks that it holds no managed runtime or
assembly and that it is not a bundle. The managed offerings are folders, because the in-process F# frontend reads
compiler references from assembly locations, which a single-file bundle does not have. They stay untrimmed. The
legacy release asset in this repository's rollback publication path (`LegacyAsset.TrimmedSingleFile`) is a managed
trimmed single-file build; these checks do not cover it. Official delivery in finos/morphir instead aliases the
Native AOT executable's exact bytes under the legacy `morphir-<rid>` names. That alias is not another offering.

## Packaged sizes

`PackRelease` writes `<artifact>.build-contents.json`, which lists every file this artifact contains with its size,
read from the artifact, together with the release, the artifact's SHA-256 and where it was packed (commit, whether
the working tree was clean, pack time). It regenerates `artifacts/release/size-report.md` over every artifact packed
there, with release and SHA-256 columns; contents files of another release are listed apart, not mixed into the
totals. A tar link entry fails the report instead of being dropped. The report also counts each artifact's
satellite resource folders: the managed offerings have 13 (cs, de, es, fr, it, ja, ko, pl, pt-BR, ru, tr, zh-Hans
and zh-Hant), which hold the translated messages of the F# compiler and FSharp.Core. For the `dotnet-tool` row the
files are package entries, including the NuGet package metadata, and the extracted size is not the installed size.
These are build outputs, not public contracts, and there is no size target.

Two packs of the tool from the same commit are byte for byte the same when the build folders are clean. They differ
when an earlier build of the same projects ran with other properties: `PackRelease` passes `DebugType=none`, which does
not make the compiler run again, so the pack takes the assemblies of the earlier build, whose debug directory holds
the absolute path of a `.pdb` under `obj/`. This explains the two 2.4 tool packs (21,145,493 and 21,170,651 bytes):
between them the E2E script had built `src/Morphir.Tool` in Release, and repeating that sequence (a plain
`dotnet build src/Morphir.Tool -c Release`, then the pack) gives a difference of the same size, 25,111 bytes here
against 25,158 then, in 20 assemblies and no other entry. The fix
(packing from clean intermediate folders, `ContinuousIntegrationBuild` and path mapping) is for task 2.5.

## Platform status

| Platform | native-aot | self-contained | framework-dependent | dotnet-tool |
|---|---|---|---|---|
| win-arm64 | Packaged checks pass: local build, run on the build machine. Not qualified | Packaged checks pass: local build, run on the build machine. Not qualified | Packaged checks pass: local build, run on the build machine. Not qualified | Packaged checks pass: local build, run on the build machine. Not qualified |
| win-x64 | Not run | Not run | Not run | Not run |
| osx-arm64 | Not run | Not run | Not run | Not run |
| osx-x64 | Not run | Not run | Not run | Not run |
| linux-x64 | Not run | Not run | Not run | Not run |
| linux-arm64 | Not run | Not run | Not run | Not run |

win-arm64 means one Windows 11 Home 10.0.26300 machine on ARM64, release `0.4.0-alpha.3`, with no .NET at the default
or registered location (the machine's .NET 10.0.13 and 10.0.14 runtimes are in a folder reached only through
`DOTNET_ROOT`). For each offering, all 8 packaged scenarios pass, including "No installed .NET runtime", and 37 of the
38 other non-manual E2E scenarios pass; the 38th, "Verify with quiet mode", is tagged `@Ignore` in the existing suite.
The release lanes of task 2.5 and the acquired-artifact checks of group 6 still apply before anything is qualified.
The `dotnet-tool` package is portable; only its win-arm64 installation has been run.

Unsupported, and refused by `PackRelease`:

- Any platform outside the six above, for example `linux-musl-x64`, `win-x86` or `linux-arm`.
- `native-aot` built on a machine of another operating system than the RID: Native AOT does not cross-compile
  between operating systems.

Issue #264 says Native AOT is not supported on Linux ARM64 and Windows ARM64. On one Windows 11 ARM64 machine the
`native-aot` profile published and its archive passed the packaged checks, so the failure #264 describes did not
occur there. That does not settle #264 for Windows ARM64 without a hosted-runner run, and it says nothing about Linux
ARM64.

## Operating-system prerequisites

Observed on one win-arm64 build machine:

- `native-aot` and `self-contained` start and work with no .NET runtime, SDK or `DOTNET_*` variable reachable.
- `framework-dependent` needs `Microsoft.NETCore.App` for the machine's architecture. Its runtimeconfig asks for
  version `10.0.0` with the default roll-forward (Minor): the newest installed 10.0 patch, or a later 10.x minor when no
  10.0 patch is installed; .NET 9 or 11 alone does not serve. Only 10.0.14 was observed: the host chose it over 10.0.13.
- `dotnet-tool` needs the .NET SDK 10.0 to install and the .NET 10 runtime to run.

Not established, to be recorded from runner evidence with acquired artifacts:

- Linux: the minimum glibc version, and whether ICU and OpenSSL libraries are needed (the projects do not set
  `InvariantGlobalization`).
- macOS: the minimum macOS version, and what Gatekeeper does with a downloaded, unsigned executable.
- Windows x64: anything beyond the operating system itself.

### Six-platform source-build observations

[Run 43010468595](https://github.com/finos/morphir-dotnet/actions/runs/43010468595) ran the manual native probe on
`f4fc12a2a80072b5739cf1922911d8f72ac3d21a`, after #578 and #584 merged. All six jobs produced reports. The
[evidence record](evidence/native-probe-f4fc12a2.json) retains the 248 command outcomes, generated-file comparisons,
and SHA-256 of each original report and publish log. Executable sizes below exclude archives and notices.

| Platform | Executable bytes | Measured requirement |
|---|---:|---|
| linux-x64 | 23,706,800 | Highest required glibc symbol 2.34; linked inside pinned Ubuntu 22.04, tested at its glibc 2.35 |
| linux-arm64 | 24,394,912 | Highest required glibc symbol 2.34; linked inside pinned Ubuntu 22.04, tested at its glibc 2.35 |
| osx-arm64 | 23,216,856 | `LC_BUILD_VERSION` declares macOS 12.0; tested on the hosted runner, not macOS 12.0 |
| osx-x64 | 23,756,696 | `LC_BUILD_VERSION` declares macOS 12.0; tested on the hosted runner, not macOS 12.0 |
| win-arm64 | 28,806,656 | Command execution on the hosted runner; no Windows minimum established |
| win-x64 | 28,118,016 | Command execution on the hosted runner; no Windows minimum established |

Both Linux reports distinguish the native libraries a command reaches:

- With neither ICU nor OpenSSL, every command stops at startup for missing ICU.
- In invariant mode, or with ICU installed and OpenSSL removed, `--version`, extension listing and validation run.
  C# generation, F# generation, configuration hashing and Ion generation stop for missing libssl.
- The generated files match the runner's outputs in invariant mode and under `tr-TR` where those runs succeed.
  This compares the probe's own inputs and outputs, not the frozen F# acquisition journey.

The deliberate MSBuild overflow case exits 2 and reaches its child-stop marker. `dist install` still reports
unimplemented, so it provides no HTTPS evidence. A successful probe job does not require all its commands to pass.
The reports describe Native AOT builds without the separate WASM engine. They do not establish a minimum OS by
running on it, exercise acquired release archives, or qualify the full installer/offline workflow. No new version,
tag or release was created. In particular, the build's existing `0.4.0-alpha.4` version is not a new release identity.

### Historical native probe evidence

[Run 42878326618](https://github.com/finos/morphir-dotnet/actions/runs/42878326618), on PR #584 head `e8f4a00b`,
produced reports whose checkout is recorded as `d2d4245`. The four reports are retained as workflow artifacts.
These measurements describe those builds, without the separate WASM engine, rather than current release candidates:

| Build | Recorded dependency observation |
|---|---|
| linux-x64 and linux-arm64 | Highest linked glibc symbol version 2.34, from a publish inside pinned Ubuntu 22.04. ICU is required at startup unless invariant globalization is selected. With ICU present but OpenSSL removed, generation and hashing commands terminate because libssl is unavailable. |
| osx-arm64 | The executable's `LC_BUILD_VERSION` declares macOS 12.0. Execution on the runner does not prove execution on that minimum OS. |
| win-x64 | The report contains runner command results. It does not establish a Windows minimum or test a clean consumer machine. |

`dist install` reported that it was not implemented, so that command provided no HTTPS-path evidence. The probe's
success status did not require successful command outcomes. No win-arm64 or osx-x64 report exists in this run, and
none of these reports qualifies an acquired release or establishes a glibc floor for another build.

Until an explicitly tested deployment changes these requirements, Linux installation guidance must include compatible
ICU and OpenSSL libraries and CA certificates for HTTPS, in addition to the offering's .NET runtime prerequisite.
Do not describe a runtime-bundled offering as needing no operating-system libraries.
