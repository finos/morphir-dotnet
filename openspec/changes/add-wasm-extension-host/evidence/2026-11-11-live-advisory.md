# Live dependency advisory correction

The `dotnet-scan` job on `61c214afa155ff883471dac18932d86241a7d9c6`
failed its audit with NU1903 for `Microsoft.AspNetCore.WebUtilities` 9.0.19,
[GHSA-x33c-w48q-qrgc](https://github.com/advisories/GHSA-x33c-w48q-qrgc).
[Microsoft's CVE-2026-114149 advisory](https://github.com/dotnet/announcements/issues/511)
lists standalone WebUtilities 9.0 versions through 9.0.22 as affected and
9.0.23 as patched.

The Live and Live.Tests lockfiles at that head match main
`017b37554c09279450be762b41ff1de566c34941`. This dependency was not introduced
by the WASM change. An earlier successful main scan does not establish that
main historically failed this advisory.

The coordinator assigned this narrow correction to the existing #570 owner.
The source change adds a central WebUtilities 9.0.23 version in the existing
security-fix group. Central transitive pinning was already enabled. Fun.Blazor
4.1.11 requires WebUtilities at least 9.0.19. The correction keeps that direct
dependency and its major version, with no direct WebUtilities project reference
or audit suppression.

## Validation status

The coordinator granted one bounded restore, one fresh build and the focused
component tests after acquisition released CPU2. All three commands passed on
Windows ARM64 with SDK 10.0.403:

| Command | Result | Wall time |
| --- | --- | --- |
| Targeted forced restore with all-severity audit errors | Passed | 3.25 seconds |
| Release Live.Tests build, including Live Blazor output | Passed, zero warnings/errors | 11.44 seconds |
| TUnit ComponentTests filter | 3 passed, 0 failed, 0 skipped | 2.35 seconds |

Every entry in both lockfiles was compared with the source-head lock data.
Only WebUtilities and Http.Headers changed, from 9.0.19 to 9.0.23.
WebUtilities is now `CentralTransitive` with the requested lower bound 9.0.23;
Http.Headers now requires Primitives at least 9.0.23. The existing resolved
Primitives version stays 10.0.14. No unrelated graph drift or other lockfile
change occurred.

Completed bounded validation:

1. Force-evaluate the `tests/Morphir.Live.Tests/Morphir.Live.Tests.fsproj`
   graph with NuGet audit enabled for all dependencies and NU1901 through
   NU1904 as errors. Disable parallel restore and use one MSBuild node.
2. Inspect every changed lock entry and confirm only the two intended lockfiles
   changed, with no unrelated dependency changes.
3. Build that test project in Release with no restore. This builds its referenced
   `src/Morphir.Live/Morphir.Live.fsproj` as well.
4. Run the TUnit `ComponentTests` filter from the freshly built test project,
   requiring discovery and execution of all three existing tests. The F# project
   declares TUnit and its imports include TUnit.Core and TUnit.Engine. The
   rebuilt assembly discovered and passed all three methods.

Commands used SDK 10.0.403, two processors, one project at a time, no shared
compiler or node reuse, captured hidden processes and a 240-second bound per
command with whole-tree termination and waiting on timeout. No timeout occurred.
Every owned-process inventory was empty; a final machine process inventory found
no dotnet, MSBuild, testhost, VBCSCompiler or fsi process. CPU2 was explicitly
released immediately after the focused result.
The exact-head full CVE workflow remains a final gate. This preparation does
not establish remediation of other package or runtime advisories.

Exact proposed commands, each through the bounded process wrapper:

```powershell
dotnet restore tests/Morphir.Live.Tests/Morphir.Live.Tests.fsproj --force-evaluate --disable-parallel -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904
dotnet build tests/Morphir.Live.Tests/Morphir.Live.Tests.fsproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet exec tests/Morphir.Live.Tests/bin/Release/net10.0/Morphir.Live.Tests.dll --treenode-filter '/*/*/ComponentTests/*'
```

The existing failed CI audit is the retained RED evidence. No replacement
advisory test or unrelated component behavior change is needed for this
dependency correction.

Separate stdout, stderr and process manifests are retained locally under
`.dev/out/wasm-resume/live-advisory/` with prefixes
`restore-20261111T1405247693001`, `build-20261111T1405476494208` and
`tests-20261111T1406058253952`.

The coordinator explicitly authorized this cohesive intermediate commit after
the targeted checks, under the previously recorded bounded campaign sequencing
exception. Full-suite tests, coverage, full formatting and final exact-head CI
are not established by this burst. No installed Git hook was disabled or
bypassed. The Registry formatter limitation and all WASM platform/security gates
remain as recorded in the availability integration evidence. No additional
restore, package, AOT, native engine execution or production publication ran.
