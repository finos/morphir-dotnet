#!/usr/bin/env bash
set -e

# Run end-to-end tests against Morphir executables
# Usage: ./scripts/run-e2e-tests.sh [EXECUTABLE_TYPE] [CONFIGURATION]
#   EXECUTABLE_TYPE: aot, trimmed, untrimmed, or all (default: all)
#   CONFIGURATION: Build configuration (default: Release)
# This builds executables if needed and runs E2E tests

EXECUTABLE_TYPE="${1:-all}"
CONFIG="${2:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

# Detect current platform RID
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    if [[ $(uname -m) == "aarch64" ]] || [[ $(uname -m) == "arm64" ]]; then
        RID="linux-arm64"
    else
        RID="linux-x64"
    fi
elif [[ "$OSTYPE" == "darwin"* ]]; then
    if [[ $(uname -m) == "arm64" ]]; then
        RID="osx-arm64"
    else
        RID="osx-x64"
    fi
elif [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "win32" ]]; then
    RID="win-x64"
else
    echo "Unknown platform: $OSTYPE"
    exit 1
fi

echo "Detected platform RID: $RID"
echo "Executable type: $EXECUTABLE_TYPE"
echo "Configuration: $CONFIG"

# Determine which executable types to test
if [[ "$EXECUTABLE_TYPE" == "all" ]]; then
    EXECUTABLE_TYPES=("aot" "trimmed" "untrimmed")
else
    EXECUTABLE_TYPES=("$EXECUTABLE_TYPE")
fi

# Build all executables that will be tested
for TYPE in "${EXECUTABLE_TYPES[@]}"; do
    if [[ "$TYPE" == "aot" ]]; then
        EXE_PATH="$PROJECT_ROOT/artifacts/executables/$RID/morphir"
        if [[ "$RID" == win-* ]]; then
            EXE_PATH="$PROJECT_ROOT/artifacts/executables/$RID/morphir.exe"
        fi
        
        if [ ! -f "$EXE_PATH" ]; then
            echo "Building AOT executable for $RID..."
            CONFIGURATION="$CONFIG" just publish-executable "$RID"
        else
            echo "✓ AOT executable found: $EXE_PATH"
        fi
    elif [[ "$TYPE" == "trimmed" ]]; then
        EXE_PATH="$PROJECT_ROOT/artifacts/single-file/$RID/morphir"
        if [[ "$RID" == win-* ]]; then
            EXE_PATH="$PROJECT_ROOT/artifacts/single-file/$RID/morphir.exe"
        fi
        
        if [ ! -f "$EXE_PATH" ]; then
            echo "Building trimmed single-file executable for $RID..."
            if just --list | grep -q "publish-single-file"; then
                CONFIGURATION="$CONFIG" just publish-single-file "$RID"
            else
                echo "⚠ Warning: publish-single-file task not found, skipping trimmed executable build"
            fi
        else
            echo "✓ Trimmed executable found: $EXE_PATH"
        fi
    elif [[ "$TYPE" == "untrimmed" ]]; then
        EXE_PATH="$PROJECT_ROOT/artifacts/single-file-untrimmed/$RID/morphir"
        if [[ "$RID" == win-* ]]; then
            EXE_PATH="$PROJECT_ROOT/artifacts/single-file-untrimmed/$RID/morphir.exe"
        fi
        
        if [ ! -f "$EXE_PATH" ]; then
            echo "Building untrimmed single-file executable for $RID..."
            if just --list | grep -q "publish-single-file-untrimmed"; then
                CONFIGURATION="$CONFIG" just publish-single-file-untrimmed "$RID"
            else
                echo "⚠ Warning: publish-single-file-untrimmed task not found, skipping untrimmed executable build"
            fi
        else
            echo "✓ Untrimmed executable found: $EXE_PATH"
        fi
    fi
done

# Build the E2E test project
echo ""
echo "Building E2E test project..."
dotnet build tests/Morphir.E2E.Tests/Morphir.E2E.Tests.csproj \
    --configuration "$CONFIG" \
    --no-restore

# Track results for each executable type
FAILED_TYPES=()
PASSED_TYPES=()

# Run E2E tests for each executable type
for TYPE in "${EXECUTABLE_TYPES[@]}"; do
    echo ""
    echo "================================================"
    echo "Testing executable type: $TYPE"
    echo "================================================"
    
    # Set environment variable for executable type
    export MORPHIR_EXECUTABLE_TYPE="$TYPE"
    
    # Run tests (use || true to prevent set -e from exiting on failure)
    EXIT_CODE=0
    dotnet exec "tests/Morphir.E2E.Tests/bin/$CONFIG/net10.0/Morphir.E2E.Tests.dll" || EXIT_CODE=$?
    
    if [ $EXIT_CODE -eq 0 ]; then
        echo ""
        echo "✓ E2E tests PASSED for $TYPE"
        PASSED_TYPES+=("$TYPE")
    else
        echo ""
        echo "✗ E2E tests FAILED for $TYPE (exit code: $EXIT_CODE)"
        FAILED_TYPES+=("$TYPE")
    fi
done

# Report summary
echo ""
echo "================================================"
echo "E2E Test Summary"
echo "================================================"
if [ ${#PASSED_TYPES[@]} -gt 0 ]; then
    echo "✓ Passed: ${PASSED_TYPES[*]}"
fi
if [ ${#FAILED_TYPES[@]} -gt 0 ]; then
    echo "✗ Failed: ${FAILED_TYPES[*]}"
fi
echo "================================================"

# Exit with failure if any executable type failed
if [ ${#FAILED_TYPES[@]} -gt 0 ]; then
    exit 1
else
    exit 0
fi

