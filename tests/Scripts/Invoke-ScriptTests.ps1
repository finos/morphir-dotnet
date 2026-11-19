# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
#
# Runs the Pester tests of the PowerShell scripts of the repository. The TestScripts target of the build runs it with
# the Pester module that the restore of build/_build.csproj downloaded (a PackageDownload, pinned) and the build output
# of the stub tool:
#
#   ./build.ps1 --target TestScripts
#
# Exit code: 0 when every test passed, else the number of failed tests (Pester Run.Exit).

param(
    # Pester.psd1 of the pinned Pester package.
    [Parameter(Mandatory)] [string] $PesterModule,
    # The build output of tests/Morphir.Mck.Stub.
    [Parameter(Mandatory)] [string] $StubFolder,
    # The folder of the NUnit XML report.
    [Parameter(Mandatory)] [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'
Import-Module -Name $PesterModule -Force

$configuration = New-PesterConfiguration
$configuration.Run.Container = @(
    New-PesterContainer -Path (Join-Path $PSScriptRoot 'install-tool-package.Tests.ps1') -Data @{ StubFolder = $StubFolder }
)
$configuration.Run.Exit = $true
$configuration.Output.Verbosity = 'Detailed'
$configuration.TestResult.Enabled = $true
$configuration.TestResult.OutputFormat = 'NUnitXml'
$configuration.TestResult.OutputPath = Join-Path $ResultsDirectory 'pester.xml'
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

Invoke-Pester -Configuration $configuration
