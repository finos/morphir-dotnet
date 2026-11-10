# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
#
# Installs a built dotnet tool package so that only its own bytes can be installed, and proves it. cli-offering.yml uses
# it to start the produced dotnet-morphir, and tests/Morphir.Build.Tests runs it against conflicting feeds and caches.
#
# - The package is copied into a new feed folder that holds nothing else.
# - dotnet tool install reads only a new nuget.config: <clear/> sources, that one feed, no fallback folders, and new
#   global-packages, HTTP-cache and plugin-cache folders. A same id and version on nuget.org, in another configured feed
#   or in a machine cache cannot be chosen.
# - After the install, the package in the tool store (.store/<id>/<version>/<id>/<version>/<id>.<version>.nupkg, ids
#   and versions in lower case) must have the SHA-256 of the built package, and every tools/ file extracted beside it
#   must equal the same entry of the built package.
#
# Prints the path of the installed command on stdout; everything else goes to the host.
# Dot-source it (. ./install-tool-package.ps1) to load the functions without running the install.

[CmdletBinding()]
param(
    [string] $Package,
    [string] $ToolPath,
    [string] $WorkDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Get-ToolPackageIdentity([string] $Package) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $nuspecs = @($zip.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
        if ($nuspecs.Count -ne 1) { throw "$Package has $($nuspecs.Count) nuspec files at its root, not one." }
        $reader = New-Object IO.StreamReader($nuspecs[0].Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $settings = @($zip.Entries | Where-Object { $_.FullName -match '^tools/[^/]+/any/DotnetToolSettings\.xml$' })
        if ($settings.Count -ne 1) { throw "$Package has $($settings.Count) DotnetToolSettings.xml files, not one." }
        $reader = New-Object IO.StreamReader($settings[0].Open())
        try { [xml] $tool = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $commands = @($tool.DotNetCliTool.Commands.Command)
        if ($commands.Count -ne 1) { throw "$Package declares $($commands.Count) commands, not one." }
        [pscustomobject]@{
            Id      = $nuspec.package.metadata.id
            Version = $nuspec.package.metadata.version
            Command = $commands[0].Name
        }
    } finally { $zip.Dispose() }
}

function Assert-InstalledToolPackage([string] $Package, [string] $ToolPath) {
    $identity = Get-ToolPackageIdentity $Package
    $id = $identity.Id.ToLowerInvariant()
    $version = $identity.Version.ToLowerInvariant()
    $folder = [IO.Path]::Combine($ToolPath, '.store', $id, $version, $id, $version)
    $installed = Join-Path $folder "$id.$version.nupkg"
    if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) { throw "The tool store has no $installed." }
    # NuGet also keeps its own .stage folder there.
    $stores = @(Get-ChildItem -LiteralPath (Join-Path $ToolPath '.store') -Directory | Where-Object { -not $_.Name.StartsWith('.') })
    if ($stores.Count -ne 1 -or $stores[0].Name -ne $id) { throw "The tool store holds $($stores.Name -join ', '), not only $id." }

    $built = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash
    $actual = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
    if ($actual -ne $built) { throw "The installed $id $version package has SHA-256 $actual, and the built package $Package has $built." }

    # The files the command runs are the ones extracted beside it: each must be the built package's entry.
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName.StartsWith('tools/') -and -not $_.FullName.EndsWith('/') })
        if ($entries.Count -eq 0) { throw "$Package has no tools/ files." }
        foreach ($entry in $entries) {
            $file = Join-Path $folder ([Uri]::UnescapeDataString($entry.FullName))
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "The installed tool lacks $($entry.FullName)." }
            $stream = $entry.Open()
            try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            $extracted = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
            if ($extracted -ne $expected) { throw "The installed $($entry.FullName) differs from the built package." }
        }
    } finally { $zip.Dispose() }

    Write-Host "✓ $id $version in the tool store is the built package (SHA-256 $built) and its $($entries.Count) tools/ files match it"
    Join-Path $ToolPath ($IsWindows ? "$($identity.Command).exe" : $identity.Command)
}

function Install-ToolPackage([string] $Package, [string] $ToolPath, [string] $WorkDir) {
    $Package = (Resolve-Path -LiteralPath $Package).Path
    foreach ($path in $ToolPath, $WorkDir) {
        if (Test-Path -LiteralPath $path) { throw "$path exists; the install needs new folders." }
    }
    $identity = Get-ToolPackageIdentity $Package
    $feed = Join-Path $WorkDir 'feed'
    New-Item -ItemType Directory -Force -Path $feed | Out-Null
    Copy-Item -LiteralPath $Package -Destination (Join-Path $feed "$($identity.Id).$($identity.Version).nupkg")
    $packages = Join-Path $WorkDir 'packages'
    $config = Join-Path $WorkDir 'nuget.config'
    $xml = [Security.SecurityElement]
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="built" value="$($xml::Escape($feed))" />
  </packageSources>
  <fallbackPackageFolders>
    <clear />
  </fallbackPackageFolders>
  <config>
    <add key="globalPackagesFolder" value="$($xml::Escape($packages))" />
  </config>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8NoBOM

    $saved = @{}
    $isolated = @{
        NUGET_PACKAGES           = $packages
        NUGET_HTTP_CACHE_PATH    = Join-Path $WorkDir 'http-cache'
        NUGET_PLUGINS_CACHE_PATH = Join-Path $WorkDir 'plugins-cache'
        NUGET_FALLBACK_PACKAGES  = $null
    }
    foreach ($name in $isolated.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $isolated[$name])
    }
    # The current directory's NuGet.config files are not read with --configfile, and the work folder has no others.
    Push-Location -LiteralPath $WorkDir
    try {
        & dotnet tool install $identity.Id --version $identity.Version --tool-path $ToolPath --configfile $config 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool install $($identity.Id) $($identity.Version) failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }

    Assert-InstalledToolPackage $Package $ToolPath
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Package -or -not $ToolPath -or -not $WorkDir) { throw 'Usage: install-tool-package.ps1 -Package <nupkg> -ToolPath <new folder> -WorkDir <new folder>' }
    Install-ToolPackage $Package $ToolPath $WorkDir
}
