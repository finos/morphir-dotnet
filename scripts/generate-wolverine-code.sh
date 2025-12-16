#!/usr/bin/env bash
set -e

# Generate Wolverine code using the Morphir executable
# Usage: ./scripts/generate-wolverine-code.sh [CONFIGURATION]
#   CONFIGURATION: Build configuration (default: Release)

CONFIG="${1:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

echo "Building Morphir project for code generation..."
dotnet build src/Morphir/Morphir.csproj --configuration "$CONFIG" --no-restore

echo "Generating Wolverine code..."
# Create a temporary entry point that runs codegen
# We'll use dotnet exec with a simple C# program that initializes Wolverine and runs codegen
dotnet exec "$(dotnet build src/Morphir/Morphir.csproj --configuration "$CONFIG" --no-restore -nologo -v q 2>&1 | grep -oP '(?<=-> ).*Morphir\.dll')" codegen write || {
    # If that doesn't work, try using JasperFx.CodeGeneration.Commands directly
    echo "Attempting alternative code generation method..."
    dotnet tool run jasper-codegen -- codegen write --project src/Morphir.Tooling/Morphir.Tooling.csproj || {
        echo "⚠ Warning: Direct codegen command not available, trying build-time generation..."
        # Build will trigger code generation if configured
        dotnet build src/Morphir.Tooling/Morphir.Tooling.csproj --configuration "$CONFIG"
    }
}

# Check if code was generated
if [ -d "src/Morphir.Tooling/Internal/Generated" ]; then
    echo "✓ Wolverine code generated successfully"
    find src/Morphir.Tooling/Internal/Generated -name "*.cs" | wc -l | xargs echo "Generated files:"
else
    echo "⚠ Warning: Generated code directory not found"
fi

