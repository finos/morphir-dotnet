<#
.SYNOPSIS
    Installs, runs, updates and uninstalls the probe tool from the local feed pack.ps1 made, and records each step.

.DESCRIPTION
    Nothing outside <WorkDirectory> is touched: global installs use --tool-path, the NuGet configuration names
    only the local feed, and NUGET_PACKAGES points into <WorkDirectory> (local tools and dnx keep their packages
    there). Each step's command, exit code, output and the probe's report go into <WorkDirectory>/result.json
    and a readable <WorkDirectory>/result.md. A failed step is a result; the script fails only on its own error.

.PARAMETER OlderSdks
    SDK versions (for example 8.0.4xx, 9.0.3xx) to try an install with as well, each pinned by its own
    global.json. Each must already be installed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [Parameter(Mandatory)] [string] $Machine,
    [string] $Sdk = '10.0.403',
    [string[]] $OlderSdks = @()
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

New-Item -ItemType Directory -Force $WorkDirectory | Out-Null
$WorkDirectory = (Resolve-Path $WorkDirectory).Path
$Feed = (Resolve-Path $Feed).Path
$env:NUGET_PACKAGES = Join-Path $WorkDirectory 'nuget-packages'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$config = Join-Path $WorkDirectory 'nuget.config'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="local" value="$Feed" /></packageSources>
</configuration>
"@ | Set-Content $config

# A directory per SDK, each with a global.json that selects exactly that SDK.
function New-SdkDirectory([string] $version) {
    $dir = Join-Path $WorkDirectory "sdk-$version"
    New-Item -ItemType Directory -Force $dir | Out-Null
    "{ `"sdk`": { `"version`": `"$version`", `"rollForward`": `"disable`" } }" | Set-Content (Join-Path $dir 'global.json')
    $dir
}
$sdkDirectory = New-SdkDirectory $Sdk
$exe = if ($IsWindows) { '.exe' } else { '' }
$steps = [System.Collections.Generic.List[object]]::new()

function Invoke-Step([string] $name, [string] $directory, [string] $file, [string[]] $arguments) {
    Push-Location $directory
    try {
        $output = & $file @arguments 2>&1 | ForEach-Object { "$_" }
        $exit = $LASTEXITCODE
    } catch {
        $output = @("$_")
        $exit = -1
    } finally { Pop-Location }
    $text = ($output -join "`n").Trim()
    $report = $null
    foreach ($line in $output) {
        if ($line.StartsWith('{"version"')) { $report = $line | ConvertFrom-Json -AsHashtable }
    }
    $step = [ordered]@{
        step = $name
        command = "$([IO.Path]::GetFileName($file)) $($arguments -join ' ')".Replace($WorkDirectory, '<work>').Replace($Feed, '<feed>')
        exitCode = $exit
        ok = ($exit -eq 0)
        report = $report
        output = if ($report) { $null } else { $text.Replace($WorkDirectory, '<work>').Replace($Feed, '<feed>') }
    }
    $steps.Add($step)
    Write-Host "[$Machine] $name -> exit $exit"
    if ($text) { Write-Host $text }
    $step
}

# What a tool path or the packages folder holds: the package directories, and the engine libraries in them.
function Get-Disk([string] $root) {
    if (-not (Test-Path $root)) { return [ordered]@{ exists = $false } }
    $files = @(Get-ChildItem $root -Recurse -File -Force)
    [ordered]@{
        exists = $true
        bytes = [long](($files | ForEach-Object Length) -as [long[]] | ForEach-Object -Begin { $sum = 0L } -Process { $sum += $_ } -End { $sum })
        packages = @(Get-ChildItem $root -Recurse -Directory -Force | Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
            ForEach-Object { "$($_.Parent.Name)/$($_.Name)" } | Where-Object { $_ -like 'spike.*' } | Sort-Object -Unique)
        engineLibraries = @($files | Where-Object { $_.Name -in 'wasmtime.dll', 'libwasmtime.so', 'libwasmtime.dylib' } |
            ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
        topLevel = @(Get-ChildItem $root -Force | ForEach-Object Name)
    }
}

$dotnetVersion = & { Push-Location $sdkDirectory; try { (& dotnet --version) } finally { Pop-Location } }
$hostRid = & { Push-Location $sdkDirectory; try { ((& dotnet --info) | Select-String '^\s*RID:\s*(\S+)').Matches[0].Groups[1].Value } finally { Pop-Location } }
$disk = [ordered]@{}

# 1. Global install with --tool-path: install, run, update, run, partial update, run, uninstall.
$toolPath = Join-Path $WorkDirectory 'tool-path'
$probe = Join-Path $toolPath "spike-platform-probe$exe"
$id = 'Spike.PlatformProbe'
Invoke-Step 'global install 1.0.0' $sdkDirectory 'dotnet' @('tool', 'install', $id, '--version', '1.0.0', '--tool-path', $toolPath, '--configfile', $config) | Out-Null
Invoke-Step 'global run 1.0.0' $sdkDirectory $probe @('--report') | Out-Null
$disk['global after install 1.0.0'] = Get-Disk $toolPath
Invoke-Step 'global update to 1.1.0' $sdkDirectory 'dotnet' @('tool', 'update', $id, '--version', '1.1.0', '--tool-path', $toolPath, '--configfile', $config) | Out-Null
Invoke-Step 'global run after update to 1.1.0' $sdkDirectory $probe @('--report') | Out-Null
$disk['global after update to 1.1.0'] = Get-Disk $toolPath
Invoke-Step 'global update to 1.2.0 (partial: no linux-x64 or linux-musl-x64 package)' $sdkDirectory 'dotnet' @('tool', 'update', $id, '--version', '1.2.0', '--tool-path', $toolPath, '--configfile', $config) | Out-Null
Invoke-Step 'global run after partial update' $sdkDirectory $probe @('--report') | Out-Null
$disk['global after partial update'] = Get-Disk $toolPath
Invoke-Step 'global uninstall' $sdkDirectory 'dotnet' @('tool', 'uninstall', $id, '--tool-path', $toolPath) | Out-Null
$disk['global after uninstall'] = Get-Disk $toolPath

# 2. Local install through a tool manifest.
$localDirectory = Join-Path $WorkDirectory 'local'
New-Item -ItemType Directory -Force $localDirectory | Out-Null
Copy-Item (Join-Path $sdkDirectory 'global.json') $localDirectory
Invoke-Step 'local new tool-manifest' $localDirectory 'dotnet' @('new', 'tool-manifest') | Out-Null
Invoke-Step 'local install 1.0.0' $localDirectory 'dotnet' @('tool', 'install', '--local', $id, '--version', '1.0.0', '--configfile', $config) | Out-Null
Invoke-Step 'local run 1.0.0' $localDirectory 'dotnet' @('tool', 'run', 'spike-platform-probe', '--', '--report') | Out-Null
Invoke-Step 'local update to 1.1.0' $localDirectory 'dotnet' @('tool', 'update', '--local', $id, '--version', '1.1.0', '--configfile', $config) | Out-Null
Invoke-Step 'local run after update to 1.1.0' $localDirectory 'dotnet' @('tool', 'run', 'spike-platform-probe', '--', '--report') | Out-Null
$disk['packages folder after local update to 1.1.0'] = Get-Disk $env:NUGET_PACKAGES
Invoke-Step 'local uninstall' $localDirectory 'dotnet' @('tool', 'uninstall', '--local', $id) | Out-Null
# SDK 10 writes the manifest to dotnet-tools.json at the top of the directory; earlier SDKs to .config/.
$manifestFile = Get-ChildItem $localDirectory -Recurse -Force -Filter 'dotnet-tools.json' | Select-Object -First 1
$manifest = Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json -AsHashtable
$disk['manifest after local uninstall'] = [ordered]@{
    manifest = [IO.Path]::GetRelativePath($localDirectory, $manifestFile.FullName).Replace('\', '/')
    tools = @($manifest.tools.Keys)
}

# 3. The same arrangement without musl packages: which package does this machine get through the RID graph?
$noMuslPath = Join-Path $WorkDirectory 'tool-path-nomusl'
Invoke-Step 'no-musl arrangement: global install 1.0.0' $sdkDirectory 'dotnet' @('tool', 'install', 'Spike.PlatformProbe.NoMusl', '--version', '1.0.0', '--tool-path', $noMuslPath, '--configfile', $config) | Out-Null
Invoke-Step 'no-musl arrangement: run' $sdkDirectory (Join-Path $noMuslPath "spike-platform-probe$exe") @('--report') | Out-Null
$disk['no-musl arrangement after install'] = Get-Disk $noMuslPath
Invoke-Step 'no-musl arrangement: uninstall' $sdkDirectory 'dotnet' @('tool', 'uninstall', 'Spike.PlatformProbe.NoMusl', '--tool-path', $noMuslPath) | Out-Null

# 3b. An arrangement with no package for this machine's platform (no Linux package): the `any` fallback.
$fallbackPath = Join-Path $WorkDirectory 'tool-path-fallback'
Invoke-Step 'fallback arrangement: global install 1.0.0' $sdkDirectory 'dotnet' @('tool', 'install', 'Spike.PlatformProbe.Fallback', '--version', '1.0.0', '--tool-path', $fallbackPath, '--configfile', $config) | Out-Null
Invoke-Step 'fallback arrangement: run' $sdkDirectory (Join-Path $fallbackPath "spike-platform-probe$exe") @('--report') | Out-Null
$disk['fallback arrangement after install'] = Get-Disk $fallbackPath
Invoke-Step 'fallback arrangement: uninstall' $sdkDirectory 'dotnet' @('tool', 'uninstall', 'Spike.PlatformProbe.Fallback', '--tool-path', $fallbackPath) | Out-Null

# 4. A one-shot run with dnx.
Invoke-Step 'dnx 1.1.0' $sdkDirectory 'dotnet' @('dnx', "$id@1.1.0", '--configfile', $config, '--report') | Out-Null

# 5. Older SDKs.
foreach ($older in $OlderSdks) {
    $olderDirectory = New-SdkDirectory $older
    $olderPath = Join-Path $WorkDirectory "tool-path-sdk-$older"
    Invoke-Step "SDK $older dotnet --version" $olderDirectory 'dotnet' @('--version') | Out-Null
    $install = Invoke-Step "SDK $older global install 1.0.0" $olderDirectory 'dotnet' @('tool', 'install', $id, '--version', '1.0.0', '--tool-path', $olderPath, '--configfile', $config)
    if ($install.ok) {
        Invoke-Step "SDK $older run" $olderDirectory (Join-Path $olderPath "spike-platform-probe$exe") @('--report') | Out-Null
        $disk["SDK $older after install"] = Get-Disk $olderPath
        Invoke-Step "SDK $older uninstall" $olderDirectory 'dotnet' @('tool', 'uninstall', $id, '--tool-path', $olderPath) | Out-Null
    }
}

$result = [ordered]@{
    machine = $Machine
    hostRid = $hostRid
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    sdk = $dotnetVersion
    steps = $steps
    disk = $disk
}
$result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $WorkDirectory 'result.json')

$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("## Tool platform packages: $Machine")
[void]$md.AppendLine()
[void]$md.AppendLine("SDK ``$dotnetVersion``, host RID ``$hostRid``, $($result.os).")
[void]$md.AppendLine()
[void]$md.AppendLine('| Step | Exit | Package RID run | Version | Engine files | Engine created | Note |')
[void]$md.AppendLine('| --- | --- | --- | --- | --- | --- | --- |')
foreach ($s in $steps) {
    $r = $s.report
    $ridDir = if ($r) { if ($r.baseDirectory -match '[\\/]tools[\\/]net10\.0[\\/]([^\\/]+)[\\/]?$') { $matches[1] } else { '?' } } else { '' }
    $files = if ($r) { (@($r.engineFiles) | ForEach-Object { $_.path }) -join ', ' } else { '' }
    $engine = if ($r) { if ($r.engine.created) { 'yes' } else { "no: $($r.engine.exception)" } } else { '' }
    $note = if ($s.output) { (($s.output -split "`n") | Where-Object { $_ -match '\S' } | Select-Object -Last 1).Replace('|', '/') } else { '' }
    if ($note.Length -gt 160) { $note = $note.Substring(0, 160) + '...' }
    [void]$md.AppendLine("| $($s.step) | $($s.exitCode) | $ridDir | $(if ($r) { $r.version }) | $files | $engine | $note |")
}
[void]$md.AppendLine()
[void]$md.AppendLine('| On disk | Packages | Engine libraries | Bytes |')
[void]$md.AppendLine('| --- | --- | --- | --- |')
foreach ($k in $disk.Keys) {
    $d = $disk[$k]
    if ($d.Contains('packages')) {
        [void]$md.AppendLine("| $k | $($d.packages -join ', ') | $($d.engineLibraries -join ', ') | $($d.bytes) |")
    } elseif ($d.Contains('tools')) {
        [void]$md.AppendLine("| $k | ``$($d.manifest)`` tools: $(if ($d.tools.Count) { $d.tools -join ', ' } else { 'none' }) | | |")
    } else {
        [void]$md.AppendLine("| $k | (directory absent) | | |")
    }
}
$md.ToString() | Set-Content (Join-Path $WorkDirectory 'result.md')
Write-Host $md.ToString()
# A failed step is recorded above; do not let its exit code become the script's (GitHub's pwsh wrapper exits with it).
$global:LASTEXITCODE = 0
