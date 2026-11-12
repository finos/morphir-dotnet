# Local developer toolchain candidate

The preview.1 local command path installs an explicitly supplied, source-built Windows
x64 self-contained Morphir folder. It requires the candidate manifest, its exact
ZIP and explicit `--local-developer` consent. A checksum establishes integrity
against that manifest. It does not authenticate a publisher or qualify a
production release.

The manifest and state documents use the separate local-toolchains family from
[the parent contract](https://github.com/finos/morphir/pull/1158). Existing recipe
planning, historical toolchain documents and the pure TUF client retain their
existing behavior. TUF is unused and unqualified for production authority.

## Preview.3 composed inputs

The preview.3 platform and origin contract is the parent merge
`7586a0a4b10195b0af3461639a5003026d0eaaa1`,
[published in the parent repository](https://github.com/finos/morphir/blob/7586a0a4b10195b0af3461639a5003026d0eaaa1/docs/spec/toolchain/local-platforms-and-release-bundle.md).
It supports `win-x64` and `win-arm64`. The candidate, receipt and bundle carry
the platform; selection and generation bind it through their existing hashes.
The installation path stays `store/tools/sha256/<archive-sha256>`.
Preview.1 and preview.2 retain their existing readers and behavior.

Default setup acquires the official release bundle and its checksums. Its origin
is `release-checksum`, not publisher-authenticated. Its completed generation
retains the exact bundle and checksum bodies and binds both digests.

For an explicitly supplied preview.3 local candidate, the producer's
`--toolchain-local-developer` option declares `local-developer` instead. Supply
its existing nine-role bundle and local sibling assets:

```powershell
morphirup setup --candidate candidate.json --local-archive host.zip --local-bundle bundle.json --home C:\morphir-local --local-developer --dry-run
morphirup setup --candidate candidate.json --local-archive host.zip --local-bundle bundle.json --home C:\morphir-local --local-developer --yes
morphirup select --generation <generation-sha256-from-setup> --home C:\morphir-local
morphirup run --home C:\morphir-local -- --version
```

This route reads local files without network acquisition. It uses the same
bounded transfer, complete-folder installation, installed provider session
checks and fixed compile/generate readiness path as release setup. It publishes
the completed generation without selecting it. The candidate, receipt, pointer
and result retain local-developer origin, and the generation has no acquisition
member or files. Neither a bundle URL nor a matching checksum upgrades that
origin. These command paths still require actual native artifact testing before
any claim of platform delivery.

## Preview.1 host-only inputs

For initial preview.1 setup, use an absolute, empty, isolated Home. Supply the complete official managed
folder inventory, including the bundled runtime, compiler, backend and reference
files. Setup checks the archive and every declared file before publication.
Setup admission stays strict: it refuses foreign or legacy state before any
mutation or probe and performs no cleanup, migration or adoption.

```powershell
morphirup setup --candidate candidate.json --local-archive candidate.zip --home C:\morphir-local --local-developer --dry-run
morphirup setup --candidate candidate.json --local-archive candidate.zip --home C:\morphir-local --local-developer --yes
morphirup select --generation <raw-candidate-sha256> --home C:\morphir-local --dry-run
morphirup select --generation <raw-candidate-sha256> --home C:\morphir-local
morphirup show --explain --json --home C:\morphir-local
morphirup run --home C:\morphir-local -- --version
```

Setup does not select a default. Its receipt requires an actual installed
`--version` probe matching the full candidate version, including build metadata.
Selection verifies the complete installed file inventory and publishes the
default pointer last. `use` is an alias of `select`. Both dry runs validate inputs
and existing state without creating Home, taking a writer lease, staging files,
probing an executable or publishing state.

`show --explain` reports local developer provenance and exact candidate/archive
identities on stderr and one result document on stdout. `run` uses the absolute
selected executable, forwards arguments without shell evaluation and returns its
exit status. The executable passes its OS stdin handle directly to the child;
stdout and stderr remain captured and forwarded independently. The cooperating
writer lease remains held until the complete owned Windows job reaches zero active processes and output settlement finishes. The unnamed job has kill-on-close and no breakaway. A creation-time job attribute contains the child before it can execute; an explicit handle list passes only standard input, output and error, never the job handle. Manager death therefore closes the job owner. On ordinary root exit, cancellation or output failure, the manager terminates remaining descendants and joins actual job settlement before releasing its lease. The
embedding API accepts a cancellation-cooperative input stream; arbitrary
noncooperative streams are not a supported cancellation boundary.

After setup and selection, installed runs need neither the source manifest path
nor the source archive. They read the retained generation and selected pointer.
Run checks the retained launch structure and file lengths. It does not claim
that a user-owned Home is immutable against malicious same-user mutation.
Explicit selection and object reuse check all file hashes again.

The installed host intentionally shares `MORPHIR_HOME` with the manager. For
example, compile retains target install locks under `locks/install`. Later
run, show and select validate only the manager-owned `store/tools`,
`toolchains`, `tmp/local-toolchain-*` and `.morphir-dotnet-io.lock` namespaces.
They ignore unknown root siblings, other `store` siblings and other `tmp`
siblings without traversing them or treating them as authority. Foreign entries
and links within the owned namespaces are still refused. This phase distinction
allows host locks, global configuration and catalogs to coexist without
weakening setup admission or installed object, receipt and selection checks.

Publication reuses the installer's internal directory, archive inspection and
cooperating lease boundaries through one manager-owned folder adapter. It does
not use the extension single-file layout. Incomplete private stages are not
installation authority; cleanup and recovery of abandoned stages remain future
lifecycle work.
Selection likewise prepares its bytes inside a private temporary stage and
atomically moves them to the default pointer only after validation. Cancellation
or an exception before that move leaves the previous pointer intact. Abandoned
preparation is never selection authority and does not block later commands.

The focused product-API integration covers a real full-folder candidate, actual
installed version probe, compile followed by generate, fixed IR and generated
F# bytes, shared Home siblings and refusals inside owned namespaces. The first
native journey exposed the overly strict runtime Home check after successful
compile. A corrected native candidate passed the real installed online/offline
journey. Independent review then found that its launcher waited only for the
direct child. That artifact remains historical functional evidence and must not
be distributed. The launch-ownership correction has normal-exit, owner-death,
cancellation, output-failure, argument, stdin, startup-failure and final-disposal
controls. Final disposal runs exactly once and records a secondary cleanup
failure on the original operation error. The test project copies the current
build configuration's runtime fixture beside its output, including satellite
assemblies. A replacement native artifact, repeated installed/offline journey
and independent correction review are required before handoff. An API test
alone does not establish those results.
Authenticated acquisition, durable trust, portable lock restore, external
providers and other platforms remain unfinished parts of the full lifecycle.
