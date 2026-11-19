#Requires -Version 7
<#
.SYNOPSIS
    Task 5.2 of add-wasm-extension-host: pack each offering on this machine's platform, run the black-box tests
    against it, run the crafted wasm-host corpus through the packaged executable, and record the results.

.DESCRIPTION
    For each offering, in turn:
      1. PackRelease (the repository's target) into <WorkDirectory>/release.
      2. TestE2E for the offering (scripts/run-e2e-tests.cs), which installs the packed artifact into a new directory
         whose path has a space and runs each group of the black-box tests, the packaged feature's engine scenarios
         included.
      3. Installs the artifact itself, the same way: an archive extracted into a directory whose path has a space, the
         tool package with dotnet tool install --tool-path from a local feed. Then 'mck wasm-host run' over
         conformance/suites/wasm-host with the packaged executable as runner and host, and 'report-check' with
         --expect-offering and --expect-platform.
      4. On Linux, the highest GLIBC_x.y that the launch executable and the engine library need (readelf -V).

    Writes <WorkDirectory>/results/<rid>-<offering>.json for each offering, results.json, summary.md (appended to the
    GitHub job summary when there is one) and the logs. A failing offering does not stop the others. The script exits
    with 0 when every result was written; the workflow's last step reads failed.txt and fails the job after the
    results are uploaded.

    These are runs on a machine with the .NET SDK installed (a GitHub runner), not clean-machine runs.

.EXAMPLE
    pwsh scripts/wasm-offerings.ps1 -Rid win-arm64 -WorkDirectory ../scratch/offerings -Offerings native-aot,self-contained
#>
param(
    [Parameter(Mandatory)] [ValidateSet('linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64', 'win-x64', 'win-arm64')] [string] $Rid,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [ValidateSet('native-aot', 'self-contained', 'framework-dependent', 'dotnet-tool')] [string[]] $Offerings =
        @('native-aot', 'self-contained', 'framework-dependent', 'dotnet-tool'),
    [string] $RunnerLabel = '',
    [string] $Configuration = 'Release',
    # Diagnosis for win-x64 only: whether the launch executable is marked CET-compatible, and the corpus again from a copy
    # whose launch executable has the mark cleared (editbin /CETCOMPAT:NO). Recorded, never counted as a pass or failure.
    [switch] $DiagnoseCetCompat
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null
$WorkDirectory = (Resolve-Path $WorkDirectory).Path
$release = Join-Path $WorkDirectory 'release'
$installs = Join-Path $WorkDirectory 'installs'
$logs = Join-Path $WorkDirectory 'logs'
$results = Join-Path $WorkDirectory 'results'
foreach ($directory in @($release, $installs, $logs, $results)) { New-Item -ItemType Directory -Force $directory | Out-Null }

$kit = 'conformance/suites/wasm-host'
$wasmtime = ([xml](Get-Content (Join-Path $root 'Directory.Packages.props'))).Project.ItemGroup.PackageVersion |
    Where-Object Include -eq 'Wasmtime' | ForEach-Object Version
$commit = (& git -C $root rev-parse HEAD)
$isWindowsRid = $Rid.StartsWith('win')
$isLinuxRid = $Rid.StartsWith('linux')

function Get-RunnerImage {
    if ($env:ImageOS -or $env:ImageVersion) { return "$($env:ImageOS) $($env:ImageVersion)".Trim() }
    return [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
}

# The repository's build script, in a child process: build.ps1 on Windows, build.sh elsewhere. Output goes to $Log.
function Invoke-Build([string[]] $Arguments, [string] $Log, [hashtable] $Environment = @{}) {
    $saved = @{}
    foreach ($key in $Environment.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, $Environment[$key]) }
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $root
    try {
        Write-Host "build $($Arguments -join ' ')"
        if ($IsWindows) { & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'build.ps1') @Arguments *> $Log }
        else { & bash (Join-Path $root 'build.sh') @Arguments *> $Log }
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
    Get-Content $Log -Tail 12 | ForEach-Object { Write-Host "  $_" }
    [pscustomobject]@{ ExitCode = $exitCode; Seconds = [math]::Round($clock.Elapsed.TotalSeconds) }
}

# A log line without its colours and without a GitHub annotation prefix such as '::warning::'.
function Get-PlainLines([string] $Log) {
    Get-Content $Log | ForEach-Object { ($_ -replace '\x1b\[[0-9;]*m', '') -replace '^::\w+[^:]*::', '' }
}

# The distinct IL warnings of a publish log. The build prints each warning more than once, with different prefixes.
function Get-IlWarnings([string] $Log) {
    @(Get-PlainLines $Log | ForEach-Object {
            if ($_ -match '(?<where>\S+\(\d+,\d+\)|\S+\.dll|ILC)\s*:\s*warning (?<code>IL\d{4}):(?<message>[^\[]*)') {
                "$($matches.where): $($matches.code):$($matches.message.TrimEnd())"
            }
        } | Sort-Object -Unique)
}

# The test totals of a TestE2E log: the sum of the 'Test run summary' of each group of the black-box tests.
function Get-TestTotals([string] $Log) {
    $totals = [ordered]@{ total = $null; succeeded = $null; failed = $null; skipped = $null }
    foreach ($line in Get-PlainLines $Log) {
        if ($line -match '\b(?<name>total|succeeded|failed|skipped):\s*(?<count>\d+)\s*$') { $totals[$matches.name] = [int]$totals[$matches.name] + [int]$matches.count }
    }
    return $totals
}

# The highest GLIBC_x.y that an ELF file needs, from its version needs (readelf -V).
function Get-GlibcFloor([string] $File) {
    $versions = @(& readelf -V -W $File 2>$null | ForEach-Object { if ($_ -match 'Name: GLIBC_(\d+\.\d+(\.\d+)?)') { $matches[1] } } | Sort-Object -Unique)
    if ($versions.Count -eq 0) { return $null }
    return 'GLIBC_' + ($versions | Sort-Object { [version]$_ } | Select-Object -Last 1)
}

function Find-MsvcTool([string] $Name) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    & $vswhere -latest -products * -find "VC/Tools/MSVC/**/bin/Hostx64/x64/$Name" | Select-Object -First 1
}

# The CET diagnosis of one installed artifact; see -DiagnoseCetCompat.
function Get-CetDiagnosis([string] $Offering, [object] $Installed) {
    $dumpbin = Find-MsvcTool 'dumpbin.exe'; $editbin = Find-MsvcTool 'editbin.exe'
    if (-not $dumpbin -or -not $editbin) { return [ordered]@{ problem = 'dumpbin or editbin not found' } }
    $marked = [bool](& $dumpbin /nologo /headers $Installed.Launch | Select-String 'CET compatible')
    $copy = Join-Path $installs "$Offering cet off"
    if (Test-Path $copy) { Remove-Item -Recurse -Force $copy }
    Copy-Item -Recurse $Installed.Directory $copy
    $launch = Join-Path $copy ([System.IO.Path]::GetRelativePath($Installed.Directory, $Installed.Launch))
    $editbinOutput = @(& $editbin /nologo /CETCOMPAT:NO $launch 2>&1 | ForEach-Object { "$_" })
    $editbinExitCode = $LASTEXITCODE
    $cleared = -not [bool](& $dumpbin /nologo /headers $launch | Select-String 'CET compatible')
    $report = Join-Path $results "$Rid-$Offering-cet-off-wasm-host-report.json"
    $run = Invoke-Json $launch @('mck', 'wasm-host', 'run', '--kit', $kit, '--host', $launch, '--offering', $Offering,
        '--platform', $Rid, '--engine', "wasmtime $wasmtime", '--report', $report) "$Offering-cet-off-corpus-run"
    Remove-Item -Recurse -Force $copy
    [ordered]@{ launchMarkedCetCompatible = $marked; editbinExitCode = $editbinExitCode; editbinOutput = $editbinOutput; markCleared = $cleared; corpusExitCode = $run.ExitCode; corpusTotals = $run.Json.totals }
}

function Install-Artifact([string] $Offering, [object] $Launch) {
    $directory = Join-Path $installs "$Offering install"
    if (Test-Path $directory) { Remove-Item -Recurse -Force $directory }
    New-Item -ItemType Directory $directory | Out-Null
    $archive = Join-Path $release $Launch.artifact
    if ($Offering -eq 'dotnet-tool') {
        $feed = New-Item -ItemType Directory -Force (Join-Path $installs 'tool feed')
        Copy-Item $archive $feed
        $config = Join-Path $installs 'nuget.tool.config'
        "<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear /><add key=`"morphir-release`" value=`"$([System.Security.SecurityElement]::Escape($feed.FullName))`" /></packageSources></configuration>" |
            Set-Content $config
        $saved = $env:NUGET_PACKAGES
        $env:NUGET_PACKAGES = (New-Item -ItemType Directory -Force (Join-Path $installs 'tool nuget packages')).FullName
        try {
            & dotnet tool install Morphir.Tool --version $Launch.release --tool-path $directory --configfile $config *> (Join-Path $logs "$Rid-$Offering-install.log")
            if ($LASTEXITCODE -ne 0) { throw "dotnet tool install exited with $LASTEXITCODE" }
        }
        finally { $env:NUGET_PACKAGES = $saved }
        $id = 'morphir.tool'; $version = $Launch.release.ToLowerInvariant()
        return [pscustomobject]@{
            Directory    = $directory
            Launch       = Join-Path $directory ($isWindowsRid ? 'dotnet-morphir.exe' : 'dotnet-morphir')
            ArtifactRoot = Join-Path $directory ".store/$id/$version/$id/$version"
        }
    }
    if ($archive.EndsWith('.zip')) { Expand-Archive $archive $directory }
    else {
        & tar -xzf $archive -C $directory
        if ($LASTEXITCODE -ne 0) { throw "tar exited with $LASTEXITCODE" }
    }
    return [pscustomobject]@{ Directory = $directory; Launch = Join-Path $directory $Launch.launchPath; ArtifactRoot = $directory }
}

function Invoke-Json([string] $Executable, [string[]] $Arguments, [string] $Name) {
    $stdout = Join-Path $logs "$Rid-$Name.out.json"
    $stderr = Join-Path $logs "$Rid-$Name.err.txt"
    Push-Location $root
    try { & $Executable @Arguments 2> $stderr | Set-Content $stdout; $exitCode = $LASTEXITCODE }
    finally { Pop-Location }
    $json = $null
    try { $json = Get-Content $stdout -Raw | ConvertFrom-Json } catch { }
    [pscustomobject]@{ ExitCode = $exitCode; Json = $json }
}

function Test-Offering([string] $Offering) {
    $result = [ordered]@{
        task             = 'add-wasm-extension-host 5.2'
        platform         = $Rid
        runnerLabel      = $RunnerLabel
        runnerImage      = Get-RunnerImage
        osDescription    = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        machine          = 'runner with the .NET SDK installed; not a clean machine'
        commit           = $commit
        offering         = $Offering
        wasmtimePackage  = $wasmtime
        artifact         = $null
        archiveBytes     = $null
        fileCount        = $null
        extractedBytes   = $null
        files            = @()
        engines          = $null
        pack             = $null
        ilWarnings       = $null
        e2e              = $null
        corpus           = $null
        reportCheck      = $null
        glibc            = $null
        cetDiagnosis     = $null
        failures         = [System.Collections.Generic.List[string]]::new()
    }

    # 1. Pack.
    $packArguments = @('--target', 'PackRelease', '--deployment', $Offering, '--configuration', $Configuration, '--release-dir', $release)
    if ($Offering -ne 'dotnet-tool') { $packArguments += @('--rid', $Rid) }
    $packLog = Join-Path $logs "$Rid-$Offering-pack.log"
    $pack = Invoke-Build $packArguments $packLog
    $result.pack = [ordered]@{ exitCode = $pack.ExitCode; seconds = $pack.Seconds }
    if ($Offering -eq 'native-aot') {
        $warnings = Get-IlWarnings $packLog
        $result.ilWarnings = [ordered]@{ count = $warnings.Count; lines = [object[]]$warnings }
    }
    if ($pack.ExitCode -ne 0) {
        $result.failures.Add("PackRelease exited with $($pack.ExitCode)")
        return $result
    }

    $launchFile = Get-ChildItem $release -Filter '*.build-launch.json' | Where-Object {
        $launch = Get-Content $_.FullName -Raw | ConvertFrom-Json
        $launch.deployment -eq $Offering -and ($launch.platform -eq $Rid -or $launch.platform -eq 'any')
    } | Select-Object -First 1
    $launch = Get-Content $launchFile.FullName -Raw | ConvertFrom-Json
    $contents = Get-Content (Join-Path $release "$($launch.artifact).build-contents.json") -Raw | ConvertFrom-Json
    $result.artifact = $launch.artifact
    $result.archiveBytes = $contents.archiveBytes
    $result.fileCount = $contents.fileCount
    $result.extractedBytes = $contents.extractedBytes
    $result.files = [object[]]@($contents.files)
    $result.engines = $null -eq $contents.engines ? $null : [object[]]@($contents.engines)

    # 2. Each group of the black-box tests on the offering. The black-box project was built once before the first offering.
    $e2eLog = Join-Path $logs "$Rid-$Offering-e2e.log"
    $e2e = Invoke-Build @('--target', 'TestE2E', '--skip', '--executable-type', $Offering, '--configuration', $Configuration) $e2eLog @{ MORPHIR_RELEASE_DIR = $release }
    $result.e2e = Get-TestTotals $e2eLog
    $result.e2e.exitCode = $e2e.ExitCode
    $result.e2e.seconds = $e2e.Seconds
    if ($e2e.ExitCode -ne 0) { $result.failures.Add("TestE2E exited with $($e2e.ExitCode)") }

    # 3. The crafted corpus through the packaged executable.
    try {
        $installed = Install-Artifact $Offering $launch
        $report = Join-Path $results "$Rid-$Offering-wasm-host-report.json"
        $run = Invoke-Json $installed.Launch @('mck', 'wasm-host', 'run', '--kit', $kit, '--host', $installed.Launch, '--offering', $Offering,
            '--platform', $Rid, '--engine', "wasmtime $wasmtime", '--report', $report) "$Offering-corpus-run"
        $result.corpus = [ordered]@{ exitCode = $run.ExitCode; hostSha256 = $run.Json.hostSha256; reportSha256 = $run.Json.reportSha256; totals = $run.Json.totals }
        $totals = $run.Json.totals
        if ($run.ExitCode -ne 0 -or -not $totals -or $totals.cases -ne 59 -or $totals.passed -ne 49 -or $totals.notApplicable -ne 10 -or
            $totals.failed -ne 0 -or $totals.error -ne 0 -or $totals.refused -ne 0 -or $totals.notEnforced -ne 0) {
            $result.failures.Add("mck wasm-host run: exit $($run.ExitCode), totals $($totals | ConvertTo-Json -Compress); expected 59 cases, 49 passed, 10 not applicable")
        }
        $check = Invoke-Json $installed.Launch @('mck', 'wasm-host', 'report-check', '--kit', $kit, '--report', $report,
            '--expect-offering', $Offering, '--expect-platform', $Rid) "$Offering-corpus-check"
        $result.reportCheck = [ordered]@{ exitCode = $check.ExitCode; valid = $check.Json.valid; problems = $check.Json.problems; totals = $check.Json.totals }
        if ($check.ExitCode -ne 0 -or $check.Json.valid -ne $true) { $result.failures.Add("report-check: exit $($check.ExitCode), valid $($check.Json.valid)") }
        if ($DiagnoseCetCompat -and $Rid -eq 'win-x64') { $result.cetDiagnosis = Get-CetDiagnosis $Offering $installed }

        # 4. On Linux, the glibc that the launch executable and this platform's engine library need.
        if ($isLinuxRid) {
            $needs = [ordered]@{}
            $needs[[System.IO.Path]::GetRelativePath($installed.Directory, $installed.Launch).Replace('\', '/')] = Get-GlibcFloor $installed.Launch
            foreach ($engine in @($contents.engines | Where-Object { $_.delivery -eq 'separate-library' -and $_.platform -eq $Rid })) {
                $needs[$engine.path] = Get-GlibcFloor (Join-Path $installed.ArtifactRoot $engine.path)
            }
            $highest = @($needs.Values | Where-Object { $_ } | Sort-Object { [version]($_ -replace 'GLIBC_', '') } | Select-Object -Last 1)
            $result.glibc = [ordered]@{ highest = $highest.Count -gt 0 ? $highest[0] : $null; byFile = $needs }
        }
    }
    catch {
        $result.failures.Add("corpus: $($_.Exception.Message)")
    }
    return $result
}

$all = [System.Collections.Generic.List[object]]::new()
$e2eBuild = Invoke-Build @('--target', 'BuildE2ETests', '--configuration', $Configuration) (Join-Path $logs "$Rid-build-e2e.log")
foreach ($offering in $Offerings) {
    Write-Host "=== $Rid $offering"
    if ($e2eBuild.ExitCode -ne 0) {
        $result = [ordered]@{ platform = $Rid; runnerLabel = $RunnerLabel; offering = $offering; failures = @("BuildE2ETests exited with $($e2eBuild.ExitCode)") }
    }
    else {
        try { $result = Test-Offering $offering }
        catch { $result = [ordered]@{ platform = $Rid; runnerLabel = $RunnerLabel; offering = $offering; failures = @("the script failed: $($_.Exception.Message)") } }
    }
    $result.failures = [object[]]@($result.failures)
    $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $results "$Rid-$offering.json")
    $all.Add([pscustomobject]$result)
}

$all | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $WorkDirectory 'results.json')
$failed = @($all | Where-Object { $_.failures.Count -gt 0 } | ForEach-Object { "$($_.platform) $($_.offering): $($_.failures -join '; ')" })
# Written even when empty: the workflow's last step reads it and treats a missing file as a script that did not finish.
[System.IO.File]::WriteAllLines((Join-Path $WorkDirectory 'failed.txt'), [string[]]$failed)

function Format-Count($value) { $null -eq $value ? '' : ([long]$value).ToString('N0', [System.Globalization.CultureInfo]::InvariantCulture) }
$rows = foreach ($item in $all) {
    $engines = @($item.engines | Where-Object { $_ })
    $delivery = $engines.Count -eq 0 ? '' : (($engines | Group-Object delivery | ForEach-Object { "$($_.Name)$($_.Count -gt 1 ? " x$($_.Count)" : '')" }) -join ', ')
    $e2e = $item.e2e ? "$($item.e2e.total) total, $($item.e2e.succeeded) passed, $($item.e2e.failed) failed, $($item.e2e.skipped) skipped" : ''
    $corpus = $item.corpus.totals ? "$($item.corpus.totals.passed) passed, $($item.corpus.totals.notApplicable) n/a, $($item.corpus.totals.failed) failed of $($item.corpus.totals.cases)" : ''
    $check = $item.reportCheck ? ($item.reportCheck.valid ? 'valid' : 'invalid') : ''
    $glibc = $item.glibc ? $item.glibc.highest : ''
    $il = $item.ilWarnings ? $item.ilWarnings.count : ''
    $status = $item.failures.Count -eq 0 ? 'ok' : "**failed**: $($item.failures -join '; ')"
    "| $($item.offering) | $delivery | $(Format-Count $item.archiveBytes) | $(Format-Count $item.extractedBytes) | $e2e | $corpus | $check | $glibc | $il | $status |"
}
$markdown = @(
    "### WASM engine in the offerings: $Rid"
    ''
    "Runner ``$RunnerLabel``, image $(Get-RunnerImage). Commit ``$commit``. Wasmtime $wasmtime. A runner with the .NET SDK installed, not a clean machine."
    ''
    '| Offering | Engine delivery | Archive bytes | Extracted bytes | Packaged E2E | Corpus | report-check | glibc needed | IL warnings | Result |'
    '| --- | --- | ---: | ---: | --- | --- | --- | --- | ---: | --- |'
    $rows
) -join "`n"
$markdown | Set-Content (Join-Path $WorkDirectory 'summary.md')
if ($env:GITHUB_STEP_SUMMARY) { $markdown | Add-Content $env:GITHUB_STEP_SUMMARY }
Write-Host $markdown
exit 0
