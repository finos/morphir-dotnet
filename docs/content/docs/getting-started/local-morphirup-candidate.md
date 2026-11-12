---
title: Try a local managed toolchain candidate
---

This Windows x64 development journey installs an explicitly supplied candidate
through the real standalone `morphirup` manager. It uses the whole untrimmed
self-contained managed Morphir folder, including its runtime, F# compiler service
and reference assemblies. A single executable copied from that folder will not
work. No system .NET SDK is needed by the installed host's file-input workflow.

Status: the actual standalone manager installed, selected and showed the
candidate, then compiled and generated the frozen outputs. The same installed
selection repeated successfully after acquisition inputs were withdrawn and
system .NET was hidden. Both phases matched the frozen IR and exact 112-byte F#;
all 320 installed files, receipt and selection stayed unchanged. This is a local
AMD64-artifact journey on Windows ARM64 emulation, not native physical x64,
acquired-release, production or full acceptance qualification.
Commands below follow the root-authored local candidate contract at
`docs/spec/toolchain/local-developer-candidate.md` in finos/morphir, whose
publication is being coordinated separately.
Their compile/generate options were confirmed by root against the actual
packaged executable; the harness also captures its help through the manager.
The retained evidence below binds the successful journey to exact local bytes.

## Inputs

Obtain the standalone Windows x64 manager, full candidate ZIP and its producer's
`candidate.json` sidecar. Keep the exact archive digest and producer source/build
identity with them. `local-developer` is explicit consent to run those local
bytes; a checksum does not authenticate the publisher. The candidate family is
`local-toolchains/0.1.0-preview.1`, separate from authenticated release metadata.

The candidate's raw JSON digest names its generation. Its `archive.sha256` names
the full host folder. Its `files` array binds every published file, including
runtime, FCS, backend and reference assemblies. The two identities are distinct.
Do not edit/reformat the sidecar after recording its digest.

The existing official producer command is:

```powershell
dotnet run --project build/_build.csproj --configuration Release -- --target PackRelease --deployment self-contained --rid win-x64 --release-dir .dev/local-mvp/host-release
```

This is a producer command requiring the pinned SDK, not a command the consumer
needs to install the candidate. It selects `Deployment.SelfContained` in
`build/Helpers/ReleaseProfiles.cs`, publishes `src/Morphir.Managed`, checks the
folder through the existing release layout check, and archives every file through
the existing release packer. It sets `MorphirDeployment=self-contained`,
`SelfContained=true`, `PublishTrimmed=false` and `PublishSingleFile=false`.
The legacy single-file and Native AOT targets are different offerings.

Root produced the current development ZIP with the existing `0.4.0-alpha.4`
identity at source `eb7d164bccbb24e069087a61c0dd1ffffff326e5`. The retained
build-contents sidecar discloses a dirty working tree. The ZIP is 58,042,046 bytes,
SHA-256 `bff0dbad0d70b15843237ab74f163c014e858ccc6e8aad8a7ec654176a7c1a6e`,
with 320 files and 136,932,607 extracted bytes. This is a source-built local
candidate, not a new version choice, release or authenticated acquisition.
Use the actual candidate sidecar produced alongside it, never a fabricated one.
The actual host reports the full SemVer
`0.4.0-alpha.4+eb7d164bccbb24e069087a61c0dd1ffffff326e5`. The handed-off raw
candidate sidecar has SHA-256
`89ee87439452be1a0afd294fc40af0cb2aea9d3f80b0bb15d922a964134ca037`.
The archive release label and exact host version remain separate facts.

The supplied standalone native manager was produced by the existing
`PackMorphirup` target. Its AMD64 executable is 6,822,400 bytes, SHA-256
`393b710f37066dff35443a6817034830f5d5cd190ac789310dd18fbd1f963aae`.
The supplied bundle retains this identity in EVIDENCE.json. A source checkpoint
rebuilt later has its own exact native digest and producer commit in the milestone
PR and bundle evidence; never relabel a rebuild as the same bytes. These are local artifacts;
they are not a published production release or complete acceptance evidence.

## Produce another explicit local candidate

Producer builds require the pinned SDK and the Windows native compiler/toolchain;
consumer commands below do not. Use fresh output directories. These commands
produce local bytes with their own full version, raw manifest and archive digests;
they do not reproduce an older candidate identity or publish a release.

```powershell
$ReleaseDir = Join-Path (Get-Location) 'artifacts/local-managed-candidate'
dotnet run --project build/_build.csproj --configuration Release -- --target PackRelease --deployment self-contained --rid win-x64 --release-dir $ReleaseDir
if ($LASTEXITCODE -ne 0) { throw 'Host packing failed' }
dotnet run --project build/_build.csproj --configuration Release -- --target PackMorphirup --rid win-x64 --release-assets-dir artifacts/local-manager-candidate
if ($LASTEXITCODE -ne 0) { throw 'Native manager packing failed' }
```

Author the explicit local sidecar from that exact archive and its build origin.
This writes declarations and hashes, not an independent installer or verifier.
Setup still validates the archive, complete file closure, executable and actual
installed version before it writes a launch-ready receipt.

```powershell
$Archives = @(Get-ChildItem -LiteralPath $ReleaseDir -Filter '*-self-contained-win-x64.zip' -File)
if ($Archives.Count -ne 1) { throw 'Use a fresh directory with exactly one host archive' }
$Archive = $Archives[0].FullName
$Contents = Get-Content -LiteralPath ($Archive + '.build-contents.json') -Raw | ConvertFrom-Json
$ArchiveSha = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ArchiveSha -ne $Contents.sha256) { throw 'Archive differs from its build origin sidecar' }
$PublishedHost = Join-Path $ReleaseDir 'publish/self-contained/win-x64/morphir.exe'
$Version = (& $PublishedHost --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Published host version failed' }
$Zip = [IO.Compression.ZipFile]::OpenRead($Archive)
try {
    $Files = @($Zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | Sort-Object FullName | ForEach-Object {
        $Stream = $_.Open()
        try { $Hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Stream)).ToLowerInvariant() }
        finally { $Stream.Dispose() }
        [ordered]@{ path = $_.FullName; bytes = $_.Length; sha256 = $Hash }
    })
} finally { $Zip.Dispose() }
$Entry = @($Files | Where-Object { $_.path -ceq 'morphir.exe' })
if ($Entry.Count -ne 1) { throw 'The whole-folder archive needs exactly one morphir.exe entry point' }
$Document = [ordered]@{
    '$schema' = 'https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.1/candidate.schema.json'
    origin = 'local-developer'; implementation = 'dotnet'; deployment = 'self-contained'; platform = 'win-x64'
    version = $Version; sourceCommit = $Contents.origin.commit
    buildIdentity = ('local official SelfContained build; workingTreeClean=' + $Contents.origin.workingTreeClean)
    sourceRepository = 'https://github.com/finos/morphir-dotnet'
    archive = [ordered]@{ format = 'zip'; sha256 = $ArchiveSha; bytes = (Get-Item -LiteralPath $Archive).Length }
    entryPoint = [ordered]@{ path = 'morphir.exe'; sha256 = $Entry[0].sha256 }
    files = $Files
}
$Candidate = Join-Path $ReleaseDir 'candidate.json'
if (Test-Path -LiteralPath $Candidate) { throw 'Preserve existing candidate bytes; choose a fresh output directory' }
[IO.File]::WriteAllText($Candidate, ($Document | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Get-FileHash -LiteralPath $Candidate -Algorithm SHA256
```

Keep both build sidecars with the archive and disclose the recorded dirty/clean
producer state. Selection uses the new raw JSON digest. Do not edit the frozen
manifest used by the retained milestone evidence.
## Install and select explicitly

Set these paths to the extracted candidate bundle and an empty absolute Home. The consumer needs neither this repository nor a system .NET installation.

```powershell
$Bundle = '<absolute path to the extracted candidate bundle>'
$Manager = Join-Path $Bundle 'morphirup.exe'
$Candidate = Join-Path $Bundle 'candidate.json'
$Archive = Join-Path $Bundle 'morphir-dotnet-0.4.0-alpha.4-self-contained-win-x64.zip'
$MorphirHome = Join-Path $env:TEMP ('morphir-local-' + [Guid]::NewGuid().ToString('N'))
$CandidateSha = (Get-FileHash -LiteralPath $Candidate -Algorithm SHA256).Hash.ToLowerInvariant()
$Declared = Get-Content -Raw -LiteralPath $Candidate | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Declared.archive.sha256) { throw 'Archive digest differs' }

& $Manager setup --candidate $Candidate --local-archive $Archive --home $MorphirHome --local-developer --dry-run
& $Manager setup --candidate $Candidate --local-archive $Archive --home $MorphirHome --local-developer --yes
& $Manager select --generation $CandidateSha --home $MorphirHome
& $Manager show --explain --json --home $MorphirHome
& $Manager run --home $MorphirHome -- --version
```

Check each command's exit code before continuing. Dry-run must leave Home absent
and execute no candidate code. Setup installs without selecting a new default.
Select writes the Home-local pointer explicitly. Setup/select/show report
`origin: local-developer`, the raw candidate identity and the Home-relative receipt
path. Run forwards the selected host's output and exit status.

Installed bytes live in `store/tools/sha256/<archiveSha256>/`. The exact candidate
and generated receipt live in `toolchains/generations/<candidateSha256>/`.
`toolchains/default.json` binds that receipt's path and digest. These are the root
contract's paths; the extension-specific single-file store is not used here.

## Compile the frozen source and generate F#

Capture the actual command help first. The executable, rather than an assumed
developer entry point, must accept the following options.

```powershell
& $Manager run --home $MorphirHome -- compile --help
& $Manager run --home $MorphirHome -- generate --help
$Fixture = Join-Path $Bundle 'sample'
$Project = Join-Path $env:TEMP ('morphir-pricing-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Project | Out-Null
Copy-Item -LiteralPath (Join-Path $Fixture 'Pricing.fs') -Destination (Join-Path $Project 'Pricing.fs')
$Compiled = Join-Path $Project 'compiled'
$Ir = Join-Path $Compiled 'morphir-ir.json'
$Generated = Join-Path $Project 'generated'
Push-Location -LiteralPath $Project
try {
    & $Manager run --home $MorphirHome -- compile --language fsharp --input Pricing.fs --package-name acme/demo --ir-version 4 --output compiled --out-dir out --json
    if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
    & $Manager run --home $MorphirHome -- generate --target fsharp --input compiled/morphir-ir.json --output generated --option format=false --json
    if ($LASTEXITCODE -ne 0) { throw 'Generate failed' }
} finally { Pop-Location }
Get-FileHash -LiteralPath (Join-Path $Generated 'Pricing.fs') -Algorithm SHA256
```

An earlier manager refused generate after compile created legitimate
`Home/locks/install/` state. The current manager keeps strict setup admission and
validates its owned state for run/show/select, allowing the host's separate state
to coexist. The successful test kept the same Home throughout; it did not delete
locks, redirect the host's Home or bypass identity checks.

The authority is the existing fixed workflow `case.json`, not a newly generated
expectation. Its IR comparison ignores JSON object member order. Its generated
`Pricing.fs` must match the frozen file byte for byte: exactly 112 bytes, SHA-256
`64e08313f3e57be2c18995a2e8b275db9d921341958da2c8cd960c601c066de0`.
Keep formatting off. A different result is a failing journey, not permission to
update the fixed expectation.

## Actual-executable regression and offline evidence

`LocalDeveloperToolchainTests.InstallsSelectsCompilesAndGeneratesOfflineWithoutSystemDotnet`
in the existing E2E project consumes only these explicit inputs:

```powershell
$env:MORPHIR_LOCAL_MANAGER_EXE = $Manager
$env:MORPHIR_LOCAL_CANDIDATE_MANIFEST = $Candidate
$env:MORPHIR_LOCAL_CANDIDATE_ARCHIVE = $Archive
$env:MORPHIR_LOCAL_CANDIDATE_SHA256 = $Declared.archive.sha256
$env:MORPHIR_LOCAL_E2E_REPORT = '<new empty absolute evidence directory>'
```

The harness has fixed manager command vectors and fixed output expectations.
There is no command/assertion plugin or fake manager. It uses the existing
`TestSandbox`, `ChildProcess`, packaged-artifact metadata and runtime-trace helpers.
It verifies the supplied archive against the explicit digest and existing
build-launch sidecar, starts with an empty isolated Home and copies the fixed
source into a temporary project. It captures help from the real selected host,
compiles and generates through the manager, and checks the frozen outputs.

For the repeat, it withdraws its own acquisition-input copies, retains the same
installed selection, and removes runtime environment/PATH access through the
existing hiding plan. A native Windows `where.exe` copied as `dotnet.exe` becomes
the hostile PATH/`DOTNET_HOST_PATH` guard; an explicit `--version` control must
fail. No new guard binary or framework-dependent Morphir surrogate is built.
The configured test refuses to claim runtime hiding if Windows has a registered
.NET location that the existing hiding mechanism cannot hide. It never changes
the machine registry or system installation.

Both phases require host traces showing that the selected runtime loads only
from the installed folder. The harness independently hashes the entire installed
`files` closure at installation, before the repeat and afterward, refusing extra,
missing or linked entries. The receipt and selection bytes must stay unchanged.
Blocked proxy endpoints and withdrawn inputs make acquisition unavailable to the
local command path; these controls do not claim an OS-level network sandbox.

Raw stdout/stderr, exact argument vectors, UTC child timings, runtime traces,
input sidecar, build provenance, receipt/selection bytes, whole-folder hash
observations and both fixed outputs remain in the supplied evidence directory.
Successful evidence records actual manager/host/runtime digests and OS/test
process architecture, so x64 execution under emulation is not confused with a
native x64 machine. Children are hidden, bounded, killed as a tree on timeout and
awaited through TestSupport. Temporary ownership uses awaited sandbox cleanup.

The successful run used one fresh no-restore build, zero warnings/errors, and one
exact filtered test: one passed, zero failed/skipped, 41.745 seconds. Raw build and
runner streams, UTC metadata, source/runtime closure and release inventory are
under `.dev/local-mvp/e2e-actual-batch4/`. Actual command/phase evidence is in
`.dev/local-mvp/actual-native-journey-attempt5/`, including both IR/F# outputs,
receipt/pointer bytes, three full-folder hash observations, raw stdout/stderr and
app-local runtime traces. The hostile native `dotnet --version` control exited 1;
offline show, compile and generate each exited 0.

Earlier D4D2 and FF118 successful functional journeys remain immutable in
`e2e-actual-batch2/`, `e2e-actual-batch3/` and their phase directories. The current
393B artifact follows the launch ownership and cleanup corrections. Its normal journey is verified here. The separate independent ownership/interop recheck cleared the process-tree and final-disposal corrections. Nine launch controls passed in Release and Debug with the active-configuration fixture copies verified. Those are separate evidence, not conclusions of this functional test.

Two earlier failures remain immutable under `.dev/local-mvp/e2e-actual-batch/`
and `actual-native-journey{,-attempt2}/`. One exposed a harness assumption about
help's output stream, corrected to inspect captured help across both streams.
The other exposed the Home-state integration refusal fixed in the new manager.
That earlier compile's IR was compared but not retained before cleanup. The
successful source now copies phase outputs before later checks, preserving all
existing assertions and fixed bytes. Historical failures are not relabeled as
passes, and root's separate direct-host proof is not substituted for this run.
Without explicit inputs the opt-in test is ignored, not counted as a passed
milestone. Full installer breadth, orphan sweep,
independent process providers, portable-lock restore, other offerings/platforms,
production authentication and final acceptance remain separate unfinished work.
