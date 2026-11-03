param(
    [Parameter(Mandatory = $true)][string]$EvidenceRoot,
    [string]$ExistingFeed
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (Test-Path -LiteralPath $EvidenceRoot) { throw "Evidence root already exists: $EvidenceRoot" }
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
$null = New-Item -ItemType Directory -Path $evidence
$oldEnvironment = @{}
foreach ($name in @('TEMP', 'TMP', 'DOTNET_PROCESSOR_COUNT', 'MSBUILDDISABLENODEREUSE', 'DOTNET_HOST_TRACE', 'DOTNET_HOST_TRACEFILE')) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$results = [Collections.Generic.List[object]]::new()

function Invoke-Captured([string]$Name, [string[]]$Arguments, [string]$WorkingDirectory = $repo) {
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(180000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$Name timed out; process tree terminated"
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $output | Set-Content -LiteralPath (Join-Path $evidence "$Name.log") -Encoding utf8
        [ordered]@{ executable = 'dotnet'; arguments = $Arguments; workingDirectory = $WorkingDirectory; exitCode = $process.ExitCode } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence "$Name.command.json") -Encoding utf8
        Write-Output "$Name exit=$($process.ExitCode)"
        if ($process.ExitCode -ne 0) { throw "$Name failed: $output" }
    } finally { $process.Dispose() }
}

function Read-Package([string]$Package) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entry = @($zip.Entries | Where-Object FullName -Like '*.nuspec')[0]
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        return @{ Metadata = $nuspec.package.metadata; Entries = @($zip.Entries | ForEach-Object FullName) }
    } finally { $zip.Dispose() }
}

try {
    $env:TEMP = $env:TMP = Join-Path $evidence 'temp'
    $env:DOTNET_PROCESSOR_COUNT = '2'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $null = New-Item -ItemType Directory -Path $env:TEMP
    $feed = if ($ExistingFeed) { (Resolve-Path -LiteralPath $ExistingFeed).Path } else { Join-Path $evidence 'feed' }
    if (-not $ExistingFeed) {
        $null = New-Item -ItemType Directory -Path $feed
        Invoke-Captured 'canonical-pack' @('pack', (Join-Path $repo 'src/Morphir.SDK.FSharp/Morphir.SDK.FSharp.fsproj'), '-c', 'Release', '-o', $feed, '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false')
        Invoke-Captured 'legacy-pack' @('pack', (Join-Path $repo 'src/Morphir.SDK/Morphir.SDK.fsproj'), '-c', 'Release', '-o', $feed, '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false')
    }
    $legacyPackage = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | Where-Object { (Read-Package $_.FullName).Metadata.id -eq 'Morphir.SDK' })
    if ($legacyPackage.Count -ne 1) { throw 'Feed must have exactly one legacy SDK package' }
    $legacy = Read-Package $legacyPackage[0].FullName
    $version = [string]$legacy.Metadata.version
    $baseline = Join-Path $PSScriptRoot 'Baseline'
    $oldBinary = Join-Path $baseline 'LegacyConsumer.dll'
    $oldHash = '41afc3e57b4ebf350467d0d812d0fdb63eda1878eecd9099d30c992d60768228'
    if ((Get-FileHash -LiteralPath $oldBinary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $oldHash) { throw 'Precompiled baseline binary changed' }
    foreach ($graph in @('canonical', 'legacy', 'combined')) {
        try {
            $consumer = Join-Path $evidence $graph
            $cache = Join-Path $consumer 'packages'
            $null = New-Item -ItemType Directory -Path $consumer, $cache
            $ids = switch ($graph) { canonical { @('Morphir.SDK.FSharp') }; legacy { @('Morphir.SDK') }; combined { @('Morphir.SDK.FSharp', 'Morphir.SDK') } }
            $references = ($ids | ForEach-Object { "<PackageReference Include=`"$_`" Version=`"$version`" />" }) -join ''
            @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><GenerateProgramFile>false</GenerateProgramFile><FSharpCoreImplicitPackageVersion>11.0.100</FSharpCoreImplicitPackageVersion><WarningsAsErrors>NU1605</WarningsAsErrors></PropertyGroup>
  <ItemGroup>$references</ItemGroup>
  <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'LegacyConsumer.fsproj') -Encoding utf8
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyConsumer.fs') -Destination (Join-Path $consumer 'Program.fs')
            Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination (Join-Path $consumer 'global.json')
            @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear/><add key="sdk-test" value="$feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><packageSource key="sdk-test"><package pattern="Morphir.SDK*"/></packageSource><packageSource key="nuget"><package pattern="FSharp.Core"/></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'NuGet.Config') -Encoding utf8
            Invoke-Captured "$graph-restore" @('restore', 'LegacyConsumer.fsproj', '--packages', $cache, '--configfile', 'NuGet.Config', '--disable-parallel', '-m:1', '-nodeReuse:false') $consumer
            $assets = Get-Content -LiteralPath (Join-Path $consumer 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
            $target = $assets.targets['net10.0']
            $runtimeAssets = @($target.GetEnumerator() | ForEach-Object {
                if ($_.Value.ContainsKey('runtime')) {
                    foreach ($path in $_.Value.runtime.Keys) { if ($path.EndsWith('/Morphir.SDK.dll')) { [pscustomobject]@{ Package = $_.Key; Asset = $path } } }
                }
            })
            if ($runtimeAssets.Count -ne 1 -or $runtimeAssets[0].Package -ne "Morphir.SDK.FSharp/$version") { throw "Expected exactly one canonical-owned runtime; got $($runtimeAssets | ConvertTo-Json -Compress)" }
            $allowed = @("Morphir.SDK.FSharp/$version", 'FSharp.Core/11.0.100')
            if ($graph -ne 'canonical') { $allowed += "Morphir.SDK/$version" }
            if (@($target.Keys | Where-Object { $_ -notin $allowed }).Count) { throw "Unexpected package dependencies: $($target.Keys -join ',')" }
            if (@($legacy.Entries | Where-Object { $_ -match '\.(dll|exe)$' }).Count) { throw 'Legacy package contains a compiled assembly' }
            $deps = @($legacy.Metadata.dependencies.group.dependency)
            if ($deps.Count -ne 1 -or $deps[0].id -ne 'Morphir.SDK.FSharp' -or $deps[0].version -notin @("[$version]", "[$version, $version]")) { throw "Legacy dependency must be exact canonical version $version" }
            Invoke-Captured "$graph-build" @('build', 'LegacyConsumer.fsproj', '--no-restore', '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false') $consumer
            $output = Join-Path $consumer 'bin/Release/net10.0'
            $runtime = Join-Path $output 'Morphir.SDK.dll'
            $canonicalRuntime = Join-Path $cache "morphir.sdk.fsharp/$version/lib/net10.0/Morphir.SDK.dll"
            $canonicalHash = (Get-FileHash -LiteralPath $canonicalRuntime -Algorithm SHA256).Hash
            if ((Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash -ne $canonicalHash) { throw 'Fresh output runtime differs from canonical package' }
            $identity = [Reflection.AssemblyName]::GetAssemblyName($runtime).FullName
            if ($identity -ne 'Morphir.SDK, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null') { throw "Runtime identity changed: $identity" }
            $api = Join-Path $evidence "$graph-public-api.txt"
            Invoke-Captured "$graph-source-run" @('exec', (Join-Path $output 'LegacyConsumer.dll'), $api) $consumer
            $missing = @(Compare-Object (Get-Content -LiteralPath (Join-Path $baseline 'public-api.txt')) (Get-Content -LiteralPath $api) | Where-Object SideIndicator -EQ '<=')
            if ($missing.Count) { throw "Old public declarations missing: $($missing.InputObject -join '; ')" }
            $binaryRun = Join-Path $consumer 'precompiled-run'
            $null = New-Item -ItemType Directory -Path $binaryRun
            Get-ChildItem -LiteralPath $output -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $binaryRun }
            Copy-Item -LiteralPath $oldBinary -Destination (Join-Path $binaryRun 'LegacyConsumer.dll') -Force
            $loadedRuntime = Join-Path $binaryRun 'Morphir.SDK.dll'
            if ((Get-FileHash -LiteralPath $loadedRuntime -Algorithm SHA256).Hash -ne $canonicalHash) { throw 'Binary consumer runtime is not the migrated canonical runtime' }
            if ((Get-FileHash -LiteralPath (Join-Path $binaryRun 'LegacyConsumer.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $oldHash) { throw 'Old consumer was recompiled or changed' }
            $env:DOTNET_HOST_TRACE = '1'
            $env:DOTNET_HOST_TRACEFILE = Join-Path $evidence "$graph-precompiled-host-trace.txt"
            Invoke-Captured "$graph-binary-run" @('exec', (Join-Path $binaryRun 'LegacyConsumer.dll')) $binaryRun
            $trace = Get-Content -LiteralPath $env:DOTNET_HOST_TRACEFILE -Raw
            if (-not $trace.Contains($loadedRuntime)) { throw "Host did not resolve migrated runtime at $loadedRuntime" }
            [Environment]::SetEnvironmentVariable('DOTNET_HOST_TRACE', $oldEnvironment['DOTNET_HOST_TRACE'], 'Process')
            [Environment]::SetEnvironmentVariable('DOTNET_HOST_TRACEFILE', $oldEnvironment['DOTNET_HOST_TRACEFILE'], 'Process')
            $generated = @()
            if ($graph -eq 'canonical') {
                # Both representations are generated by the unchanged emitter and
                # checked against this exact fixture in the generation suite.
                $fixture = Join-Path $PSScriptRoot 'Generated/Ordering.fs'
                $fixtureHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
                foreach ($representation in @('classic-3', 'v4-4.0.0')) {
                    $generatedConsumer = Join-Path $consumer "generated-$representation"
                    $null = New-Item -ItemType Directory -Path $generatedConsumer
                    Copy-Item -LiteralPath $fixture -Destination (Join-Path $generatedConsumer 'Ordering.fs')
                    if ((Get-FileHash -LiteralPath (Join-Path $generatedConsumer 'Ordering.fs') -Algorithm SHA256).Hash -ne $fixtureHash) { throw 'Generated source bytes changed' }
                    @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><GenerateProgramFile>false</GenerateProgramFile><FSharpCoreImplicitPackageVersion>11.0.100</FSharpCoreImplicitPackageVersion><WarningsAsErrors>NU1605</WarningsAsErrors></PropertyGroup><ItemGroup><PackageReference Include="Morphir.SDK.FSharp" Version="[$version]"/><Compile Include="Ordering.fs"/><Compile Include="Program.fs"/></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $generatedConsumer 'GeneratedConsumer.fsproj') -Encoding utf8
                    @'
module GeneratedConsumer
open Morphir.SDK
[<EntryPoint>]
let main _ =
    if Car.Rental.Ordering.less <> LT || Car.Rental.Ordering.compare 1 2 <> LT || Car.Rental.Ordering.compare 2 2 <> EQ || Car.Rental.Ordering.compare 3 2 <> GT then failwith "Generated Order behavior changed"
    printfn "Generated Order: 4 assertions passed"
    0
'@ | Set-Content -LiteralPath (Join-Path $generatedConsumer 'Program.fs') -Encoding utf8
                    Invoke-Captured "$representation-generated-restore" @('restore', 'GeneratedConsumer.fsproj', '--packages', $cache, '--configfile', (Join-Path $consumer 'NuGet.Config'), '--disable-parallel', '-m:1', '-nodeReuse:false') $generatedConsumer
                    Invoke-Captured "$representation-generated-build" @('build', 'GeneratedConsumer.fsproj', '--no-restore', '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false') $generatedConsumer
                    $generatedOutput = Join-Path $generatedConsumer 'bin/Release/net10.0'
                    if ((Get-FileHash -LiteralPath (Join-Path $generatedOutput 'Morphir.SDK.dll') -Algorithm SHA256).Hash -ne $canonicalHash) { throw 'Generated consumer did not load canonical package runtime' }
                    Invoke-Captured "$representation-generated-run" @('exec', (Join-Path $generatedOutput 'GeneratedConsumer.dll')) $generatedConsumer
                    $generated += [ordered]@{ representation = $representation; sourceSha256 = $fixtureHash.ToLowerInvariant(); assertions = 4; passed = $true }
                }
            }
            $results.Add([ordered]@{ graph = $graph; passed = $true; runtimeIdentity = $identity; loadedRuntime = $loadedRuntime; runtimeSha256 = $canonicalHash.ToLowerInvariant(); precompiledConsumerSha256 = $oldHash; packages = @($target.Keys); generatedConsumers = $generated })
        } catch {
            $results.Add([ordered]@{ graph = $graph; passed = $false; error = $_.Exception.Message })
            Write-Output "$graph FAILED: $($_.Exception.Message)"
        }
    }
    if (-not $ExistingFeed) {
        # Compile targets must not emit an empty compatibility DLL anywhere.
        $compatibilityAssemblies = @(Get-ChildItem -LiteralPath (Join-Path $repo 'src/Morphir.SDK') -Recurse -File -Filter '*.dll' | Where-Object { $_.FullName -match '[/\\](bin|obj)[/\\]' })
        if ($compatibilityAssemblies.Count) { throw "Compatibility project emitted/retains assemblies: $($compatibilityAssemblies.FullName -join ', ')" }
    }
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'package-results.json') -Encoding utf8
    if (@($results | Where-Object { -not $_.passed }).Count) { throw 'Package migration graph checks failed; see package-results.json' }
    Write-Output 'All three package graphs passed source, binary, API and runtime ownership checks.'
} finally {
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process') }
}
