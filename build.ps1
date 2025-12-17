#!/usr/bin/env pwsh

$DotNetInstallerUri = 'https://dot.net/v1/dotnet-install.ps1';
$DotNetUnixInstallerUri = 'https://dot.net/v1/dotnet-install.sh';
$DotNetChannel = 'LTS';

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_MULTILEVEL_LOOKUP = 0

###########################################################################
# CONFIGURATION
###########################################################################

$BuildProjectFile = "$PSScriptRoot\build\_build.csproj"
$TempDirectory = "$PSScriptRoot\\.nuke\temp"

$DotNetGlobalFile = "$PSScriptRoot\\global.json"
$DotNetInstallUrl = if ($IsWin) { $DotNetInstallerUri } else { $DotNetUnixInstallerUri }
$DotNetChannel = if ($DotNetGlobalFile -and (Test-Path $DotNetGlobalFile)) {
    (Get-Content $DotNetGlobalFile | ConvertFrom-Json).sdk.version
} else { $DotNetChannel }

###########################################################################
# EXECUTION
###########################################################################

function ExecSafe([scriptblock] $cmd) {
    & $cmd
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

# If dotnet CLI is installed globally and it matches requested version, use for execution
if ($null -ne (Get-Command "dotnet" -ErrorAction SilentlyContinue) -and `
     $(dotnet --version 2>&1) -and `
     $LASTEXITCODE -eq 0) {
    $env:DOTNET_EXE = (Get-Command "dotnet").Path
}

Write-Output "Microsoft (R) .NET SDK version $(& $env:DOTNET_EXE --version)"

ExecSafe { & dotnet build $BuildProjectFile /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet }
ExecSafe { & dotnet run --project $BuildProjectFile --no-build -- $Args }
