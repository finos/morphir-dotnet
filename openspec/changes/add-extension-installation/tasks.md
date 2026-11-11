# Tasks

This is the implementation breakdown of the .NET extension installer (extension-model PRD step 3). The current implementation stays on one cohesive branch; checkboxes describe reviewable increments, not a requirement to open a scaffold-only pull request. The indented notes name the first failing test, its project and the narrowest command.

Implementation boundary, 11 November 2026: #567, finos/morphir#1147, #1150, #1151 and #1152, and this repository's #590 are merged. Parent #1156 now publishes group 1 at immutable main `0439c4c06d8ca6d4a46e41afb226ba0872f9bff8`, including seven schemas, normative tables, identity/package cases and PKGVER-A. Source authority is published. Root checked all seven production schema URLs for HTTP 200, exact $id and matching text; no production raw-byte hash comparison is claimed. The maintainer approved Q-LEVELS (b), Q-OVERLAY (a) above project pins, Q-CONFIGREF (a), Q-DEVPOLICY (a), Q-PROVENANCE (a), CMD-1/stable diagnostics and user-owned Home with stable writer locks/tamper detection. No arbitrary malicious same-UID isolation is promised. Decision approval does not implement lifecycle tasks. Every mutating command needs `--dry-run`.

GitHub issue: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558) owns the deployment and acquisition effort.
<!-- TRACKING: link the dedicated installer issue here once it exists. -->
No Beads id is recorded; do not invent one.

Dependencies. The design groups its questions in two. One group follows today's reference behavior or an open parent proposal, which stands in for the shared contract until finos/morphir writes it down; those need no maintainer answer to start, and a change in the parent's review changes the named specs first. The other group needs the maintainer. Parent contracts are named where a task waits on them.

- Implemented: 2.2, 2.3 and 2.10. Independent security/interop re-review of exact `b6936ba5` found no concrete remaining finding in the four I/O sources/delta under the approved cooperating Home boundary. Task 3.3 remains reopened pending Unix evidence. Corrections pass 131 executed Windows tests, with three Unix-only interleavings ignored. The six runtime RED cases are recorded. Full 3.4 inventory/rebuild still needs shared pair types. 5.1 remains unchecked for AOT. No partial lifecycle task is completed by a maintainer decision, static review or Windows-only evidence.
- Parent contract adoption:
  - Publication gates are cleared for 2.4 to 2.8, 3.1, 3.2, 3.5, 3.6, 4.1 and 4.9. Implement against #1156 at `0439c4c0`, including schemas, authoritative member tables and cases; do not create a shadow installer/checker or copy reference quirks where the published contract differs.
  - Next tranche starts with exact failing tests for typed readers/comparison, then inventory, state locks/journal/recovery and probes, using the existing installation/MCK projects. Existing tasks remain unchecked until implemented and verified. Keep every current commit; request a separate CPU2 burst.
  - 4.3 also on the claims `$schema` in `describe` answers: the probe refuses an answer without it, so the test guest, and every guest built on `Morphir.Mep`, must answer with it first (provisional on #1150).
  - 8.1 can now consume the published shared id corpus; .NET runner integration is still unimplemented.
  - 5.3 and the frontend for adoption task 3.3 now have published package tables/cases and PKGVER-A. VERIFY-B/LAUNCH-A remain required; no one-executable recommendation is restored. Their reader, install/verify/activation implementations are still needed.
  - 6.1 and 6.4 wait on the parent toolchain selection/overlay contract, its exact published schema, paths, member tables and corpus. Their product decisions are now approved. Root coordinates adoption; do not implement a .NET-only unversioned pin or overlay file. 7.1 uses the manager's plan rules after installer prerequisites.
- Waits on a maintainer answer:
  - 5.2 on the outer release-assets question of Q-ARCHIVE; the one-executable target recommendation is superseded. 5.3/6.6 still have Q-WINLAUNCH's executable-name/path assumptions to resolve with evidence.
  - Q-LEVELS/Q-OVERLAY/Q-CONFIGREF/Q-COMMANDS/Q-DIAG/Q-DEVPOLICY/Q-PROVENANCE and the Unix threat boundary no longer wait on a maintainer answer. Their implementation/contract prerequisites remain.
  - An answer that differs from the recommendation changes the named specs first, through task 1.1.
- Waits on other work:
  - 5.6 on PRD step 2a, a WASM runtime in this host.
  - 7.2 on the adoption change's group 4 and the separately evaluated TUF client.
  - Real extensions to install: until morphir-dotnet's bundle authoring republishes them at the family version (Decision 13), only extensions published at that version, such as the F# guests, can be installed by a current host (an unconfirmed reading recorded in #1146).
- Not a prerequisite for the first .NET delivery: 9.1, the cross-host check with the MoonBit host.
- The adoption change (`openspec/changes/add-deployment-and-acquisition`, on main) consumes this one:
  - its task 3.3 needs 3.5, 5.2, 5.3 and 7.1, and the package contract of #1151;
  - its 3.4 needs 6.5;
  - its 4.4 needs 6.4.

Commands follow the repository's build settings. Build one project with `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false`. Run Expecto assemblies with `dotnet exec <dll> --filter-test-list "<list>"` and TUnit assemblies with `dotnet exec <dll> --treenode-filter "<filter>"`, as `build/Build.Testing.cs` does. In the notes below:

- `<inst>` stands for `tests/Morphir.Extensions.Installation.Tests/bin/Release/net10.0/Morphir.Extensions.Installation.Tests.dll`;
- `<tooling>` stands for `tests/Morphir.Tooling.Tests/bin/Release/net10.0/Morphir.Tooling.Tests.dll`;
- `<e2e>` stands for `tests/Morphir.E2E.Tests/bin/Release/net10.0/Morphir.E2E.Tests.dll`, run after the executables are built as `tests/Morphir.E2E.Tests` documents. Reqnroll names each feature class after its feature title.

## 1. Decisions and upstream inputs, no code

- [ ] 1.1 Record the maintainer's answer to each question in the design's "needs the maintainer" group beside the question, and change the specs and tasks where an answer differs from the recommendation; verify with `mise exec -- openspec validate add-extension-installation --strict` and `mise run openspec:verify`.
  - Recorded the 11 November selection, developer/provenance, commands/diagnostics and user-owned Home decisions. Explicit staged consent supersedes old default-auto consent wording: omitted inherited `auto` authorizes no installation. The existing schema default is unchanged. Outer Q-ARCHIVE and Q-WINLAUNCH remain open, so this whole task stays unchecked.
- [ ] 1.2 Raise the upstream asks of the design's "Raised upstream" list in finos/morphir, and the PRD superset proposal; verify that each question in `design.html` links its issue or proposal.
  - The asks:
    - the maintainer decision MEP-1: MEP `0.1` is not supported, and the supported protocol is the whole 0.2 line (`>=0.2.0-0`, `<0.3.0`, under the npm and Cargo prerelease rule) as an explicit exception to the exact-match rule for prereleases. Say so in `protocol.md` and the extension README; no Rust product host work is required (Decision 16A);
    - the remaining asks listed in the design's "Asks for the shared extension contract" (finos/morphir#1149 task 1.4 and #1150);
    - the overlay, the level pins and the receipt launch path, for the toolchain contract;
    - the rules that only the reference source specifies today and that #1150 does not yet cover, to be written into the contract pages: Q-PLATFORM, Q-REQHOST, Q-PROBELIMITS, Q-GC and Q-FIXTURES (#1150 covers Q-LOCKPRIM, Q-JOURNAL, Q-STRICTLOCK, Q-NUMBERS and the path spelling; Decision 15A retires Q-CATVER);
    - target and package archives (Q-ARCHIVE), as review comments on finos/morphir#1151, and a provenance kind (Q-PROVENANCE);
    - the claim check after `update` (Q-CLAIMCHECK).
  - Do not write issue numbers before they exist.

## 2. Installation library and pure readers

- [ ] 2.1 Create `src/Morphir.Extensions.Installation` (F#, referencing `Morphir.Extensions`, `Morphir.Mep` and existing `Morphir.Toolchain` values, not `Morphir.Tooling`), its Expecto tests and a C# AOT probe. Use existing appropriate checks or the manual `TestExtensionInstallationAot` lane; add no qualification job to PR CI. Native AOT qualification still requires zero IL2026 and IL3050 warnings.
  - Files: `Morphir.slnx`, `build/Build.Testing.cs` (test project lists), the probe target beside `TestMepAot`.
  - Follow the `fsharp-csharp-interop` skill for every public member C# will call.
  - Implemented: library/test/probe scaffolding, solution and test-list registration, manual probe target. Targeted project builds and JIT probe passed. Still unchecked: no full-solution build or Native AOT publish was authorized for the CPU2 burst, so this is not AOT qualification.
- [x] 2.2 Add distribution identifiers: the id rule of finos/morphir#1149 (merged) with its first-segment device-name rule, the reserved `morphir.` prefix, artifact file names, declared relative paths, channels, selections and SHA-256 digests; verify the `Identifiers` tests pass.
  - First failing tests: `con`, `con.tools`, `a-`, `A`, `a..b` and 239 characters are refused with `extension.id_not_portable`; `morphir-elm` and `org.finos.morphir.fsharp-frontend` are accepted; `morphir.example` is refused with `extension.reserved_id`; a declared path of 513 UTF-8 bytes or 241 UTF-16 units is refused. Until 8.1 brings in #1149's shared cases, the cases come from #1149's spec scenarios and the toolchain target path tables.
  - Command: build `tests/Morphir.Extensions.Installation.Tests/Morphir.Extensions.Installation.Tests.fsproj`, then `dotnet exec <inst> --filter-test-list "Identifiers"`.
- [x] 2.3 Add the platform table from the toolchain contracts: RID to descriptor triple to the "Tool release v1 platform" `os` and `arch`, with musl giving no index platform; verify the `Platforms` tests pass.
  - First failing test: each row of the table maps both ways; `linux-musl-x64` has no index platform, and resolution names it in `extension.no_matching_artifact`.
  - Command: `dotnet exec <inst> --filter-test-list "Platforms"` after the build above.
- [ ] 2.4 Add claim sets that keep their JSON beside the typed `Morphir.Mep` view, compare over the typed projection without schema members, check `critical` and evaluate `requires.host`; verify the `Claims` tests pass.
  - In progress: Claims runtime RED recorded 27 executed failures after separate missing-API/setup evidence. Claims GREEN now passes 27/27, with the affected SharedReaders 19/19 also passing; both repeated after pinned Fantomas formatting and no-restore rebuild. Equivalence and host-range controls execute production code. The internal reader extracts embedded parsing from Descriptors rather than retaining two readers, and reuses Toolchain.Comparator.tryParse/allMet for the published single-comparator rule. Complete public boundaries, full corpus conformance and install-time probe integration remain unfinished; the task stays unchecked.
  - First failing tests:
    - a claim set holding `1.0` is refused, because shared documents hold integers only (provisional on #1150);
    - key order is ignored and array order is not;
    - an unknown top-level member is ignored, and an absent `critical` equals `[]`;
    - a standalone claim set without the claims `$schema`, including one with `claimsVersion` and nothing else, is refused with `extension.unsupported_contract_version` and is not converted (DOC-1);
    - an embedded claim set with a `$schema` of its own is refused.
  - Command: `dotnet exec <inst> --filter-test-list "Claims"`.
- [ ] 2.5 Add index records, JSONL histories and pure resolution; verify the `Resolution` tests pass.
  - First failing tests:
    - a history with `1.0.0+a` and `1.0.0+b` is refused naming both;
    - stable skips a prerelease listed on `stable`;
    - a release whose `requires.host` excludes the host is skipped for an older one;
    - only-`0.1` releases give `extension.no_compatible_mep_version`;
    - two artifacts for one platform are refused;
    - a WASM record with `platform` `null` is refused, as the reference index reader refuses it;
    - a history whose lines 1 and 2 have `schemaVersion` `2.0.0-draft.2` and whose line 3 is current is refused whole, naming line 1 (#1150);
    - a history with no record is refused.
  - Command: `dotnet exec <inst> --filter-test-list "Resolution"`.
- [ ] 2.6 Add release descriptor reading at the family's release schema URL (`0.1.0-preview.1`, Decision 15A), with the platform-differences check; verify the `Descriptors` tests pass.
  - In progress in draft #593: internal shared reader, six named parent release/schema cases, two I-JSON cases and named semantic tests. The first runtime RED ran 19 tests: 16 failed and three passed. The I-JSON refusal checks passed only because the declaration seam rejected all inputs; they did not establish implementation. Copied schemas and 137/66 case inventories are hash-checked offline at parent `0439c4c0`; unimplemented operations are not counted as executed. Full recursive metadata, claims/package semantics and typed public boundaries remain unfinished.
  - First increment GREEN: 19/19 SharedReaders tests passed after implementation and again after pinned Fantomas formatting. Both I-JSON refusal tests now require a valid parse control first. No public MEP/Toolchain codec changed. This is not full descriptor-family or corpus conformance; the task remains unchecked.
  - First failing test: two artifacts whose claims differ at `capabilities.backend.targets` with `platformDifferences` `none` are refused naming that member; a descriptor mixing `wasm` and `process` is refused; descriptors with integer `schemaVersion` 1, `"1.0"`, `2.0.0-draft.2` or `2.0.0-draft.3` and no `$schema`, or another family URL, are refused with `extension.unsupported_contract_version` (DOC-1).
  - Command: `dotnet exec <inst> --filter-test-list "Descriptors"`.
- [ ] 2.7 Add catalog and lock codecs that write the members and values the shared contract defines, with the family `$schema` written first; catalogs are read with must-ignore unless `critical`, and locks refuse unknown members. Include `critical` and `requires`, and pin evidence fixtures with provenance. Verify the `StateCodecs` tests pass.
  - Fixtures: `tests/Fixtures/ExtensionAcquisition/` holds:
    - `frontend-catalog-v1.json` and `frontend-lock-v1.json`, copied from `ecosystem/morphir-rust/crates/morphir-distribution/tests/fixtures` at submodule `c9f79da`;
    - a catalog, lock, journal, repositories file, history and descriptor written by the pinned Rust CLI, including an entry with `requires.host` and one installed from a version 1 record.
  - The README names each fixture's source, commit and SHA-256, and the capture commands. These fixtures are evidence of today's reference behavior, not a target to match byte for byte. Replace them with finos/morphir data when the contract pages and Q-FIXTURES land.
  - First failing tests:
    - reading `frontend-lock-v1.json` and writing it back gives the same member set and values, with `"platform": null`;
    - the lock writer never adds a top-level member outside the contract's list;
    - every catalog and lock is written with its family `$schema` and no `schemaVersion`, and the evidence fixtures (which have `schemaVersion`) are read as old state, not converted;
    - an entry for a release with `requires.host` is written with `critical` `["requires.host"]`;
    - a catalog out of order, or naming one id twice, is refused naming the first entry out of place;
    - a document with a repeated member name, a fraction, or `null` where the member does not allow it is refused.
  - Command: `dotnet exec <inst> --filter-test-list "StateCodecs"`.
- [ ] 2.8 Add the journal and repositories-file codecs; verify the `StateCodecs` tests pass.
  - Journal: exactly `$schema`, `nextLock` and `nextCatalog`, each `null` or an array of integers from 0 to 255.
  - Repositories configuration: its family `$schema`, no unknown members, and repository paths written as #1150 spells them.
  - First failing tests:
    - a journal with `nextLock` `null` reads as a deletion;
    - the writer never adds a member;
    - a journal or repositories configuration with `schemaVersion` and no `$schema` is reported as old state;
    - on Windows, a final path reported as `\\?\C:\ext-repo` is written as `C:\ext-repo`, and a current file that names `\\?\C:\ext-repo` is refused.
  - Command: `dotnet exec <inst> --filter-test-list "StateCodecs"`.
- [x] 2.10 Add the supported MEP line and the implemented set: the comparators `>=0.2.0-0` and `<0.3.0` under the npm and Cargo prerelease rule, the implemented versions (today `0.2.0-draft.1`), and the runnable check with its two reasons; verify the `MepLine` tests pass.
  - Reuse centrally pinned `Semver` 3.0.0, already used by `Morphir.Toolchain`. Use `SemVersionRange.ParseNpm` with the default prerelease rule, the existing bounded `SemVer` reader and `Morphir.Mep.Lifecycle.protocolVersions`. No handwritten version fallback or second implementation list. The C# probe exercises it; Native AOT evidence remains outstanding under 2.1.
  - First failing tests:
    - `0.2.0-draft.1`, `0.2.0-rc.1`, `0.2.0` and `0.2.7` are in the line;
    - `0.1`, `0.1.0`, `0.2.1-rc.1`, `0.3.0-draft.1` and `0.3.0` are not;
    - an entry with `["0.2.0-draft.2"]` is unavailable as "not implemented yet", one with `["0.3.0"]` as "not supported", and one with `["0.1", "0.2.0-draft.1"]` is runnable over `0.2.0-draft.1`.
  - Command: `dotnet exec <inst> --filter-test-list "MepLine"`.

## 3. Shared state on disk

- [ ] 3.1 Resolve Morphir Home and implement the state and repositories locks with the contract's primitive (Q-LOCKPRIM, provisional on #1150): blocking `flock` `LOCK_EX` on a file opened `O_RDWR | O_CREAT | O_CLOEXEC` on Unix, and blocking `LockFileEx` exclusive over the whole range on a file opened to share read, write and delete on Windows. Bind both through `LibraryImport`, because `FileStream.Lock` and `FileShare.None` do not take that lock. Add the waiting line, cancellation and the network file system refusal. Verify the `StateLock` tests pass on Windows and on Linux or macOS.
  - Add a small helper process, `tests/Morphir.Extensions.Installation.LockHelper`, that takes the lock with `LockFileEx` on Windows and `flock` elsewhere, standing in for any other host, such as the MoonBit host, that shares the home.
  - First failing tests:
    - while the helper holds the lock, an acquisition prints one waiting line and blocks;
    - while .NET holds it, the helper's non-blocking attempt reports contention, and the helper's open of the file succeeds;
    - the lock file is never written or removed, and the state and repositories locks are never held together;
    - a relative `MORPHIR_HOME` resolves against the current directory.
  - Command: build the helper and the tests, then `dotnet exec <inst> --filter-test-list "StateLock"`.
- [ ] 3.2 Implement atomic file replacement through `.stage-` files, and the transaction journal: one at a time, recovered at every lock acquisition after old-state detection, with the saved documents validated before they are written. Verify the `Transactions` tests pass.
  - First failing tests:
    - with a writer seam that fails after the lock write, the next acquisition applies the journal and the pair agrees;
    - a journal in the format of the 2.7 evidence fixtures is written back by nothing and reported as old state;
    - a current journal whose saved catalog has no `$schema` is not applied;
    - a stale `.stage-` file is ignored;
    - a journal with `nextLock` `null` deletes the lock;
    - a reader during a replacement sees the old or the new file.
  - Command: `dotnet exec <inst> --filter-test-list "Transactions"`.
- [ ] 3.3 Implement the content-addressed store; verify the `Store` tests pass.
  - The store covers: a staged temporary file, hash-while-copy from staging compared with the recorded digest, no-clobber publication, a rehash before reuse, containment inside Morphir Home, the 256 MiB limit and the Unix owner execute bit.
  - First failing tests:
    - a tampered object fails with `extension.store_object_mismatch` and is unchanged;
    - a linked digest directory fails with `extension.path_escape`;
    - a staged file changed after its first hash fails with `extension.digest_mismatch` and nothing is published.
  - Command: `dotnet exec <inst> --filter-test-list "Store"`.
  - Implemented in `SingleFileStore`: existing toolchain streaming hash, same-directory staging, flush, no-clobber publication and rehash on reuse/race. Windows symlink/reparse, tampering, cancellation and publication-race tests pass in the 123-test installer suite. Unix descriptor-relative operations and owner execute-bit code are implemented but not exercised on Windows; Linux/macOS runs remain necessary before platform claims. No corrupt object repair, shared pair or package metadata is added.
  - Review correction: keep the verified descriptor through handle rename/fd link/fd clone; no pathname fallback. Stable private I/O coordination and directory identity checks implement the approved cooperating-writer boundary. Windows regression GREEN is 131 passed/3 Unix-only ignored. Independent security/interop re-review of exact `b6936ba5` found no concrete remaining finding; Unix runtime evidence remains. No arbitrary malicious same-UID isolation is promised. Task stays unchecked until remaining platform evidence.
  - Subsequent hosted macOS runtime RED at `4c908047`: 180 executed, 177 passed, two failed and one errored, including permission/reopen and disposal failures. Darwin ARM64 `open`/`openat` variadic bindings are corrected with private-mode/reopen regressions. Focused Windows Store 15/15 and RunCopies 5/5 passed twice, three Unix tests ignored; new Unix permission assertions were not executed. Exact-head macOS/Linux verification remains pending. No lifecycle checkbox is completed by this correction.
  - Follow-up platform evidence supersedes that pending status: Development `43029092889` at exact `749dd2fee3b6573a2ad874635c832fab1f4e2389`, macOS job `130551180282` and Ubuntu job `130551179939`, each executed/passed 182 installer tests with zero ignored/failed/errored. Raw assembly summaries were read. This closes the ABI platform gap, not Native AOT, acquired-artifact or full-lifecycle qualification; this task is not marked complete by the status note.
  - Next private byte staging slices are source-only TDD drafts: 16 unexecuted tests for Begin/Write/Seal ownership, caller bounds, interrupted-prefix faulting, concurrency and interprocess lease availability while writes pause. No product declaration/implementation or runtime RED yet. Reuse reviewed Digest.Incremental only after coordinated normal dependency integration. Generic stages choose no extension cap/layout; host mapping, same-object tamper/commit controls, actual Darwin verification outcome and observable deferred cleanup remain later work. No shared metadata or parallel store is introduced.
  - Private staging compile boundary: missing-API build records FS0039 plus setup/inference fallout. Declaration-only types/internal writer seam and compile registration were then added; the second build stopped at 18 test-local FS0072 errors. Test annotations are drafted but unverified. Zero of the 16 tests executed; there is no runtime-stub RED or mechanism evidence. The declarations always refuse without I/O and have no final API approval. CPU2 was released at the two-build boundary with empty global .NET/MSBuild/compiler/test inventory; no additional build or implementation is implied.
  - Separately granted one-build followup resolved the stage bindings but stopped at seven remaining FS0072 errors on awaited Stream/ChildProcessResult values. Their annotations are drafted but unverified. Zero runtime tests executed, no stale binary used; CPU2 released immediately at this compile boundary with empty global process inventory. No further build or mechanism implementation is authorized by these setup corrections.
  - Completion burst: explicit Stream/ChildProcessResult annotations compiled on the first fresh Release/no-restore build, four existing transitive TFM warnings and zero errors. The exact PrivateStaging list ran once: 16 executed, zero passed/ignored/failed, 16 errored at intentional Begin refusal. This is runtime-stub missing-behavior evidence only; byte/ownership/prefix-fault/tamper/concurrency mechanisms did not execute. No product mechanism is implemented. CPU2 released immediately with empty global .NET/MSBuild/compiler/test inventory; no further heavy work or implementation follows without authorization.
  - Authorized next source increment: verified reviewed PR #594 remote head `8f28d5ca7881adeafc5443f1e9069ee1710378b0`, normally merged as `bb1aaef1900155f15ef4b397f4535ba8b75eec7a` with empty index and every draft's byte hash preserved. Reuses its existing Digest.Incremental, never a copy. Begin/Write/Seal source now implements typed single-owner phases, bounded async writes outside the mutation lease, interrupted-write faulting, same-object flush/rehash, one-time sealed transfer and awaited/observable cleanup. FileSafety adds opt-in async opening and contention-only lease waiting; existing Store/publication code is unchanged. Eight new source controls bring the planned PrivateStaging list to 24. None of the new implementation or eight additions has been built/run; original 16 stub errors remain missing-behavior evidence only. No heavy command, substantive commit/push or task completion in this source turn. Later sealed publication, actual commit/clone verification, orphan sweep, host mapping, C# probe and platform/AOT evidence remain open.
  - Focused Windows GREEN now runs actual mechanisms: fresh formatted build succeeds, PrivateStaging 24/24, affected Store 15/15 with one Unix-only ignored and RunCopies 5/5 with two Unix-only ignored. Separate build/formatter syntax diagnostics remain in logs, not labeled mechanism RED. Pinned four-file format check passes; 90 lock files and project registrations unchanged. All three build slots consumed, so no extra rebuild/list repeat. CPU2 released with empty global process inventory. New staging Unix/C# consumer/AOT evidence, orphan sweep, publication/host mapping and lifecycle remain open; no task checkbox is completed by this local evidence.
  - Public C# JIT consumer follow-up preserves the existing 15 output cases and adds nine staging cases, 24 deterministic lines at exit 0. Expected CS8602 compiler negatives prove nullable CleanupReport and Failure; two consumer-only explicit-interface disposal mistakes were corrected through IAsyncDisposable casts. The guarded no-restore Release build passes and the fresh assembly runs once. Production API/source, project and 90 locks are unchanged; no AOT/native publication, other suite, commit or push. CPU2 released with empty global process inventory. Unix/new-source review, native/AOT qualification and unfinished lifecycle remain open.
  - Root review replaced recursive task-based lease retry with a single iterative waiter. A new contended-Begin cancellation/retry control passed before this refactor; it is not claimed as a heap-growth RED. Final formatted no-restore builds passed with four existing dependency TFM warnings and no errors; PrivateStaging 25/25, Store 15/15, RunCopies 5/5 and a fresh public C# JIT probe with 24 lines passed. Three Unix-only cases remain ignored on Windows, all 90 lock hashes unchanged, final process inventory empty. Independent security/interop review, new async Unix/AOT evidence and later publication/orphan/lifecycle work remain pending; no checkbox is completed.
  - Actual hosted Unix RED at exact `98df40356676d4facdd358e43effa9fad795af43`: full macOS and Ubuntu Run tests logs each report 207 executed, 182 passed, zero ignored/failed and 25 errored. All 25 staging cases fail at Begin because the FileStream constructor rejects async=true for a raw Unix SafeFileHandle with IsAsync=false. Source-only correction describes the handle truthfully while retaining Windows OVERLAPPED, the original descriptor and awaited async writes/flush/durability. Existing valid chunks now cross the file buffer; same-open ownership checks platform handle metadata. All 25 tests and tamper/lease/disposal/Unix controls remain. No corrected-source build, format or runtime result yet; exact-head Unix verification is required. Store, public API, publication, orphan sweep and host mapping are unchanged; task remains unchecked.
  - Separate bounded Windows verification of that correction: both fresh Release/no-restore builds pass, four existing dependency TFM warnings and zero errors each. Exact lists run before and after pinned Fantomas 5.2.4 formatting of only FileSafety.fs/PrivateStagingTests.fs: each pass executes/passes PrivateStaging 25 with zero ignored, Store 15 with one Unix-only ignored and RunCopies five with two Unix-only ignored, zero failed/errored throughout. Only formatter layout changed between builds; final format check passes. Both implementation closures match their test output, 90 locks/protected sources/raw CI logs unchanged, and final global/owned process inventory is empty. CPU2 released. No probe, other suite, AOT, commit or push. These Windows results do not close the actual hosted Unix RED or complete any lifecycle task.
- [ ] 3.4 Implement the .NET inventory below `data/morphir-dotnet/extensions/installed/`, its rebuild from a shared pair, and the ownership of .NET run copies below `tmp/extensions/`; verify the `Inventory` tests pass.
  - The inventory holds the .NET-only facts that the closed catalog and lock member sets cannot carry: the true source, the archive digest, the level, and every installed version for pins and overlays. It is needed whatever Q-LEVELS decides.
  - First failing tests:
    - a second record for the same id and digest with other `args` replaces the first and warns;
    - a record rebuilt from a pair another host wrote has source kind `shared-catalog`;
    - the copy sweep removes a stale `morphir-dotnet-run-*` directory and leaves `morphir-extension-*` directories in place.
  - Command: `dotnet exec <inst> --filter-test-list "Inventory"`.
  - Partial implementation only: `RunCopies` owns an exclusive empty `.owner` file, skips live owners, refuses reparse points, and sweeps only `morphir-dotnet-run-*`. The `RunCopies` tests pass on Windows, and the C# JIT probe covers live ownership. The guest process-tree integration and installed inventory/rebuild are unfinished; this task remains unchecked.
  - Security correction holds an interprocess creation/sweep lease until ownership exists and checks original home/directory/owner identities on disposal. Separate-process creator/sweeper RED then GREEN ran on Windows. Unix directory/owner replacement tests are authored but ignored on Windows. The probe was not rerun in this correction burst.
- [ ] 3.5 Implement the commit path: every global install writes the catalog entry and lock through the journal, and an install at another level writes a pin; read a catalog that holds entries this host cannot run. Verify the `Commit` tests pass.
  - First failing tests (`Commit`):
    - a global install writes the entry and lock in one journal, and the pair agrees;
    - a second global install of an id the catalog holds fails with `extension.already_installed`;
    - a catalog holding a 0.1-only entry lists that entry as unavailable, saying that MEP `0.1` is not supported, and lists the others as usual.
  - Command: `dotnet exec <inst> --filter-test-list "Commit"`.
- [ ] 3.6 Detect old state under the state lock before any recovery, and implement the one move aside to `backups/extensions-<time>/` with its completion marker (Decisions 10 and 11; layout provisional on #1150); verify the `OldState` tests pass.
  - First failing tests:
    - a home whose catalog has `schemaVersion` `2.0.0-draft.2` is reported as old state, naming the catalog, before any journal is read;
    - a current catalog beside one lock at `"1.0"` makes the whole home old state;
    - the move takes every lock and journal, then the catalog, writes `.complete`, and leaves the store, a current repositories configuration and both lock files in place;
    - an interrupted move is completed in the same backup directory, and no second one is started.
  - Command: `dotnet exec <inst> --filter-test-list "OldState"`.

## 4. Install from a named repository, and the commands

- [ ] 4.1 Read and write the upstream repositories file under `locks/extensions.repositories.lock`, and add `extension repository add|list|remove`; verify the repository command tests pass.
  - First failing test: `RepositoryCommandTests` in `tests/Morphir.Tooling.Tests/Features/Extensions`: `add` records the canonical directory as an enabled `local-directory` endpoint; a directory without `extensions/` is refused and the file is unchanged; while the helper from 3.1 holds the repositories lock, `add` waits.
  - Command: build `tests/Morphir.Tooling.Tests/Morphir.Tooling.Tests.csproj`, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/RepositoryCommandTests/*"`.
- [ ] 4.2 Implement the pipeline without a probe; verify the `Pipeline` tests pass.
  - Steps: resolve, check repository containment, stage and hash, store, commit through 3.5, and report with `authentication` kind `local-developer` and `claimCheck` `unchecked`.
  - First failing test: a repository artifact whose bytes differ from its record fails with `extension.digest_mismatch`, and nothing is written to the store, catalog, locks or inventory. A linked artifact fails with `extension.path_escape`.
  - Tests pass `MORPHIR_HOME` explicitly to a temporary directory; none reads the user's home.
  - Command: `dotnet exec <inst> --filter-test-list "Pipeline"`.
- [ ] 4.3 Add the bounded describe probe, the session fallback on `-32601` and `-32014` only, and the environment filter, and record `claimCheck` from this install only; verify the `Probe` tests pass.
  - Add a test guest, `tests/Morphir.Extensions.TestGuest`, built on the `Morphir.Mep` guest loop. Its modes:
    - agree, lie and hang;
    - write junk on stdout;
    - answer `-32601`, or answer `-32600` with `not initialized`;
    - spawn a child;
    - report its environment;
    - rewrite its staged file.
  - First failing tests:
    - the lying guest fails with `extension.claims_mismatch` naming `claims.capabilities.backend.targets`;
    - the hanging guest and its child are ended after the deadline with `extension.probe_failed`;
    - the `-32600` guest fails with `extension.probe_failed`, with no fallback;
    - the environment guest sees no `GITHUB_TOKEN`;
    - the rewriting guest fails with `extension.digest_mismatch`;
    - an index record that says `probed`, installed with `--no-probe`, is recorded `unchecked`.
  - Command: build the test guest and the tests, then `dotnet exec <inst> --filter-test-list "Probe"`.
- [ ] 4.4 Wire `extension install --repository` and `extension uninstall` with its alias `remove` in both hosts, with JSON reports on stdout, messages on stderr, exit codes 0, 1 and 2, and stable codes; verify the command tests pass.
  - Files: `src/Morphir.Tooling/Scenarios/ManagementScenario.cs`, `src/Morphir.Tooling/Features/Extensions/`.
  - First failing tests, in `ExtensionInstallCommandTests`:
    - a URL argument exits 2;
    - `--platform` exits 2;
    - `--json` stdout parses as one document with the report members;
    - installing twice fails with `extension.already_installed`;
    - a global uninstall removes the entry and lock in one journal and keeps the store object;
    - `morphir.example` is refused with `extension.reserved_id`;
    - in a home with old state, an install without a terminal and without `--yes` fails, changes nothing and names `morphir doctor --fix` and `--yes`; with `--yes` it moves the old state through 3.6 and installs.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionInstallCommandTests/*"`.
- [ ] 4.5 Wire `extension update` and `extension info`, including the platform-differences report; verify the command tests pass.
  - First failing tests, in `ExtensionUpdateCommandTests` and `ExtensionInfoCommandTests`:
    - `update` without a source exits 2;
    - a failed update probe keeps the old record;
    - `info --json` has the members of the requirement;
    - `info` of an id that is not installed fails with `extension.not_installed`;
    - a declared platform difference is named in the install report and in `info`.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionUpdateCommandTests/*"`, then the same filter with `ExtensionInfoCommandTests`.
- [ ] 4.6 List installed extensions in `extension list`, with deliveries `process` and `wasm`, `deployments`, `reason`, the `installation` member, and `--platform`; refuse an installed id equal to a built-in id. Verify the registry and list tests pass.
  - Files: `src/Morphir.Extensions/Registry.fs` (delivery cases, installed claims), `src/Morphir.Tooling/Features/Extensions/ExtensionListReport.cs`. Run the `fsharp-csharp-interop-reviewer` agent on the F# changes.
  - First failing tests, in `tests/Morphir.Extensions.Tests/RegistryTests.fs`:
    - an installed entry whose id equals `morphir.fsharp` is refused with `extension.duplicate_id`, exit 1;
    - an installed process entry has delivery `process`, both hosts and all four deployments;
    - a 0.1-only shared entry has `deployments` `[]` and a `reason`;
    - `--platform amiga` exits 2.
  - Command: build `tests/Morphir.Extensions.Tests/Morphir.Extensions.Tests.fsproj`, then `dotnet exec tests/Morphir.Extensions.Tests/bin/Release/net10.0/Morphir.Extensions.Tests.dll --filter-test-list "Registry"`.
  - Keep `ExecutableExtensionList.feature` green: with nothing installed, its id list does not change.
- [ ] 4.7 Add `tests/Morphir.E2E.Tests/Features/ExecutableExtensionInstall.feature`: install the test guest from a repository, list it and uninstall it, in `morphir` and `dotnet-morphir`; verify the feature passes.
  - Command: `dotnet exec <e2e> --treenode-filter "/*/*/ExecutableExtensionInstallFeature/*"`.
- [ ] 4.8 Retire the download handlers for extensions, and update the docs; verify `ExecutableExtensionInstall.feature` and the remaining management tests pass.
  - Remove `ExtensionHandlers.cs`, `ExtensionCommands.cs` and the extension use of `ArtifactPathResolver` and `OsPaths`, with their tests in `tests/Morphir.Tooling.Tests/Features/Management`.
  - Docs:
    - rewrite `docs/content/docs/cli/management.md` for extensions;
    - mark step 3 in progress in the extension-model PRD, with the corrections in the design's finding 6;
    - recommend closing #492 as superseded.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Management/*/*"`.
- [ ] 4.9 Add `morphir doctor` and `morphir doctor --fix` in both hosts over 3.6's move (Decisions 10 and 11); verify the doctor command tests pass.
  - First failing tests, in `DoctorCommandTests`:
    - `morphir doctor` in a home with old state lists what `--fix` would move, exits 1 and changes no file;
    - `morphir doctor --fix --yes` moves it and exits 0, and a second run finds nothing and exits 0;
    - `morphir doctor --fix` without a terminal and without `--yes` changes nothing.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/DoctorCommandTests/*"`.

## 5. Package archives and staged folders

- [ ] 5.1 Read zip and tar gzip package archives with the entry rules and recorded limits, detecting the format from content; verify the `Archives` tests pass in both formats and the AOT probe covers both readers with no new warning.
  - Runtime reader implemented without extraction or package-layout adoption. Security RED/131-test GREEN covers all decompressed tar bytes, metadata/padding, global PAX counts, final-drain cancellation and no payload double charging. Three Unix-only cases are ignored. The earlier JIT probe covered both readers; no probe/AOT run occurred during this correction. Parent #1156 is now published at `0439c4c0`; corpus-driven format adoption is next, not implied by this generic reader. This task stays unchecked.
  - First failing test: for each format, archives built in the test are each refused before any file is written. They contain:
    - `../../evil.exe`, an absolute path or a drive letter;
    - a symbolic link, a hard link, or a FIFO (tar);
    - a GNU long name that adds `..`, or a 2 MiB PAX header;
    - a case alias, a non-ASCII name, or `CON.exe`;
    - a zip whose local and central names differ;
    - 1025 entries;
    - a ratio bomb, as a zip entry and as a gzip stream, and inside a nested archive.
  - A gzip of one file is `extension.unsupported_package`.
  - Command: `dotnet exec <inst> --filter-test-list "Archives"`.
- [ ] 5.2 Read a package archive's layout: one `*.release.json`, checksums against the descriptor, and extra entries refused; verify the `PackageLayout` tests pass.
  - Waits on Q-ARCHIVE.
  - First failing test: a zip of release assets for the current RID is accepted; two descriptors are refused naming both; a replaced artifact with a matching replaced `.sha256` but the original descriptor digest fails with `extension.digest_mismatch`.
  - Command: `dotnet exec <inst> --filter-test-list "PackageLayout"`.
- [ ] 5.3 Install target archives as packages under merged finos/morphir#1151, including multi-file packages; verify the `TargetArchives` tests pass against parent group 1's published cases.
  - Waits on those package tables/cases and Q-WINLAUNCH. Q-ARCHIVE's outer release-assets packaging question remains separate; its one-executable target recommendation is superseded.
  - First failing tests: a one-file target archive remains a package with its package digest; a self-contained multi-file frontend installs with its manifest and entry point. Full verification at install and explicit verification, structural verification under the state lock at activation (VERIFY-B), and launch from the store with a process-tree lease (LAUNCH-A) follow the parent contract. A raw single-file artifact still uses its private verified run copy. Do not invent package members in advance of the parent tables.
  - Command: `dotnet exec <inst> --filter-test-list "TargetArchives"`.
- [ ] 5.4 Install a package archive given as a file through the pipeline, with generated local repositories for entries that enter the shared catalog and WASM packages unchecked; verify the `PackageInstall` tests pass.
  - Q-PROVENANCE (a) is approved. Waits on 5.2 and published parent repository/package tables.
  - First failing tests:
    - a global install's lock has `index.kind` `local-directory`, naming a directory below `data/morphir-dotnet/extensions/repositories/`;
    - the generated WASM history omits `platform`;
    - the repositories file is unchanged.
  - Command: `dotnet exec <inst> --filter-test-list "PackageInstall"`.
  - Docs: an "Install an extension" page under `docs/content/docs/cli/`, with both archive formats and the trust note for process guests.
- [ ] 5.5 Add the staged folders, the `[extension_install] staged` setting, the automatic pass for WASM packages only, and `extension install --staged`; verify the configuration, staged-pass and E2E tests pass.
  - Approved explicit consent: distinguish omitted inherited `auto` from explicitly configured `auto`; only the latter permits automatic unsigned WASM installation. Explicit source/staged commands are consent. Preserve the existing schema default; add no configuration member. Pending first failing tests in configuration/staged-pass/E2E projects must prove omission writes no state, explicit `auto` permits the narrow pass, and both retain `local-developer`. These tests and the integration are not implemented yet.
  - The global staged folder needs only 5.4; the project, workspace and system folders also need 6.1.
  - Files: `src/Morphir.Configuration/Schema.fs` and its tests in `tests/Morphir.Configuration.Tests`; the pass in `src/Morphir.Tooling/Features/Extensions/`.
  - First failing tests:
    - `manual` mode leaves a staged WASM package uninstalled on compile;
    - a staged process package prints the install note on stderr, runs no guest and installs nothing;
    - a broken staged archive leaves `compile --json` stdout unchanged.
  - Commands: the configuration test list in `tests/Morphir.Configuration.Tests`, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/StagedPassTests/*"`, then the E2E feature `ExecutableExtensionStaged.feature`.
- [ ] 5.6 Install a bare `.wasm` module through `describe` in the WASM runtime; verify the `BareWasm` tests pass.
  - Waits on PRD step 2a. Until then, the refusal `extension.wasm_host_unavailable` stays.

## 6. Selection layers and activation

- [ ] 6.1 Read and write pin files with an exact `$schema` match, critical members and per-platform digests; verify the `Pins` tests pass.
  - Q-LEVELS (b) is approved. Waits on the parent toolchain contract's published pin schema/member table/corpus; no .NET-only unversioned file.
  - First failing test: a pin whose `$schema` is not the URL the toolchain contract defines, or that has `schemaVersion` and no `$schema`, is refused with `extension.unsupported_contract_version`; a pin with an absolute path member value or a credential member is refused.
  - Command: `dotnet exec <inst> --filter-test-list "Pins"`.
- [ ] 6.2 Resolve through the layers with the discovery boundary and isolation, refuse duplicates within a layer, and report shadowing; verify the `Layers` tests pass.
  - First failing tests:
    - a project pin of 0.2.0 over a global 0.1.0 selects 0.2.0 only inside the project;
    - a pin for a version not installed fails with `extension.not_installed` and does not fall back;
    - a pin with only `win-x64` fails on another platform with `extension.platform_not_pinned`;
    - `MORPHIR_ISOLATED` omits the shared catalog and the system pin;
    - a pin file listing one id twice fails with `extension.duplicate_id`, exit 1.
  - Command: `dotnet exec <inst> --filter-test-list "Layers"`.
- [ ] 6.3 Add `extension use`, `extension which` and `extension list --local`, with pin writes under a lock keyed by the canonical pin path; verify the command tests pass.
  - First failing tests:
    - `use` of a version that is not installed fails and changes no pin;
    - `which --json` names the shadowed global selection;
    - `list --local` shows only pinned entries and the built-ins;
    - two concurrent `use` commands on one pin file leave the complete result of both, in some order.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionUseCommandTests/*"`.
- [ ] 6.4 Read the generation overlay from `MORPHIR_EXTENSION_OVERLAY`, refuse paths outside `toolchains/generations/`, and never write there; verify the `Overlay` tests pass.
  - Q-OVERLAY (a), above project pins, is approved. Waits on the parent toolchain contract for overlay schema/layout/corpus. Approved task 4.4 consumes this seam.
  - First failing tests:
    - two overlays selecting 0.2.0 and 0.3.0 of one id resolve to their own versions, while the global selection still names 0.1.0;
    - an overlay of `/tmp/gen` fails with `extension.overlay_invalid`;
    - with an overlay set, a catalog an older CLI wrote is handled as 3.6 says: without a terminal and without `--yes`, the command fails and changes nothing.
  - Command: `dotnet exec <inst> --filter-test-list "Overlay"`.
  - Docs: the overlay contract for `morphirup` in `src/Morphir.Extensions.Installation/README.md`.
- [ ] 6.5 Activate offline: pair agreement, containment, mode and MEP version checks; hash the store object in the read that writes the private copy; launch with the filtered environment and an explicit working directory; check the session against the installed claims. Verify the `Activation` tests pass on Windows and Linux.
  - Needs 3.5. It serves the shared catalog first; pins join after 6.2.
  - First failing tests:
    - a store object changed after install fails with `extension.digest_mismatch` and starts no process;
    - a test seam that changes the object mid-read fails the same way;
    - the environment guest sees no `GITHUB_TOKEN` and the project directory as its working directory;
    - a guest whose initialization offers an unclaimed target is refused before any operation;
    - on Windows, uninstall while the guest runs succeeds and the guest keeps running.
  - Command: `dotnet exec <inst> --filter-test-list "Activation"`.
- [ ] 6.6 Check the Windows launch path length; verify the `WindowsLaunch` tests pass on Windows.
  - Waits on Q-WINLAUNCH. First confirm with a test whether process creation refuses an executable path over 260 characters. If it does not, drop the provisional requirement and record that in the design.
  - Command: `dotnet exec <inst> --filter-test-list "WindowsLaunch"`.
- [ ] 6.7 Add `[frontend.<language>] extension_id`, and let compile start the selected installed frontend through the private copy; verify the configuration, compile and E2E tests pass.
  - Q-CONFIGREF (a) is approved. Also add `id` to an `extensions` table; preserve existing path-string meaning. Integration still waits on the selection pipeline, without changing schema defaults here.
  - Files: `src/Morphir.Configuration/Schema.fs`, `src/Morphir.Tooling/Features/Compile/MepFrontendExtensionConnector.cs`.
  - First failing tests:
    - setting both `extension` and `extension_id` fails configuration loading, naming both;
    - with the test guest installed as a frontend, `morphir compile` starts its verified copy;
    - an `extension_id` that is not installed fails with `extension.not_installed`, with the install command and no network access.
  - Commands: the configuration test list, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Compile/*/*"`.

## 7. Hand-over for morphirup

- [ ] 7.1 Add the in-process hand-over of a descriptor, one artifact and an authentication statement, installing through the same checks and returning the statement unchanged; verify the `HandOver` tests pass.
  - First failing tests:
    - a hand-over with `recipe-digest` whose artifact bytes differ from the descriptor digest fails with `extension.digest_mismatch`;
    - a successful one reports the caller's statement and the probe outcome separately;
    - a statement of kind `local-developer` is refused;
    - the "what would this write" answer names exactly the files the following install changes.
  - Command: `dotnet exec <inst> --filter-test-list "HandOver"`.
  - Docs: the hand-over contract in `src/Morphir.Extensions.Installation/README.md`. It says that the installer checks no signature and no repository identity: `repository_mismatch` is the manager's check.
- [ ] 7.2 Exercise the hand-over from `morphirup setup` with descriptors and artifacts authenticated by the tool release metadata client; verify the adoption change's task 4.3 tests pass against this installer.
  - Waits on the adoption change's group 4 and the separately evaluated TUF client.

## 8. Dotted ids

- [ ] 8.1 Run finos/morphir#1149's shared extension id cases (its task 1.2) through the compatibility kit runner, and fix what they find in 2.2, 2.7 and 3.5; verify every case reports its expected outcome.
  - The runner is the standalone `morphir-mck` executable, which `morphir mck` dispatches to as a process (maintainer decision, 10 November 2026). The cases and expectations stay in finos/morphir. This task adds no second checker.
  - Waits on #1149's task 1.2, which publishes the case manifest.
  - First failing tests: a home holding `acme-backend` and `org.finos.morphir.fsharp-backend` has one catalog listing both and two locks in `locks/extensions/`, and every derived-name, device-name and length case gives the manifest's outcome.
## 9. Integration checks

- [ ] 9.1 Check a shared Morphir Home with a second current host: the MoonBit host, once it installs extensions. Each host lists, activates and uninstalls what the other installed, concurrent installs serialize on the state lock, and a journal either leaves is applied by the other. Record the outcome and the revisions in the pull request.
  - Waits on the MoonBit host. It is not a prerequisite for the first .NET delivery. The Rust product CLI is to be deleted (Decision 16) and is not part of this check.
  - Official qualification of a release belongs to finos/morphir's build orchestration and qualification (Decision 16A). This task records a development check only.
## Workflow follow-up

- Track this work under existing #558 while the Beads conflict persists. Link a dedicated installer issue only if the maintainer creates one; do not block work or invent an id.
- Follow AGENTS.md and `openspec/config.yaml` for review and explicit approval before merge.
- Archive only after the implemented scope passes. Full installer and user/technical documentation remain the destination. Keep package work under merged finos/morphir#1151 and its published tables/cases; do not revive the single-executable recommendation. Unfinished tasks remain unchecked.
- The `extension-registry` delta is written against the text of `add-deployment-and-acquisition`, which is active on main. If that change archives first, this delta applies as written. If this change archives first, the adoption change's MODIFIED block must take this one's installed-entry sentences.
