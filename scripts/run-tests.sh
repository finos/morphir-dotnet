#!/usr/bin/env bash
set -e

# Run tests script for .NET 10 TUnit with Microsoft Testing Platform
# This script runs test executables directly instead of using 'dotnet test'
# to support TUnit's integration with the new Microsoft Testing Platform in .NET 10

CONFIGURATION="${1:-Debug}"

echo "Running tests with configuration: $CONFIGURATION"
echo "================================================"

# Run Morphir.Core.Tests
echo ""
echo "Running Morphir.Core.Tests..."
dotnet exec "tests/Morphir.Core.Tests/bin/$CONFIGURATION/net10.0/Morphir.Core.Tests.dll"
CORE_EXIT_CODE=$?

# Run Morphir.Tooling.Tests
echo ""
echo "Running Morphir.Tooling.Tests..."
dotnet exec "tests/Morphir.Tooling.Tests/bin/$CONFIGURATION/net10.0/Morphir.Tooling.Tests.dll"
TOOLING_EXIT_CODE=$?

# Check if any tests failed
if [ $CORE_EXIT_CODE -ne 0 ] || [ $TOOLING_EXIT_CODE -ne 0 ]; then
    echo ""
    echo "================================================"
    echo "Tests FAILED"
    echo "  Morphir.Core.Tests: exit code $CORE_EXIT_CODE"
    echo "  Morphir.Tooling.Tests: exit code $TOOLING_EXIT_CODE"
    exit 1
fi

echo ""
echo "================================================"
echo "All tests PASSED"
exit 0
