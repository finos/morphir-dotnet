# Provenance of the Extism kernel module

`extism-kernel.wasm` is the Extism kernel module of `moonrockz/extism` **0.3.0**, the version that
finos/morphir-moonbit pins in `pkgs/morphir-host/moon.mod` (read at `origin/main` `89f8924`).

| | |
| --- | --- |
| File | `extism-kernel.wasm`, 2,303 bytes |
| SHA-256 | `3d23b39dc4cb508c32d5f8911ceeb625070dbe1335288d871e17feb03a7fba17` |
| Recorded digest | `moonrockz/extism` 0.3.0 records the same SHA-256 in `kernel/kernel_bytes.mbt` (`kernel_sha256`). finos/morphir-moonbit records no digest of its own; it uses the package's. |
| Source | `kernel/` of <https://github.com/moonrockz/extism>, tag `v0.3.0` (commit `151bae26a06bc043f547742002738be5826285ed`), Zig, built for `wasm32-freestanding` |
| Licence | Apache-2.0 (`LICENSE`, copied unchanged from the package). The package has no NOTICE file; `NOTICE` here records the attribution. |

## How the bytes were obtained

The package publishes the kernel as lowercase hex in `kernel/kernel_bytes.mbt`. The bytes here are that
hex, decoded, with nothing else changed.

1. The mooncakes registry index (`https://mooncakes.io/git/index`, file `user/moonrockz/extism.index`)
   lists version 0.3.0 with package checksum
   `b7c6f1bcddce03526fa3930ff203d4da5ed47a865f6ebbc8e87b5e22f73fd59f`.
2. The package was downloaded from `https://download.mooncakes.io/user/moonrockz/extism/0.3.0.zip`. Its
   SHA-256 equals that checksum.
3. The lines that start with `#|` in `kernel/kernel_bytes.mbt` were joined and decoded from hex
   (PowerShell):

   ```powershell
   $hex = (Get-Content kernel\kernel_bytes.mbt | Where-Object { $_ -match '^\s*#\|' } |
       ForEach-Object { ($_ -replace '^\s*#\|', '').Trim() }) -join ''
   [IO.File]::WriteAllBytes('extism-kernel.wasm', [Convert]::FromHexString($hex))
   ```

4. The SHA-256 of the result equals the `kernel_sha256` the package records. `ExtismKernel.Bytes` checks
   the digest again each time the library loads the embedded module.

`LICENSE` has SHA-256 `c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4`.

## What the module is

The package's own tests (`kernel/kernel_test.mbt`) state, and the kernel proof (task 2.2 of the
OpenSpec change `add-wasm-extension-host`) observed: the module has no imports, one memory of 16
initial pages that it exports as `memory`, and the 20 functions of the upstream Extism kernel with
their upstream names and signatures. Its source says it keeps the memory layout and results of the
upstream Rust kernel (`extism/extism`, `kernel/src/lib.rs`), and returns 0 where upstream would trap.

To move to another version, repeat the steps above for the version finos/morphir-moonbit pins, and
update `ExtismKernel.PackageVersion`, `ExtismKernel.Sha256` and this file in the same change.
