#Requires -Version 7
<#
.SYNOPSIS
    Task 5.1 of add-wasm-extension-host: try the static link of the Wasmtime engine into the Native AOT probe on
    this machine's platform, and record the result.

.DESCRIPTION
    Publishes tests/Morphir.Extensions.Wasm.AotProbe with -p:MorphirWasmtimeStaticLink=true. When that publish
    fails, or the executable does not run on its own, the reason is recorded and the probe is published the
    default way, with the engine's dynamic library beside it. Either way the published probe runs from a
    directory that holds only the executable, and then, for the dynamic build, beside its library.

    Writes result.json and result.md into the work directory and appends result.md to the GitHub job summary
    when there is one. A failed static link is a result, not a failure: the script exits with 1 only when the
    build that would ship (static, or dynamic after a failed static link) fails to publish or its probe fails.

    The guest is the published Python 0.6.0 extension; the probe refuses any other digest.

.EXAMPLE
    pwsh scripts/wasm-engine-static-link.ps1 -Rid win-arm64 -Guest artifacts/wasm-guests/morphir-python-binding-0.6.0.wasm -WorkDirectory ../scratch/5.1
#>
param(
    [Parameter(Mandatory)] [ValidateSet('linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64', 'win-x64', 'win-arm64')] [string] $Rid,
    [Parameter(Mandatory)] [string] $Guest,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [string] $RunnerLabel = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Guest = (Resolve-Path $Guest).Path
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null
$WorkDirectory = (Resolve-Path $WorkDirectory).Path
$probeDirectory = Join-Path $root 'tests/Morphir.Extensions.Wasm.AotProbe'
$project = Join-Path $probeDirectory 'Morphir.Extensions.Wasm.AotProbe.csproj'
$isWindowsRid = $Rid.StartsWith('win')
$exeName = 'Morphir.Extensions.Wasm.AotProbe' + ($isWindowsRid ? '.exe' : '')
$libraryName = $isWindowsRid ? 'wasmtime.dll' : ($Rid.StartsWith('osx') ? 'libwasmtime.dylib' : 'libwasmtime.so')

function Get-RunnerImage {
    if ($env:ImageOS -or $env:ImageVersion) { return "$($env:ImageOS) $($env:ImageVersion)".Trim() }
    return [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
}

function Invoke-Publish([string] $Mode) {
    $out = Join-Path $WorkDirectory "publish-$Mode"
    $log = Join-Path $WorkDirectory "publish-$Mode.log"
    foreach ($path in @($out, (Join-Path $probeDirectory 'bin'), (Join-Path $probeDirectory 'obj'))) {
        if (Test-Path $path) { Remove-Item -Recurse -Force $path }
    }

    $arguments = @('publish', $project, '-c', 'Release', '-r', $Rid, '-p:MorphirProbeAot=true', '-o', $out,
        '-nologo', '-tl:off', '-clp:Summary', '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false')
    if ($Mode -eq 'static') { $arguments += '-p:MorphirWasmtimeStaticLink=true' }
    Write-Host "dotnet $($arguments -join ' ')"
    & dotnet @arguments *> $log
    $exitCode = $LASTEXITCODE
    Get-Content $log -Tail 25 | ForEach-Object { Write-Host "  $_" }
    $lines = Get-Content $log
    [pscustomobject]@{
        Mode       = $Mode
        Directory  = $out
        Log        = $log
        ExitCode   = $exitCode
        IlLines    = @($lines | Where-Object { $_ -match '\b(warning|error) IL\d{4}' } | ForEach-Object { $_.Trim() } | Sort-Object -Unique)
        ErrorLines = @($lines | Where-Object { $_ -match '(?i)(\berror\b|undefined reference|undefined symbol|unresolved external|duplicate symbol|multiply defined)' -and $_ -notmatch '0 Error\(s\)' } |
            ForEach-Object { $_.Trim() } | Select-Object -Unique -First 40)
    }
}

function Get-Files([string] $Directory) {
    @(Get-ChildItem $Directory -Recurse -File | Sort-Object FullName | ForEach-Object {
            [pscustomobject]@{ name = [System.IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\', '/'); bytes = $_.Length }
        })
}

# What ships: the executable and any native library. Symbols (.pdb, .dbg, .dSYM) and XML documentation do not.
function Get-Shipped([object[]] $Files) {
    @($Files | Where-Object { $_.name -eq $exeName -or $_.name -match '\.(dll|so|dylib)$' })
}

function Get-Dependencies([string] $Executable) {
    try {
        if ($Rid.StartsWith('linux')) {
            return @(& readelf -d $Executable 2>&1 | Where-Object { $_ -match 'NEEDED' } | ForEach-Object { ($_ -replace '.*\[(.*)\].*', '$1').Trim() })
        }
        if ($Rid.StartsWith('osx')) {
            return @(& otool -L $Executable 2>&1 | Select-Object -Skip 1 | ForEach-Object { ($_.Trim() -split ' \(')[0] })
        }
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $dumpbin = & $vswhere -latest -products * -find 'VC/Tools/MSVC/**/bin/Host*/*/dumpbin.exe' | Select-Object -First 1
        if (-not $dumpbin) { return @('dumpbin not found: not checked') }
        return @(& $dumpbin /nologo /dependents $Executable 2>&1 | Where-Object { $_ -match '^\s+\S+\.dll\s*$' } | ForEach-Object { $_.Trim() })
    }
    catch {
        return @("not checked: $($_.Exception.Message)")
    }
}

# Runs the probe from the repository root (its --source paths are relative to it) on the executable in $Directory.
function Invoke-Probe([string] $Directory, [string] $PublishLog, [string] $Name) {
    $output = Join-Path $WorkDirectory "probe-$Name.txt"
    $probeArguments = @($Guest, '--publish-log', $PublishLog,
        '--source', 'src/Morphir.Extensions.Wasm', '--source', 'tests/Morphir.Extensions.Wasm.AotProbe',
        '--source', 'src/Morphir.Mep/Morphir.Mep.fsproj', '--source', 'Directory.Build.props',
        '--source', 'Directory.Build.targets', '--source', 'src/Directory.Build.props',
        '--source', 'Directory.Packages.props', '--source', 'build/wasmtime',
        '--bounds', 'tests/Fixtures/MepWasm/fixtures/wasm/extism-spin.wasm',
        '--sandbox', 'tests/Fixtures/MepWasm/fixtures/wasm')
    Push-Location $root
    try {
        & (Join-Path $Directory $exeName) @probeArguments *> $output
        $exitCode = $LASTEXITCODE
    }
    catch {
        "the probe did not start: $($_.Exception.Message)" | Out-File -Append $output
        $exitCode = -1
    }
    finally {
        Pop-Location
    }
    $lines = @(Get-Content $output)
    $summaryAt = [array]::IndexOf($lines, 'summary:')
    Write-Host "probe ($Name): exit $exitCode"
    $lines | Select-Object -Last 12 | ForEach-Object { Write-Host "  $_" }
    [pscustomobject]@{
        exitCode = $exitCode
        summary  = $summaryAt -ge 0 ? @($lines[($summaryAt + 1)..($lines.Count - 1)]) : @($lines | Select-Object -Last 12)
    }
}

# A directory holding only the given files from the publish directory.
function New-RunDirectory([string] $From, [string] $Name, [string[]] $Files) {
    $directory = Join-Path $WorkDirectory "run-$Name"
    if (Test-Path $directory) { Remove-Item -Recurse -Force $directory }
    New-Item -ItemType Directory $directory | Out-Null
    foreach ($file in $Files) { Copy-Item (Join-Path $From $file) $directory }
    return $directory
}

$result = [ordered]@{
    task                     = 'add-wasm-extension-host 5.1'
    platform                 = $Rid
    runnerLabel              = $RunnerLabel
    runnerImage              = Get-RunnerImage
    osDescription            = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    commit                   = (& git -C $root rev-parse HEAD)
    wasmtimePackage          = ([xml](Get-Content (Join-Path $root 'Directory.Packages.props'))).Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Wasmtime' | ForEach-Object Version
    wasmtimeCApi             = ([xml](Get-Content (Join-Path $root 'build/wasmtime/WasmtimeCApi.props'))).Project.PropertyGroup.MorphirWasmtimeCApiVersion
    staticLink               = $null
    delivery                 = $null
    shipped                  = @()
    shippedBytes             = 0
    runsWithoutEngineLibrary = $false
    probeExitCode            = $null
    probeSummary             = @()
    dependencies             = @()
    ilWarningLines           = @()
    published                = @()
    dynamic                  = $null
}

$static = Invoke-Publish 'static'
$staticRecord = [ordered]@{ succeeded = $false; publishExitCode = $static.ExitCode; reason = $null; errorLines = [object[]]@($static.ErrorLines); probeAloneExitCode = $null }
$final = $null
$finalProbe = $null
if ($static.ExitCode -ne 0) {
    $staticRecord.reason = 'the static-link publish failed; errorLines has the error lines of its log'
}
else {
    $files = Get-Files $static.Directory
    $leftOver = @($files | Where-Object { $_.name -eq $libraryName })
    $alone = New-RunDirectory $static.Directory 'static-alone' @($exeName)
    $probe = Invoke-Probe $alone $static.Log 'static-alone'
    $staticRecord.probeAloneExitCode = $probe.exitCode
    if ($leftOver.Count -gt 0) {
        $staticRecord.reason = "the static publish still put $libraryName beside the executable"
    }
    elseif ($probe.exitCode -ne 0) {
        $staticRecord.reason = "the statically linked executable, alone in a directory, exited with $($probe.exitCode); see probe-static-alone.txt"
    }
    else {
        $staticRecord.succeeded = $true
        $final = $static
        $finalProbe = $probe
        $result.delivery = 'static link'
        $result.runsWithoutEngineLibrary = $true
        $result.dependencies = [object[]]@(Get-Dependencies (Join-Path $alone $exeName))
    }
}
$result.staticLink = $staticRecord

if (-not $staticRecord.succeeded) {
    $dynamic = Invoke-Publish 'dynamic'
    $result.delivery = 'separate library'
    $result.dynamic = [ordered]@{ publishExitCode = $dynamic.ExitCode; errorLines = [object[]]@($dynamic.ErrorLines); probeAloneExitCode = $null; probeAloneSummary = @() }
    if ($dynamic.ExitCode -eq 0) {
        $final = $dynamic
        $alone = New-RunDirectory $dynamic.Directory 'dynamic-alone' @($exeName)
        $probeAlone = Invoke-Probe $alone $dynamic.Log 'dynamic-alone'
        $result.dynamic.probeAloneExitCode = $probeAlone.exitCode
        $result.dynamic.probeAloneSummary = [object[]]@($probeAlone.summary)
        $result.runsWithoutEngineLibrary = $probeAlone.exitCode -eq 0
        $beside = New-RunDirectory $dynamic.Directory 'dynamic-beside' @($exeName, $libraryName)
        $finalProbe = Invoke-Probe $beside $dynamic.Log 'dynamic-beside'
        $result.dependencies = [object[]]@(Get-Dependencies (Join-Path $beside $exeName))
    }
}

if ($final) {
    $files = Get-Files $final.Directory
    $result.published = [object[]]@($files)
    $result.shipped = [object[]]@(Get-Shipped $files)
    $result.shippedBytes = [long]($result.shipped | Measure-Object bytes -Sum).Sum
    $result.ilWarningLines = [object[]]@($final.IlLines)
}
if ($finalProbe) {
    $result.probeExitCode = $finalProbe.exitCode
    $result.probeSummary = [object[]]@($finalProbe.summary)
}

$json = $result | ConvertTo-Json -Depth 6
$json | Set-Content (Join-Path $WorkDirectory 'result.json')

$markdown = @(
    "### Wasmtime static link: $Rid"
    ''
    "Runner ``$RunnerLabel``, image $($result.runnerImage). Wasmtime package $($result.wasmtimePackage), C API $($result.wasmtimeCApi)."
    ''
    "- Delivery: **$($result.delivery)**"
    "- Static link: $($staticRecord.succeeded ? 'succeeded' : "failed: $($staticRecord.reason)")"
    "- Shipped: $(($result.shipped | ForEach-Object { "``$($_.name)`` $($_.bytes) bytes" }) -join ', ') (total $($result.shippedBytes))"
    "- Runs with no engine library beside it: $($result.runsWithoutEngineLibrary)"
    "- Probe exit status: $($result.probeExitCode)"
    "- IL lines: $($result.ilWarningLines.Count)"
    ''
    '```json'
    $json
    '```'
) -join "`n"
$markdown | Set-Content (Join-Path $WorkDirectory 'result.md')
if ($env:GITHUB_STEP_SUMMARY) { $markdown | Add-Content $env:GITHUB_STEP_SUMMARY }
Write-Host $markdown

if (-not $final -or $result.probeExitCode -ne 0) { exit 1 }
exit 0
