<#
.SYNOPSIS
    Packs the probe tool into a local folder feed for test.ps1 and writes an inventory of the packages.

.DESCRIPTION
    Writes to <OutDirectory>:
      feed/          Spike.PlatformProbe 1.0.0 and 1.1.0 complete; 1.2.0 with its linux-x64 and linux-musl-x64
                     packages left out, as a partial publish; Spike.PlatformProbe.NoMusl 1.0.0 (six engine
                     platforms and `any`, no musl packages); Spike.PlatformProbe.Fallback 1.0.0 (Windows, macOS
                     and `any`, no Linux package).
      sdk-alone/     Spike.PlatformProbe 1.0.0 packed with SpikeKeepEngineEverywhere=true: what the SDK puts in
                     each package without the spike's target. Inventory only, never installed.
      packages.json  Every package: name, bytes, runner, engine libraries.
      pack-*.log     The pack logs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$project = Join-Path $PSScriptRoot 'PlatformProbe/PlatformProbe.csproj'
New-Item -ItemType Directory -Force $OutDirectory | Out-Null
$OutDirectory = (Resolve-Path $OutDirectory).Path
$feed = Join-Path $OutDirectory 'feed'
$sdkAlone = Join-Path $OutDirectory 'sdk-alone'
Remove-Item -Recurse -Force $feed, $sdkAlone -ErrorAction SilentlyContinue

function Invoke-Pack([string] $name, [string] $output, [string[]] $properties) {
    foreach ($dir in 'bin', 'obj') { Remove-Item -Recurse -Force (Join-Path $PSScriptRoot "PlatformProbe/$dir") -ErrorAction SilentlyContinue }
    $log = Join-Path $OutDirectory "pack-$name.log"
    & dotnet pack $project -c Release -o $output @properties -nodeReuse:false -tl:off -v:m -m:1 *> $log
    if ($LASTEXITCODE -ne 0) { throw "pack $name failed, see $log" }
}

Invoke-Pack 'v1.0.0' $feed @('-p:Version=1.0.0')
Invoke-Pack 'v1.1.0' $feed @('-p:Version=1.1.0')
$partial = Join-Path $OutDirectory 'partial'
Invoke-Pack 'v1.2.0' $partial @('-p:Version=1.2.0')
Get-ChildItem $partial -Filter '*.nupkg' |
    Where-Object { $_.Name -notin 'Spike.PlatformProbe.linux-x64.1.2.0.nupkg', 'Spike.PlatformProbe.linux-musl-x64.1.2.0.nupkg' } |
    Move-Item -Destination $feed
Remove-Item -Recurse -Force $partial
Invoke-Pack 'nomusl-v1.0.0' $feed @('-p:Version=1.0.0', '-p:PackageId=Spike.PlatformProbe.NoMusl', '-p:SpikeMuslPackages=false')
# No Linux package at all, so that a Linux machine shows what a platform with no package of its own installs.
Invoke-Pack 'fallback-v1.0.0' $feed @('-p:Version=1.0.0', '-p:PackageId=Spike.PlatformProbe.Fallback', '-p:SpikeMuslPackages=false',
    '-p:SpikeLinuxPackages=false')
Invoke-Pack 'sdk-alone-v1.0.0' $sdkAlone @('-p:Version=1.0.0', '-p:SpikeKeepEngineEverywhere=true')
foreach ($dir in 'bin', 'obj') { Remove-Item -Recurse -Force (Join-Path $PSScriptRoot "PlatformProbe/$dir") -ErrorAction SilentlyContinue }

function Get-PackageInventory([string] $directory, [string] $set) {
    foreach ($package in Get-ChildItem $directory -Filter '*.nupkg' | Sort-Object Name) {
        $zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $engines = @($zip.Entries | Where-Object { $_.Name -in 'wasmtime.dll', 'libwasmtime.so', 'libwasmtime.dylib' } |
                ForEach-Object { [ordered]@{ path = $_.FullName; bytes = $_.Length } })
            $settings = $zip.Entries | Where-Object Name -eq 'DotnetToolSettings.xml' | Select-Object -First 1
            $reader = [IO.StreamReader]::new($settings.Open())
            $xml = [xml]$reader.ReadToEnd()
            $reader.Dispose()
            $command = $xml.DotNetCliTool.Commands.Command
            $ridPackages = @($xml.SelectNodes('//RuntimeIdentifierPackage') | ForEach-Object { $_.RuntimeIdentifier })
            $hostfxr = @($zip.Entries | Where-Object { $_.Name -match '^(lib)?(hostfxr|coreclr)\.(dll|so|dylib)$' }).Count -gt 0
            [ordered]@{
                set = $set
                file = $package.Name
                bytes = $package.Length
                toolSettingsPath = $settings.FullName
                toolSettingsVersion = $xml.DotNetCliTool.Version
                runner = if ($command.HasAttribute('Runner')) { $command.Runner } else { $null }
                entryPoint = if ($command.HasAttribute('EntryPoint')) { $command.EntryPoint } else { $null }
                runtimeIdentifierPackages = $ridPackages
                carriesRuntime = $hostfxr
                engineLibraries = $engines
                files = $zip.Entries.Count
            }
        } finally { $zip.Dispose() }
    }
}

$inventory = @(Get-PackageInventory $feed 'feed') + @(Get-PackageInventory $sdkAlone 'sdk-alone')
[ordered]@{
    sdk = (& dotnet --version)
    packages = $inventory
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutDirectory 'packages.json')
$inventory | ForEach-Object {
    '{0,-10} {1,-52} {2,10} {3,-10} {4}' -f $_.set, $_.file, $_.bytes, ($_.runner ?? 'pointer'), (($_.engineLibraries | ForEach-Object { $_.path }) -join ', ')
}
