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

## Preview.5 composed inputs

The composed family is local-toolchains `0.1.0-preview.5`, specified in the parent page
[local toolchain platforms and release bundles](https://github.com/finos/morphir/blob/main/docs/spec/toolchain/local-platforms-and-release-bundle.md)
(preview.4 in [finos/morphir#1164](https://github.com/finos/morphir/pull/1164), merged as `648ae374`; preview.5 in
[finos/morphir#1171](https://github.com/finos/morphir/pull/1171), merged as `00a48d4a`). Preview.5 differs from preview.4 only
in the release repository: a bundle's asset URLs are `https://github.com/finos/morphir/releases/download/v<version>/<name>`,
and a generation's `acquisition.repository` is `https://github.com/finos/morphir`. The candidate's `sourceRepository`
stays `https://github.com/finos/morphir-dotnet`, the repository the code is built from.
It supports `win-x64`, `win-arm64` and `osx-arm64`. The candidate, receipt and bundle
carry the platform; selection and generation bind it through their existing hashes.
A Windows candidate declares `"archive": {"format": "zip"}` and the entry point
`morphir.exe`. An `osx-arm64` candidate declares `"archive": {"format": "tar-gzip"}`
and the entry point `morphir`.
The installation path stays `store/tools/sha256/<archive-sha256>`.

The readers match each `$schema` URL as exact text. They read preview.5 only and do not
convert from preview.4, so preview.4 candidates, bundles, receipts, selections and
generations (written by 0.5.0-alpha.1) are refused, as are preview.3 ones. `select`, `show` and `run`
read only the preview.5 selection pointer. In a Home set up with preview.2, preview.3 or preview.4
they refuse with `local.setup_required`, which says to run `morphirup setup --yes` again
in that Home, and on the local-candidate route to select the new generation then.
Setup in such a Home does not read or convert the earlier files. It checks only the
location and the SHA-256 name of each earlier generation, installs preview.5 beside it,
and replaces the selection pointer (release setup selects; local setup leaves the
selection to `select`). The historical preview.1 local route keeps its separate reader
and behavior.

Default setup acquires the official release bundle and its checksums from the finos/morphir release
`v<version>`. Its origin
is `release-checksum`, not publisher-authenticated. Its completed generation
retains the exact bundle and checksum bodies and binds both digests.

For an explicitly supplied preview.5 local candidate, the producer's
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

A refused `setup`, `select` or `show` writes one local-toolchains `0.1.0-preview.5`
result, on the preview.1 route too, with `status: refused` and a `diagnostic`. The
schema requires `origin`. It is the origin of the generation that `select` names,
or of the selection that `show` reads, when that is a preview.5 document its reader
accepts. Otherwise it is `local-developer`, because every setup on these routes is
explicit local-developer setup. `run` writes its refusals to stderr only. A completed
preview.1 step still writes the preview.1 result, beside its preview.1 receipt and
selection.

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
