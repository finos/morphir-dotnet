#!/usr/bin/env bash
set -e

# Publish single-file executable without AOT and without trimming (baseline)
# Usage: ./scripts/publish-single-file-untrimmed.sh [RID] [CONFIGURATION] [VERSION] [OUTPUT_DIR]
#   RID: Runtime identifier (e.g., linux-arm64, win-x64)
#   CONFIGURATION: Build configuration (default: Release)
#   VERSION: Package version (optional)
#   OUTPUT_DIR: Output directory (default: ./artifacts/single-file-untrimmed)

RID="${1:-}"
if [ -z "$RID" ]; then
    echo "Error: RID is required"
    echo "Usage: $0 <RID> [CONFIGURATION] [VERSION] [OUTPUT_DIR]"
    exit 1
fi

CONFIG="${2:-Release}"
VERSION="${3:-}"
OUTPUT_DIR="${4:-./artifacts/single-file-untrimmed}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

# Generate Wolverine code before publishing (required for Static mode)
# Use Morphir CLI's codegen command (no Oakton needed - uses System.CommandLine)
echo "Generating Wolverine code..."
dotnet build src/Morphir/Morphir.csproj --configuration "$CONFIG" --no-restore || {
    echo "⚠ Warning: Build failed, attempting code generation anyway"
}
# Run codegen write command via Morphir CLI
dotnet run --project src/Morphir/Morphir.csproj --configuration "$CONFIG" --no-build -- codegen write || {
    echo "⚠ Warning: Code generation failed, continuing with build"
}
# Verify generated code exists
if [ -d "src/Morphir.Tooling/Internal/Generated" ]; then
    echo "✓ Found generated code directory"
    find src/Morphir.Tooling/Internal/Generated -name "*.cs" | wc -l | xargs echo "  Generated files:"
else
    echo "⚠ Warning: Generated code directory not found at src/Morphir.Tooling/Internal/Generated"
fi

RID_OUTPUT_DIR="$OUTPUT_DIR/$RID"
mkdir -p "$RID_OUTPUT_DIR"

PUBLISH_ARGS=(
    "--configuration" "$CONFIG"
    "--self-contained" "true"
    "--property:PublishSingleFile=true"
)

if [ -n "$VERSION" ]; then
    PUBLISH_ARGS+=("--property:Version=$VERSION")
fi

echo "Publishing single-file executable (managed, untrimmed) for $RID..."
dotnet publish src/Morphir/Morphir.csproj \
    "${PUBLISH_ARGS[@]}" \
    --runtime "$RID" \
    --output "$RID_OUTPUT_DIR"

# Find the executable (single-file executables use lowercase name)
if [[ "$RID" == win-* ]]; then
    EXE_NAME="morphir.exe"
else
    EXE_NAME="morphir"
fi

if [ -f "$RID_OUTPUT_DIR/$EXE_NAME" ]; then
    SIZE=$(du -h "$RID_OUTPUT_DIR/$EXE_NAME" | cut -f1)
    echo "✓ Created: $RID_OUTPUT_DIR/$EXE_NAME"
    ls -lh "$RID_OUTPUT_DIR/$EXE_NAME"
    file "$RID_OUTPUT_DIR/$EXE_NAME"
    echo ""
    echo "Executable size: $SIZE"
else
    echo "✗ Error: Executable not found at $RID_OUTPUT_DIR/$EXE_NAME (or lowercase variant)"
    echo "Contents of $RID_OUTPUT_DIR:"
    ls -la "$RID_OUTPUT_DIR" 2>/dev/null || echo "Directory does not exist"
    exit 1
fi

