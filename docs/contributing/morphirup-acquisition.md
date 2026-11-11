# Manager acquisition implementation status

The manager lifecycle is being implemented under the approved
[`add-deployment-and-acquisition` change](../../openspec/changes/add-deployment-and-acquisition/proposal.md).
The first tranche is bounded acquisition I/O. It does not connect `setup` to downloads or installation.

## Current status

| Work | Status | Evidence or next step |
| --- | --- | --- |
| Existing document readers, resolution and plan/dry-run behavior | Delivered on main | `Morphir.Toolchain` and `Morphirup`; unchanged by this tranche |
| Streaming transport and integrity tests | GREEN for the bounded tranche | 58 passed, 0 ignored, failed or errored across the three new test lists |
| Acquisition adapter | Implemented and tested in isolation | `src/Morphir.Toolchain.IO`; Release build has 0 warnings and 0 errors |
| Existing CI test lane | Source wiring added; current-head execution pending | One `Morphir.Toolchain.IO.Tests` entry in Nuke `Test`; existing Development matrix runs it on Ubuntu, Windows and macOS |
| Concrete immutable filesystem store | Deferred integration | Installer FileSafety/Store code is under independent review; no copy or merge in this tranche |
| Production publisher authentication | Unqualified | Pure `TufClient` and `TrustRules` require security review and an asynchronous integration design |
| Trusted-state durability, generations and transactions | Pending | Later lifecycle tranche |
| Activation, proxy, readiness and installer integration | Pending | Later lifecycle tranche; no setup apply behavior yet |

The authoritative toolchain contracts and corpus live in the parent Morphir repository at
`docs/spec/toolchain`. The .NET source delivery revision and the parent's contract-test pin are separate.
This tranche does not edit the parent corpus, frozen expectations or provenance. Parent
[#1156](https://github.com/finos/morphir/pull/1156) merged at `0439c4c06d8ca6d4a46e41afb226ba0872f9bff8`.
Its extension member tables, seven schemas and current corpus are authoritative. They do not authorize a local
replacement for the separate portable toolchain pin/overlay contracts being published by the coordinator.

## Boundary under test

`Morphir.Toolchain.IO` depends on the pure `Morphir.Toolchain` library, with no dependency on `Morphir.Tooling`.
It accepts an explicit source URI for every request, a caller byte limit, an absolute deadline and an online/offline
mode. It has no repository discovery, implicit trusted URL, key configuration or installer command.

Metadata reads return bounded bytes for a future verifier. A supplied `TufMetaFile` gives the exact length and SHA-256
to check; its version remains the verifier's responsibility. Root or timestamp reads without a parent link still have
a caller byte bound. Returning their bytes makes no statement about trust.

Target acquisition takes the existing `TargetPath` and `AuthenticatedTarget` record. The I/O adapter checks their
expected length and SHA-256 over the streamed bytes. These inputs are expectations supplied by the caller; accepting
the record does not authenticate its provenance. SHA-256 uses the existing managed Toolchain digest implementation,
with incremental updates so that asynchronous reading needs no second hash implementation or synchronous enumerable.

The tests require the adapter to check recorded and declared lengths before allocating body storage or starting a
private object. A missing Content-Length is permitted. A lying header, short body, byte past the effective limit or
wrong digest is refused. Read requests are capped by a fixed working buffer and the remaining limit plus one byte
used to detect excess. The excess byte is never written to staging or returned as metadata. A target's total size
never determines an array allocation. A metadata limit that cannot fit a byte array is an invalid request.

Cancellation and the deadline cover opening the response, reading, beginning and writing private staging, and publication. Caller
cancellation propagates as `OperationCanceledException`; deadline expiry has its own I/O failure. Every owned response
and private stage must be disposed before the operation completes. Failure while disposing a completed response
must refuse before publication. Offline requests refuse before any transport or
store call. This boundary does not add a cache policy.

The HTTP transport requests headers before consuming content. Its production handler disables automatic
redirects, decompression and cookies. Only a complete 200 response with unencoded content is accepted. A redirect
does not authorize a different source. Transport integrity remains separate from publisher authentication even when
the explicit source uses HTTPS. The HTTP adapter requires an absolute HTTP(S) URI with no user-information or
fragment component. It configures no credentials or authentication policy. A future authenticated source adapter
must supply its own reviewed credential handling rather than place secrets in a source URI.

Metadata reads require a limit that fits a .NET byte array. Deadlines must fit the platform timer's supported due
time. Unsupported requests refuse before transport. Injected streams, transports and stores must honor operation
cancellation; their resource disposal must perform bounded cleanup. The adapter awaits cleanup rather than detach
an unobserved operation after a cancellation or deadline.

## Store seam and publication obligations

The injectable store begins a private object for the existing target path and recorded target metadata. Its private
object accepts asynchronous writes and exposes a separate publication operation. The adapter may call publication
only after EOF, exact length and digest have all passed. An interrupted stage is disposed without becoming visible.
Successful acquisition returns no installation receipt and does not change a selection or readiness state.

The store owns private staging, cleanup and immutable atomic publication. Publication must never replace an existing
winner. An identical verified winner may be reused; a conflicting winner is a refusal. A production implementation
must enforce this against concurrent writers and filesystem path substitution, including symlinks/reparse points,
and must honor cancellation before its publication commit. Disposal must release staging on every refusal. The
in-memory store in the tests demonstrates the calling contract; it does not establish filesystem safety or crash
durability. No production filesystem store is supplied by this tranche. The later filesystem integration follows
the supported-writer threat model and published installer contracts; this seam makes no promise of isolation from
a malicious process running as the same user.

Cancellation cannot roll back an atomic publication that has already committed. The store must seal and flush its
private bytes before reporting successful publication, and report the committed outcome consistently. The adapter
attempts stage disposal even after success. Expected I/O, access and cancellation errors during that postcommit
cleanup cannot turn the committed object into an uncommitted refusal. Before commit, those errors refuse acquisition.
The tests include cancellation immediately after commit and after-commit cleanup failure, alongside the failures
before publication. Later generation transactions must define their own durable commit boundary.

## Async trust integration still required

The existing pure `MetadataSource` is a set of synchronous callbacks returning complete byte arrays. `TufClient`
also catches source callback exceptions and maps them to repository availability failures. Putting asynchronous
network reads behind those callbacks with `.Result`, `.Wait()` or `.GetAwaiter().GetResult()` would block and lose the
cancellation/deadline contract. Preloading every possible role would also defeat incremental root rotation and
linked metadata bounds.

A later reviewed change must separate the pure acceptance transitions from orchestration. The orchestrator must
await bounded metadata reads for the next required role, apply the pure transition, and durably preserve each
accepted root and role before continuing. It must carry the caller's cancellation and deadline through reads and
durability operations. No such adapter is activated here. `TufClient`, `TrustRules`, repository identities and
TEST-ONLY keys remain outside production acquisition. Security review is required before production authentication.

## Next adapter increment, proposed for review

The adapter belongs in `Morphirup` composition, referencing `Morphir.Toolchain.IO` and the qualified installer
library. The dependency direction stays one way: IO and installer each depend on existing domain libraries;
neither depends on the manager or the other adapter. The manager supplies the explicit source, existing parsed
target path and digest, caller limits, deadline and cancellation. It supplies no default store or trusted repository.
This is a composition proposal, not an implemented API or a new persistent format.

The inspected installer `SingleFileStore.PublishAsync` accepts a file already privately staged beneath the supplied
Home. It rehashes, flushes and holds a verified descriptor through publication, with cooperative writer locking.
It does not currently expose the private streaming handle needed by `IAcquisitionStore.BeginAsync`. Its existing
Task-returning wrapper executes synchronous file work. Calling it after creating a separate manager-owned staging
implementation would duplicate the file-safety boundary. Blocking asynchronous downloads behind that wrapper, or
holding its writer lease while awaiting the network, would also violate the acquisition boundary.

The installer owner must define, test and review the exact private-stage API. The following are ownership and
ordering requirements, not proposed method signatures or layout members:

| Phase | Owner and completion contract | Cancellation, deadline and failure contract |
| --- | --- | --- |
| Begin | Installer creates a private stage, owns its directory/file identity and returns exclusive handle ownership to the adapter | No global state/writer lease is held while waiting for transport; failure cleans partial creation and returns no stage |
| Write | IO awaits each bounded chunk write through the adapter before reusing the buffer; installer consumes it through its owned handle | Use the operation's cancellation/deadline, refuse a short or failed write, and retain no caller buffer after completion |
| Seal/flush | Installer seals writes, flushes and preserves the identity of the same staged object | Completion means the bytes needed for commit are durable under the installer's contract; failure or cancellation before commit exposes nothing |
| Verify | IO's streamed EOF/length/digest checks must have passed; installer retains its verified-descriptor/tamper checks | Matching the stream digest alone cannot authorize a different filesystem object, installation receipt, trust or readiness |
| Commit | Installer acquires the cooperative writer lease only for publication, revalidates the handle/location and atomically publishes without clobbering | Waiting for the lease honors the same cancellation/deadline; before-commit refusal preserves any winner and prior selection; successful commit reports success consistently |
| Dispose | Installer closes/releases the stage and removes only private objects whose identity it still owns | Cleanup is bounded and awaited after failed begin/write/seal/commit; foreign replacements survive; postcommit cleanup cannot imply rollback |

The adapter must not reopen an unverified path to substitute for the verified handle. It must not create a second
hash implementation, installer, staging layout, state lock or receipt. Filename restrictions, file limits, executable
permissions, store layout and ownership remain the installer's published contracts. The current single-file store's
extension-specific layout and limits are not silently generalized to host archives. That applicability must be
resolved with the installer owner using the relevant published host/extension contracts.

Offline reuse needs an installer-owned way to find and reverify an existing immutable object without creating a
stage. The present acquisition `Offline` result forbids all fetch/staging calls; it is not an offline inventory or
restore implementation. The composition must check the verified local inventory first and report missing content
without a transport call. No separate manager cache or local object-index format is introduced by this proposal.

### Adapter controls to implement after the installer API review

| Control | Positive observation | Negative observation |
| --- | --- | --- |
| Dependency direction | Manager composition references IO and installer using their existing types | No IO-to-installer, installer-to-manager or Tooling dependency; no copied filesystem implementation |
| Lease scope | A paused download leaves the global writer lease available to another supported writer | No network read or streamed write occurs while that publication lease is held |
| Buffer ownership | Chunk completion consumes exact bytes before the caller overwrites its buffer | A pending write cannot retain a reused buffer or publish partial data |
| Sealing and flush | The same handle is sealed/flushed before publication; its exact bytes remain readable | Injected flush failure exposes no object and creates no receipt or selection |
| Identity preservation | The verified open object is the object committed | Replacing a pathname or changing the open stage is refused or the original verified handle is published, according to reviewed installer behavior; foreign replacement cleanup is forbidden |
| No clobber | An identical verified winner is reused under the lease | A conflicting winner is retained and acquisition fails without repair or replacement |
| Cancel/deadline before commit | Cancellation during begin/write/seal/lease acquisition stops before visibility and releases handles/leases | No pending await is replaced by a synchronous wait or mapped to publisher authentication failure |
| Commit race | A confirmed commit stays successful when cancellation arrives immediately afterward | No return value claims the committed object was rolled back; prior generation selection remains a separate transaction |
| Cleanup | Interrupted private staging is removed and all resources become available | Another writer's identity is never removed; cleanup failure does not hide caller cancellation before commit |
| Offline | Existing bytes are reverified through the installer primitive without staging | Missing or changed bytes cause a structured refusal with zero transport calls |

These controls are not yet implemented or executed. They require qualified installer primitives and a separate
coordinated RED/GREEN cycle. The existing 58 tests cover the injected seam, not this filesystem adapter.

The coordinator's private-stage review accepts awaited native syscall completion, not a hard wall-clock bound for
a hung kernel flush, rename or close. Cancellation stops new admission, asynchronous work and lease waits, then is
checked around native durability/publication. Native work or cleanup must not continue as an unobserved detached
task. Logical abandonment may leave a closed, installer-owned private orphan for a later coordinated sweep when
immediate deletion cannot acquire a busy cooperative lease. The adapter must observe that deferred cleanup, release
all handles/leases, preserve foreign replacements and keep cancellation's primary cause. The IO comment that dispose
discards uncommitted bytes needs this precise physical-cleanup reconciliation before concrete adapter integration.
These refinements do not authorize any host blob namespace or the extension-specific raw-artifact limit for hosts.

## Pure trust durability design, not production authority

The next trust work must refactor the existing pure acceptance transitions rather than implement another TUF
checker. The existing readers, signature checks, repository identity, role/target naming and bound metadata types
remain the only implementations. The effect driver may await bounded reads and persistence, but must never call
an asynchronous transport through the current synchronous `MetadataSource` callbacks.

For review, model the next required input and each accepted checkpoint as in-memory control flow. A checkpoint
must retain the exact envelope bytes accepted for that role together with the existing parsed state and repository
purpose/identity. `TufTrustedSet` currently returns parsed roles; it is not by itself a durable authenticated-envelope
format. Re-encoding those parsed records is not evidence of the original signatures. No checkpoint serializer,
filename, journal member or trust-root provisioning mechanism is defined here.

The driver must await durable acceptance before consuming the next role/root or exposing authenticated target
authority. It must preserve every accepted root, role rollback protection and learned revocations according to
the published profile, including when a later read or verification fails. Cache deletion must not delete security
state. The exact distinction between disposable role-byte caches and durable rollback/revocation evidence needs
review against the profile and the existing pure-client failure-state tests; this document does not replace either.

The authority handoff to acquisition must bind the named repository, exact target path, recorded length/digest and
durably accepted trust state. A public `AuthenticatedTarget` record or successful stream check alone is insufficient
to produce that authority. Test-purpose state must remain distinguishable and unusable for production. Root keys,
repository identities, canonical encoding and revocation handling need security review before any production path
can request the handoff. No root or fixture key is selected here.

### Pure checkpoint and effect-order controls to prepare

| Scenario | Required observation before production integration |
| --- | --- |
| Accepted root N+1, later root/role failure | Its checkpoint preserves the exact accepted envelope and key transition; later failure cannot restore the revoked root keys |
| Durability await paused | No next-root/role read and no authenticated-target handoff occurs until the checkpoint completes |
| Durability failure or cancellation | No checkpoint success or target authority is fabricated; caller cancellation remains cancellation; prior durable state is recoverable |
| Root key rotation | The same timestamp/snapshot/targets invalidation behavior as the current pure engine is checkpointed before new-role use |
| Root rotation limit | Accepted roots remain durable when the existing bounded rotation limit pauses the refresh |
| Timestamp/snapshot/targets acceptance | Existing signature, linked length/digest/version, rollback and expiry checks keep their order and bound the next fetch |
| Same-version or rollback response | Refusal preserves previously durable acceptance and cannot overwrite it with different content |
| Learned revocation, failed refresh, then offline use | Recipe/host revocation remains enforceable; unavailable transport or cache cleanup cannot erase it |
| TEST-ONLY or wrong repository | No production authority can be created from either, even if a digest or signature check succeeds |
| Failed target transfer | The previously durable trust evidence is retained, but no partial object, receipt or activation is published |
| Process restart between acceptance and generation publication | Restart sees only acknowledged durable trust and complete objects; an incomplete candidate cannot become selected |

New acquisition and installed launch are different cases. The published tool-release profile permits launch of an
otherwise valid installed release despite expired repository metadata, provided installed bytes are reverified and
learned revocations remain enforced. An expired cache cannot authorize new resolution. Existing
`RecipeTargets.checkLockedGeneration`, receipt/lock binding and derived readiness logic must be reused rather than
duplicated or replaced with a blanket cache-expiry rule.

Generation assembly, readiness probes and activation follow this authority/object boundary. The published receipt
contract requires every component to be capability-checked and no check to have failed before activation; a skipped
workflow remains visible and is not workflow qualification. The prior selection survives every failure. Persisted
selection and overlays remain gated on the separately published parent contracts, so these source-only scenarios
define no local competing state. No new trust or lifecycle test has run as part of this design work.

### Current source-only checkpoint test tranche

The first three control slices are in `tests/Morphir.Toolchain.Tests/TufCheckpointTests.fs`: exact accepted
envelope snapshots and buffer isolation; paused acknowledgements for root, timestamp, snapshot and targets; and
refused/cancelled persistence with accepted and acknowledged progress distinguished. They use the existing signed
TEST-ONLY fixture helpers, including their relinker, rather than generate another verifier or change the corpus.
Envelope whitespace differs from canonical output, so re-encoding parsed records cannot pass the exact-byte control.
An immediate-acknowledgement control compares the awaited driver with the existing synchronous refresh result.
Their first 15 controls reached declaration-only runtime RED. The separate GREEN grant then exercised the
implementation and seven additional controls: all 22 passed. This is in-memory acceptance/checkpoint evidence;
production composition and durable storage remain unimplemented.

The internal pull boundary is `TufAcceptance.start`, producing metadata request, accepted-checkpoint,
complete or refused control flow. Requests carry the existing role, optional version, byte bound and parent link;
checkpoints own defensive copies of the exact envelope and expose accepted parsed state without granting authority.
An acknowledgement continuation is the only route to the next request. The synchronous compatibility entry point
now drives this same core with immediate acknowledgements. The existing reader/signature/check ordering was moved
into this single path. The 114 registered TUF regressions and current external corpus passed under the separate
GREEN grant. IO contains no checker; independent security review is still required.

The internal IO driver is `TufCheckpointRefresh.runAsync`, with injected fetch and checkpoint callbacks,
and progress separating accepted, acknowledged and pending state. Persistence refusal and cancellation are explicit
internal outcomes, distinct from the existing publisher/metadata failures and from completion. A cancelled outcome
retains the caller token and progress; it is not a public cancellation or persistence contract and creates no target
authority. Composition must not silently convert it to success or a publisher failure. No public/persisted API,
production identity, root import, checkpoint filename, JSON, journal or receipt is introduced in this tranche.

The targeted RED grant completed before substantive implementation. A separate GREEN and independent security-sensitive review
must retain the existing rotation threshold/limit, invalidation, rollback, version-reuse, expiry and corpus
regressions. Further restart/revocation controls remain to be added; the first three slices do not claim them or
production qualification. The earlier 58 byte-acquisition tests are not trust coverage.

The source keeps accepted evidence in private byte copies taken before parsing, after checking the existing role
bound. Oversized inputs follow the existing refusal path without a copy. Checkpoint access returns fresh envelope
and parsed-key copies, and the core owns its starting key arrays. Caller mutation must not alter later verification.

The awaited driver records accepted and pending evidence before checking cancellation at a checkpoint. An `Ok`
callback completion advances acknowledged state before a subsequent cancellation check. Refusal or cancellation
without that acknowledgement leaves the checkpoint pending and reports only earlier state as acknowledged.
Pending means unacknowledged by this callback contract; it does not assert that a failed storage operation wrote
nothing. Recovery and durable storage remain separate design/review work.

Reads and acknowledgements receive the caller token, which may be linked to an operation deadline. Their tasks are
awaited to completion, including when cancellation arrives during an operation already in progress. No detached
write or hard kernel-operation deadline is promised. Additional source controls cover successful acknowledgement
after cancellation for each role, thrown storage/transport failures, fetch cancellation, and mutation of parsed keys.
The three lists contain 22 tests; the original 15-test RED remains distinct from their later GREEN evidence.

## Integration boundary questions

These questions concern ownership and evidence at integration. They do not reopen the maintainer's extension
selection or unsigned local-developer policy decisions, and they do not block bounded byte acquisition.

| Boundary | Evidence the integration must supply | Current treatment |
| --- | --- | --- |
| Publisher trust | The reviewed authentication path and out-of-band repository identity that produced the target expectation | Caller-supplied length and digest remain expectations, never proof of publisher trust |
| Local-developer consent | An explicit local source or evidence that the user explicitly enabled staged mode | An inherited default `auto` alone is not consent; the I/O request carries no install-policy decision |
| Local-directory provenance | The installer-owned release/provenance using the parent's published members | No receipt or provenance object is generated by this boundary |
| Durable publication | Reviewed installer primitives and their cooperative locks, tamper detection and commit behavior | Only an injectable private staging/publication interface is supplied here; no copied filesystem implementation |
| Portable selection and overlay | Published parent schema/corpus for the shared host/toolchain integration | No local metadata or activation/overlay format is introduced |

The remaining concrete authentication question is which reviewed component will hand the manager an authenticated
expectation together with its durable trust evidence. This tranche can transport and check bytes without choosing
that component or silently promoting a matching digest to trust. The coordinator owns that later integration.

## Full lifecycle delivery

The acquisition branch and its draft PR carry the full manager lifecycle. The bounded I/O work is the first
increment, not a separate installer or the endpoint for this change. Remaining work includes authenticated target
acquisition with durable trust and revocation handling, verified object durability, offline setup/restore,
generations and transactions, activation and proxy/run behavior, and update/rollback/repair/remove/self-update.
Commands continue to describe their implemented plan-only behavior until their apply paths are wired and tested.

Integration must reuse the installer primitives delivered through
[#593](https://github.com/finos/morphir-dotnet/pull/593) once their required qualification is available. That draft's
full installer lifecycle and qualification are still in progress. No installer sources are copied or merged here.
The parent portable selection/overlay candidate is not yet published or adopted, so this branch adds no such
persisted metadata. Production authentication requires its own narrow design, tests and security review before
activation. The existing pure TUF code remains unused and unqualified.

## Validation and capacity

The first RED cycle was limited to the new acquisition test graph. The pinned mise SDK was `10.0.403`; restore
succeeded without changing existing versions or lock files. The first build failed on the missing acquisition and
incremental digest APIs. That compile RED was not an assets-missing failure and was not counted as runtime evidence.

After the approved narrow restore for the new IO project, declaration-only stubs built with zero warnings or errors.
All 52 initial tests then ran in the executable's three lists: bounded acquisition, HTTP acquisition transport and
existing managed SHA-256 incremental use. The result was 0 passed, 0 ignored, 0 assertion failures and 52 intentional
`NotImplementedException` runtime errors. This is evidence of missing behavior, not exercised integrity or security
coverage. The slot was released at a verified no-process boundary before substantive implementation began.

Under the separate GREEN grant, the isolated Release IO test graph built with `--no-restore` in 11.4 seconds,
with 0 warnings and 0 errors. Its three sequenced lists ran 58 tests: 58 passed, 0 ignored, 0 failed and 0 errored.
There were no intermediate GREEN failures or fixes. These runs exercised the implemented success/refusal,
cancellation, deadline, disposal and publication paths. They qualify the narrow byte-acquisition behavior under
the scripted transport and store contracts, not publisher trust, real filesystem publication or readiness.

Both cycles used `DOTNET_PROCESSOR_COUNT=2`, single-node MSBuild, disabled shared compilation and node reuse, and
hidden captured execution with a 180-second whole-process-tree kill and await bound. No timeout occurred. CPU2 was
released after a process inventory found no .NET build/test/compiler-server processes. The GREEN logs are
`.dev/acquisition-red/CompileGreen-20261111T1208328349019.log` and
`.dev/acquisition-red/RuntimeGreen-20261111T1208400618226.log`; the runtime RED and declaration build logs are preserved
in the same ignored directory. Source/whitespace/XML checks are separate from build/test evidence.

The scripted tests use no network, real home directories, installer state or global environment changes. They cover
chunked and short reads, declared/missing/lying lengths, exact limits and digest mismatch, partial staging, offline
refusal, cancellation and deterministic deadlines, response disposal failures, private store failures, operation
timer cleanup, and no-clobber publication. The same project checks incremental use of the existing managed SHA-256
with an official digest and a reused read buffer. HTTP tests use an
injected message handler and content that refuses eager buffering. No full-solution, AOT, live network, cross-platform
or tool-install validation ran in these slots. Existing package versions and lock files are unchanged; only the two
new projects have new lock files. Independent review and later integration qualification remain separate work.

The coordinator's independent read-only review of exact `0bfff39abc21c09eb824141d5c3dae44ce1d18a0` reported no
actionable security, correctness or interop findings and accepted the 58 controls as meaningful. That is static
review of the bounded increment. It does not qualify real filesystem publication, live HTTP, other operating
systems, Native AOT, C# consumers, trust or the remaining lifecycle.

The subsequent change adds only the IO suite's missing entry to Nuke's existing test list and updates these status
docs. `Morphir.slnx` already includes both new projects. The existing Development workflow builds that solution and
invokes Nuke `Test` on its three operating systems, so no new job or YAML change is needed. This wiring has not been
run locally; its execution and results remain pending current-head CI. The earlier 58-test local evidence applies
to the reviewed I/O source, not an unexecuted full Nuke/CI run.

The separate trust-checkpoint RED boundary uses three new lists and 15 tests for exact accepted envelopes,
paused acknowledgements, and refusal/cancellation progress. The first focused Release `--no-restore` build
reached the tests and reported 98 missing-API and cascading compiler errors, with zero warnings. Declaration-only
internal pull/checkpoint types, an awaited-driver stub and friend-assembly wiring then built with zero warnings
or errors. The existing synchronous checker is unchanged; the new declarations are not connected to it.

The exact three lists ran sequenced: zero passed, ignored or assertion failures, and 15 intentional
`NotImplementedException` errors (one pure-core entry and 14 awaited-driver entries). This establishes missing
behavior only. Exact-byte isolation, acknowledgement ordering, refusal and cancellation assertions still need
to execute against an implementation; the earlier 58 IO controls do not cover trust acceptance.

This RED run used pinned SDK `10.0.403`, two processors, single-node MSBuild, disabled shared compilation and
node reuse, and hidden captured execution with a 180-second whole-tree kill/await bound. No timeout or restore
occurred, and no package versions or lock files changed. The focused project-reference edge has not had restore
validation. No old/full test suite, probe, AOT or live network ran. CPU2 was explicitly released after an empty
.NET build/test/compiler process inventory, before substantive implementation. Logs are retained under
`.dev/trust-checkpoint/`: `CompileRed-20261111T1348059278580.log`,
`CompileRed-20261111T1348525024352.log` and `RuntimeRed-20261111T1349032207176.log`.
Production trust remains unused and unqualified; no persisted checkpoint format or default authority is added.

Under the separate trust-checkpoint GREEN grant, the first focused Release build reported seven compiler errors:
two nullness diagnostics and cascading errors from an ambiguous record type. These were corrected with
`ReferenceEquals` null checks and an explicit `TufTrustedSet` annotation. The next build had zero warnings or
errors. This initial failure is retained in `CompileGreen-20261111T1400229462536.log`.

The IO project reference was absent from both test assets and its lock file. One permitted narrow restore of
the same seven-project graph added only the `morphir.toolchain.io` project entry to
`tests/Morphir.Toolchain.Tests/packages.lock.json`. Every existing member of every tracked lock file was compared
with its saved prior value and remained unchanged, including all package versions. The final Release
`--no-restore` build had zero warnings and errors in 1.8 seconds. No production source or source formatting changed
after that build.

The executed sections were:

| Section | Exact selection | Passed | Ignored / failed / errored |
| --- | --- | ---: | --- |
| New checkpoint controls, pinned fixtures | `--sequenced --summary --filter-test-list "TUF checkpoint"` | 22 | 0 / 0 / 0 |
| Existing registered TUF subtree, external corpus | `--sequenced --summary --filter-test-list "The TUF client over the signed repository"` | 114 | 0 / 0 / 0 |
| Current external corpus registration | `--sequenced --summary` with external corpus selected | 1,069 | 0 / 0 / 0 |

The external root was `C:/Users/damre/.t3/worktrees/morphir/t3-f4b4c4f8-aa775c70a7fa14ff`, at
`ee331648746962044d127d1a22a8565dea46b5d5`. Hashes of 329 files under its toolchain pages/fixtures,
schemas and workflow scope were unchanged before and after execution. The existing test registration determines
which published toolchain contracts run; no additional family format or candidate was adopted.

The count wrapper initially rejected successful checkpoint child output because of ANSI sequences, then rejected
the successful corpus child output because its counts use thousands separators. Both failures and all child logs
are preserved. The corrected parser verified the exact nonempty counts from each retained log, including zero
ignored, failed and errored tests. These were wrapper validation defects; the child processes exited zero and
there were no behavioral test failures in this GREEN cycle.

GREEN evidence is retained in `.dev/trust-checkpoint/`: `Restore-20261111T1401302864702.log`,
`CompileGreen-20261111T1401451540723.log`, `RuntimeGreen-20261111T1402247430969.log`,
`TufGreen-20261111T1403055115179.log`, `CorpusGreen-20261111T1403229952568.log` and
`Green-all-sections-count-validation.log`. The run used the same pinned SDK, processor/build limits and hidden
180-second whole-tree kill/await bound. No timeout occurred. CPU2 was explicitly released after an empty process
inventory; no further burst, full solution, probe, formatter, AOT, commit or push was performed.

These results exercise the shared pure acceptance core and injected awaited checkpoint contract. Restart storage,
cache deletion/revocation durability, real filesystem persistence and production trust activation remain separate
work and qualification. A new independent security-sensitive review is required before integration proceeds.
