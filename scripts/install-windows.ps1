# Morphir CLI Standalone Executable Installation Script for Windows
#
# This script installs the standalone Morphir executable (no .NET SDK required).
# For dotnet tool installation, use: dotnet tool install -g Morphir.Tool
#
# .SYNOPSIS
#   Install Morphir CLI standalone executable from NuGet or GitHub releases
#
# .DESCRIPTION
#   This script installs the Morphir CLI standalone executable with configurable
#   options for source selection, version, and preview releases.
#
# .PARAMETER Version
#   Specific version to install (e.g., "0.3.0")
#
# .PARAMETER Source
#   Installation source: "nuget" or "github" (default: nuget)
#
# .PARAMETER Preview
#   Include preview/pre-release versions
#
# .PARAMETER GitHubUrl
#   Custom GitHub releases URL
#
# .PARAMETER NuGetUrl
#   Custom NuGet package URL for extracting executables
#
# .PARAMETER InstallDir
#   Custom installation directory
#
# .PARAMETER ListVersions
#   List available versions
#
# .PARAMETER Help
#   Show help message
#
# .EXAMPLE
#   .\install-windows.ps1
#   Install latest stable from NuGet
#
# .EXAMPLE
#   .\install-windows.ps1 -Version 0.3.0
#   Install specific version
#
# .EXAMPLE
#   .\install-windows.ps1 -Preview
#   Install latest preview
#
# .EXAMPLE
#   .\install-windows.ps1 -Source github
#   Use GitHub releases
#
# .EXAMPLE
#   .\install-windows.ps1 -ListVersions
#   List available versions

param(
    [string]$Version = "",
    [ValidateSet("nuget", "github")]
    [string]$Source = "",
    [switch]$Preview,
    [string]$GitHubUrl = "",
    [string]$NuGetUrl = "",
    [string]$InstallDir = "",
    [switch]$ListVersions,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Show help
if ($Help) {
    Get-Help $MyInvocation.MyCommand.Path -Detailed
    exit 0
}

# Configuration from environment variables or parameters
$NuGetPackage = "morphir"

if (-not $Source) {
    $Source = if ($env:MORPHIR_SOURCE) { $env:MORPHIR_SOURCE } else { "nuget" }
}

if (-not $GitHubUrl) {
    $GitHubUrl = if ($env:MORPHIR_GITHUB_URL) { $env:MORPHIR_GITHUB_URL } else { "https://github.com/finos/morphir-dotnet/releases" }
}

if (-not $NuGetUrl) {
    $NuGetUrl = if ($env:MORPHIR_NUGET_URL) { $env:MORPHIR_NUGET_URL } else { "https://api.nuget.org/v3-flatcontainer" }
}

if (-not $InstallDir) {
    $InstallDir = if ($env:MORPHIR_INSTALL_DIR) { $env:MORPHIR_INSTALL_DIR } else { "$env:LOCALAPPDATA\morphir\bin" }
}

$IncludePreview = $Preview -or ($env:MORPHIR_INCLUDE_PREVIEW -eq "true")

# Detect architecture
$Arch = $env:PROCESSOR_ARCHITECTURE
if ($Arch -eq "AMD64") {
    $RID = "win-x64"
} else {
    Write-Host "Error: Unsupported architecture: $Arch" -ForegroundColor Red
    Write-Host "Supported architectures: AMD64"
    exit 1
}

# List versions command
if ($ListVersions) {
    Write-Host "Available Morphir versions:"
    Write-Host ""
    
    # Parse GitHub URL to get API URL
    $RepoUrl = $GitHubUrl -replace '/releases$', ''
    $ApiUrl = $RepoUrl -replace 'github.com', 'api.github.com/repos'
    
    try {
        if ($IncludePreview) {
            Write-Host "Fetching all versions (including preview)..."
            $releases = Invoke-RestMethod -Uri "$ApiUrl/releases"
        } else {
            Write-Host "Fetching stable versions..."
            $releases = Invoke-RestMethod -Uri "$ApiUrl/releases" | Where-Object { -not $_.prerelease }
        }
        
        $releases | Select-Object -First 20 | ForEach-Object {
            $ver = $_.tag_name -replace '^v', ''
            if ($ver -match '-') {
                Write-Host "  $ver (preview)"
            } else {
                Write-Host "  $ver"
            }
        }
        
        Write-Host ""
        Write-Host "Install a specific version:"
        Write-Host "  .\install-windows.ps1 -Version <version>"
    } catch {
        Write-Host "Error: Could not fetch versions from $ApiUrl" -ForegroundColor Red
        Write-Host $_.Exception.Message
        exit 1
    }
    
    exit 0
}

# Function to install from NuGet (extract executable from package)
function Install-FromNuGet {
    param([string]$ver)
    
    Write-Host "Installing standalone executable from NuGet package..."
    
    # Create install directory
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    
    # Determine version
    if (-not $ver) {
        Write-Host "Fetching latest version from NuGet..."
        try {
            $indexUrl = "$NuGetUrl/$NuGetPackage/index.json"
            $indexData = Invoke-RestMethod -Uri $indexUrl
            $ver = $indexData.versions | Select-Object -Last 1
            
            if (-not $ver) {
                Write-Host "Error: Could not determine latest version from NuGet" -ForegroundColor Red
                return $false
            }
            Write-Host "Latest version: $ver"
        } catch {
            Write-Host "Error: Failed to fetch version from NuGet" -ForegroundColor Red
            return $false
        }
    }
    
    # Download NuGet package
    $packageUrl = "$NuGetUrl/$NuGetPackage/$ver/$NuGetPackage.$ver.nupkg"
    $TempDir = New-TemporaryFile | ForEach-Object { Remove-Item $_; New-Item -ItemType Directory -Path $_ }
    $packageFile = Join-Path $TempDir "package.nupkg"
    
    Write-Host "Downloading package from NuGet..."
    try {
        Invoke-WebRequest -Uri $packageUrl -OutFile $packageFile -UseBasicParsing
    } catch {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
        Write-Host "Error: Failed to download package from $packageUrl" -ForegroundColor Red
        return $false
    }
    
    # Extract executable from package (nupkg is a zip file)
    Write-Host "Extracting executable..."
    $extractDir = Join-Path $TempDir "extracted"
    try {
        Expand-Archive -Path $packageFile -DestinationPath $extractDir -Force
    } catch {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
        Write-Host "Error: Failed to extract package" -ForegroundColor Red
        return $false
    }
    
    # Find the executable for our RID
    $exePath = Get-ChildItem -Path $extractDir -Recurse -Filter "morphir.exe" | 
        Where-Object { $_.FullName -match "\\tools\\.*\\$RID\\" -or $_.FullName -match "\\runtimes\\$RID\\native\\" } |
        Select-Object -First 1
    
    if (-not $exePath) {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
        Write-Host "Error: Executable not found in package for $RID" -ForegroundColor Red
        return $false
    }
    
    # Install executable
    Copy-Item -Path $exePath.FullName -Destination (Join-Path $InstallDir "morphir.exe") -Force
    Remove-Item -Recurse -Force $TempDir
    
    Write-Host "✓ Morphir CLI installed to $InstallDir\morphir.exe" -ForegroundColor Green
    Write-Host ""
    Write-Host "To use morphir, ensure $InstallDir is in your PATH:"
    Write-Host "  [Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path', 'User') + ';$InstallDir', 'User')"
    return $true
}

# Function to install from GitHub releases
function Install-FromGitHub {
    param([string]$ver)
    
    Write-Host "Installing from GitHub releases..."
    
    # Create install directory
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    
    # Determine version to download
    if (-not $ver) {
        Write-Host "Fetching latest version..."
        $RepoUrl = $GitHubUrl -replace '/releases$', ''
        $ApiUrl = $RepoUrl -replace 'github.com', 'api.github.com/repos'
        
        try {
            if ($IncludePreview) {
                $release = Invoke-RestMethod -Uri "$ApiUrl/releases" | Select-Object -First 1
            } else {
                $release = Invoke-RestMethod -Uri "$ApiUrl/releases/latest"
            }
            
            $ver = $release.tag_name -replace '^v', ''
            Write-Host "Latest version: $ver"
        } catch {
            Write-Host "Error: Could not determine version" -ForegroundColor Red
            return $false
        }
    }
    
    # Construct download URL
    $ReleaseTag = "v$($ver -replace '^v', '')"
    $AssetName = "morphir-$RID.exe"
    $DownloadUrl = "$GitHubUrl/download/$ReleaseTag/$AssetName"
    
    Write-Host "Downloading Morphir $ver for $RID..."
    $TempDir = New-TemporaryFile | ForEach-Object { Remove-Item $_; New-Item -ItemType Directory -Path $_ }
    $ExeFile = Join-Path $TempDir "morphir.exe"
    
    try {
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $ExeFile -UseBasicParsing
        Copy-Item -Path $ExeFile -Destination (Join-Path $InstallDir "morphir.exe") -Force
        Remove-Item -Recurse -Force $TempDir
        
        Write-Host "✓ Morphir CLI installed to $InstallDir\morphir.exe" -ForegroundColor Green
        Write-Host ""
        Write-Host "To use morphir, ensure $InstallDir is in your PATH:"
        Write-Host "  [Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path', 'User') + ';$InstallDir', 'User')"
        return $true
    } catch {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
        Write-Host "Error: Failed to download from $DownloadUrl" -ForegroundColor Red
        return $false
    }
}

# Main installation logic
Write-Host "Installing Morphir CLI for $RID..."
Write-Host "Source: $Source (fallback available)"
if ($Version) {
    Write-Host "Version: $Version"
}
if ($IncludePreview) {
    Write-Host "Including preview releases"
}
Write-Host ""

# Try primary source
$success = $false
if ($Source -eq "nuget") {
    $success = Install-FromNuGet $Version
    if (-not $success) {
        Write-Host ""
        Write-Host "NuGet installation failed. Trying GitHub releases as fallback..."
        $success = Install-FromGitHub $Version
    }
} elseif ($Source -eq "github") {
    $success = Install-FromGitHub $Version
    if (-not $success) {
        Write-Host ""
        Write-Host "GitHub releases installation failed. Trying NuGet as fallback..."
        $success = Install-FromNuGet $Version
    }
} else {
    Write-Host "Error: Invalid source: $Source" -ForegroundColor Red
    Write-Host "Valid sources: nuget, github"
    exit 1
}

# If we get here and not successful, both methods failed
if (-not $success) {
    Write-Host ""
    Write-Host "Error: All installation methods failed" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please try:"
    Write-Host "  1. Download manually from: $GitHubUrl"
    Write-Host "  2. For dotnet tool: dotnet tool install -g Morphir.Tool"
    Write-Host ""
    Write-Host "Requirements for NuGet source:"
    Write-Host "  - PowerShell 5.0+ with Expand-Archive cmdlet"
    exit 1
}

