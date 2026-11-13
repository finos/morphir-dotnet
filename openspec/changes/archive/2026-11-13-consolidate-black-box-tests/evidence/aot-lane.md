# The Native AOT lane after the removal of the old probe targets (task 10.1)

Date: 2026-11-12.

## The run that permits the removal

The workflow `Development` runs only for a pull request, a manual dispatch and a workflow call. A push to `main`
does not start it. So the evidence is the last pull request run of pull request 5 (#622), on the head
`38978b6b`, which had `origin/main` merged. That head merged to `main` as `8d752e7d`.

Run: https://github.com/finos/morphir-dotnet/actions/runs/43299285516, job `test (ubuntu-latest)`.

The Linux step ran the old targets and then `TestAot`, in this order:

| Target | Result | Time |
| --- | --- | ---: |
| `TestMorphirupAot` | 162 lines equal under JIT and Native AOT (linux-x64); binary 13,598,992 bytes | |
| `TestMepAot` | 44 lines equal under JIT and Native AOT (linux-x64); binary 6,157,936 bytes | |
| `TestMyriadJsonAot` | 37 lines equal under JIT and Native AOT (linux-x64); binary 5,304,896 bytes | |
| The three old targets together | Succeeded | about 2 min 17 s (20:45:34 to 20:47:51) |
| `TestAot` (`@lane @native-aot` of `Morphir.BlackBox.Tests`) | 8 passed, 0 failed, 0 skipped | 4 min 20 s |

`TestAot` publishes each of the 8 built programs with Native AOT and compares its output with the output of the
same program under JIT. Its programs include `morphirup`, `mep-probe` and `myriad-json-probe`, the programs of the
three old targets. So the old targets check nothing that `TestAot` does not check.

## The change

- `build/Build.MyriadJson.cs` no longer has the targets `TestMyriadJsonAot`, `TestMepAot` and `TestMorphirupAot`.
  It keeps `MorphirupProbeDirectory` and `TestExtensionInstallationAot` (the target of the manual extension
  workflow).
- The Linux step of `.github/workflows/development.yml` runs `TestAot` only.
- The comments of the probe programs and `src/Morphir.Mep/README.md` name `TestAot`.

## Verification on pull request 6

The Linux test job of pull request 6 must still publish with Native AOT: its log has the line
`Morphir.BlackBox.Tests: passed` of the step `TestAot`, with 8 tests and no skip. The run is recorded in
`evidence/ci-times.md`.
