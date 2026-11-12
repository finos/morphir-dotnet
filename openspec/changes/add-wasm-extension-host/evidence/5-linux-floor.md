# Tasks 5.2 to 5.4: the engine's Linux floor

Date: 10 November 2026. Branch `ci/wasm-engine-linux-floor`, commits `7f8d455` (run 1) and `4d96c3d` (run 2), on
GitHub-hosted runners `ubuntu-24.04` (image `ubuntu24 20261101.360.1`) and `ubuntu-24.04-arm`
(`ubuntu24-arm64 20261101.172.1`), both glibc 2.39. Wasmtime C API and package 50.0.1.

## The question

What a Native AOT executable that carries the WASM engine needs from a Linux machine, and whether it starts on a clean
one. The repository's floor for a released Linux executable is glibc 2.35 (`MorphirupRelease.GlibcFloor` on `main`).
The subject is `tests/Morphir.Extensions.Wasm.AotProbe`, published with the engine linked in
(`-p:MorphirWasmtimeStaticLink=true`) and, for comparison, with `libwasmtime.so` beside it. It was linked in two places:
on the runner, as task 5.1 did, and inside `ubuntu:22.04@sha256:076e7bf8…5e11` (glibc 2.35), the image and digest
`morphirup-build.yml` uses, with the runner's .NET SDK mounted.

## Short answer

- **Link place sets the floor, and the engine does not.** Linked on the runner, the executable needs `GLIBC_2.38`, so it
  does not start on Debian 12 or Ubuntu 22.04. The .NET runtime sets that version, not Wasmtime: `fmod` and `fmodf` from
  `libm`, and the dynamic build, which has no engine inside it, needs them too. Linked inside `ubuntu:22.04`, both
  builds need at most `GLIBC_2.34` on both architectures.
- **The Wasmtime static library fits under 2.35.** It links in `ubuntu:22.04` with no error on both architectures. Of
  the glibc symbols the executable imports, the highest that `libwasmtime.a` references is `GLIBC_2.34`, wherever it is
  linked. The package's `libwasmtime.so` needs `GLIBC_2.28` (x64) and `GLIBC_2.18` (arm64).
- **No build starts on a clean machine: it needs OpenSSL.** Each build linked in `ubuntu:22.04` prints
  `No usable version of libssl was found` and dies, static and dynamic alike. Two calls in `Morphir.Extensions.Wasm`
  need OpenSSL: `SHA256.HashData` and `RandomNumberGenerator`. With both replaced (an experiment, not a product
  change), every check of the probe holds in both clean images on both architectures.
- **ICU is not needed while invariant globalization is on.** The probe sets `InvariantGlobalization`. Without it, the
  same build stops at the first culture lookup with "Couldn't find a valid ICU package". `src/Morphir` does not set it
  on this branch.

## What each executable needs (run 2; run 1 gave the same for the first four rows)

"From libwasmtime.a" is the highest version among the glibc imports that the archive's objects reference.

| Platform | Build | Linked in | Publish | Executable bytes | Highest GLIBC | Symbols that set it | From libwasmtime.a | Shared libraries needed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `linux-x64` | static | runner, glibc 2.39 | ok | 21,511,576 | **2.38** | `fmod`, `fmodf` (libm) | 2.34 | `libm.so.6`, `libgcc_s.so.1`, `libc.so.6`, `ld-linux-x86-64.so.2` |
| `linux-x64` | dynamic | runner, glibc 2.39 | ok | 6,405,408 + `libwasmtime.so` | **2.38** | `fmod`, `fmodf` (libm) | n/a | `libm.so.6`, `libc.so.6`, `ld-linux-x86-64.so.2` |
| `linux-x64` | static | `ubuntu:22.04`, glibc 2.35 | ok | 21,511,576 | 2.34 | `__libc_start_main`, `dlopen`, `dlsym`, `pthread_create` and 19 more `pthread_*`/`dl*` | 2.34 | as the runner's static build |
| `linux-x64` | dynamic | `ubuntu:22.04`, glibc 2.35 | ok | 6,405,408 + `libwasmtime.so` | 2.34 | the same family | n/a | as the runner's dynamic build |
| `linux-arm64` | static | runner, glibc 2.39 | ok | 18,222,520 | **2.38** | `fmod`, `fmodf` (libm) | 2.34 | `libm.so.6`, `libgcc_s.so.1`, `libc.so.6`, `ld-linux-aarch64.so.1` |
| `linux-arm64` | dynamic | runner, glibc 2.39 | ok | 6,622,376 + `libwasmtime.so` | **2.38** | `fmod`, `fmodf` (libm) | n/a | `libm.so.6`, `libc.so.6`, `ld-linux-aarch64.so.1` |
| `linux-arm64` | static | `ubuntu:22.04`, glibc 2.35 | ok | 18,161,080 | 2.34 | `__libc_start_main`, `dlopen`, `dlsym`, `pthread_create` and 19 more | 2.34 | as the runner's static build |
| `linux-arm64` | dynamic | `ubuntu:22.04`, glibc 2.35 | ok | 6,597,800 + `libwasmtime.so` | 2.34 | the same family | n/a | as the runner's dynamic build |

- `GLIBC_2.34` comes from the merge of libpthread and libdl into libc in glibc 2.34. Any link against 2.34 or later
  binds those symbols at 2.34, and the .NET runtime needs the same version (`__libc_start_main`, `dladdr`,
  `pthread_kill`). This is below the floor.
- What the engine adds over the dynamic executable: the unwinder in `libgcc_s.so.1` (`_Unwind_*` at `GCC_3.0` to
  `GCC_4.2.0`, and `__register_frame`), `statx@GLIBC_2.28`, `getrandom@GLIBC_2.25`, `gettid@GLIBC_2.30`,
  `stat64`/`fstat64@GLIBC_2.33`, `epoll_*`, `eventfd` and `pthread_join`/`pthread_detach`/`pthread_key_delete@GLIBC_2.34`.
  `libgcc_s.so.1` is in both clean images.
- `libwasmtime.a` is 68,762,084 bytes on x64 and 60,076,044 on arm64. It leaves 144 and 135 symbols for the system
  libraries. Some are weak (`getrandom`, `gettid`, `statx`, `__cxa_thread_atexit_impl`, and on x64 `dlopen`, `dlsym`
  and the `pthread_mutex*` family). An archive's objects carry no symbol versions, so the versions above are the ones a
  link against that glibc assigns.

| Platform | `libwasmtime.so` (package `runtimes/<rid>/native`) | Bytes | Highest GLIBC | Symbol that sets it | Needs |
| --- | --- | --- | --- | --- | --- |
| `linux-x64` | same file as the C API archive's `lib/` | 29,667,272 | 2.28 | `statx` | `libgcc_s.so.1`, `libpthread.so.0`, `libm.so.6`, `libdl.so.2`, `libc.so.6`, `ld-linux-x86-64.so.2` |
| `linux-arm64` | same | 25,962,952 | 2.18 | `__cxa_thread_atexit_impl` | `libgcc_s.so.1`, `libpthread.so.0`, `libm.so.6`, `libdl.so.2`, `libc.so.6`, `ld-linux-aarch64.so.1` |

## The clean images

Each image was checked as shipped, then OpenSSL was removed in a derived image (`dpkg --purge --force-depends`), as the
morphirup check does. The derived image was checked to have no .NET, `libicu*`, `libssl*` or `libcrypto*`. Every run
had `--network none`. The guest was the published Python 0.6.0 module, its SHA-256
`ce0cba07f0395ff8179b42eb021ade05c68197f69b8e9c0c8bc369d2a035727c` checked on the runner and mounted read-only.

| Image | glibc | Ships .NET | Ships ICU | Ships OpenSSL | Removed |
| --- | --- | --- | --- | --- | --- |
| `debian:12-slim` (resolved to `sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc`) | 2.36 | no | no | no | nothing |
| `ubuntu:22.04@sha256:076e7bf82319619f28dd0aa9c02ab34b055ad37100395b4f49a2ffba04fb5e11` | 2.35 | no | no | `libssl3` 3.0.2-0ubuntu1.30 (`libssl.so.3`, `libcrypto.so.3`) | `libssl3` |

The results are the same on both images and in both runs. The exit status is the container's: 128 + signal.

| Build | `linux-x64` | `linux-arm64` | Last line |
| --- | --- | --- | --- |
| static or dynamic, linked on the runner | exit 1 | exit 1 | ``libm.so.6: version `GLIBC_2.38' not found`` (the loader, before `main`) |
| static or dynamic, linked in `ubuntu:22.04` | exit 139, SIGSEGV | exit 133, SIGTRAP | `No usable version of libssl was found` |
| the same in `ubuntu:22.04` as shipped, with `libssl3` (control) | exit 0 | exit 0 | all seven checks hold |
| experiment: managed SHA-256 in the probe only | 139, SIGSEGV | 133, SIGTRAP | `No usable version of libssl was found`, after `guest digest equals the published digest` |
| experiment: managed SHA-256 in the library | 139, SIGSEGV | 133, SIGTRAP | the same, after `extism-clock-random.wasm has the corpus digest` (sandbox check) |
| experiment: managed SHA-256 and `/dev/urandom` in the library | **exit 0** | **exit 0** | all seven checks hold |
| the previous build without invariant globalization | 139, SIGSEGV | 133, SIGTRAP | `Couldn't find a valid ICU package installed on the system` |

All of these builds start and pass on the runner, which has OpenSSL and ICU.

## What needs OpenSSL, and what needs ICU

On Linux, .NET's `System.Security.Cryptography` loads OpenSSL at the first call. When OpenSSL is missing, the process
prints the line above and dies with a signal. No exception is thrown. Bisected with the experiment:

1. **`SHA256.HashData`** in `Morphir.Extensions.Wasm`. The first call is `ExtismKernel.Load`, which checks the embedded
   kernel's digest (`ExtismKernel.cs:45`). `ModuleDigest.Of` (`CompiledModule.cs:20`), which hashes every module the
   engine loads, and `WasmProfileContract` (`WasmProfileContract.cs:285`) make the same call. The library cannot load
   a module without it.
2. **`RandomNumberGenerator`**: `RandomNumberGenerator.Fill` in the WASI `random_get` callback (`WasiSandbox.cs:268`,
   reached by the corpus guest `extism-clock-random`), and `RandomNumberGenerator.GetBytes` in the session workspace's
   name (`SessionWorkspace.cs:61`). Nothing else in the probe's path needs OpenSSL.

ICU: with `InvariantGlobalization` off, the first culture lookup aborts. Here that is `ExtismKernel.Load` →
`GetManifestResourceStream` → `AssemblyName.CultureName` → `CultureInfo`. Invariant globalization costs nothing here:
the build with ICU support is about 200 KB larger (21,701,144 against 21,503,384 bytes on x64).

## The option, and its cost

The experiment (`scripts/wasm-engine-linux-floor/ManagedCrypto.targets`) is passed to one publish as
`CustomAfterMicrosoftCommonTargets`. It compiles a class named `SHA256` and a class named `RandomNumberGenerator` into
the namespace `Morphir.Extensions.Wasm`, so the unqualified calls resolve to them. No tracked project or source file
changes. `SHA256` wraps BouncyCastle's managed `Sha256Digest`, the digest `Morphir.Toolchain` uses; see
`tests/Morphir.Toolchain.Tests/Sha256Tests.fs` on `main`. `RandomNumberGenerator` reads `/dev/urandom`.

- **Dependency direction.** `Morphir.Extensions.Wasm` should not reference `Morphir.Toolchain`, morphirup's contract
  library (F#, with CsToml and Semver). It can reference `BouncyCastle.Cryptography` directly: the package is already
  pinned centrally (2.7.0), and that is what the experiment did. A small internal SHA-256 would also work.
- **AOT.** No new IL line: the publish logs still have only the two FSharp.Core summary lines. The executable is 8,192
  bytes smaller on x64 (21,503,384 against 21,511,576) and 4,096 bytes smaller on arm64.
- **Speed**, on the 4,797,409-byte Python guest, as the median of 21 rounds after warm-up, JIT, on the runners:

  | Platform | .NET `SHA256` (OpenSSL) | BouncyCastle `Sha256Digest` | Ratio |
  | --- | --- | --- | --- |
  | `linux-x64` | 2.2 to 3.0 ms | 15.5 to 28.7 ms (two runs) | 7 to 9.5× |
  | `linux-arm64` | 2.2 ms | 20.7 ms | 9.3× |

  That is about 20 ms per module load. Modules are hashed once per load and cached by digest, and the cost is small
  next to compiling a 5 MB module.
- **The random generator** needs a source other than OpenSSL. `getrandom(2)` (glibc 2.25, which the engine already
  imports) or `/dev/urandom` both work. WASI `random_get` asks for high-quality random data, which both give.
- The probe's own `SHA256` calls (test code) need OpenSSL too. That does not matter for a product.

Nothing on the main line changes here. Whether the library hashes and draws random bytes this way is the maintainer's
decision.

## Workflow runs

`.github/workflows/wasm-engine-linux-floor.yml` with `scripts/wasm-engine-linux-floor.ps1`. It is triggered by
`workflow_dispatch` and by a push to `ci/wasm-engine-linux-floor`, and has `contents: read`, no secrets and two Linux
jobs. Each job uploads `wasm-engine-linux-floor-<rid>` with `result.json`, `result.md`, the publish logs, every probe
output and the archive's undefined symbols.

- Run 1: <https://github.com/finos/morphir-dotnet/actions/runs/42810656607>, commit `7f8d455`: the six builds up to
  the managed SHA-256 experiments.
- Run 2: <https://github.com/finos/morphir-dotnet/actions/runs/42811879197>, commit `4d96c3d`: the same, plus the
  random-generator and ICU variants. Jobs took 5 and 11 minutes.

## What this means for tasks 5.2 to 5.4

- **A single file on Linux at the floor holds for the engine, but not for the library as it is.** Linked inside
  `ubuntu:22.04`, the `native-aot` executable carries Wasmtime and needs only glibc 2.34 (`libc`, `libm`,
  `ld-linux`) and `libgcc_s.so.1`. Both are on any glibc system. It still needs OpenSSL at run time for the module
  digest and the random generator. Until those two calls stop needing it, the offering can only promise "glibc 2.35
  and OpenSSL 3 (or 1.1)", or ship as it is and fail on machines without OpenSSL.
- **A released Linux executable with the engine must be linked in `ubuntu:22.04` pinned by digest, as `morphirup`
  is.** Never link it on the runner. Then check it the way `CheckMorphirup` does: refuse a `GLIBC_` need above 2.35,
  and run it with no network in `debian:12-slim` and in the build image with OpenSSL removed, after confirming those
  images have no .NET, ICU or OpenSSL. The run in the clean image must load a real guest module: `--version` alone
  would pass, because OpenSSL is loaded only at the first digest.
- **`self-contained` and `framework-dependent`**: `libwasmtime.so` fits (2.28 and 2.18). Their executables have the
  same OpenSSL need, which the .NET runtime of a framework-dependent install usually covers. A self-contained one must
  either set the same Linux prerequisites or have the same fix. Their link place matters for the same reason: a
  self-contained apphost and runtime published on `ubuntu-latest` would carry the runner's glibc.
- **5.4 (musl)**: nothing new was measured. Note that the `fmod@GLIBC_2.38` need comes from the .NET runtime and moves
  with the build machine's glibc, whatever the engine.
- `src/Morphir` does not set `InvariantGlobalization`. If it is meant to run without ICU, it needs that setting, or the
  release notes need to list ICU.
