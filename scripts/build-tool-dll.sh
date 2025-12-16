#!/usr/bin/env bash
set -e

# Build managed DLL for tool entry point (without AOT)
# Usage: ./scripts/build-tool-dll.sh [CONFIGURATION] [OUTPUT_DIR]
#   CONFIGURATION: Build configuration (default: Release)
#   OUTPUT_DIR: Output directory for the DLL (default: ./artifacts/tool-dll)

CONFIG="${1:-Release}"
OUTPUT_DIR="${2:-./artifacts/tool-dll}"

mkdir -p "$OUTPUT_DIR"

echo "Building managed DLL for tool entry point (without AOT)..."
# Build without AOT by temporarily disabling it
dotnet build src/Morphir/Morphir.csproj \
    --configuration "$CONFIG" \
    --no-restore \
    /p:PublishAot=false \
    /p:OutputPath="$OUTPUT_DIR"

# Copy the DLL to the expected location
if [ -f "$OUTPUT_DIR/morphir.dll" ]; then
    echo "✓ Managed DLL created: $OUTPUT_DIR/morphir.dll"
else
    echo "✗ Error: morphir.dll not found in $OUTPUT_DIR"
    exit 1
fi

