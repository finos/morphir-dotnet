<#
.SYNOPSIS
Install morphirup, the Morphir toolchain manager, on Windows x64 or ARM64.

.DESCRIPTION
Downloads morphirup-<rid>.exe and morphirup-v<version>.sha256 from a finos/morphir GitHub
release, checks the SHA-256 with Get-FileHash, and installs nothing unless it matches. It installs
morphirup.exe into %LOCALAPPDATA%\Programs\morphirup by default and adds that folder to the user
PATH. Runs on Windows PowerShell 5.1 and PowerShell 7.

  irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex

  & ([scriptblock]::Create((irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1))) -Version <version>

  .\install-morphirup.ps1 [-Version <version>] [-InstallDir <dir>] [-NoModifyPath]

Environment variables (useful with irm | iex, which cannot pass parameters):
  MORPHIRUP_VERSION          Same as -Version.
  MORPHIRUP_INSTALL_DIR      Same as -InstallDir.
  MORPHIRUP_NO_MODIFY_PATH   Set to 1 for -NoModifyPath.
  MORPHIRUP_DOWNLOAD_URL     HTTPS mirror of https://github.com/finos/morphir/releases/download,
                             laid out the same way: <url>/v<version>/<file>.
  MORPHIRUP_API_URL          Release list to read when no version is given. Default:
                             https://api.github.com/repos/finos/morphir/releases?per_page=100
  GITHUB_TOKEN               Sent to api.github.com only, to avoid the unauthenticated rate limit.
  MORPHIRUP_SHA256_FILE      Testing hook: verify against this local checksum file instead of the
                             release's morphirup-v<version>.sha256.
  MORPHIRUP_RELEASES_FILE    Testing hook: read the release list from this local file, in the form of
                             the GitHub API, instead of MORPHIRUP_API_URL.

Behind a proxy: the script uses the Windows proxy settings, or HTTPS_PROXY (with NO_PROXY) when it is
set, and signs in to the proxy as the current Windows user. If the proxy inspects TLS, your company's
root certificate must be in the Windows certificate store. The hosts used are github.com and
release-assets.githubusercontent.com, plus api.github.com when no version is given. If api.github.com
is blocked, the script reads the release feed on github.com instead.

.PARAMETER Version
Release to install, such as 0.5.0-alpha.1 (a leading v is accepted). Default: the newest finos/morphir
release, prereleases included, that has morphirup for this machine's architecture. A release without
it, such as a release of the earlier Rust CLI (v0.4.0-beta.*), is skipped.

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

    $repo = 'finos/morphir'
    $apiUrl = if ($env:MORPHIRUP_API_URL) { $env:MORPHIRUP_API_URL } else { "https://api.github.com/repos/$repo/releases?per_page=100" }
    $feedUrl = "https://github.com/$repo/releases.atom"
    $downloadUrl = if ($env:MORPHIRUP_DOWNLOAD_URL) { $env:MORPHIRUP_DOWNLOAD_URL.TrimEnd('/') } else { "https://github.com/$repo/releases/download" }
    $networkHint = 'If your network needs a proxy, set it in the Windows proxy settings or in HTTPS_PROXY. If it ' +
        'inspects TLS, your company''s root certificate must be in the Windows certificate store. The hosts needed are ' +
        'github.com and release-assets.githubusercontent.com, or set MORPHIRUP_DOWNLOAD_URL to an HTTPS mirror.'

    function Say([string] $message) { [Console]::Error.WriteLine("morphirup-install: $message") }

    $legacy = $PSVersionTable.PSVersion.Major -lt 6
    if ($legacy) {
        # Windows PowerShell 5.1 may not offer TLS 1.2 by default.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    elseif (-not $IsWindows) {
        throw 'install-morphirup.ps1 is for Windows; on Linux and macOS use install-morphirup.sh'
    }

    # Proxies. Both shells use the Windows proxy settings. PowerShell 7 also reads HTTPS_PROXY and
    # NO_PROXY itself; Windows PowerShell 5.1 does not, so the script passes HTTPS_PROXY to each request.
    # A proxy that asks for sign-in gets the current Windows user's credentials (NTLM or Kerberos).
    $proxyUrl = $null
    foreach ($name in 'HTTPS_PROXY', 'ALL_PROXY') {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { $proxyUrl = $value; break }
    }
    if ($proxyUrl -and $proxyUrl -notmatch '^[A-Za-z][A-Za-z0-9+.-]*://') { $proxyUrl = "http://$proxyUrl" }
    $noProxy = @(([string] [Environment]::GetEnvironmentVariable('NO_PROXY')) -split '[,\s]+' | Where-Object { $_ } |
        ForEach-Object { $_.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant() })
    if ($legacy) {
        if ([Net.WebRequest]::DefaultWebProxy) {
            [Net.WebRequest]::DefaultWebProxy.Credentials = [Net.CredentialCache]::DefaultNetworkCredentials
        }
    }
    else {
        $defaultProxy = [System.Net.Http.HttpClient]::DefaultProxy
        if ($defaultProxy -and -not $defaultProxy.Credentials) {
            $defaultProxy.Credentials = [Net.CredentialCache]::DefaultNetworkCredentials
        }
    }

    # WebArgs <uri>: the common Invoke-WebRequest / Invoke-RestMethod arguments for one request.
    function WebArgs([string] $uri) {
        $webArgs = @{ Uri = $uri; UseBasicParsing = $true }
        if ($legacy -and $proxyUrl) {
            $targetHost = ([Uri] $uri).Host.ToLowerInvariant()
            $bypass = @($noProxy | Where-Object { $_ -eq '*' -or $targetHost -eq $_ -or $targetHost.EndsWith(".$_") }).Count -gt 0
            if (-not $bypass) {
                $proxy = [Uri] $proxyUrl
                if ($proxy.UserInfo) {
                    $parts = $proxy.UserInfo.Split(':', 2)
                    $password = if ($parts.Count -gt 1) { [Uri]::UnescapeDataString($parts[1]) } else { '' }
                    $secure = New-Object Security.SecureString
                    foreach ($ch in $password.ToCharArray()) { $secure.AppendChar($ch) }
                    $webArgs['ProxyCredential'] = New-Object Management.Automation.PSCredential ([Uri]::UnescapeDataString($parts[0])), $secure
                }
                else {
                    $webArgs['ProxyUseDefaultCredentials'] = $true
                }
                $webArgs['Proxy'] = $proxy.GetLeftPart([UriPartial]::Authority)
            }
        }
        $webArgs
    }

    # UrlExists <uri>: true when a HEAD request succeeds. (Windows PowerShell 5.1 does not let a script
    # set the Range header, so a one-byte GET is not an option there.)
    function UrlExists([string] $uri) {
        $webArgs = WebArgs $uri
        try { Invoke-WebRequest @webArgs -Method Head | Out-Null; $true } catch { $false }
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

    # GITHUB_TOKEN goes to api.github.com only, never to another host named by MORPHIRUP_API_URL.
    $headers = @{ Accept = 'application/vnd.github+json' }
    if ($env:GITHUB_TOKEN -and $apiUrl.StartsWith('https://api.github.com/')) { $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }

    $asset = "morphirup-$rid.exe"
    $release = $null
    if ($Version) {
        $Version = $Version.Trim()
        if ($Version.StartsWith('v')) { $Version = $Version.Substring(1) }
        if ($Version -notmatch '^[0-9A-Za-z.+-]+$') { throw "'$Version' is not a version, such as 0.5.0-alpha.1" }
    }
    else {
        Say "finding the newest release with $asset"
        $releases = $null
        if ($env:MORPHIRUP_RELEASES_FILE) {
            Say "reading the release list from $($env:MORPHIRUP_RELEASES_FILE) (testing hook)"
            $releases = [IO.File]::ReadAllText($env:MORPHIRUP_RELEASES_FILE) | ConvertFrom-Json
        }
        else {
            try {
                $webArgs = WebArgs $apiUrl
                $releases = Invoke-RestMethod @webArgs -Headers $headers
            }
            catch {
                Say "could not read ${apiUrl} ($($_.Exception.Message)); reading the release feed on github.com instead"
            }
        }
        if ($null -ne $releases) {
            # The list is newest first. A release without the file is skipped: finos/morphir also has
            # releases of the earlier Rust CLI (v0.4.0-beta.* and older) and may have releases of other
            # tools. Drafts are skipped.
            foreach ($r in $releases) {
                if ($r.draft) { continue }
                if (@($r.assets | Where-Object { $_.name -eq $asset }).Count -gt 0) { $release = $r; break }
            }
            if (-not $release) { throw "no release of $repo has $asset; pass -Version <version>" }
            $Version = ([string] $release.tag_name).TrimStart('v')
        }
        else {
            # The Atom feed lists the newest releases first, prereleases included, but not their files,
            # so try each v* tag until one has the file. A tag without it, such as a Rust CLI release, is skipped.
            try {
                $webArgs = WebArgs $feedUrl
                $feed = (Invoke-WebRequest @webArgs).Content
            }
            catch {
                throw "could not read $feedUrl either: $($_.Exception.Message); pass -Version <version>. $networkHint"
            }
            if ($feed -is [byte[]]) { $feed = [Text.Encoding]::UTF8.GetString($feed) }
            $tags = [regex]::Matches($feed, "/$repo/releases/tag/(v[^`"]+)`"") | ForEach-Object { $_.Groups[1].Value }
            foreach ($tag in $tags) {
                if (UrlExists "$downloadUrl/$tag/$asset") { $Version = $tag.Substring(1); break }
            }
            if (-not $Version) { throw "found no release with $asset in $feedUrl; pass -Version <version>. $networkHint" }
        }
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
        Say "downloading $asset $Version from $base/$asset"
        try {
            $webArgs = WebArgs "$base/$asset"
            Invoke-WebRequest @webArgs -OutFile $downloaded
        }
        catch {
            throw "could not download $base/${asset}: $($_.Exception.Message); check that release v$Version exists and has $asset. $networkHint"
        }

        $sumsFile = Join-Path $work $sums
        if ($env:MORPHIRUP_SHA256_FILE) {
            Say "verifying against the local checksum file $($env:MORPHIRUP_SHA256_FILE) (testing hook)"
            Copy-Item -LiteralPath $env:MORPHIRUP_SHA256_FILE -Destination $sumsFile
        }
        else {
            try {
                $webArgs = WebArgs "$base/$sums"
                Invoke-WebRequest @webArgs -OutFile $sumsFile
            }
            catch {
                throw "could not download $base/${sums}: $($_.Exception.Message); nothing was installed. $networkHint"
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

    # Say so when the release has no toolchain for this platform, since morphirup setup then refuses.
    $toolchain = "morphir-toolchain-$rid.json"
    $hasToolchain = $true
    if ($release) {
        $hasToolchain = @($release.assets | Where-Object { $_.name -eq $toolchain }).Count -gt 0
    }
    else {
        $hasToolchain = UrlExists "$base/$toolchain"
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
