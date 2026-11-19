# Library hosts, probes and the globalization tests (task 9.1)

Date: 2026-11-12. Machine: macOS arm64, .NET SDK 11.0.100, Release.

## Which smoke hosts stay

A host is necessary only when the CLI does not reach the library for the behavior under test (design GRP-2 and the
requirement "A library host is a subject that a driver runs"). The measurement is
`evidence/library-reach.cs`: for each library, the type and member references in the metadata of the assemblies of
the CLI (the build output of `src/Morphir`, less the library itself), against the references of the host. It counts
direct references; a call inside the library is not a reference of the CLI.

```bash
./build.sh --target Compile
dotnet run openspec/changes/consolidate-black-box-tests/evidence/library-reach.cs
```

| Library | In the CLI | Members that the CLI reaches | Members that the host uses | Of those, not reached by the CLI | Host |
| --- | --- | ---: | ---: | ---: | --- |
| `Morphir.Compilation` | yes | 147 | 31 | 9 (`Checked.bind`, `map`, `note`, `tryValue`, `zip`, `Diagnostic.warning`, the members of `IIrWriter`) | `Morphir.Compilation.Smoke` stays |
| `Morphir.Configuration` | yes | 382 | 142 | 1 (`ConfigValue.NewSecretReference`) | `Morphir.Configuration.Inspection.Smoke` stays (see below) |
| `Morphir.Configuration.IO` | yes | 94 | 133 | 47 (the transaction host, `ConfigurationLoader.LoadAndResolve`, `ProjectSystem.Load`, `TomlEditor.Edit`, the in-memory project tree, ...) | `Morphir.Configuration.IO.Smoke` stays |
| `Morphir.Configuration.Microsoft` | no | 0 | 11 | 11 (`AddMorphirConfiguration`, `GetMorphirConfiguration`, the projection) | `Morphir.Configuration.Microsoft.Smoke` stays |
| `Morphir.Conformance` | no | 0 | 29 | 29 (the corpus, the adapter, the report) | `Morphir.Conformance.Smoke` stays |

Each of the five hosts uses members that the CLI does not reach, so each stays as a subject. For
`Morphir.Configuration.Inspection.Smoke` the difference is one member, but the host has a second reason: it compiles
the report sources of the CLI (`ConfigurationReport.cs`, `ConfigurationEditReport.cs`) with their generated
serializer, and checks values (non-finite numbers, deep trees) that a black-box test through the CLI cannot make with
the configuration files that the CLI reads.

## The move table

| Old check | Where it ran | New test | Group, lane | Allowed trim and AOT warnings |
| --- | --- | --- | --- | --- |
| `Morphir.Compilation.Smoke`, run of the built program | Manual (README of the host) | `Smoke/LibraryHosts.feature`: `The host of the compilation library passes each of its checks` | smoke | |
| `Morphir.Configuration.IO.Smoke --fixtures`, run | Manual | `Smoke/LibraryHosts.feature`: `A host that writes one JSON result reports success` (row `configuration-io-smoke`) | smoke | |
| `Morphir.Configuration.Microsoft.Smoke`, run | Manual | The same outline (row `configuration-microsoft-smoke`) | smoke | |
| `Morphir.Configuration.Inspection.Smoke`, run | `Test` target, as the program suite `Morphir.Configuration.Inspection.Smoke` | The same outline (row `configuration-inspection-smoke`). The program suite is removed from `TestPlan.Suites`. | smoke | |
| `Morphir.Conformance.Smoke <conformance>`, run | Manual | `Smoke/LibraryHosts.feature`: `The host of the compatibility kit writes one report of the kit` | smoke | |
| The trimmed and Native AOT publishes of the five hosts | Manual (README of each host) | `Smoke/NativeAot.feature`, one row for each host | smoke, `@lane @native-aot` | The summary warnings IL2104 and IL3053 of `FSharp.Core` |
| `TestMyriadJsonAot` (`tests/Morphir.Myriad.SystemTextJson.AotProbe`) | Linux step of the pull request | `Smoke/NativeAot.feature`, row `myriad-json-probe` | smoke, `@lane @native-aot` | The same |
| `TestMepAot` (`tests/Morphir.Mep.AotProbe`) | Linux step of the pull request | `Smoke/NativeAot.feature`, row `mep-probe` | smoke, `@lane @native-aot` | The same |
| `TestMorphirupAot` (`src/Morphirup`, the plan of `tests/Morphir.Toolchain.Tests/AotProbe`) | Linux step of the pull request | `Smoke/NativeAot.feature`, row `morphirup` | smoke, `@lane @native-aot` | The same |
| `GlobalizationTests`: `morphirup runs in invariant globalization mode` | `Morphir.Toolchain.Tests` | `EndToEnd/MorphirupBuildOutput.feature`: `morphirup runs in invariant globalization mode` | e2e, no lane | |
| `GlobalizationTests`: `neither morphirup nor Morphir.Toolchain calls .NET's Unicode normalization` | `Morphir.Toolchain.Tests` | `An assembly of morphirup calls no Unicode normalization of .NET` (2 rows) | e2e, no lane | |
| `GlobalizationTests`: `... uses the platform's cryptography library` | `Morphir.Toolchain.Tests` | `An assembly of morphirup uses no cryptography library of the platform` (2 rows) | e2e, no lane | |
| `GlobalizationTests`: `... uses regular expressions` | `Morphir.Toolchain.Tests` | `An assembly of morphirup uses no regular expressions` (2 rows) | e2e, no lane | |
| `GlobalizationTests`: `a morphirup that can load no ICU still plans the recorded digest` | `Morphir.Toolchain.Tests` | `Smoke/Morphirup.feature`: `A morphirup that can load no ICU still plans the recorded digest` | smoke, no lane | |

The old probe targets compared the Native AOT output with the output of the built program under the JIT. The new
tests make the same comparison. The old filter allowed each IL warning of a line that named `FSharp.Core` with the
code IL2104 or IL3053; the new check allows those two summary warnings of the assembly `FSharp.Core` only.

The driver gets the `morphirup` executable as the probe did: the solution build builds `src/Morphirup`, the library
host driver runs that built assembly for the managed run, and it publishes `src/Morphirup` with Native AOT into the
workspace of the test (`-p:MorphirProbeAot=true`) for the native run.

## Runs

| Command | Result |
| --- | --- |
| The smoke and end-to-end groups (`--treenode-filter "/*/*/*/*[(Category=<group>)&(Category!=lane)]"`) | smoke 10 tests, e2e 7 tests, all pass |
| `./build.sh --target TestAot --skip Restore Compile` | 8 tests pass in 41 s; each publish has only the two summary warnings of `FSharp.Core` |
| `./build.sh --target Test --skip Restore Compile` | 38 suites pass, with the new `e2e` group |
| `Morphir.Toolchain.Tests` after the move | Passes; the five cases of `GlobalizationTests` are not there |
