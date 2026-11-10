#Requires -Version 7
<#
.SYNOPSIS
    The Linux floor of the WASM engine (add-wasm-extension-host, packaging tasks 5.2 to 5.4): what a Native AOT
    executable with Wasmtime linked in needs from a Linux machine, and whether it starts on a clean one.

.DESCRIPTION
    Publishes tests/Morphir.Extensions.Wasm.AotProbe for this machine's Linux RID several ways:

      static-runner      engine linked in (-p:MorphirWasmtimeStaticLink=true), linked on this runner
      dynamic-runner     engine as libwasmtime.so beside the executable, linked on this runner
      static-jammy       engine linked in, linked inside the build image (ubuntu:22.04 pinned by digest, glibc 2.35)
      dynamic-jammy      engine as libwasmtime.so, linked inside the build image
      static-jammy-managed-probe, static-jammy-managed-library
                         EXPERIMENT: static-jammy with SHA256.HashData replaced by BouncyCastle's managed digest in
                         the probe only, or in Morphir.Extensions.Wasm (and so the probe); see
                         scripts/wasm-engine-linux-floor/ManagedSha256.targets. No tracked file changes.

    For each it records the shared libraries the executable needs (readelf -d, ldd), the highest GLIBC_ version it
    needs and the symbols that need it, and, for the static builds, which of those symbols libwasmtime.a references.
    It records the same for the package's libwasmtime.so, and the external undefined symbols of libwasmtime.a.

    Then it runs every build on this runner, and in each clean image with no network: debian:12-slim and the build
    image, after recording what each image ships and removing OpenSSL (dpkg --purge --force-depends) in a derived
    image, as the morphirup check does, and checking that the derived image has no .NET, libicu, libssl or
    libcrypto. As a control the jammy builds also run in the build image as shipped, with its libssl3.

    Writes result.json and result.md into the work directory and appends result.md to the GitHub job summary. The
    result is the data: a failed link or a crashing probe is recorded, and the script exits 0 unless it breaks.

.EXAMPLE
    pwsh scripts/wasm-engine-linux-floor.ps1 -Rid linux-x64 -Guest $RUNNER_TEMP/guest/morphir-python-binding-0.6.0.wasm -WorkDirectory $RUNNER_TEMP/floor
#>
param(
    [Parameter(Mandatory)] [ValidateSet('linux-x64', 'linux-arm64')] [string] $Rid,
    [Parameter(Mandatory)] [string] $Guest,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [string] $RunnerLabel = '',
    # MorphirupRelease.LinuxBuildImage on main: Ubuntu 22.04, glibc 2.35, the multi-architecture index.
    [string] $BuildImage = 'ubuntu:22.04@sha256:076e7bf82319619f28dd0aa9c02ab34b055ad37100395b4f49a2ffba04fb5e11',
    [string[]] $CleanImages = @('debian:12-slim', 'ubuntu:22.04@sha256:076e7bf82319619f28dd0aa9c02ab34b055ad37100395b4f49a2ffba04fb5e11')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Guest = (Resolve-Path $Guest).Path
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null
$WorkDirectory = (Resolve-Path $WorkDirectory).Path
$probeDirectory = Join-Path $root 'tests/Morphir.Extensions.Wasm.AotProbe'
$libraryDirectory = Join-Path $root 'src/Morphir.Extensions.Wasm'
$project = Join-Path $probeDirectory 'Morphir.Extensions.Wasm.AotProbe.csproj'
$experimentTargets = Join-Path $PSScriptRoot 'wasm-engine-linux-floor/ManagedSha256.targets'
$exeName = 'Morphir.Extensions.Wasm.AotProbe'
$libraryName = 'libwasmtime.so'
$glibcFloor = [version]::new(2, 35, 0)
$owner = "$((& id -u).Trim()):$((& id -g).Trim())"
$sdk = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Split-Path (& readlink -f (Get-Command dotnet).Source) }
$sdk = (Resolve-Path $sdk).Path
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
New-Item -ItemType Directory -Force $packages | Out-Null
$packages = (Resolve-Path $packages).Path

function Write-Step([string] $Text) { Write-Host "::group::$Text" }
function End-Step { Write-Host '::endgroup::' }

function Invoke-Captured([string] $File, [string[]] $Arguments) {
    $output = & $File @Arguments 2>&1 | ForEach-Object { "$_" }
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = @($output) }
}

function Compare-GlibcVersion([string] $Name) {
    $match = [regex]::Match($Name, '^GLIBC_(\d+(?:\.\d+)+)$')
    if (-not $match.Success) { return $null }
    $version = [version]$match.Groups[1].Value
    return [version]::new($version.Major, $version.Minor, [Math]::Max($version.Build, 0))
}

function Format-Version([version] $Version) {
    if ($null -eq $Version) { return $null }
    return $Version.Build -gt 0 ? "$($Version.Major).$($Version.Minor).$($Version.Build)" : "$($Version.Major).$($Version.Minor)"
}

# The GLIBC_ and GCC_ versions in the version needs section, and the undefined dynamic symbols with their version.
function Get-ElfNeeds([string] $File) {
    $needed = @(& readelf -d -W $File 2>&1 | Where-Object { $_ -match '\(NEEDED\)' } | ForEach-Object { ($_ -replace '.*\[(.*)\].*', '$1').Trim() })
    $versionNeeds = [System.Collections.Generic.List[object]]::new()
    $inNeeds = $false
    $from = ''
    foreach ($line in (& readelf -V -W $File 2>&1)) {
        if ($line -match '^Version ') { $inNeeds = $line -match '^Version needs section'; continue }
        if (-not $inNeeds) { continue }
        if ($line -match '\bFile:\s+(\S+)') { $from = $Matches[1] }
        if ($line -match '\bName:\s+(\S+)') { $versionNeeds.Add([pscustomobject]@{ file = $from; name = $Matches[1] }) }
    }
    $imports = @(& readelf --dyn-syms -W $File 2>&1 | ForEach-Object {
            if ($_ -match '\sUND\s+([^@\s]+)@+(\S+)') { [pscustomobject]@{ symbol = $Matches[1]; version = $Matches[2] } }
        })
    $glibc = @($versionNeeds | Where-Object { Compare-GlibcVersion $_.name } | Sort-Object { Compare-GlibcVersion $_.name })
    $highest = $glibc.Count -gt 0 ? (Compare-GlibcVersion $glibc[-1].name) : $null
    $highestName = $glibc.Count -gt 0 ? $glibc[-1].name : $null
    [pscustomobject]@{
        needed           = $needed
        versionNeeds     = @($versionNeeds | ForEach-Object { "$($_.file) $($_.name)" })
        glibcHighest     = Format-Version $highest
        aboveFloor       = $null -ne $highest -and $highest -gt $glibcFloor
        symbolsAtHighest = @($imports | Where-Object version -eq $highestName | ForEach-Object symbol | Sort-Object -Unique)
        aboveFloorSymbols = @($imports | Where-Object { $v = Compare-GlibcVersion $_.version; $v -and $v -gt $glibcFloor } |
            ForEach-Object { "$($_.symbol)@$($_.version)" } | Sort-Object -Unique)
        imports          = $imports
    }
}

# The highest GLIBC_ version among the given imports, with the symbols that have it.
function Get-Highest([object[]] $Imports) {
    $glibc = @($Imports | Where-Object { Compare-GlibcVersion $_.version })
    if ($glibc.Count -eq 0) { return [pscustomobject]@{ glibcHighest = $null; symbols = @() } }
    $top = ($glibc | ForEach-Object { Compare-GlibcVersion $_.version } | Sort-Object)[-1]
    [pscustomobject]@{
        glibcHighest = Format-Version $top
        symbols      = @($glibc | Where-Object { (Compare-GlibcVersion $_.version) -eq $top } | ForEach-Object symbol | Sort-Object -Unique)
    }
}

function Get-Signal([int] $ExitCode) {
    if ($ExitCode -le 128 -or $ExitCode -gt 128 + 64) { return $null }
    $names = @{ 4 = 'SIGILL'; 6 = 'SIGABRT'; 7 = 'SIGBUS'; 8 = 'SIGFPE'; 9 = 'SIGKILL'; 11 = 'SIGSEGV'; 15 = 'SIGTERM' }
    $number = $ExitCode - 128
    return $names.ContainsKey($number) ? "$($names[$number]) ($number)" : "signal $number"
}

function Get-ProbeArguments([string] $GuestPath, [string] $PublishLog) {
    @($GuestPath, '--publish-log', $PublishLog,
        '--source', 'src/Morphir.Extensions.Wasm', '--source', 'tests/Morphir.Extensions.Wasm.AotProbe',
        '--source', 'src/Morphir.Mep/Morphir.Mep.fsproj', '--source', 'Directory.Build.props',
        '--source', 'Directory.Build.targets', '--source', 'src/Directory.Build.props',
        '--source', 'Directory.Packages.props', '--source', 'build/wasmtime',
        '--bounds', 'tests/Fixtures/MepWasm/fixtures/wasm/extism-spin.wasm',
        '--sandbox', 'tests/Fixtures/MepWasm/fixtures/wasm')
}

function Read-ProbeOutput([string] $File, [int] $ExitCode) {
    $lines = @(Get-Content $File)
    $summaryAt = [array]::IndexOf($lines, 'summary:')
    [pscustomobject]@{
        exitCode  = $ExitCode
        signal    = Get-Signal $ExitCode
        lastLines = @($lines | Select-Object -Last 12)
        summary   = $summaryAt -ge 0 ? @($lines[($summaryAt + 1)..($lines.Count - 1)]) : @()
        lineCount = $lines.Count
    }
}

# The image the jammy publishes run in: the build image with clang and the zlib headers Native AOT links with, and
# libicu and the certificates the SDK needs (the executable needs none of them), installed once.
function New-BuilderImage {
    $name = "floor-builder-$Rid"
    $container = "$name-setup"
    & docker rm -f $container *> $null
    $setup = Invoke-Captured docker @('run', '--name', $container, $BuildImage, 'sh', '-c',
        'set -eu; export DEBIAN_FRONTEND=noninteractive; apt-get update -qq; apt-get install -y -qq --no-install-recommends clang zlib1g-dev libicu70 libssl3 ca-certificates > /dev/null; ldd --version | head -n 1')
    if ($setup.ExitCode -ne 0) { throw "setting up the builder image failed: $($setup.Lines -join "`n")" }
    & docker commit $container "${name}:latest" | Out-Null
    & docker rm $container | Out-Null
    Write-Host "builder image ${name}: $($setup.Lines[-1])"
    return "${name}:latest"
}

function Invoke-Publish([string] $Name, [bool] $Static, [string] $Place, [string] $Experiment = '') {
    Write-Step "publish $Name"
    $out = Join-Path $WorkDirectory "publish-$Name"
    $log = Join-Path $WorkDirectory "publish-$Name.log"
    foreach ($path in @($out, (Join-Path $probeDirectory 'bin'), (Join-Path $probeDirectory 'obj'), (Join-Path $libraryDirectory 'bin'), (Join-Path $libraryDirectory 'obj'))) {
        if (Test-Path $path) { Remove-Item -Recurse -Force $path }
    }
    $arguments = @('publish', $project, '-c', 'Release', '-r', $Rid, '-p:MorphirProbeAot=true', '-o', $out,
        '-nologo', '-tl:off', '-clp:Summary', '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false')
    if ($Static) { $arguments += '-p:MorphirWasmtimeStaticLink=true' }
    if ($Experiment) { $arguments += @("-p:CustomAfterMicrosoftCommonTargets=$experimentTargets", "-p:MorphirExperimentManagedSha256=$Experiment") }

    $started = Get-Date
    if ($Place -eq 'runner') {
        Write-Host "dotnet $($arguments -join ' ')"
        & dotnet @arguments *> $log
        $exitCode = $LASTEXITCODE
    }
    else {
        # As MorphirupRelease publishes: the repository, this runner's SDK (read-only), the NuGet folder and the work
        # directory are mounted at their own paths; root in the container gives what it wrote back to the runner user.
        $shell = @'
set -eu
printf '[safe]\n\tdirectory = *\n' > "$HOME/.gitconfig"
echo "Publishing in $(. /etc/os-release && echo "$PRETTY_NAME"), $(ldd --version | head -n 1)"
status=0
dotnet "$@" || status=$?
chown -R "$FLOOR_OWNER" "$FLOOR_REPOSITORY" "$NUGET_PACKAGES" "$FLOOR_WORK"
exit "$status"
'@
        $docker = @('run', '--rm',
            '--volume', "${root}:${root}", '--workdir', $root,
            '--volume', "${sdk}:${sdk}:ro",
            '--volume', "${packages}:${packages}", '--env', "NUGET_PACKAGES=$packages",
            '--volume', "${WorkDirectory}:${WorkDirectory}",
            '--env', "FLOOR_OWNER=$owner", '--env', "FLOOR_REPOSITORY=$root", '--env', "FLOOR_WORK=$WorkDirectory",
            '--env', "DOTNET_ROOT=$sdk", '--env', "PATH=${sdk}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
            '--env', 'DOTNET_NOLOGO=1', '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', '--env', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1',
            $script:builderImage, 'sh', '-c', $shell, 'sh') + $arguments
        Write-Host "docker (in $BuildImage) dotnet $($arguments -join ' ')"
        & docker @docker *> $log
        $exitCode = $LASTEXITCODE
    }
    $lines = @(Get-Content $log)
    $lines | Select-Object -Last 15 | ForEach-Object { Write-Host "  $_" }
    $exe = Join-Path $out $exeName
    $record = [ordered]@{
        name            = $Name
        static          = $Static
        linkedIn        = $Place -eq 'runner' ? "runner ($RunnerLabel)" : $BuildImage
        experiment      = $Experiment ? "managed SHA-256 in $Experiment" : $null
        publishExitCode = $exitCode
        seconds         = [int]((Get-Date) - $started).TotalSeconds
        linkedOn        = @($lines | Where-Object { $_ -match '^Publishing in ' } | Select-Object -First 1)
        ilLines         = @($lines | Where-Object { $_ -match '\b(warning|error) IL\d{4}' } | ForEach-Object { $_.Trim() } | Sort-Object -Unique)
        errorLines      = @($lines | Where-Object { $_ -match '(?i)(\berror\b|undefined reference|undefined symbol|multiply defined|cannot find)' -and $_ -notmatch '0 Error\(s\)' } |
            ForEach-Object { $_.Trim() } | Select-Object -Unique -First 40)
        executableBytes = $null
        elf             = $null
        ldd             = @()
        log             = $log
        directory       = $out
    }
    if ($exitCode -eq 0 -and (Test-Path $exe)) {
        $record.executableBytes = (Get-Item $exe).Length
        $record.elf = Get-ElfNeeds $exe
        $record.ldd = @((Invoke-Captured ldd @($exe)).Lines | ForEach-Object { $_.Trim() })
        $record.leftOverLibrary = Test-Path (Join-Path $out $libraryName)
    }
    End-Step
    return [pscustomobject]$record
}

# A directory holding only the executable, and for a dynamic build the engine library beside it.
function New-RunDirectory([object] $Build) {
    $directory = Join-Path $WorkDirectory "run-$($Build.name)"
    if (Test-Path $directory) { Remove-Item -Recurse -Force $directory }
    New-Item -ItemType Directory $directory | Out-Null
    Copy-Item (Join-Path $Build.directory $exeName) $directory
    if (-not $Build.static) { Copy-Item (Join-Path $Build.directory $libraryName) $directory }
    return $directory
}

function Invoke-RunnerProbe([object] $Build, [string] $Directory) {
    $output = Join-Path $WorkDirectory "probe-$($Build.name)-runner.txt"
    Push-Location $root
    try {
        & (Join-Path $Directory $exeName) @(Get-ProbeArguments $Guest $Build.log) *> $output
        $exitCode = $LASTEXITCODE
    }
    catch {
        "the probe did not start: $($_.Exception.Message)" | Out-File -Append $output
        $exitCode = -1
    }
    finally { Pop-Location }
    $run = Read-ProbeOutput $output $exitCode
    Write-Host "probe $($Build.name) on the runner: exit $exitCode $($run.signal)"
    return $run
}

# Records what the image ships, and returns a derived image without OpenSSL, checked to have no .NET, libicu,
# libssl or libcrypto.
function Get-CleanImage([string] $Pinned, [int] $Index) {
    Write-Step "clean image $Pinned"
    & docker pull -q $Pinned | Out-Null
    $digest = (& docker image inspect --format '{{join .RepoDigests " "}}' $Pinned).Trim()
    $inventory = 'ls -d /usr/lib/*/libicu* /lib/*/libicu* /usr/lib/libicu* /usr/lib/*/libssl.so* /lib/*/libssl.so* /usr/lib/libssl.so* /usr/lib/*/libcrypto.so* /lib/*/libcrypto.so* /usr/lib/libcrypto.so* /usr/share/dotnet /usr/lib/dotnet 2>/dev/null; command -v dotnet || true'
    $packagesQuery = "dpkg-query -W -f='`${Package} `${Version} `${Status}\n' 'libssl*' 'libicu*' 'openssl' 2>/dev/null | grep 'install ok installed' | cut -d' ' -f1,2 || true"
    $shipped = @((Invoke-Captured docker @('run', '--rm', '--network', 'none', $Pinned, 'sh', '-c', $inventory)).Lines | Where-Object { $_ })
    $shippedPackages = @((Invoke-Captured docker @('run', '--rm', '--network', 'none', $Pinned, 'sh', '-c', $packagesQuery)).Lines | Where-Object { $_ })
    $glibc = ((Invoke-Captured docker @('run', '--rm', '--network', 'none', $Pinned, 'sh', '-c', 'ldd --version | head -n 1')).Lines | Select-Object -First 1)
    $os = ((Invoke-Captured docker @('run', '--rm', '--network', 'none', $Pinned, 'sh', '-c', '. /etc/os-release && echo "$PRETTY_NAME"')).Lines | Select-Object -First 1)

    $container = "floor-clean-$Rid-$Index"
    $image = "floor-clean-${Rid}:$Index"
    & docker rm -f $container *> $null
    $purge = (Invoke-Captured docker @('run', '--network', 'none', '--name', $container, $Pinned, 'sh', '-c', @'
removed=""
for package in libssl3 libssl1.1; do
    if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -q 'install ok installed'; then
        dpkg --purge --force-depends "$package" > /dev/null 2>&1
        removed="$removed $package"
    fi
done
echo "$removed"
'@)).Lines -join ' '
    & docker commit $container $image | Out-Null
    & docker rm $container | Out-Null
    $leftovers = @((Invoke-Captured docker @('run', '--rm', '--network', 'none', $image, 'sh', '-c', $inventory)).Lines | Where-Object { $_ })
    $record = [pscustomobject]@{
        image           = $Pinned
        repoDigests     = $digest
        os              = $os
        glibc           = $glibc
        ships           = $shipped
        shipsPackages   = $shippedPackages
        removed         = $purge.Trim()
        derivedImage    = $image
        cleanAfterPurge = $leftovers.Count -eq 0
        leftovers       = $leftovers
    }
    Write-Host ($record | ConvertTo-Json -Depth 4)
    End-Step
    return $record
}

function Invoke-ImageProbe([object] $Build, [string] $Directory, [object] $Image, [string] $ImageRef, [string] $Label) {
    $guestDirectory = Split-Path $Guest
    $guestName = Split-Path $Guest -Leaf
    $runName = Split-Path $Directory -Leaf
    $mounts = @('--volume', "${root}:/repo:ro", '--volume', "${WorkDirectory}:/work:ro", '--volume', "${guestDirectory}:/guest:ro")
    $publishLog = "/work/$(Split-Path $Build.log -Leaf)"
    $output = Join-Path $WorkDirectory "probe-$($Build.name)-$Label.txt"
    $arguments = @('run', '--rm', '--network', 'none') + $mounts + @('--workdir', '/repo', $ImageRef, "/work/$runName/$exeName") + (Get-ProbeArguments "/guest/$guestName" $publishLog)
    & docker @arguments *> $output
    $exitCode = $LASTEXITCODE
    $run = Read-ProbeOutput $output $exitCode
    $ldd = (Invoke-Captured docker (@('run', '--rm', '--network', 'none') + $mounts + @($ImageRef, 'ldd', "/work/$runName/$exeName"))).Lines
    Write-Host "probe $($Build.name) in $Label ($ImageRef): exit $exitCode $($run.signal)"
    $run.lastLines | ForEach-Object { Write-Host "  $_" }
    [pscustomobject]@{
        build     = $Build.name
        image     = $Image.image
        variant   = $Label
        imageRef  = $ImageRef
        lacks     = $Label -like '*as-shipped' ? 'nothing removed' : 'OpenSSL (purged), and had no .NET or libicu'
        exitCode  = $run.exitCode
        signal    = $run.signal
        lastLines = $run.lastLines
        summary   = $run.summary
        lddNotFound = @($ldd | Where-Object { $_ -match 'not found' } | ForEach-Object { $_.Trim() })
        ldd       = @($ldd | ForEach-Object { $_.Trim() })
    }
}

# ---------------------------------------------------------------------------------------------------------------

$capi = [xml](Get-Content (Join-Path $root 'build/wasmtime/WasmtimeCApi.props'))
$pin = $capi.Project.ItemGroup.MorphirWasmtimeCApiArchive | Where-Object Include -eq $Rid
$archive = Join-Path $root "artifacts/wasmtime-c-api/$($pin.Root)/$($pin.StaticLibrary)"
$result = [ordered]@{
    measurement     = 'add-wasm-extension-host: the Linux floor of the WASM engine'
    platform        = $Rid
    runnerLabel     = $RunnerLabel
    runnerImage     = "$($env:ImageOS) $($env:ImageVersion)".Trim()
    runnerOs        = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    runnerGlibc     = (& ldd --version | Select-Object -First 1)
    commit          = (& git -C $root rev-parse HEAD)
    wasmtimeCApi    = $capi.Project.PropertyGroup.MorphirWasmtimeCApiVersion
    buildImage      = $BuildImage
    glibcFloor      = Format-Version $glibcFloor
    staticLibrary   = $null
    sharedLibrary   = $null
    builds          = [ordered]@{}
    runnerRuns      = [ordered]@{}
    images          = @()
    cleanRuns       = @()
    sha256Speed     = $null
}

Write-Step 'libwasmtime.a: external undefined symbols'
# readelf -s of every member: the names some member leaves undefined and no member defines are what the link
# resolves from the system libraries. Only names matter; an archive's objects carry no symbol versions.
$archiveSymbols = Join-Path $WorkDirectory 'libwasmtime.a-undefined.txt'
& bash -c @'
set -eu
readelf -sW "$1" > "$2.all"
awk '$1 ~ /^[0-9]+:$/ && $7 == "UND" && $8 != "" { print $8 }' "$2.all" | sort -u > "$2.und"
awk '$1 ~ /^[0-9]+:$/ && $7 != "UND" && ($5 == "GLOBAL" || $5 == "WEAK") && $8 != "" { print $8 }' "$2.all" | sort -u > "$2.def"
comm -23 "$2.und" "$2.def" > "$2"
awk '$1 ~ /^[0-9]+:$/ && $7 == "UND" && $5 == "WEAK" && $8 != "" { print $8 }' "$2.all" | sort -u > "$2.weak"
rm -f "$2.all" "$2.und" "$2.def"
'@ bash $archive $archiveSymbols
$archiveExternal = [System.Collections.Generic.HashSet[string]]::new([string[]]@(Get-Content $archiveSymbols))
$archiveWeak = [System.Collections.Generic.HashSet[string]]::new([string[]]@(Get-Content "$archiveSymbols.weak"))
$result.staticLibrary = [ordered]@{
    path                      = [System.IO.Path]::GetRelativePath($root, $archive)
    bytes                     = (Get-Item $archive).Length
    externalUndefinedSymbols  = $archiveExternal.Count
    weakUndefinedSymbols      = @($archiveExternal | Where-Object { $archiveWeak.Contains($_) } | Sort-Object)
    note                      = 'archive members carry unversioned references; the versions come from the libc a link resolves them against'
}
Write-Host "libwasmtime.a: $($archiveExternal.Count) external undefined symbols"
End-Step

$script:builderImage = New-BuilderImage
$builds = [ordered]@{}
$builds['static-runner'] = Invoke-Publish 'static-runner' $true 'runner'
$builds['dynamic-runner'] = Invoke-Publish 'dynamic-runner' $false 'runner'
$builds['static-jammy'] = Invoke-Publish 'static-jammy' $true 'jammy'
$builds['dynamic-jammy'] = Invoke-Publish 'dynamic-jammy' $false 'jammy'
$builds['static-jammy-managed-probe'] = Invoke-Publish 'static-jammy-managed-probe' $true 'jammy' 'probe'
$builds['static-jammy-managed-library'] = Invoke-Publish 'static-jammy-managed-library' $true 'jammy' 'library'

# Attribution: of the glibc symbols a static executable imports, those that libwasmtime.a references, and those the
# dynamic executable of the same link place (the .NET runtime and the probe, no engine) imports too.
foreach ($pair in @(@('static-runner', 'dynamic-runner'), @('static-jammy', 'dynamic-jammy'))) {
    $static = $builds[$pair[0]]
    $dynamic = $builds[$pair[1]]
    if (-not $static.elf) { continue }
    $dynamicNames = [System.Collections.Generic.HashSet[string]]::new([string[]]@($dynamic.elf ? ($dynamic.elf.imports | ForEach-Object symbol) : @()))
    $byWasmtime = @($static.elf.imports | Where-Object { $archiveExternal.Contains($_.symbol) })
    $wasmtimeOnly = @($byWasmtime | Where-Object { -not $dynamicNames.Contains($_.symbol) })
    $static | Add-Member -NotePropertyName attribution -NotePropertyValue ([ordered]@{
            importsReferencedByLibwasmtime   = $byWasmtime.Count
            highestReferencedByLibwasmtime   = Get-Highest $byWasmtime
            importsOnlyLibwasmtimeAdds       = @($wasmtimeOnly | ForEach-Object { "$($_.symbol)@$($_.version)" } | Sort-Object -Unique)
            highestOfTheRest                 = Get-Highest @($static.elf.imports | Where-Object { -not $archiveExternal.Contains($_.symbol) })
            symbolsAtHighestReferencedByLibwasmtime = @($static.elf.symbolsAtHighest | Where-Object { $archiveExternal.Contains($_) })
        })
}

foreach ($name in @('dynamic-runner', 'dynamic-jammy')) {
    $so = Join-Path $builds[$name].directory $libraryName
    if (Test-Path $so) {
        $elf = Get-ElfNeeds $so
        $result.sharedLibrary = [ordered]@{
            source           = "the Wasmtime package's runtimes/$Rid/native/$libraryName, as published beside the executable"
            bytes            = (Get-Item $so).Length
            sha256           = (Get-FileHash $so -Algorithm SHA256).Hash.ToLowerInvariant()
            needed           = $elf.needed
            versionNeeds     = $elf.versionNeeds
            glibcHighest     = $elf.glibcHighest
            aboveFloor       = $elf.aboveFloor
            symbolsAtHighest = $elf.symbolsAtHighest
        }
        break
    }
}

Write-Step 'runs on the runner'
$runDirectories = @{}
foreach ($build in $builds.Values) {
    if ($build.publishExitCode -ne 0 -or -not $build.executableBytes) { continue }
    $runDirectories[$build.name] = New-RunDirectory $build
    $result.runnerRuns[$build.name] = Invoke-RunnerProbe $build $runDirectories[$build.name]
}
End-Step

$index = 0
foreach ($pinned in $CleanImages) {
    $image = Get-CleanImage $pinned $index
    $result.images += $image
    $label = ($pinned -split '@')[0] -replace '[:/]', '-'
    Write-Step "runs in $label"
    foreach ($build in $builds.Values) {
        if (-not $runDirectories.ContainsKey($build.name)) { continue }
        $result.cleanRuns += Invoke-ImageProbe $build $runDirectories[$build.name] $image $image.derivedImage "$label-clean"
    }
    # The control: the jammy builds in the image as shipped, OpenSSL included where it ships it.
    if ($image.removed) {
        foreach ($name in @('static-jammy', 'dynamic-jammy')) {
            if ($runDirectories.ContainsKey($name)) {
                $result.cleanRuns += Invoke-ImageProbe $builds[$name] $runDirectories[$name] $image $pinned "$label-as-shipped"
            }
        }
    }
    End-Step
    $index++
}

Write-Step 'SHA-256 speed, platform and managed'
$speed = Invoke-Captured dotnet @('fsi', (Join-Path $PSScriptRoot 'wasm-engine-linux-floor/sha256-speed.fsx'), $Guest)
$json = $speed.Lines | Where-Object { $_ -match '^\{' } | Select-Object -Last 1
$result.sha256Speed = $json ? ($json | ConvertFrom-Json) : [ordered]@{ exitCode = $speed.ExitCode; output = $speed.Lines }
$speed.Lines | ForEach-Object { Write-Host "  $_" }
End-Step

foreach ($build in $builds.Values) {
    $view = [ordered]@{}
    foreach ($property in $build.PSObject.Properties) {
        if ($property.Name -in @('directory')) { continue }
        $view[$property.Name] = $property.Value
    }
    $view.log = Split-Path $build.log -Leaf
    if ($build.elf) {
        $view.elf = [ordered]@{
            needed            = $build.elf.needed
            versionNeeds      = $build.elf.versionNeeds
            glibcHighest      = $build.elf.glibcHighest
            aboveFloor        = $build.elf.aboveFloor
            symbolsAtHighest  = $build.elf.symbolsAtHighest
            aboveFloorSymbols = $build.elf.aboveFloorSymbols
            glibcImports      = @($build.elf.imports | Where-Object { Compare-GlibcVersion $_.version } | ForEach-Object { "$($_.symbol)@$($_.version)" } | Sort-Object -Unique)
        }
    }
    $result.builds[$build.name] = $view
}

$json = $result | ConvertTo-Json -Depth 8
$json | Set-Content (Join-Path $WorkDirectory 'result.json')

function Cell([object] $Value) { (@($Value) -join ', ') -replace '\|', '\|' }
$md = [System.Collections.Generic.List[string]]::new()
$md.Add("### WASM engine Linux floor: $Rid")
$md.Add('')
$md.Add("Runner ``$RunnerLabel`` ($($result.runnerImage), $($result.runnerGlibc)). Wasmtime C API $($result.wasmtimeCApi). Floor GLIBC_$($result.glibcFloor). Commit ``$($result.commit)``.")
$md.Add('')
$md.Add('| Build | Linked in | Publish | Executable bytes | Highest GLIBC | Symbols at it | Highest from libwasmtime.a | Needs |')
$md.Add('| --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($build in $builds.Values) {
    $attr = $build.PSObject.Properties['attribution'] ? $build.attribution : $null
    $fromArchive = $attr ? "$($attr.highestReferencedByLibwasmtime.glibcHighest) ($(Cell ($attr.highestReferencedByLibwasmtime.symbols | Select-Object -First 6)))" : ''
    $md.Add("| $($build.name) | $(Cell $build.linkedIn) | exit $($build.publishExitCode) | $($build.executableBytes) | $($build.elf.glibcHighest) | $(Cell ($build.elf.symbolsAtHighest | Select-Object -First 8)) | $fromArchive | $(Cell $build.elf.needed) |")
}
if ($result.sharedLibrary) {
    $md.Add("| libwasmtime.so (package) | Wasmtime release | | $($result.sharedLibrary.bytes) | $($result.sharedLibrary.glibcHighest) | $(Cell ($result.sharedLibrary.symbolsAtHighest | Select-Object -First 8)) | | $(Cell $result.sharedLibrary.needed) |")
}
$md.Add('')
$md.Add('| Image | glibc | Ships | Removed | Clean after |')
$md.Add('| --- | --- | --- | --- | --- |')
foreach ($image in $result.images) {
    $md.Add("| $($image.image) | $($image.glibc) | $(Cell $image.shipsPackages) | $($image.removed) | $($image.cleanAfterPurge) |")
}
$md.Add('')
$md.Add('| Build | Where | Exit | Signal | Last line | ldd not found |')
$md.Add('| --- | --- | --- | --- | --- | --- |')
foreach ($entry in $result.runnerRuns.GetEnumerator()) {
    $md.Add("| $($entry.Key) | runner | $($entry.Value.exitCode) | $($entry.Value.signal) | $(Cell ($entry.Value.lastLines | Select-Object -Last 1)) | |")
}
foreach ($run in $result.cleanRuns) {
    $md.Add("| $($run.build) | $($run.variant) | $($run.exitCode) | $($run.signal) | $(Cell ($run.lastLines | Select-Object -Last 1)) | $(Cell $run.lddNotFound) |")
}
$md.Add('')
$md.Add("SHA-256 of the guest: $(($result.sha256Speed | ConvertTo-Json -Compress))")
$md.Add('')
$md.Add('<details><summary>result.json</summary>')
$md.Add('')
$md.Add('```json')
$md.Add($json)
$md.Add('```')
$md.Add('</details>')
$markdown = $md -join "`n"
$markdown | Set-Content (Join-Path $WorkDirectory 'result.md')
if ($env:GITHUB_STEP_SUMMARY) { $markdown | Add-Content $env:GITHUB_STEP_SUMMARY }
Write-Host ($md | Select-Object -First 40 | Out-String)
exit 0
