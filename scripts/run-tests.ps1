# Run tests script for .NET 10 TUnit with Microsoft Testing Platform
# This script runs test executables directly instead of using 'dotnet test'
# to support TUnit's integration with the new Microsoft Testing Platform in .NET 10

param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

Write-Host "Running tests with configuration: $Configuration"
Write-Host "================================================"

# Run Morphir.Core.Tests
Write-Host ""
Write-Host "Running Morphir.Core.Tests..."
dotnet exec "tests/Morphir.Core.Tests/bin/$Configuration/net10.0/Morphir.Core.Tests.dll"
$coreExitCode = $LASTEXITCODE

# Run Morphir.Tooling.Tests
Write-Host ""
Write-Host "Running Morphir.Tooling.Tests..."
dotnet exec "tests/Morphir.Tooling.Tests/bin/$Configuration/net10.0/Morphir.Tooling.Tests.dll"
$toolingExitCode = $LASTEXITCODE

# Check if any tests failed
if ($coreExitCode -ne 0 -or $toolingExitCode -ne 0) {
    Write-Host ""
    Write-Host "================================================"
    Write-Host "Tests FAILED"
    Write-Host "  Morphir.Core.Tests: exit code $coreExitCode"
    Write-Host "  Morphir.Tooling.Tests: exit code $toolingExitCode"
    exit 1
}

Write-Host ""
Write-Host "================================================"
Write-Host "All tests PASSED"
exit 0
