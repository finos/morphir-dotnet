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
