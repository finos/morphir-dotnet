# Install Morphir CLI from NuGet
# Usage: .\install-windows.ps1 [VERSION]
#   VERSION: Optional version to install (default: latest)

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$NuGetPackage = "Morphir"
$NuGetSource = "https://api.nuget.org/v3/index.json"
$ToolCommand = "dotnet-morphir"
$InstallDir = if ($env:MORPHIR_INSTALL_DIR) { $env:MORPHIR_INSTALL_DIR } else { "$env:LOCALAPPDATA\morphir\bin" }

# Detect architecture
$Arch = $env:PROCESSOR_ARCHITECTURE
if ($Arch -eq "AMD64") {
    $RID = "win-x64"
} else {
    Write-Host "Error: Unsupported architecture: $Arch"
    Write-Host "Supported architectures: AMD64"
    exit 1
}

Write-Host "Installing Morphir CLI for $RID..."

# Check if dotnet is available
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetPath) {
    Write-Host "Using dotnet tool install..."
    if ($Version) {
        dotnet tool install --global $NuGetPackage --version $Version --add-source $NuGetSource
        if ($LASTEXITCODE -ne 0) {
            dotnet tool update --global $NuGetPackage --version $Version --add-source $NuGetSource
        }
    } else {
        dotnet tool install --global $NuGetPackage --add-source $NuGetSource
        if ($LASTEXITCODE -ne 0) {
            dotnet tool update --global $NuGetPackage --add-source $NuGetSource
        }
    }
    
    Write-Host "✓ Morphir CLI installed successfully as $ToolCommand" -ForegroundColor Green
    Write-Host ""
    Write-Host "To use morphir, run: $ToolCommand"
    Write-Host ""
    Write-Host "Ensure %USERPROFILE%\.dotnet\tools is in your PATH"
    Write-Host "Or restart your terminal/PowerShell session"
} else {
    Write-Host "dotnet not found. Installing standalone executable from GitHub releases..."
    
    # Create install directory
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    
    # Determine version to download
    if (-not $Version) {
        Write-Host "Fetching latest version from GitHub releases..."
        try {
            $releaseResponse = Invoke-RestMethod -Uri "https://api.github.com/repos/finos/morphir-dotnet/releases/latest"
            $Version = $releaseResponse.tag_name -replace '^v', ''
            Write-Host "Latest version: $Version"
        } catch {
            Write-Host "Error: Could not determine latest version" -ForegroundColor Red
            Write-Host $_.Exception.Message
            exit 1
        }
    }
    
    # Construct download URL for the executable
    $ReleaseTag = "v$($Version -replace '^v', '')"
    $AssetName = "morphir-$RID.exe"
    $DownloadUrl = "https://github.com/finos/morphir-dotnet/releases/download/$ReleaseTag/$AssetName"
    
    Write-Host "Downloading Morphir $Version for $RID..."
    $TempDir = New-TemporaryFile | ForEach-Object { Remove-Item $_; New-Item -ItemType Directory -Path $_ }
    $ExeFile = Join-Path $TempDir "morphir.exe"
    
    try {
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $ExeFile -UseBasicParsing
        Copy-Item -Path $ExeFile -Destination (Join-Path $InstallDir "morphir.exe") -Force
        Remove-Item -Recurse -Force $TempDir
        
        Write-Host "✓ Morphir CLI installed to $InstallDir\morphir.exe" -ForegroundColor Green
        Write-Host ""
        Write-Host "To use morphir, add $InstallDir to your PATH:"
        Write-Host "  [Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path', 'User') + ';$InstallDir', 'User')"
        Write-Host ""
        Write-Host "Or manually add it via System Properties > Environment Variables"
    } catch {
        Write-Host "Error: Failed to download executable for $RID" -ForegroundColor Red
        Write-Host $_.Exception.Message
        Write-Host ""
        Write-Host "Please download manually from:"
        Write-Host "  https://github.com/finos/morphir-dotnet/releases/tag/$ReleaseTag"
        Write-Host ""
        Write-Host "Or install .NET runtime and use: dotnet tool install --global Morphir"
        Remove-Item -Recurse -Force $TempDir
        exit 1
    }
}

Write-Host ""
Write-Host "Verify installation:"
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Write-Host "  $ToolCommand --version"
} else {
    Write-Host "  morphir --version"
}

