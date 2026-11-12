#Requires -Version 7
<#
.SYNOPSIS
    The win-x64 trap diagnosis (ci/wasm-win-x64-trap, issue #563): which launch executables die when a WASM guest traps,
    for which kinds of trap, why, and what stops it. Run by .github/workflows/wasm-win-x64-trap.yml on windows-2025 and,
    as the control, on windows-11-arm. A process that dies is data: it is recorded and the script goes on.

.DESCRIPTION
    1. The machine: processor, Windows build, the system's mitigation policy, WER.
    2. Builds the spike (TrapRepro) as each launch kind, once as the SDK makes it and once with -p:CETCompat=false, each
       from a clean obj (an incremental build keeps the apphost it made before): a framework-dependent apphost, the same
       DLL through dotnet.exe, a self-contained apphost, Native AOT, and a .NET tool (its install-time shim, 'dotnet tool
       run' of a local tool, and 'dotnet traprepro').
    3. The CET and guard marks of every launch executable, dotnet.exe and wasmtime.dll (the spike's reader and dumpbin).
    4. The matrix: every launch kind by every case, with signals-based traps on and off, one process per case.
    5. The crash itself (win-x64): minidumps through WER LocalDumps, read by the spike and by cdb when it is installed,
       the Application event log, and one run under cdb.
    6. One change at a time (win-x64): the CET bit cleared on copies of the launch executables; the process mitigation
       policy (Set-ProcessMitigation) turning shadow stacks off for TrapRepro.exe and on for a copy of dotnet.exe.
    7. The product: morphir framework-dependent, the crafted corpus (mck wasm-host run) through its apphost, through
       dotnet.exe, with the engine's signals-based traps off (MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS=false) and from
       a CETCompat=false build; and the Morphir.Extensions.Wasm tests through dotnet exec and through their apphost.

    Writes <WorkDirectory>/results.json, summary.md (appended to the job summary), logs/ and dumps/.
#>
param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-arm64')] [string] $Rid,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [switch] $SkipProduct,
    # Leave the registry alone (WER LocalDumps and the mitigation experiments): for a run on a developer's machine.
    [switch] $NoSystemChanges
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$spike = $PSScriptRoot
$project = Join-Path $spike 'TrapRepro.csproj'
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null
$WorkDirectory = (Resolve-Path $WorkDirectory).Path
$builds = Join-Path $WorkDirectory 'builds'; $logs = Join-Path $WorkDirectory 'logs'; $dumps = Join-Path $WorkDirectory 'dumps'
$feed = Join-Path $WorkDirectory 'feed'
foreach ($d in @($builds, $logs, $dumps, $feed)) { New-Item -ItemType Directory -Force $d | Out-Null }
$isX64 = $Rid -eq 'win-x64'
$results = [ordered]@{ rid = $Rid; commit = (& git -C $root rev-parse HEAD); started = (Get-Date).ToUniversalTime().ToString('o') }
$summary = [System.Collections.Generic.List[string]]::new()
$started = Get-Date

function Format-Status([int] $Code) {
    $unsigned = [BitConverter]::ToUInt32([BitConverter]::GetBytes($Code), 0)
    if ($unsigned -ge [uint32]3221225472) { return ('0x{0:X8}' -f $unsigned) }
    return "$Code"
}

# Runs a process with its window hidden and its output captured, and stops its whole tree at the timeout.
function Invoke-Captured([string] $File, [string[]] $Arguments, [hashtable] $Environment = @{}, [string] $WorkingDirectory = $WorkDirectory, [int] $TimeoutSeconds = 120) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($File)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.WorkingDirectory = $WorkingDirectory
    foreach ($key in $Environment.Keys) {
        if ($null -eq $Environment[$key]) { [void]$info.Environment.Remove($key) } else { $info.Environment[$key] = [string]$Environment[$key] }
    }
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true) }
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; Status = (Format-Status $process.ExitCode); TimedOut = $timedOut; Stdout = $stdout.Result; Stderr = $stderr.Result; Seconds = [math]::Round($clock.Elapsed.TotalSeconds, 2) }
}

function Write-Log([string] $Name, [object] $Run) {
    $path = Join-Path $logs "$Name.log"
    @("exit: $($Run.Status) timed out: $($Run.TimedOut) seconds: $($Run.Seconds)", '--- stdout', $Run.Stdout, '--- stderr', $Run.Stderr) | Set-Content $path
}

function Find-Tool([string] $Pattern) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }
    & $vswhere -latest -products * -find $Pattern | Select-Object -First 1
}

$dotnet = (Get-Command dotnet).Source
$dumpbin = Find-Tool "VC/Tools/MSVC/**/bin/Host$($isX64 ? 'x64/x64' : 'arm64/arm64')/dumpbin.exe"
if (-not $dumpbin) { $dumpbin = Find-Tool 'VC/Tools/MSVC/**/bin/Hostx64/x64/dumpbin.exe' }
$cdb = @("${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\$($isX64 ? 'x64' : 'arm64')\cdb.exe", "${env:ProgramFiles}\Windows Kits\10\Debuggers\$($isX64 ? 'x64' : 'arm64')\cdb.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1

# 1. The machine.
$processor = Get-CimInstance Win32_Processor | Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$systemPolicy = (& powershell.exe -NoProfile -NonInteractive -Command 'Get-ProcessMitigation -System | Out-String -Width 200') -join "`n"
$werKey = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting'
$werDisabled = (Get-ItemProperty $werKey -ErrorAction SilentlyContinue).Disabled
$results.machine = [ordered]@{
    processor = $processor.Name; manufacturer = $processor.Manufacturer; os = "$($os.Caption) $($os.Version) build $($os.BuildNumber)"
    image = "$env:ImageOS $env:ImageVersion".Trim(); dotnet = $dotnet; dotnetVersion = (& dotnet --version)
    werDisabled = $werDisabled; werService = (Get-Service WerSvc -ErrorAction SilentlyContinue).Status.ToString()
    dumpbin = $dumpbin; cdb = $cdb
}
$systemPolicy | Set-Content (Join-Path $logs 'system-mitigation-policy.log')
$summary.Add("## WASM trap diagnosis on $Rid"); $summary.Add('')
$summary.Add("Processor: $($processor.Name). $($results.machine.os). Image $($results.machine.image). SDK $($results.machine.dotnetVersion). dumpbin: $([bool]$dumpbin). cdb: $([bool]$cdb).")
$shadowLines = @($systemPolicy -split "`n" | Select-String -Pattern 'UserShadowStack|ShadowStack' | ForEach-Object { $_.Line.Trim() })
$summary.Add(''); $summary.Add("System policy: $($shadowLines -join '; ')"); $summary.Add('')

# WER keeps a minidump of every process that dies (LocalDumps), so the fail-fast code can be read.
if (-not $NoSystemChanges) {
if ($werDisabled -eq 1) { Set-ItemProperty $werKey -Name Disabled -Value 0 }
# LocalDumps are written by the WER service, which is stopped on the runner image.
try { Start-Service WerSvc -ErrorAction Stop } catch { Write-Host "WerSvc did not start: $($_.Exception.Message)" }
$localDumps = Join-Path $werKey 'LocalDumps'
New-Item -Force $localDumps | Out-Null
New-ItemProperty $localDumps -Name DumpFolder -PropertyType ExpandString -Value $dumps -Force | Out-Null
New-ItemProperty $localDumps -Name DumpType -PropertyType DWord -Value 1 -Force | Out-Null
New-ItemProperty $localDumps -Name DumpCount -PropertyType DWord -Value 100 -Force | Out-Null
}

# 2. The builds. Each from a clean obj, because an incremental publish keeps the apphost it made before.
function Build-Spike([string] $Kind, [string] $Variant) {
    $out = Join-Path $builds "$Variant-$Kind"
    foreach ($d in @((Join-Path $spike 'obj'), (Join-Path $spike 'bin'), $out)) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
    $properties = @('-nologo', '-c', 'Release')
    if ($Variant -eq 'cet-off') { $properties += '-p:CETCompat=false' }
    $version = $Variant -eq 'cet-off' ? '0.0.2' : '0.0.1'
    $run = switch ($Kind) {
        'fd' { Invoke-Captured $dotnet (@('publish', $project, '-r', $Rid, '--self-contained', 'false', '-o', $out) + $properties) -TimeoutSeconds 900 }
        'sc' { Invoke-Captured $dotnet (@('publish', $project, '-r', $Rid, '--self-contained', 'true', '-o', $out) + $properties) -TimeoutSeconds 900 }
        'aot' { Invoke-Captured $dotnet (@('publish', $project, '-r', $Rid, '-p:PublishAot=true', '-o', $out) + $properties) -TimeoutSeconds 1200 }
        'tool' {
            $pack = Invoke-Captured $dotnet (@('pack', $project, '-p:TrapReproTool=true', "-p:Version=$version", '-o', $feed) + $properties) -TimeoutSeconds 900
            Write-Log "build-$Variant-tool-pack" $pack
            if ($pack.ExitCode -ne 0) { $pack } else {
                $global = Invoke-Captured $dotnet @('tool', 'install', 'TrapRepro.Spike', '--version', $version, '--tool-path', (Join-Path $out 'tool-path'), '--add-source', $feed) -TimeoutSeconds 300
                Write-Log "build-$Variant-tool-install" $global
                $local = Join-Path $out 'local'; New-Item -ItemType Directory -Force $local | Out-Null
                [void](Invoke-Captured $dotnet @('new', 'tool-manifest') -WorkingDirectory $local)
                $localInstall = Invoke-Captured $dotnet @('tool', 'install', 'TrapRepro.Spike', '--version', $version, '--add-source', $feed) -WorkingDirectory $local -TimeoutSeconds 300
                Write-Log "build-$Variant-tool-install-local" $localInstall
                if ($global.ExitCode -ne 0) { $global } else { $localInstall }
            }
        }
    }
    Write-Log "build-$Variant-$Kind" $run
    Write-Host "build $Variant $Kind -> $($run.Status) in $($run.Seconds) s"
    if ($run.ExitCode -ne 0) { Write-Host ($run.Stdout -split "`n" | Select-Object -Last 30 | Out-String) }
    return [ordered]@{ exitCode = $run.ExitCode; seconds = $run.Seconds; out = $out }
}

$results.builds = [ordered]@{}
foreach ($variant in @('default', 'cet-off')) {
    foreach ($kind in @('fd', 'sc', 'aot', 'tool')) { $results.builds["$variant-$kind"] = Build-Spike $kind $variant }
}
Remove-Item -Recurse -Force (Join-Path $spike 'obj'), (Join-Path $spike 'bin') -ErrorAction SilentlyContinue
$reader = Join-Path $builds 'default-fd/TrapRepro.dll'

# The launch kinds: how a user would start the program, and the executable that then runs it.
function Get-Launches([string] $Variant) {
    $b = Join-Path $builds $Variant
    $toolPath = "$b-tool/tool-path"
    $local = "$b-tool/local"
    [ordered]@{
        'apphost-fd'      = @{ File = "$b-fd/TrapRepro.exe"; Prefix = @(); Cwd = $WorkDirectory; Env = @{} }
        'dotnet-dll'      = @{ File = $dotnet; Prefix = @("$b-fd/TrapRepro.dll"); Cwd = $WorkDirectory; Env = @{} }
        'apphost-sc'      = @{ File = "$b-sc/TrapRepro.exe"; Prefix = @(); Cwd = $WorkDirectory; Env = @{} }
        'native-aot'      = @{ File = "$b-aot/TrapRepro.exe"; Prefix = @(); Cwd = $WorkDirectory; Env = @{} }
        'tool-shim'       = @{ File = "$toolPath/dotnet-traprepro.exe"; Prefix = @(); Cwd = $WorkDirectory; Env = @{} }
        'dotnet-on-path'  = @{ File = $dotnet; Prefix = @('traprepro'); Cwd = $WorkDirectory; Env = @{ PATH = "$toolPath;$env:PATH" } }
        'dotnet-tool-run' = @{ File = $dotnet; Prefix = @('tool', 'run', 'dotnet-traprepro'); Cwd = $local; Env = @{} }
    }
}

# 3. The marks.
$marks = [System.Collections.Generic.List[string]]::new()
$peFiles = [System.Collections.Generic.List[string]]::new()
foreach ($variant in @('default', 'cet-off')) {
    $launches = Get-Launches $variant
    foreach ($name in @('apphost-fd', 'apphost-sc', 'native-aot', 'tool-shim')) { if (Test-Path $launches[$name].File) { $peFiles.Add($launches[$name].File) } }
}
$peFiles.Add($dotnet)
$peFiles.Add((Join-Path $builds 'default-fd/wasmtime.dll'))
$peRun = Invoke-Captured $dotnet (@($reader, 'pe') + $peFiles)
Write-Log 'pe-marks' $peRun
$results.peMarks = @($peRun.Stdout -split "`r?`n" | Where-Object { $_ })
if ($dumpbin) {
    foreach ($file in $peFiles) {
        $headers = @(& $dumpbin /nologo /headers $file | Select-String -Pattern 'CET compatible|Dynamic base|Guard' | ForEach-Object { $_.Line.Trim() })
        $loadConfig = @(& $dumpbin /nologo /loadconfig $file | Select-String -Pattern 'Guard Flags|EH Continuation|CF Instrumented|Guard EH' | ForEach-Object { $_.Line.Trim() })
        $marks.Add("$file :: headers [$($headers -join '; ')] :: loadconfig [$($loadConfig -join '; ')]")
    }
}
$results.dumpbin = $marks
$summary.Add('### Marks'); $summary.Add('')
foreach ($line in $results.peMarks) { $summary.Add("- ``$($line.Replace($WorkDirectory, '<work>'))``") }
$summary.Add('')

# 4. The matrix.
$cases = @((Invoke-Captured $dotnet @($reader, 'cases')).Stdout -split "`r?`n" | Where-Object { $_ })
function Invoke-Case([hashtable] $Launch, [string] $Case, [bool] $Signals, [hashtable] $ExtraEnv = @{}) {
    if (-not (Test-Path $Launch.File) -or ($Launch.Prefix.Count -gt 0 -and $Launch.Prefix[0] -like '*.dll' -and -not (Test-Path $Launch.Prefix[0]))) {
        return [ordered]@{ cell = 'not built'; status = ''; result = ''; process = ''; policy = ''; seconds = 0; stderr = '' }
    }
    $arguments = $Launch.Prefix + @('case', $Case) + ($Signals ? @() : @('--signals-based-traps', 'false'))
    $environment = @{} + $Launch.Env + $ExtraEnv
    $run = Invoke-Captured $Launch.File $arguments -Environment $environment -WorkingDirectory $Launch.Cwd -TimeoutSeconds 90
    $result = @($run.Stdout -split "`r?`n" | Where-Object { $_ -like 'result:*' } | Select-Object -First 1)
    $process = @($run.Stdout -split "`r?`n" | Where-Object { $_ -like 'process:*' } | Select-Object -First 1)
    $policy = @($run.Stdout -split "`r?`n" | Where-Object { $_ -like 'user shadow stack policy:*' } | Select-Object -First 1)
    $cell = if ($run.TimedOut) { 'timeout' } elseif ($run.ExitCode -eq 0) { 'ok' } elseif ($result.Count -eq 0) { "died $($run.Status)" } else { "exit $($run.Status)" }
    [ordered]@{ cell = $cell; status = $run.Status; result = ($result -join ''); process = ($process -join '') -replace '^process: ', ''; policy = ($policy -join '') -replace '^user shadow stack policy: ', ''; seconds = $run.Seconds; stderr = ($run.Stderr -split "`r?`n" | Select-Object -First 3) -join ' | ' }
}

$results.matrix = [ordered]@{}
foreach ($variant in @('default', 'cet-off')) {
    $launches = Get-Launches $variant
    foreach ($signals in @($true, $false)) {
        $label = "$variant, signals-based traps $($signals ? 'on' : 'off')"
        $table = [ordered]@{}
        foreach ($name in $launches.Keys) {
            $row = [ordered]@{}
            foreach ($case in $cases) {
                $row[$case] = Invoke-Case $launches[$name] $case $signals
                Write-Host "$label | $name | $case -> $($row[$case].cell) $($row[$case].result)"
            }
            $table[$name] = $row
        }
        $results.matrix[$label] = $table
        $summary.Add("### $label"); $summary.Add('')
        $summary.Add('| launch | executable | shadow stack policy | ' + ($cases -join ' | ') + ' |')
        $summary.Add('| --- | --- | --- | ' + (($cases | ForEach-Object { '---' }) -join ' | ') + ' |')
        foreach ($name in $table.Keys) {
            $first = $table[$name][$cases[0]]
            $summary.Add("| $name | $(Split-Path -Leaf $first.process) | $($first.policy) | " + (($cases | ForEach-Object { $table[$name][$_].cell }) -join ' | ') + ' |')
        }
        $summary.Add('')
    }
}

# 5. The crash itself (win-x64).
# Reads the dumps WER wrote since the last call: the first two of each executable, kept for the upload; the rest go.
$seenDumps = [System.Collections.Generic.HashSet[string]]::new()
$results.dumps = [ordered]@{ count = 0; read = @() }
function Read-Dumps {
    $fresh = @(Get-ChildItem $dumps -Filter *.dmp -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Where-Object { -not $seenDumps.Contains($_.Name) })
    $results.dumps.count += $fresh.Count
    foreach ($group in ($fresh | Group-Object { $_.Name.Split('.')[0] })) {
        $index = 0
        foreach ($file in $group.Group) {
            [void]$seenDumps.Add($file.Name)
            if ($index++ -ge 2) { Remove-Item $file.FullName -Force; continue }
            $read = Invoke-Captured $dotnet @($reader, 'dump', $file.FullName)
            $entry = [ordered]@{ file = $file.Name; spike = $read.Stdout.Trim() }
            if ($cdb) {
                $analysis = Invoke-Captured $cdb @('-z', $file.FullName, '-c', '.exr -1; .ecxr; kn 25; q') -TimeoutSeconds 300
                Write-Log "cdb-$($file.BaseName)" $analysis
                $entry.cdb = @($analysis.Stdout -split "`r?`n" | Where-Object { $_ -match 'ExceptionCode|ExceptionAddress|Parameter|Subcode|FAST_FAIL|NumberParameters|^ *[0-9a-f]{2} [0-9a-f`]+ [0-9a-f`]+ ' } | Select-Object -First 40)
            }
            $results.dumps.read += $entry
        }
    }
}

if ($isX64) {
    Read-Dumps
    $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in @('Application Error', 'Windows Error Reporting') } | Select-Object -First 8)
    $results.events = @($events | ForEach-Object { "$($_.ProviderName) $($_.Id): $($_.Message)" })
    if ($cdb) {
        $launches = Get-Launches 'default'
        $live = [ordered]@{}
        foreach ($pair in @(@('apphost-fd', 'unreachable'), @('native-aot', 'unreachable'), @('apphost-fd', 'host-throws'))) {
            $launch = $launches[$pair[0]]
            $arguments = @('-c', 'sxd av; sxd ii; sxd dz; g; .lastevent; .exr -1; r rip, rsp; kn 30; q', $launch.File) + $launch.Prefix + @('case', $pair[1])
            $run = Invoke-Captured $cdb $arguments -TimeoutSeconds 300
            Write-Log "cdb-live-$($pair[0])-$($pair[1])" $run
            $live["$($pair[0]) $($pair[1])"] = @($run.Stdout -split "`r?`n" | Where-Object { $_ -match 'c0000409|Subcode|FAST_FAIL|ExceptionCode|ExceptionAddress|Parameter\[|Last event|result:|rip=|^ *[0-9a-f]{2} [0-9a-f`]+ [0-9a-f`]+ ' } | Select-Object -First 50)
        }
        $results.cdbLive = $live
    }
}

# 6. One change at a time (win-x64).
if ($isX64 -and -not $NoSystemChanges) {
    $experiments = [ordered]@{}
    $defaultLaunches = Get-Launches 'default'
    # The CET bit cleared on a copy of each launch executable, nothing else changed.
    foreach ($name in @('apphost-fd', 'apphost-sc', 'native-aot', 'tool-shim')) {
        $source = Split-Path -Parent $defaultLaunches[$name].File
        $copy = Join-Path $WorkDirectory "bit-cleared-$name"
        if (Test-Path $copy) { Remove-Item -Recurse -Force $copy }
        # The whole folder: an apphost names its DLL beside it, and a tool shim names it in the store below the tool path.
        Copy-Item -Recurse $source $copy
        $exe = Join-Path $copy (Split-Path -Leaf $defaultLaunches[$name].File)
        $patch = Invoke-Captured $dotnet @($reader, 'pe-cet', $exe, 'off')
        $launch = @{ File = $exe; Prefix = @(); Cwd = $WorkDirectory; Env = @{} }
        $experiments["bit cleared: $name"] = [ordered]@{ patch = $patch.Stdout.Trim(); unreachable = Invoke-Case $launch 'unreachable' $true; 'product-trap' = Invoke-Case $launch 'product-trap' $true }
    }
    # The mitigation policy of the image name, through Windows PowerShell's ProcessMitigations module.
    function Set-Mitigation([string] $Command) { (& powershell.exe -NoProfile -NonInteractive -Command "$Command; Get-ProcessMitigation -Name $(($Command -split ' ')[2]) | Select-Object -ExpandProperty UserShadowStack | Out-String -Width 200") -join "`n" }
    $policyOff = Set-Mitigation 'Set-ProcessMitigation -Name TrapRepro.exe -Disable UserShadowStack'
    $experiments['policy: UserShadowStack disabled for TrapRepro.exe'] = [ordered]@{
        policy = $policyOff
        'apphost-fd unreachable' = Invoke-Case $defaultLaunches['apphost-fd'] 'unreachable' $true
        'apphost-sc unreachable' = Invoke-Case $defaultLaunches['apphost-sc'] 'unreachable' $true
        'native-aot unreachable' = Invoke-Case $defaultLaunches['native-aot'] 'unreachable' $true
        'native-aot product-trap' = Invoke-Case $defaultLaunches['native-aot'] 'product-trap' $true
    }
    [void](Set-Mitigation 'Set-ProcessMitigation -Name TrapRepro.exe -Remove')
    $results.experiments = $experiments
    Read-Dumps
    $summary.Add('### One change at a time'); $summary.Add('')
    foreach ($key in $experiments.Keys) {
        $cells = @($experiments[$key].Keys | Where-Object { $_ -notin @('patch', 'policy') } | ForEach-Object { "$_ = $($experiments[$key][$_].cell) ($($experiments[$key][$_].policy))" })
        $summary.Add("- $key`: $($cells -join '; ')")
    }
    $summary.Add('')
}

# The cost of explicit checks, in the spike's memory-heavy loop.
$bench = [ordered]@{}
foreach ($signals in @($true, $false)) {
    $arguments = @($reader, 'bench') + ($signals ? @() : @('--signals-based-traps', 'false'))
    $bench[$signals ? 'on' : 'off'] = (Invoke-Captured $dotnet $arguments -TimeoutSeconds 300).Stdout.Trim()
}
$results.bench = $bench
$latency = [ordered]@{}
foreach ($signals in @($true, $false)) {
    $arguments = @($reader, 'latency') + ($signals ? @() : @('--signals-based-traps', 'false'))
    $latency[$signals ? 'on' : 'off'] = (Invoke-Captured $dotnet $arguments -TimeoutSeconds 300).Stdout.Trim()
}
$results.latency = $latency
$summary.Add('### Stop latency'); $summary.Add(''); foreach ($key in $latency.Keys) { $summary.Add("- $($latency[$key])") }; $summary.Add('')
$summary.Add("### Bench"); $summary.Add(''); foreach ($key in $bench.Keys) { $summary.Add("- $($bench[$key])") }; $summary.Add('')

# 7. The product.
if (-not $SkipProduct) {
    $product = [ordered]@{}
    $morphirProject = Join-Path $root 'src/Morphir/Morphir.csproj'
    $wasmtime = ([xml](Get-Content (Join-Path $root 'Directory.Packages.props'))).Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Wasmtime' | ForEach-Object Version
    foreach ($variant in @('default', 'cet-off')) {
        $out = Join-Path $builds "morphir-$variant"
        Remove-Item -Recurse -Force (Join-Path $root 'src/Morphir/obj'), (Join-Path $root 'src/Morphir/bin') -ErrorAction SilentlyContinue
        $arguments = @('publish', $morphirProject, '-nologo', '-c', 'Release', '-r', $Rid, '--self-contained', 'false', '-o', $out) + ($variant -eq 'cet-off' ? @('-p:CETCompat=false') : @())
        $build = Invoke-Captured $dotnet $arguments -TimeoutSeconds 1800
        Write-Log "build-morphir-$variant" $build
        $product["build $variant"] = [ordered]@{ exitCode = $build.ExitCode; seconds = $build.Seconds }
    }
    $morphir = Join-Path $builds 'morphir-default/morphir.exe'
    $morphirDll = Join-Path $builds 'morphir-default/morphir.dll'
    $morphirCetOff = Join-Path $builds 'morphir-cet-off/morphir.exe'
    $product.marks = @((Invoke-Captured $dotnet @($reader, 'pe', $morphir, $morphirCetOff)).Stdout -split "`r?`n" | Where-Object { $_ })
    $runs = [ordered]@{
        'apphost'                         = @{ Runner = $morphir; Host = @('--host', $morphir); Env = @{} }
        'dotnet.exe'                      = @{ Runner = $morphir; Host = @('--host', $dotnet, '--host-arg', $morphirDll); Env = @{} }
        'apphost, signals-based traps off' = @{ Runner = $morphir; Host = @('--host', $morphir); Env = @{ MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS = 'false' } }
        'dotnet.exe, signals-based traps off' = @{ Runner = $morphir; Host = @('--host', $dotnet, '--host-arg', $morphirDll); Env = @{ MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS = 'false' } }
        'apphost built with CETCompat=false' = @{ Runner = $morphirCetOff; Host = @('--host', $morphirCetOff); Env = @{} }
    }
    $corpus = [ordered]@{}
    foreach ($name in $runs.Keys) {
        foreach ($round in 1..2) {
            $report = Join-Path $WorkDirectory "corpus-$($name -replace '[^a-zA-Z0-9]+', '-')-$round.json"
            $arguments = @('mck', 'wasm-host', 'run', '--kit', 'conformance/suites/wasm-host') + $runs[$name].Host + @('--offering', 'framework-dependent', '--platform', $Rid, '--engine', "wasmtime $wasmtime", '--report', $report)
            $run = Invoke-Captured $runs[$name].Runner $arguments -Environment $runs[$name].Env -WorkingDirectory $root -TimeoutSeconds 1800
            Write-Log "corpus-$($name -replace '[^a-zA-Z0-9]+', '-')-$round" $run
            $json = try { $run.Stdout | ConvertFrom-Json } catch { $null }
            $failedCases = @()
            if (Test-Path $report) {
                $reportJson = Get-Content $report -Raw | ConvertFrom-Json
                $failedCases = @($reportJson.cases | Where-Object { $_.outcome -and $_.outcome -notin @('passed', 'not-applicable', 'notApplicable', 'not_applicable') } | ForEach-Object { "$($_.caseId) $($_.outcome)" })
            }
            $corpus["$name #$round"] = [ordered]@{ exitCode = $run.Status; seconds = $run.Seconds; totals = $json.totals; failed = $failedCases }
            Write-Host "corpus $name #$round -> $($run.Status) in $($run.Seconds) s: $($json.totals | ConvertTo-Json -Compress)"
        }
    }
    $product.corpus = $corpus
    # The engine's own tests, in process, through dotnet exec (as the Development workflow runs them) and through their apphost.
    $testProject = Join-Path $root 'tests/Morphir.Extensions.Wasm.Tests/Morphir.Extensions.Wasm.Tests.csproj'
    $testBuild = Invoke-Captured $dotnet @('build', $testProject, '-nologo', '-c', 'Release') -TimeoutSeconds 1800
    Write-Log 'build-wasm-tests' $testBuild
    $testDir = Join-Path $root 'tests/Morphir.Extensions.Wasm.Tests/bin/Release/net10.0'
    $tests = [ordered]@{ build = $testBuild.Status }
    foreach ($launch in @('dotnet exec', 'apphost')) {
        foreach ($signals in @($true, $false)) {
            $file = $launch -eq 'apphost' ? (Join-Path $testDir 'Morphir.Extensions.Wasm.Tests.exe') : $dotnet
            $arguments = ($launch -eq 'apphost' ? @() : @('exec', (Join-Path $testDir 'Morphir.Extensions.Wasm.Tests.dll'))) + @('--no-progress')
            $environment = $signals ? @{} : @{ MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS = 'false' }
            $run = Invoke-Captured $file $arguments -Environment $environment -WorkingDirectory $testDir -TimeoutSeconds 1800
            $key = "$launch, signals-based traps $($signals ? 'on' : 'off')"
            Write-Log "wasm-tests-$($key -replace '[^a-zA-Z0-9]+', '-')" $run
            $plain = @(($run.Stdout -replace '\x1b\[[0-9;]*m', '') -split "`r?`n")
            $tests[$key] = [ordered]@{ exitCode = $run.Status; seconds = $run.Seconds; summary = @($plain | Where-Object { $_ -match '^\s*(total|failed|succeeded|skipped|duration):|Test run summary' } | ForEach-Object { $_.Trim() }) }
            Write-Host "wasm tests $key -> $($run.Status) in $($run.Seconds) s"
        }
    }
    # The cancellation test, which failed in some runs above, alone: five times each way, through dotnet exec.
    $filter = '/*/*/ChannelTests/Cancelling_during_a_call_throws_after_the_call_has_stopped'
    foreach ($signals in @($true, $false)) {
        $outcomes = @()
        foreach ($round in 1..5) {
            $environment = $signals ? @{} : @{ MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS = 'false' }
            $run = Invoke-Captured $dotnet @('exec', (Join-Path $testDir 'Morphir.Extensions.Wasm.Tests.dll'), '--no-progress', '--treenode-filter', $filter) -Environment $environment -WorkingDirectory $testDir -TimeoutSeconds 600
            $plain = @(($run.Stdout -replace '\x1b\[[0-9;]*m', '') -split "`r?`n")
            $line = @($plain | Where-Object { $_ -match '^\s*(passed|failed) Cancelling' } | ForEach-Object { $_.Trim() }) -join ''
            $outcomes += "$($run.Status) $line"
        }
        $tests["cancellation test alone x5, signals-based traps $($signals ? 'on' : 'off')"] = [ordered]@{ exitCode = ''; seconds = ''; summary = $outcomes }
    }
    $product.tests = $tests
    $results.product = $product
    $summary.Add('### Product'); $summary.Add('')
    foreach ($line in $product.marks) { $summary.Add("- ``$($line.Replace($WorkDirectory, '<work>'))``") }
    $summary.Add(''); $summary.Add('| corpus run | exit | seconds | passed / failed | failed cases |'); $summary.Add('| --- | --- | ---: | --- | --- |')
    foreach ($key in $corpus.Keys) { $c = $corpus[$key]; $summary.Add("| $key | $($c.exitCode) | $($c.seconds) | $($c.totals.passed) / $($c.totals.failed) | $($c.failed -join ', ') |") }
    $summary.Add(''); $summary.Add('| wasm tests | exit | seconds | summary |'); $summary.Add('| --- | --- | ---: | --- |')
    foreach ($key in @($tests.Keys | Where-Object { $_ -ne 'build' })) { $t = $tests[$key]; $summary.Add("| $key | $($t.exitCode) | $($t.seconds) | $($t.summary -join '; ') |") }
    $summary.Add('')
}

if ($isX64 -and $results.dumps) {
    $summary.Add('### Crash records'); $summary.Add('')
    foreach ($d in $results.dumps.read) { $summary.Add("- $($d.spike -replace "`r?`n", ' / ')") }
    foreach ($e in $results.events | Select-Object -First 3) { $summary.Add("- event: $(($e -replace "`r?`n", ' / ').Substring(0, [Math]::Min(600, $e.Length)))") }
    $summary.Add('')
}

# Last, because it changes dotnet.exe for the whole machine: the mitigation policy of the image name dotnet.exe (a renamed
# copy refuses to run), so the process that dotnet.exe starts has shadow stacks although dotnet.exe is not marked.
if ($isX64 -and -not $NoSystemChanges) {
    $dotnetPolicy = [ordered]@{}
    $fd = "$builds/default-fd/TrapRepro.dll"
    foreach ($options in @('UserShadowStack', 'UserShadowStack,SetContextIpValidation')) {
        $policy = Set-Mitigation "Set-ProcessMitigation -Name dotnet.exe -Enable $options"
        $launch = @{ File = $dotnet; Prefix = @($fd); Cwd = $WorkDirectory; Env = @{} }
        $dotnetPolicy["enabled: $options"] = [ordered]@{
            policy = $policy
            unreachable = Invoke-Case $launch 'unreachable' $true
            'host-throws' = Invoke-Case $launch 'host-throws' $true
            'unreachable, signals-based traps off' = Invoke-Case $launch 'unreachable' $false
            'product-trap' = Invoke-Case $launch 'product-trap' $true
        }
        [void](Set-Mitigation 'Set-ProcessMitigation -Name dotnet.exe -Remove')
    }
    $results.dotnetPolicy = $dotnetPolicy
    $summary.Add('### dotnet.exe with shadow stacks turned on by policy'); $summary.Add('')
    foreach ($key in $dotnetPolicy.Keys) {
        $cells = @($dotnetPolicy[$key].Keys | Where-Object { $_ -ne 'policy' } | ForEach-Object { "$_ = $($dotnetPolicy[$key][$_].cell) ($($dotnetPolicy[$key][$_].policy))" })
        $summary.Add("- $key`: $($cells -join '; ')")
    }
    $summary.Add('')
}

if (-not $NoSystemChanges) { Remove-ItemProperty $localDumps -Name DumpFolder, DumpType, DumpCount -ErrorAction SilentlyContinue }
$results.finished = (Get-Date).ToUniversalTime().ToString('o')
$results | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $WorkDirectory 'results.json')
$summary | Set-Content (Join-Path $WorkDirectory 'summary.md')
if ($env:GITHUB_STEP_SUMMARY) { $summary | Add-Content $env:GITHUB_STEP_SUMMARY }
Write-Host "results in $WorkDirectory"
