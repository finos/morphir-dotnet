#!/usr/bin/env bash
set -e

# Pack the Morphir CLI as a dotnet tool with platform-specific executables
# Usage: ./scripts/pack-tool-platform.sh [CONFIGURATION] [VERSION] [EXECUTABLES_DIR] [OUTPUT_DIR]
#   CONFIGURATION: Build configuration (default: Release)
#   VERSION: Package version (optional)
#   EXECUTABLES_DIR: Directory containing platform-specific executables (default: ./artifacts/executables)
#   OUTPUT_DIR: Output directory for the package (default: ./artifacts/packages)
#
# This packages pre-built trimmed (non-AOT) executables from EXECUTABLES_DIR into a NuGet tool package
# The managed DLL entry point will detect and use the platform-specific executable for the current platform

CONFIG="${1:-Release}"
VERSION="${2:-}"
EXECUTABLES_DIR="${3:-./artifacts/executables}"
OUTPUT_DIR="${4:-./artifacts/packages}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

mkdir -p "$OUTPUT_DIR"

# First, build the managed DLL entry point
echo "Building managed DLL entry point..."
"$SCRIPT_DIR/build-tool-dll.sh" "$CONFIG" "./artifacts/tool-dll"
DLL_DIR="./artifacts/tool-dll"

# Create temporary directory for package structure
PACKAGE_ROOT=$(mktemp -d)
TOOLS_DIR="$PACKAGE_ROOT/tools/net10.0"
mkdir -p "$TOOLS_DIR/any"

# Copy the managed DLL to the 'any' folder (entry point for all platforms)
if [ -f "$DLL_DIR/morphir.dll" ]; then
    cp "$DLL_DIR/morphir.dll" "$TOOLS_DIR/any/morphir.dll"
    echo "✓ Copied managed DLL entry point"
else
    echo "✗ Error: Managed DLL not found at $DLL_DIR/morphir.dll"
    rm -rf "$PACKAGE_ROOT"
    exit 1
fi

# Map of RIDs to their executable names (trimmed executables use lowercase)
declare -A RID_EXECUTABLES=(
    ["linux-x64"]="morphir"
    ["linux-arm64"]="morphir"
    ["win-x64"]="morphir.exe"
    ["osx-x64"]="morphir"
    ["osx-arm64"]="morphir"
)

# Copy executables to the correct RID folders
for RID in "${!RID_EXECUTABLES[@]}"; do
    EXE_NAME="${RID_EXECUTABLES[$RID]}"
    RID_DIR="$TOOLS_DIR/$RID"
    mkdir -p "$RID_DIR"
    
    # Find the executable in the artifacts directory
    # Handle both direct structure (linux-x64/morphir) and artifact structure (morphir-linux-x64/linux-x64/morphir)
    EXE_PATH=$(find "$EXECUTABLES_DIR" -path "*/$RID/$EXE_NAME" -type f | head -1)
    
    if [ -z "$EXE_PATH" ]; then
        # Try alternative paths (artifact might be in a different structure)
        EXE_PATH=$(find "$EXECUTABLES_DIR" -name "$EXE_NAME" -path "*$RID*" -type f | head -1)
    fi
    
    if [ -n "$EXE_PATH" ] && [ -f "$EXE_PATH" ]; then
        # For Windows, keep .exe extension; for others, use 'morphir'
        if [[ "$RID" == win-* ]]; then
            cp "$EXE_PATH" "$RID_DIR/morphir.exe"
        else
            cp "$EXE_PATH" "$RID_DIR/morphir"
            chmod +x "$RID_DIR/morphir"
        fi
        echo "✓ Copied $RID executable: $EXE_PATH"
    else
        echo "⚠ Warning: Executable not found for $RID (looking for $EXE_NAME)"
        echo "Searched in: $EXECUTABLES_DIR"
        find "$EXECUTABLES_DIR" -type f -name "*morphir*" 2>/dev/null | head -5 || true
    fi
done

# Create a minimal .csproj for packaging
PACK_PROJ="$PACKAGE_ROOT/Morphir.Tool.Pack.csproj"
printf '%s\n' \
    '<Project Sdk="Microsoft.NET.Sdk">' \
    '    <PropertyGroup>' \
    '        <TargetFramework>net10.0</TargetFramework>' \
    '        <PackageId>Morphir</PackageId>' \
    '        <PackageType>DotnetTool</PackageType>' \
    '        <ToolCommandName>morphir</ToolCommandName>' \
    '        <PackAsTool>true</PackAsTool>' \
    '        <NoBuild>true</NoBuild>' \
    '        <IncludeBuildOutput>false</IncludeBuildOutput>' \
    '    </PropertyGroup>' \
    '    <ItemGroup>' \
    '        <None Include="tools/**/*" Pack="true" PackagePath="tools/" />' \
    '    </ItemGroup>' \
    '</Project>' > "$PACK_PROJ"

# Set version if provided
PACK_ARGS=("--output" "$OUTPUT_DIR" "--no-build" "--no-restore")
if [ -n "$VERSION" ]; then
    PACK_ARGS+=("/p:Version=$VERSION")
fi

echo "Packing Morphir CLI tool with platform-specific executables..."
cd "$PACKAGE_ROOT"
dotnet pack "$PACK_PROJ" "${PACK_ARGS[@]}"

# Cleanup
rm -rf "$PACKAGE_ROOT"

echo "✓ Tool package created in $OUTPUT_DIR"

