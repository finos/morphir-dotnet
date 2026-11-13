# morphirup local-developer journey on macOS ARM64

On 2026-11-12, on a MacBook Pro with Apple silicon (macOS 26.6.2, build 25G83), the published
`morphirup-osx-arm64` set up, selected and ran a local-developer toolchain from inputs that
`PackWindowsToolchain --rid osx-arm64 --deployment self-contained --toolchain-local-developer` made.
The inputs were built from the working tree of branch `feat/morphirup-osx-arm64` on base
`9c9695ea9ddcdec725a1fec5118b6fc611b24ac7`, before its first commit, so the candidate records
`working tree clean: False`. This is local evidence, not a release qualification.

The documents of this journey were local-toolchains `0.1.0-preview.3`, as the branch first wrote them. The
family is now `0.1.0-preview.4` ([finos/morphir#1164](https://github.com/finos/morphir/pull/1164)), which adds
`osx-arm64` with `tar-gzip`. The journey was repeated with preview.4 documents; see
[the preview.4 repeat](#preview4-repeat).

## Inputs

| File | SHA-256 |
| --- | --- |
| `morphirup-osx-arm64` | `f3e7d89c3dc5e7a053ded18185f3129340e743c3e572be0e9d60ead0acd35e16` |
| `morphir-host-candidate-osx-arm64.json` | `4250e8c5b6698c738555abf0de5cad43192dba7c4c54156eed081499f2355994` |
| `morphir-dotnet-0.4.0-alpha.5-self-contained-osx-arm64.tar.gz` | `4511fe9003b79779bfa439bc4a63c9cca560ef58d526845de92bba180c09b9bc` |
| `morphir-toolchain-osx-arm64.json` | `809a6c9339dcb460412b7c92090c2c4f041d41800b9ab0773f07173bd8dc961a` |
| `morphir-extensions-osx-arm64.json` | `5e1462bef02a9f225ac1498d5564a2398d4efbf06bad9207c496545fbd297fa2` |
| `morphir-fsharp-frontend-0.4.0-alpha.5-osx-arm64.tar.gz` | `58af967ea8596c7f438474ee764b697b013939b29f33a4aca864e7b84dca25bc` |
| `morphir-fsharp-backend-0.4.0-alpha.5-osx-arm64.tar.gz` | `80fd8250bda2f6d082e9967c5f408e1df31bfc7ac098dd2b4eddf66e08da9382` |

## Commands and results

`A` is the producer output folder. `H` is a new Home under `$TMPDIR` (`/var/folders/.../T/mu.SsuI`).
The project is a copy of `examples/morphirup-fsharp` under `$TMPDIR`.

| Command | Exit | Result |
| --- | --- | --- |
| `$A/morphirup-osx-arm64 setup --candidate ... --local-archive ... --local-bundle ... --local-developer --home $H --dry-run` | 0 | `planned`, candidate `4250e8c5…` |
| The same command with `--yes` | 0 | `installed`, generation `2a787bba073850cfcfa1ff54a4d20ac5a457b0ce9ec2dacca1f60f8dd9be35eb` |
| `select --generation 2a787bba… --home $H` | 0 | `selected` |
| `run --home $H -- compile --output compiled --out-dir out --json` | 0 | `success: true`, module `Pricing` |
| `run --home $H -- generate --target fsharp --input compiled/morphir-ir.json --output generated --option format=false --json` | 0 | `success: true`, backend `org.finos.morphir.fsharp-backend` 0.4.0-alpha.5 over MEP 0.2.0-draft.1 |

`generated/Pricing.fs` is 112 bytes, SHA-256
`64e08313f3e57be2c18995a2e8b275db9d921341958da2c8cd960c601c066de0`: the bytes of the Windows ARM64 journey.

Setup's readiness ran compile and generate through both installed process providers. The `run -- compile`
above reads the language from `morphir.toml` and does not give `--language fsharp`, so the host used its
in-process F# frontend, as on Windows.

The two `run` commands were repeated:

- with .NET hidden: `env -i HOME=$HOME TMPDIR=$TMPDIR PATH=/usr/bin:/bin`. No `dotnet` was on `PATH`
  and `DOTNET_ROOT` was not set. Both exits 0, and `Pricing.fs` had the same 112 bytes and digest.
- with network denied: `sandbox-exec -p '(version 1)(allow default)(deny network*)'`, and the same
  `env -i`. Under that profile, `curl https://github.com` exited 6 (`Could not resolve host`). Both exits
  0, and `Pricing.fs` had the same 112 bytes and digest.

## Installed modes and signatures

| Installed file | Mode | `codesign -v` |
| --- | --- | --- |
| host `morphir` | `-rwxr-xr-x` | valid |
| host `createdump` | `-rwxr-xr-x` | not checked |
| `morphir-fsharp-frontend` | `-r-xr-xr-x` | valid |
| `morphir-fsharp-backend` | `-r-xr-xr-x` | valid |

The published `morphir` apphost is ad-hoc and linker-signed (`flags=0x20002(adhoc,linker-signed)`).

## Failures that this journey found and that the change fixes

1. The extension installer refused the tar.gz provider packages with `package.format_not_implemented`.
2. Setup readiness failed with `sources.unsupported_entry`: the installed host refuses a source below a
   link, and `$TMPDIR` is below the link `/var`. morphirup now uses the physical path of Home off Windows.

## Preview.4 repeat

On 2026-11-12, after the move to local-toolchains `0.1.0-preview.4`, the producer was rebuilt and the journey was
repeated with a new Home under `$TMPDIR`. The candidate, bundle, receipt, selection and generation all name
`https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.4/<file>.schema.json`.

| File | SHA-256 |
| --- | --- |
| `morphirup-osx-arm64` | `2fa8c551f93b97956b9c8ccac1175e364abf58234f415099d551d14cbd7e0dee` |
| `morphir-host-candidate-osx-arm64.json` | `177889aeaf4e7dcd60decd177627cef5301cec959ec411b0e33e260044501984` |
| `morphir-toolchain-osx-arm64.json` | `f37387c16a35f333c1341569bd22beb1c8b5f2a991d265e87272200a4c4289a3` |
| `morphir-dotnet-0.4.0-alpha.5-self-contained-osx-arm64.tar.gz` | `ec737158f3f84c6c4d3bf4d9fd49ed534dab26d9ec628ed866e57a4932670407` |

Setup `--dry-run` (`planned`), setup `--yes` (`installed`, generation
`6d094e9f81e9ecaaf428472e1a9d8b9d9e96271b3f0aa106dfcfb047d7db3189`), select (`selected`), `run -- compile` and
`run -- generate --target fsharp` each exited 0. `generated/Pricing.fs` was 112 bytes with SHA-256
`64e08313f3e57be2c18995a2e8b275db9d921341958da2c8cd960c601c066de0`. The two runs gave the same bytes, exit 0, with
.NET hidden (`env -i HOME=$HOME TMPDIR=$TMPDIR PATH=/usr/bin:/bin`) and under `sandbox-exec` with network denied.

The new manager refused `run` in a Home that the preview.3 build had set up (exit 1): it does not read a preview.3
selection.
