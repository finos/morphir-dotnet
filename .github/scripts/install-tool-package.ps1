# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
#
# Installs a built dotnet tool package so that only its own bytes can be installed, and proves it. cli-offering.yml uses
# it to start the produced dotnet-morphir, and tests/Morphir.Build.Tests runs it against conflicting feeds and caches.
#
# Isolation, in both modes:
# - The package is copied into a new feed folder that holds nothing else.
# - dotnet tool install reads only a new nuget.config: <clear/> sources, that one feed, no fallback folders, and a new
#   global-packages folder. NUGET_PACKAGES, NUGET_HTTP_CACHE_PATH and NUGET_PLUGINS_CACHE_PATH point at new folders and
#   NUGET_FALLBACK_PACKAGES is unset, so a same id and version on nuget.org, in another configured feed or in a machine
#   cache cannot be chosen.
#
# Modes:
# - Default: a --tool-path install into -ToolPath. The SDK extracts the package to
#   <ToolPath>/.store/<id>/<version>/<id>/<version>/ and writes the command <ToolPath>/<command>[.exe].
#   Prints that command path on stdout.
# - -Local: a --local install into a new tool manifest, <ToolPath>/dotnet-tools.json with isRoot true. The SDK extracts
#   the package to the global-packages folder, <WorkDir>/packages/<id>/<version>/, and records the command's entry
#   assembly in its resolver cache, <WorkDir>/cli-home/.dotnet/toolResolverCache/1/<id>. Run the tool as
#   `dotnet <command without its dotnet- prefix>` (dotnet morphir) from <ToolPath>, with NUGET_PACKAGES and
#   DOTNET_CLI_HOME set as the result file says; the entry assembly is not a command. Prints <ToolPath> on stdout.
#
# Checks, in both modes, after the install: the package folder holds <id>.<version>.nupkg; every .nupkg there has the
# built package's SHA-256; every tools/ file extracted there equals the built package's entry, and there is no other
# tools/ file; the store or global-packages folder holds no other package. With -Local, the manifest names exactly this
# tool, version and command, and the resolver cache sends the command to the built entry assembly in that folder.
#
# -ResultFile writes this run's evidence as JSON: internal qualification evidence for the caller, not a contract. An
# existing file is deleted first, and the new one is written only after every check has passed, so it exists only
# after a successful run. Every path in it is absolute; invocation.arguments is an array and invocation.environment
# an object, to be applied as they are.
# Everything except the stdout line goes to the host. Exit code 0 on success, 1 on any failure (under pwsh -File).
# Dot-source it (. ./install-tool-package.ps1) to load the functions without running the install.

[CmdletBinding()]
param(
    [string] $Package,
    [string] $ToolPath,
    [string] $WorkDir,
    [switch] $Local,
    [string] $ResultFile
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
            Id         = $nuspec.package.metadata.id
            Version    = $nuspec.package.metadata.version
            Command    = $commands[0].Name
            EntryPoint = ($settings[0].FullName -replace 'DotnetToolSettings\.xml$', '') + $commands[0].EntryPoint
        }
    } finally { $zip.Dispose() }
}

# Checks one extracted package folder against the built package, and returns the verified archive copies.
function Assert-PackageFolder([string] $Package, [string] $Folder) {
    $identity = Get-ToolPackageIdentity $Package
    $id = $identity.Id.ToLowerInvariant()
    $version = $identity.Version.ToLowerInvariant()
    $canonical = Join-Path $Folder "$id.$version.nupkg"
    if (-not (Test-Path -LiteralPath $canonical -PathType Leaf)) { throw "The installed package folder has no $canonical." }

    $built = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash
    $stored = @()
    foreach ($archive in @(Get-ChildItem -LiteralPath $Folder -File -Filter '*.nupkg')) {
        $actual = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash
        if ($actual -ne $built) { throw "The installed $id $version package $($archive.FullName) has SHA-256 $actual, and the built package $Package has $built." }
        $stored += [pscustomobject]@{ path = $archive.FullName; sha256 = $actual.ToLowerInvariant() }
    }

    # The files the command runs are the ones extracted beside it: each must be the built package's entry, and there
    # must be no other.
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entries = @{}
        foreach ($entry in $zip.Entries) {
            if (-not $entry.FullName.StartsWith('tools/') -or $entry.FullName.EndsWith('/')) { continue }
            $stream = $entry.Open()
            try { $entries[[Uri]::UnescapeDataString($entry.FullName)] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    if ($entries.Count -eq 0) { throw "$Package has no tools/ files." }
    $toolsFolder = Join-Path $Folder 'tools'
    $extracted = @(if (Test-Path -LiteralPath $toolsFolder) { Get-ChildItem -LiteralPath $toolsFolder -Recurse -File })
    foreach ($file in $extracted) {
        $relative = [IO.Path]::GetRelativePath($Folder, $file.FullName).Replace('\', '/')
        if (-not $entries.ContainsKey($relative)) { throw "The installed tool has $relative, which the built package does not." }
    }
    foreach ($name in $entries.Keys) {
        $file = Join-Path $Folder $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "The installed tool lacks $name." }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entries[$name]) { throw "The installed $name differs from the built package." }
    }
    Write-Host "✓ $id $version in $Folder is the built package (SHA-256 $built), with its $($entries.Count) tools/ files and no others"
    , $stored
}

# Requires that a store or global-packages folder holds only this package id and only this version of it.
function Assert-OnlyPackage([string] $Root, [string] $Id, [string] $Version) {
    if (-not (Test-Path -LiteralPath $Root)) { return }
    # NuGet keeps its own dot folders there (.stage, .tools).
    $ids = @(Get-ChildItem -LiteralPath $Root -Directory | Where-Object { -not $_.Name.StartsWith('.') })
    if ($ids.Count -eq 0) { return }
    if ($ids.Count -ne 1 -or $ids[0].Name -ne $Id) { throw "$Root holds $($ids.Name -join ', '), not only $Id." }
    $versions = @(Get-ChildItem -LiteralPath $ids[0].FullName -Directory)
    if ($versions.Count -ne 1 -or $versions[0].Name -ne $Version) { throw "$Root holds $Id $($versions.Name -join ', '), not only $Version." }
}

function Assert-InstalledToolPackage([string] $Package, [string] $ToolPath, [string] $PackagesFolder) {
    $identity = Get-ToolPackageIdentity $Package
    $id = $identity.Id.ToLowerInvariant()
    $version = $identity.Version.ToLowerInvariant()
    $store = Join-Path $ToolPath '.store'
    Assert-OnlyPackage $store $id $version
    if ($PackagesFolder) { Assert-OnlyPackage $PackagesFolder $id $version }
    $folder = [IO.Path]::Combine($store, $id, $version, $id, $version)
    if (-not (Test-Path -LiteralPath $folder)) { throw "The tool store has no $folder." }
    $stored = Assert-PackageFolder $Package $folder
    $command = Join-Path $ToolPath ($IsWindows ? "$($identity.Command).exe" : $identity.Command)
    if (-not (Test-Path -LiteralPath $command -PathType Leaf)) { throw "The installed command $command does not exist." }
    [pscustomobject]@{ Command = $command; Stored = $stored; PackageFolder = $folder }
}

function Assert-LocalToolPackage([string] $Package, [string] $ManifestFolder, [string] $PackagesFolder, [string] $CliHome) {
    $identity = Get-ToolPackageIdentity $Package
    $id = $identity.Id.ToLowerInvariant()
    $version = $identity.Version.ToLowerInvariant()

    $manifestPath = Join-Path $ManifestFolder 'dotnet-tools.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $tools = $manifest['tools']
    if ($tools.Count -ne 1 -or -not $tools.ContainsKey($id)) { throw "$manifestPath names $($tools.Keys -join ', '), not only $id." }
    $entry = $tools[$id]
    if ($entry['version'] -ne $identity.Version -and $entry['version'] -ne $version) { throw "$manifestPath has $id $($entry['version']), not $($identity.Version)." }
    if (@($entry['commands']).Count -ne 1 -or @($entry['commands'])[0] -ne $identity.Command) { throw "$manifestPath gives $id the commands $($entry['commands'] -join ', '), not $($identity.Command)." }

    Assert-OnlyPackage $PackagesFolder $id $version
    $folder = [IO.Path]::Combine($PackagesFolder, $id, $version)
    if (-not (Test-Path -LiteralPath $folder)) { throw "The global-packages folder has no $folder." }
    $stored = Assert-PackageFolder $Package $folder

    $cacheFolder = [IO.Path]::Combine($CliHome, '.dotnet', 'toolResolverCache', '1')
    $caches = @(Get-ChildItem -LiteralPath $cacheFolder -File)
    if ($caches.Count -ne 1 -or $caches[0].Name -ne $id) { throw "$cacheFolder holds $($caches.Name -join ', '), not only $id." }
    $resolved = @(Get-Content -LiteralPath $caches[0].FullName -Raw | ConvertFrom-Json | Where-Object { $_.Name -eq $identity.Command })
    if ($resolved.Count -ne 1) { throw "The resolver cache $($caches[0].FullName) has $($resolved.Count) entries for $($identity.Command), not one." }
    $entryPoint = [IO.Path]::GetFullPath($resolved[0].PathToExecutable)
    $expected = [IO.Path]::GetFullPath((Join-Path $folder $identity.EntryPoint))
    if ($entryPoint -ne $expected) { throw "The resolver cache sends $($identity.Command) to $entryPoint, not to the built entry assembly $expected." }

    $words = @($identity.Command.StartsWith('dotnet-') ? $identity.Command.Substring('dotnet-'.Length) : $identity.Command)
    [pscustomobject]@{ Manifest = $manifestPath; ResolverCache = $caches[0].FullName; EntryAssembly = $entryPoint; Words = $words; Stored = $stored; PackageFolder = $folder }
}

function Install-ToolPackage([string] $Package, [string] $ToolPath, [string] $WorkDir, [switch] $Local, [string] $ResultFile) {
    # A result file exists only after a run whose checks all passed: an old one goes first, whatever happens next.
    if ($ResultFile) {
        $ResultFile = [IO.Path]::GetFullPath($ResultFile)
        if (Test-Path -LiteralPath $ResultFile) { [IO.File]::Delete($ResultFile) }
    }
    $Package = (Resolve-Path -LiteralPath $Package).Path
    $ToolPath = [IO.Path]::GetFullPath($ToolPath)
    $WorkDir = [IO.Path]::GetFullPath($WorkDir)
    foreach ($path in $ToolPath, $WorkDir) {
        if (Test-Path -LiteralPath $path) { throw "$path exists; the install needs new folders." }
    }
    $identity = Get-ToolPackageIdentity $Package
    $built = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant()
    $feed = Join-Path $WorkDir 'feed'
    New-Item -ItemType Directory -Force -Path $feed | Out-Null
    Copy-Item -LiteralPath $Package -Destination (Join-Path $feed "$($identity.Id).$($identity.Version).nupkg")
    $packages = Join-Path $WorkDir 'packages'
    $httpCache = Join-Path $WorkDir 'http-cache'
    $pluginsCache = Join-Path $WorkDir 'plugins-cache'
    $cliHome = Join-Path $WorkDir 'cli-home'
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

    $isolated = [ordered]@{
        NUGET_PACKAGES           = $packages
        NUGET_HTTP_CACHE_PATH    = $httpCache
        NUGET_PLUGINS_CACHE_PATH = $pluginsCache
        NUGET_FALLBACK_PACKAGES  = $null
    }
    # A local tool resolves through the resolver cache under DOTNET_CLI_HOME, so it gets a new one too.
    if ($Local) { $isolated['DOTNET_CLI_HOME'] = $cliHome }

    if ($Local) {
        New-Item -ItemType Directory -Force -Path $ToolPath | Out-Null
        # isRoot: manifests in parent folders are not merged in.
        '{ "version": 1, "isRoot": true, "tools": {} }' | Set-Content -LiteralPath (Join-Path $ToolPath 'dotnet-tools.json') -Encoding utf8NoBOM
        $workingDirectory = $ToolPath
        $arguments = @('tool', 'install', $identity.Id, '--local', '--version', $identity.Version,
            '--tool-manifest', (Join-Path $ToolPath 'dotnet-tools.json'), '--configfile', $config)
    } else {
        # The current directory's NuGet.config files are not read with --configfile, and the work folder has no others.
        $workingDirectory = $WorkDir
        $arguments = @('tool', 'install', $identity.Id, '--version', $identity.Version, '--tool-path', $ToolPath, '--configfile', $config)
    }

    $saved = @{}
    foreach ($name in $isolated.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $isolated[$name])
    }
    Push-Location -LiteralPath $workingDirectory
    try {
        & dotnet @arguments 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool install $($identity.Id) $($identity.Version) failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }

    $environment = [ordered]@{}
    if ($Local) {
        $verified = Assert-LocalToolPackage $Package $ToolPath $packages $cliHome
        $environment['NUGET_PACKAGES'] = $packages
        $environment['DOTNET_CLI_HOME'] = $cliHome
        $invocation = [ordered]@{
            file             = 'dotnet'
            arguments        = $verified.Words
            workingDirectory = $ToolPath
            environment      = $environment
            manifest         = $verified.Manifest
            resolverCache    = $verified.ResolverCache
            entryAssembly    = $verified.EntryAssembly
        }
        $printed = $ToolPath
    } else {
        $verified = Assert-InstalledToolPackage $Package $ToolPath $packages
        $invocation = [ordered]@{ file = $verified.Command; arguments = @(); workingDirectory = $null; environment = $environment }
        $printed = $verified.Command
    }

    if ($ResultFile) {
        [ordered]@{
            note       = 'Internal qualification evidence of .github/scripts/install-tool-package.ps1; not a contract.'
            mode       = $Local ? 'local' : 'tool-path'
            package    = [ordered]@{ id = $identity.Id; version = $identity.Version; command = $identity.Command; path = $Package; sha256 = $built }
            install    = [ordered]@{ file = 'dotnet'; arguments = $arguments; workingDirectory = $workingDirectory; configFile = $config; feed = $feed; environment = $isolated }
            caches     = [ordered]@{ packagesFolder = $packages; httpCache = $httpCache; pluginsCache = $pluginsCache; cliHome = ($Local ? $cliHome : $null) }
            toolPath   = $ToolPath
            packageFolder = $verified.PackageFolder
            stored     = @($verified.Stored)
            invocation = $invocation
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$ResultFile.partial" -Encoding utf8NoBOM
        # Written whole, then moved into place, so a reader never sees a partial file.
        Move-Item -LiteralPath "$ResultFile.partial" -Destination $ResultFile -Force
    }

    $printed
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Package -or -not $ToolPath -or -not $WorkDir) { throw 'Usage: install-tool-package.ps1 -Package <nupkg> -ToolPath <new folder> -WorkDir <new folder> [-Local] [-ResultFile <json>]' }
    Install-ToolPackage $Package $ToolPath $WorkDir -Local:$Local -ResultFile $ResultFile
}
