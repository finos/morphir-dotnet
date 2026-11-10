# Pinned upstream inputs: the generic guest kit

Repository: https://github.com/finos/morphir

Commit: `8b3c4c35430dfb313cf439e4df1a9c0bb7e1834d`

Branch: `t3/design-file-based-layout`, the branch of the draft pull request
[finos/morphir#1148](https://github.com/finos/morphir/pull/1148). The commit is on
`origin/t3/design-file-based-layout`; it is not yet on `main`.

Pinned: 10 November 2026, for task 3.5 of the OpenSpec change `add-wasm-extension-host`
(design RUN-2): the model tier of the generic guest kit, run in-process.

The upstream files below are copied without edits from that one commit: the generic
JSON Library profile of the MEP guest kit (`guest-generic-json.json`), which holds the
kit's package, its source documents for each language and its expected IR for `3.0.0`
and `4.0.0`; the kit README, which states the plan and the three comparison rules; the
merged requirements of `mep/generic-guest-profile`; and the licence and notice of
finos/morphir. These are reviewed kit inputs. No host, guest or test in this repository
writes them, and no expectation is recorded from a host run.

`guest-generic-json.json` has not changed since `0f41d96ab` (1 November 2026, finos/morphir#1129).
The README says its expected IR was recorded from the published Python `0.4.2` guest, through
the legacy tier on MEP `0.1`.

`Morphir.Conformance.Tests` (`GuestKitProvenanceTests`) recomputes every SHA-256 in the
table, fails on a file in this directory that has no row, and checks that the pinned README
and requirements still state the three comparison rules the runner implements.
`.gitattributes` marks `tests/Fixtures/MepGuestKit/**` as `-text`, so a checkout never
changes the line endings of a pinned file.

## Layout

The kit file and the README move up from `spec/mep/mck/` to this directory. The
requirements keep their capability folder name. The kit file names no other file: its
documents and expected IR are inline. The other files of `spec/mep/mck/` (the MoonBit and
Python kits, the model-transport corpus and its `fixtures/`, the WASM host corpus) are not
part of this profile and are not pinned here. The WASM host corpus is pinned separately,
under `tests/Fixtures/MepWasm/`.

## Reference, not copied

The kit's runner is the Rust `morphir mck mep guest-run`. The .NET model-tier runner in
`src/Morphir.Conformance/GuestKit/` follows its plan, requests and comparisons:

- Path: `crates/morphir-mck/src/mep/generic.rs` at the commit above
- Blob: `3ae13e60a65ca63a8850db0bde858fb3b81509ef`

The SDK fixture guest the README names is a module of finos/morphir-moonbit, not of
finos/morphir. The tests download it at a pinned commit and check its SHA-256; it is not
committed here:

- Repository: https://github.com/finos/morphir-moonbit
- Commit: `89f892468649bc6fef6acc40e5bfb2e9800c16da` (`origin/main` on 9 November 2026)
- Path: `apps/morphir/fixtures/sdk-guest/morphir-sdk-fixture-guest.wasm`
- SHA-256: `f5e1c9cd7a3472b6261c2c3ebfe2c39aeae14da5287dac6cd6ca87ce854901f1`, as its
  `build-receipt.json` records

## Moving the pin

All files move together, to one newer finos/morphir commit, in one change: copy every
file below again from that commit, recompute every SHA-256, and update the commit, branch,
date and this table. Never edit a pinned file, and never take files from two commits.

## Files

| Local file | Upstream path | SHA-256 |
| --- | --- | --- |
| `LICENSE` | `LICENSE` | `b40930bbcf80744c86c46a12bc9da056641d722716c378f5659b9e555ef833e1` |
| `NOTICE` | `NOTICE` | `02a20174dd6ff4d866cad7d04ce8d61503306f3266f3c03261418d5011805889` |
| `README.md` | `spec/mep/mck/README.md` | `7f37b522fbae4e10e914319aac3b0c61ffd23ac59773ae56aea6334ff32a3764` |
| `generic-guest-profile/spec.md` | `openspec/specs/mep/generic-guest-profile/spec.md` | `2b38bbcb827706e1ab4eb68a7f84bbdd984b0934285d30351dbf5224f598f750` |
| `guest-generic-json.json` | `spec/mep/mck/guest-generic-json.json` | `f16f2a4c577b507527a77a19a383c2c0083b4db1db131cd06d170e65a88598ae` |
