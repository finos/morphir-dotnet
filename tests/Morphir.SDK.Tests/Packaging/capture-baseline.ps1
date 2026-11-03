param([Parameter(Mandatory = $true)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$git = 'C:/Users/damre/scoop/apps/git/current/cmd/git.exe'
if (Test-Path -LiteralPath $EvidenceRoot) { throw "Evidence root already exists: $EvidenceRoot" }
$resolvedEvidence = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not $resolvedEvidence.StartsWith('C:\Temp\morphir-fsharp-sdk-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an owned C:/Temp/morphir-fsharp-sdk-* directory' }
if (& $git -C $repo diff HEAD -- src/Morphir.SDK) { throw 'Legacy production SDK differs from HEAD' }
$baseSha = (& $git -C $repo rev-parse HEAD).Trim()
$source = Join-Path $repo 'src/Morphir.SDK/Morphir.SDK.fsproj'
$null = New-Item -ItemType Directory -Path $resolvedEvidence
$oldEnvironment = @{}
foreach ($name in @('TEMP', 'TMP', 'DOTNET_PROCESSOR_COUNT', 'MSBUILDDISABLENODEREUSE')) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

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
        $output | Set-Content -LiteralPath (Join-Path $resolvedEvidence "$Name.log") -Encoding utf8
        [ordered]@{ executable = 'dotnet'; arguments = $Arguments; workingDirectory = $WorkingDirectory; exitCode = $process.ExitCode } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedEvidence "$Name.command.json") -Encoding utf8
        Write-Output "$Name exit=$($process.ExitCode)"
        if ($process.ExitCode -ne 0) { throw "$Name failed: $output" }
    } finally { $process.Dispose() }
}

try {
    $env:TEMP = $env:TMP = Join-Path $resolvedEvidence 'temp'
    $env:DOTNET_PROCESSOR_COUNT = '2'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $feed = Join-Path $resolvedEvidence 'old-feed'
    $consumer = Join-Path $resolvedEvidence 'old-consumer'
    $cache = Join-Path $resolvedEvidence 'packages'
    $null = New-Item -ItemType Directory -Path $env:TEMP, $feed, $consumer, $cache
    Invoke-Captured 'toolchain' @('--info')
    Invoke-Captured 'old-pack' @('pack', $source, '-c', 'Release', '-o', $feed, '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false')
    $packages = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg')
    if ($packages.Count -ne 1) { throw 'Expected one untouched legacy nupkg' }
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $nuspec = @($archive.Entries | Where-Object FullName -Like '*.nuspec')[0]
        $reader = [IO.StreamReader]::new($nuspec.Open())
        try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $version = $metadata.package.metadata.version
        $fsharpCore = @($metadata.package.metadata.dependencies.group.dependency | Where-Object id -EQ 'FSharp.Core')[0].version
        $assets = @($archive.Entries | ForEach-Object FullName)
        $assets | Set-Content -LiteralPath (Join-Path $resolvedEvidence 'old-package-assets.txt') -Encoding utf8
    } finally { $archive.Dispose() }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><GenerateProgramFile>false</GenerateProgramFile><FSharpCoreImplicitPackageVersion>$fsharpCore</FSharpCoreImplicitPackageVersion><WarningsAsErrors>NU1605</WarningsAsErrors></PropertyGroup>
  <ItemGroup><PackageReference Include="Morphir.SDK" Version="$version" /></ItemGroup>
  <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'LegacyConsumer.fsproj') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyConsumer.fs') -Destination (Join-Path $consumer 'Program.fs')
    Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination (Join-Path $consumer 'global.json')
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="legacy-baseline" value="$feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'NuGet.Config') -Encoding utf8
    Invoke-Captured 'old-restore' @('restore', 'LegacyConsumer.fsproj', '--packages', $cache, '--configfile', 'NuGet.Config', '--disable-parallel', '-m:1', '-nodeReuse:false') $consumer
    Invoke-Captured 'old-build' @('build', 'LegacyConsumer.fsproj', '--no-restore', '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false') $consumer
    $consumerDll = Join-Path $consumer 'bin/Release/net10.0/LegacyConsumer.dll'
    Invoke-Captured 'old-run' @('exec', $consumerDll, (Join-Path $resolvedEvidence 'old-public-api.txt')) $consumer
    $runtime = Join-Path $consumer 'bin/Release/net10.0/Morphir.SDK.dll'
    $identity = [Reflection.AssemblyName]::GetAssemblyName($runtime).FullName
    $hashes = @($packages[0].FullName, $runtime, $consumerDll, (Join-Path $consumer 'Program.fs'), (Join-Path $resolvedEvidence 'old-public-api.txt')) |
        ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } }
    $sourceHashes = Get-ChildItem -LiteralPath (Join-Path $repo 'src/Morphir.SDK') -File |
        ForEach-Object { [ordered]@{ path = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
    [ordered]@{ baseSha = $baseSha; packageId = 'Morphir.SDK'; packageVersion = $version; assemblyIdentity = $identity; consumerExecuted = $true; productionPackagingUnmodified = $true; hashes = $hashes; sourceHashes = @($sourceHashes) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $resolvedEvidence 'baseline.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $resolvedEvidence 'baseline.json')
} finally {
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process') }
}
