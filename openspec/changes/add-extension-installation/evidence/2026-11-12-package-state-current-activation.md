# Package state and current activation checkpoint

This increment extends the existing installer with ZIP package trees, current
catalog/lock documents, durable state transactions and installer-owned current
activation records. It is a library checkpoint toward the installed process
provider journey. It does not qualify that journey or finish acquisition.

The tested source was based on `ad24ce68dea6eed6071147b458fefe70aa364288`.
Current-main dependency integration requires its own fresh validation. Earlier
local Windows manager delivery and Unix results retain their original source
identities.

## Behavior and ownership

- ZIP extraction, no-replace directory publication and reuse share canonical
  manifest and executable-declaration verification. Archive and tree identities
  are distinct. VERIFY-B acquisition checks the stored structure; explicit
  verification rehashes all regular files. Directory-only ZIP entries are
  validated without creating empty directories.
- State readers use the existing extension family and refuse old/unsupported
  state before effects. Explicit-consent recovery preserves the admitted journal
  file identity through replay and removal. Unknown optional catalog content and
  exact lock bytes survive selected-record capture.
- Registration writes explicit local developer provenance without inventing
  readiness or publisher authentication. It retains the verified package shared
  lease through its journal and capture. Capture validates the actual host
  platform and current inventory under the state lock.
- An already acquired record keeps its package lifetime. A later acquisition
  from an unpublished record requires unchanged expected current exports and
  refuses after replacement. Retained published-generation authority is not
  implemented by this API.
- Windows contended lock acquisition uses owned OVERLAPPED requests and targeted
  cancellation. Kernel completion and cancellation-registration settlement
  precede request-memory/handle release. Public installation operations return
  structured `extension.cancelled` refusals. The lower-level lease task preserves
  cancellation exceptions. Unix uses cancellable nonblocking flock retries and
  remains unexecuted in this checkpoint.

## Validation

The final reviewed Windows installer assembly passed **292 executed tests, seven
Unix-only ignores, zero failures and zero errors**, both before and after pinned
Fantomas 5.2.4 formatting. The last Release no-restore build had zero errors and
four existing upstream target-framework support warnings. All 91 baseline
lock/config files and eight protected earlier sources remained unchanged during
that repair cycle.

The 17-file tested-source manifest has SHA-256
`e07794a8f421e2fb35a9fc4aef805a52972853ed836ba36bde5f30fc5bb9db80`.
Its 198-file runtime closure has SHA-256
`609bbb98656995ae963672048339b65ccfca7cefa9f5c576893696e65f34ff80`.
The implementation and test-output copies match; both and the test DLL have
typed timestamps inside the successful final build interval,
2026-11-12 04:07:28.067Z through 04:07:43.612Z.

Independent read-only security/correctness and interop reviews cleared the
platform, journal identity, decoding boundary, empty-directory and cancellable
lease repairs, then the complete executable declaration, uppercase archive
suffix and public observation documentation repairs. Reviewers inspected source
and saved evidence without rerunning project code.

The reviewed public surface was then consumed by a fresh C# Release/JIT build.
It passed **34 deterministic output controls**: the exact original 27 and seven
additional package/state controls. The additional fixture publishes actual ZIP
bytes, checks the manifest and full verification, acquires/disposes an activation,
registers explicit local unchecked state, checks copied exports and current
capture, observes structured cancellation, and rejects old-current acquisition
after replacement while its already held owner stays valid. The fixture is not
an executable-provider or readiness test. The C# source is SHA-256
`a05591b47ff86e1277a37c708897891d068b17c3f8836cbd2f60d02a96a41a3c`.
The C# run's 65-file runtime closure is separately recorded; it is not the
installer test closure. Its release receipt is SHA-256
`2991e53aa723682c80804d6689a07189b7e3568bccf918769750b7c827a99b12`.

Raw stdout/stderr, typed build/run metadata, source/closure manifests and release
inventories are retained in the ignored evidence area. No scratch bundle is part
of this commit.

## Failure history and limits

The archive suffix RED was 14 run, 11 passed and three failed. The executable
declaration RED was 13 run, nine passed and four errored through task-wrapped
Expecto assertion exceptions. These retain their actual classifications. Earlier
compiler/setup diagnostics and all five prior defect reproductions are retained.

The first new C# run failed in read-only-manifest fixture cleanup, masking its
primary exception. Corrected fixture cleanup exposed an incorrect probe
expectation that public cancellation throws. The probe was corrected to the
existing structured refusal contract. Both failed runs remain preserved; no
production behavior changed for these probe corrections. The successful final
run has empty stderr. Earlier C# evidence remains separate.

Tar-gzip extraction, repair and collection, full corpus consumption, public
installation commands, immutable published generations, workflow readiness and
exact portable-lock/offline restore remain unfinished. New package/state Native
AOT and Unix behavior are not qualified here. Hosted/native offerings and
acquired release artifacts still need their own exact evidence. No OpenSpec
completion checkbox or full-delivery claim follows from this checkpoint.

## Current-main reconciliation

The reviewed increment was checkpointed normally as
`b0955f7a5b65c41ffd72515413aefff7c38afe28`, then main
`3da850d7b518d6cb9590d5cdb71f1a9c2f2b1a92` was merged normally as
`af71e6e06174eec0659301cb7537e9c68f1d6cd5`. All 22 owned source/test/doc Git
blobs were preserved by that merge.

The first locked restore refused stale central-transitive Primitives requests.
Machine regeneration changed exactly the installer, installer-tests and probe
lockfiles, and only `Microsoft.Extensions.Primitives` requested/resolved version
10.0.14 to 10.0.15 and its content hash, matching main's unchanged CPM. No new
version was selected. ILLink and FSharp.Core stayed unchanged. Both targeted
locked restores then passed.

Fresh isolated Release no-restore builds of the test assembly and C# probe each
reported zero errors and four existing support warnings. The merged-source
installer suite again passed 292 with seven Unix-only ignores and no failures
or errors. The C# run passed the same exact 34 output lines with empty stderr.
Both separate output directories contain implementation SHA-256
`d519b5a54699f8debf58c6ab889a7f1238dd16975b0d92135a44c3d482586135`.
Test DLL SHA-256 is
`a8b92b6d4f5339a2022a17f42a4c7e1bf329a0e8b8427e16197bd6cae43302bf`;
C# probe DLL SHA-256 is
`e493ce6aad987e2436b895822c7f62e05e9676347d42f3eb61b543fd5916ab6c`.
Their raw typed timestamp bindings and full closures are retained separately.

These artifacts were built at `af71e6e0` with the three declared lock
normalizations, before the subsequent evidence/lock commit. A receipt-only
timestamp comparison initially rounded fractional seconds after automatic JSON
date conversion. Rechecking exact ISO strings against typed UTC timestamps
confirmed freshness without rebuilding or changing raw build metadata.
Release at 2026-11-12 05:13:48.386Z records no remaining owned roots and an empty
informational managed/compiler process inventory. No broader offering,
platform, release-artifact or installed-provider qualification follows.
