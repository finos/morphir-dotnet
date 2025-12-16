# Install Morphir CLI from NuGet
# Usage: .\install-windows.ps1 [VERSION]
#   VERSION: Optional version to install (default: latest)

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$NuGetPackage = "Morphir"
$NuGetSource = "https://api.nuget.org/v3/index.json"
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
    
    Write-Host "✓ Morphir CLI installed successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "To use morphir, ensure %USERPROFILE%\.dotnet\tools is in your PATH"
    Write-Host "Or restart your terminal/PowerShell session"
} else {
    Write-Host "dotnet not found. Installing platform-specific executable..."
    
    # Create install directory
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    
    # Determine version to download
    if (-not $Version) {
        Write-Host "Fetching latest version from NuGet..."
        try {
            $indexResponse = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$NuGetPackage/index.json"
            $Version = $indexResponse.versions | Select-Object -Last 1
            Write-Host "Latest version: $Version"
        } catch {
            Write-Host "Error: Could not determine latest version" -ForegroundColor Red
            Write-Host $_.Exception.Message
            exit 1
        }
    }
    
    # Download NuGet package
    $PackageUrl = "https://www.nuget.org/api/v2/package/$NuGetPackage/$Version"
    $TempDir = New-TemporaryFile | ForEach-Object { Remove-Item $_; New-Item -ItemType Directory -Path $_ }
    $PackageFile = Join-Path $TempDir "morphir.nupkg"
    
    Write-Host "Downloading Morphir $Version..."
    try {
        Invoke-WebRequest -Uri $PackageUrl -OutFile $PackageFile -UseBasicParsing
    } catch {
        Write-Host "Error: Failed to download package" -ForegroundColor Red
        Write-Host $_.Exception.Message
        Remove-Item -Recurse -Force $TempDir
        exit 1
    }
    
    # Extract package (NuGet packages are ZIP files)
    Write-Host "Extracting package..."
    try {
        Expand-Archive -Path $PackageFile -DestinationPath $TempDir -Force
    } catch {
        Write-Host "Error: Failed to extract package" -ForegroundColor Red
        Write-Host $_.Exception.Message
        Remove-Item -Recurse -Force $TempDir
        exit 1
    }
    
    # Find and copy the platform-specific executable
    $ExeSource = Join-Path $TempDir "tools\net10.0\$RID\morphir.exe"
    if (-not (Test-Path $ExeSource)) {
        Write-Host "Error: Platform-specific executable not found for $RID" -ForegroundColor Red
        Write-Host "Available platforms:"
        Get-ChildItem (Join-Path $TempDir "tools\net10.0") -Directory | ForEach-Object { Write-Host "  $($_.Name)" }
        Remove-Item -Recurse -Force $TempDir
        exit 1
    }
    
    # Copy executable to install directory
    Copy-Item -Path $ExeSource -Destination (Join-Path $InstallDir "morphir.exe") -Force
    
    # Cleanup
    Remove-Item -Recurse -Force $TempDir
    
    Write-Host "✓ Morphir CLI installed to $InstallDir\morphir.exe" -ForegroundColor Green
    Write-Host ""
    Write-Host "To use morphir, add $InstallDir to your PATH:"
    Write-Host "  [Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path', 'User') + ';$InstallDir', 'User')"
    Write-Host ""
    Write-Host "Or manually add it via System Properties > Environment Variables"
}

Write-Host ""
Write-Host "Verify installation:"
Write-Host "  morphir --version"

