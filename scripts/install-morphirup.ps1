<#
.SYNOPSIS
Install morphirup, the Morphir toolchain manager, on Windows x64 or ARM64.

.DESCRIPTION
Downloads morphirup-<rid>.exe and morphirup-v<version>.sha256 from a finos/morphir-dotnet GitHub
release, checks the SHA-256 with Get-FileHash, and installs nothing unless it matches. It installs
morphirup.exe into %LOCALAPPDATA%\Programs\morphirup by default and adds that folder to the user
PATH. Runs on Windows PowerShell 5.1 and PowerShell 7.

  irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex

  & ([scriptblock]::Create((irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1))) -Version 0.4.0-alpha.5

  .\install-morphirup.ps1 [-Version <version>] [-InstallDir <dir>] [-NoModifyPath]

Environment variables (useful with irm | iex, which cannot pass parameters):
  MORPHIRUP_VERSION          Same as -Version.
  MORPHIRUP_INSTALL_DIR      Same as -InstallDir.
  MORPHIRUP_NO_MODIFY_PATH   Set to 1 for -NoModifyPath.
  GITHUB_TOKEN               Sent to api.github.com only, to avoid the unauthenticated rate limit.
  MORPHIRUP_SHA256_FILE      Testing hook: verify against this local checksum file instead of the
                             release's morphirup-v<version>.sha256.

.PARAMETER Version
Release to install, such as 0.4.0-alpha.5 (a leading v is accepted). Default: the newest release,
prereleases included, that has morphirup for this machine's architecture.

.PARAMETER InstallDir
Folder to install morphirup.exe into. Default: %LOCALAPPDATA%\Programs\morphirup

.PARAMETER NoModifyPath
Do not add the folder to the user PATH.
#>
param(
    [string] $Version = $env:MORPHIRUP_VERSION,
    [string] $InstallDir = $env:MORPHIRUP_INSTALL_DIR,
    [switch] $NoModifyPath
)

# Everything runs inside this function so that, under irm | iex, nothing changes the caller's session
# preferences and a failure throws instead of closing the caller's shell with exit.
function Install-Morphirup {
    param([string] $Version, [string] $InstallDir, [bool] $NoModifyPath)

    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is very slow on 5.1 with a progress bar.

    $repo = 'finos/morphir-dotnet'
    $apiUrl = "https://api.github.com/repos/$repo/releases?per_page=30"
    $downloadUrl = "https://github.com/$repo/releases/download"

    function Say([string] $message) { [Console]::Error.WriteLine("morphirup-install: $message") }

    if ($PSVersionTable.PSVersion.Major -lt 6) {
        # Windows PowerShell 5.1 may not offer TLS 1.2 by default.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    elseif (-not $IsWindows) {
        throw 'install-morphirup.ps1 is for Windows; on Linux and macOS use install-morphirup.sh'
    }

    # The machine's architecture, not the process's: an x64 or x86 PowerShell under emulation on ARM64
    # still gets win-arm64. The registry value under HKLM\SYSTEM is not redirected for emulated processes.
    $arch = $null
    try {
        $arch = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name PROCESSOR_ARCHITECTURE).PROCESSOR_ARCHITECTURE
    }
    catch { }
    if (-not $arch) { $arch = $env:PROCESSOR_ARCHITEW6432 }
    if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
    switch ($arch) {
        'ARM64' { $rid = 'win-arm64' }
        'AMD64' { $rid = 'win-x64' }
        default { throw "Windows on '$arch' is not supported; releases carry morphirup for x64 and ARM64 only" }
    }

    $headers = @{ Accept = 'application/vnd.github+json' }
    if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }

    $asset = "morphirup-$rid.exe"
    $release = $null
    if ($Version) {
        $Version = $Version.Trim()
        if ($Version.StartsWith('v')) { $Version = $Version.Substring(1) }
        if ($Version -notmatch '^[0-9A-Za-z.+-]+$') { throw "'$Version' is not a version, such as 0.4.0-alpha.5" }
    }
    else {
        Say "finding the newest release with $asset"
        try {
            $releases = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing
        }
        catch {
            throw "could not list the releases of ${repo}: $($_.Exception.Message); pass -Version <version>"
        }
        # The list is newest first; drafts are skipped.
        foreach ($r in $releases) {
            if ($r.draft) { continue }
            if (@($r.assets | Where-Object { $_.name -eq $asset }).Count -gt 0) { $release = $r; break }
        }
        if (-not $release) { throw "no release of $repo has $asset; pass -Version <version>" }
        $Version = ([string] $release.tag_name).TrimStart('v')
    }

    if (-not $InstallDir) {
        if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA is not set; pass -InstallDir <dir>' }
        $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\morphirup'
    }
    $InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')

    $sums = "morphirup-v$Version.sha256"
    $base = "$downloadUrl/v$Version"
    $work = Join-Path ([IO.Path]::GetTempPath()) ("morphirup-install-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $downloaded = Join-Path $work $asset
        Say "downloading $asset $Version"
        try {
            Invoke-WebRequest -Uri "$base/$asset" -OutFile $downloaded -UseBasicParsing
        }
        catch {
            throw "could not download $base/${asset}: $($_.Exception.Message); check that release v$Version exists and has $asset"
        }

        $sumsFile = Join-Path $work $sums
        if ($env:MORPHIRUP_SHA256_FILE) {
            Say "verifying against the local checksum file $($env:MORPHIRUP_SHA256_FILE) (testing hook)"
            Copy-Item -LiteralPath $env:MORPHIRUP_SHA256_FILE -Destination $sumsFile
        }
        else {
            try {
                Invoke-WebRequest -Uri "$base/$sums" -OutFile $sumsFile -UseBasicParsing
            }
            catch {
                throw "could not download $base/${sums}: $($_.Exception.Message); nothing was installed"
            }
        }

        # A line is "<sha256>  <name>" (sha256sum format, optionally "*<name>" for binary mode).
        $expected = $null
        foreach ($line in [IO.File]::ReadAllLines($sumsFile)) {
            if ($line -match '^\s*([0-9A-Fa-f]{64})\s+\*?(\S+)\s*$' -and $Matches[2] -ceq $asset) {
                $expected = $Matches[1].ToLowerInvariant()
                break
            }
        }
        if (-not $expected) { throw "$sums has no line for $asset; nothing was installed" }
        $actual = (Get-FileHash -LiteralPath $downloaded -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected -ne $actual) {
            throw "SHA-256 mismatch for ${asset}: expected $expected, got $actual; nothing was installed"
        }
        Say "SHA-256 verified: $actual"

        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        $target = Join-Path $InstallDir 'morphirup.exe'
        try {
            Copy-Item -LiteralPath $downloaded -Destination $target -Force
        }
        catch {
            throw "cannot write ${target} (is morphirup running?): $($_.Exception.Message)"
        }
        Say "installed $target"
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }

    try {
        $installed = & $target --version 2>$null
        Say "morphirup --version: $installed"
    }
    catch {
        Say "warning: $target --version failed: $($_.Exception.Message)"
    }

    # Today a release carries the toolchain that morphirup setup installs for some platforms only.
    $toolchain = "morphir-toolchain-$rid.json"
    $hasToolchain = $true
    if ($release) {
        $hasToolchain = @($release.assets | Where-Object { $_.name -eq $toolchain }).Count -gt 0
    }
    else {
        try { Invoke-WebRequest -Uri "$base/$toolchain" -Method Head -UseBasicParsing | Out-Null }
        catch { $hasToolchain = $false }
    }
    if (-not $hasToolchain) {
        Say "note: release v$Version has no toolchain for $rid, so 'morphirup setup' refuses on this machine"
    }

    $onPath = @($env:Path -split ';' | Where-Object { $_.TrimEnd('\') -ieq $InstallDir }).Count -gt 0
    if ($NoModifyPath) {
        if (-not $onPath) {
            Say "$InstallDir is not on your PATH; add it, or run $target directly"
        }
        return
    }

    # Read and write the raw user PATH, so that entries such as %USERPROFILE%\bin stay unexpanded.
    $key = Get-Item -Path 'HKCU:\Environment'
    $userPath = [string] $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    $entries = @($userPath -split ';' | Where-Object { $_ })
    $inUserPath = @($entries | Where-Object { [Environment]::ExpandEnvironmentVariables($_).TrimEnd('\') -ieq $InstallDir }).Count -gt 0
    if (-not $inUserPath) {
        $newPath = (@($entries) + $InstallDir) -join ';'
        Set-ItemProperty -Path 'HKCU:\Environment' -Name Path -Value $newPath -Type ExpandString
        # Setting any user variable through .NET broadcasts WM_SETTINGCHANGE, so new shells see the PATH.
        [Environment]::SetEnvironmentVariable('MORPHIRUP_INSTALL_PATH_REFRESH', '1', 'User')
        # [NullString]::Value, not $null: PowerShell passes $null to a string parameter as "", which
        # would leave the variable behind instead of deleting it.
        [Environment]::SetEnvironmentVariable('MORPHIRUP_INSTALL_PATH_REFRESH', [NullString]::Value, 'User')
        Say "added $InstallDir to your user PATH; open a new terminal to use morphirup"
    }
    if (-not $onPath) { $env:Path = "$($env:Path.TrimEnd(';'));$InstallDir" }
}

Install-Morphirup -Version $Version -InstallDir $InstallDir -NoModifyPath ($NoModifyPath.IsPresent -or $env:MORPHIRUP_NO_MODIFY_PATH -eq '1')
