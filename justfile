# Justfile for Morphir .NET build orchestration
# See https://github.com/casey/just for documentation

# Restore .NET dependencies
restore:
    dotnet restore

# Build the solution
# Usage: just build [CONFIGURATION=Release]
build:
    #!/usr/bin/env bash
    dotnet build --no-restore --configuration ${CONFIGURATION:-Release}

# Run linting/formatting checks (verifies without making changes)
lint:
    dotnet format --verify-no-changes

# Format code (applies formatting changes)
format:
    dotnet format

# Run tests
# Usage: just test [CONFIGURATION=Release]
# On Linux/macOS, uses the shell script
# On Windows, uses the PowerShell script
test:
    #!/usr/bin/env bash
    CONFIG="${CONFIGURATION:-Release}"
    if [ "$(uname)" = "Linux" ] || [ "$(uname)" = "Darwin" ]; then
        ./scripts/run-tests.sh "$CONFIG"
    else
        powershell -ExecutionPolicy Bypass -File ./scripts/run-tests.ps1 -Configuration "$CONFIG"
    fi

# Check task that runs lint
check:
    just lint

# Pre-commit hook task (runs lint)
precommit:
    just lint

# Full CI pipeline: restore, build, test, and check
# Usage: just ci [CONFIGURATION=Release]
ci: restore build test check
    @echo "CI pipeline completed successfully"

# Pack library projects as NuGet packages
# Usage: just pack-libs [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
pack-libs:
    #!/usr/bin/env bash
    CONFIG="${CONFIGURATION:-Release}"
    VERSION="${VERSION:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/packages}"
    mkdir -p "$OUTPUT_DIR"
    
    PACK_ARGS=("--configuration" "$CONFIG" "--output" "$OUTPUT_DIR")
    if [ -n "$VERSION" ]; then
        PACK_ARGS+=("/p:Version=$VERSION")
    fi
    
    echo "Packing Morphir.Core..."
    dotnet pack src/Morphir.Core/Morphir.Core.csproj "${PACK_ARGS[@]}"
    
    echo "Packing Morphir.Tooling..."
    dotnet pack src/Morphir.Tooling/Morphir.Tooling.csproj "${PACK_ARGS[@]}"

# Pack the Morphir CLI as a dotnet tool (standard managed tool)
# Usage: just pack-tool [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
pack-tool:
    #!/usr/bin/env bash
    CONFIG="${CONFIGURATION:-Release}"
    VERSION="${VERSION:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/packages}"
    mkdir -p "$OUTPUT_DIR"
    
    PACK_ARGS=("--configuration" "$CONFIG" "--output" "$OUTPUT_DIR")
    if [ -n "$VERSION" ]; then
        PACK_ARGS+=("/p:Version=$VERSION")
    fi
    
    echo "Packing Morphir CLI as dotnet tool..."
    dotnet pack src/Morphir/Morphir.csproj "${PACK_ARGS[@]}" /p:PackAsTool=true /p:ToolCommandName=morphir

# Build managed DLL for tool entry point (without AOT)
# Usage: just build-tool-dll [CONFIGURATION=Release] [OUTPUT_DIR=./artifacts/tool-dll]
build-tool-dll:
    #!/usr/bin/env bash
    ./scripts/build-tool-dll.sh "${CONFIGURATION:-Release}" "${OUTPUT_DIR:-./artifacts/tool-dll}"

# Pack the Morphir CLI as a dotnet tool with platform-specific executables
# Usage: just pack-tool-platform [CONFIGURATION=Release] [VERSION=] [EXECUTABLES_DIR=./artifacts/executables] [OUTPUT_DIR=./artifacts/packages]
# This packages pre-built trimmed (non-AOT) executables from EXECUTABLES_DIR into a NuGet tool package
# The managed DLL entry point will detect and use the platform-specific executable for the current platform
pack-tool-platform:
    #!/usr/bin/env bash
    ./scripts/pack-tool-platform.sh "${CONFIGURATION:-Release}" "${VERSION:-}" "${EXECUTABLES_DIR:-./artifacts/executables}" "${OUTPUT_DIR:-./artifacts/packages}"

# Pack all projects (libraries and tool)
# Usage: just pack-all [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
pack-all: pack-libs pack-tool
    @echo "All packages created successfully"

# Publish single-file executable for a specific platform
# Usage: just publish-executable <RID> [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/executables]
# This creates a self-contained executable that doesn't require .NET to be installed
# Common RIDs: linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64
publish-executable RID:
    #!/usr/bin/env bash
    set -e
    CONFIG="${CONFIGURATION:-Release}"
    VERSION="${VERSION:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/executables}"
    
    RID_OUTPUT_DIR="$OUTPUT_DIR/{{RID}}"
    mkdir -p "$RID_OUTPUT_DIR"
    
    PUBLISH_ARGS=("--configuration" "$CONFIG" "--self-contained" "true" "/p:PublishSingleFile=true" "/p:PublishTrimmed=true" "/p:PublishAot=true")
    if [ -n "$VERSION" ]; then
        PUBLISH_ARGS+=("/p:Version=$VERSION")
    fi
    
    echo "Publishing single-file executable for {{RID}}..."
    dotnet publish src/Morphir/Morphir.csproj \
        "${PUBLISH_ARGS[@]}" \
        --runtime "{{RID}}" \
        --output "$RID_OUTPUT_DIR"
    
    # Find the executable (name varies by platform and AOT uses project name)
    if [[ "{{RID}}" == win-* ]]; then
        EXE_NAME="Morphir.exe"
    else
        # AOT produces executable with project name (Morphir), not lowercase
        EXE_NAME="Morphir"
    fi
    
    if [ -f "$RID_OUTPUT_DIR/$EXE_NAME" ]; then
        echo "✓ Created: $RID_OUTPUT_DIR/$EXE_NAME"
        ls -lh "$RID_OUTPUT_DIR/$EXE_NAME"
        file "$RID_OUTPUT_DIR/$EXE_NAME"
    else
        echo "✗ Error: Executable not found at $RID_OUTPUT_DIR/$EXE_NAME"
        echo "Contents of $RID_OUTPUT_DIR:"
        ls -la "$RID_OUTPUT_DIR" 2>/dev/null || echo "Directory does not exist"
        exit 1
    fi

# Publish single-file executables for all platforms
# Usage: just publish-executables [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/executables]
# This creates self-contained executables that don't require .NET to be installed
# Note: Cross-compilation may not work on all systems. Use publish-executable for a specific platform.
publish-executables:
    #!/usr/bin/env bash
    set -e
    CONFIG="${CONFIGURATION:-Release}"
    VERSION="${VERSION:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/executables}"
    
    # Runtime Identifiers for different platforms
    RIDS=("linux-x64" "linux-arm64" "win-x64" "osx-x64" "osx-arm64")
    
    for RID in "${RIDS[@]}"; do
        echo ""
        echo "=== Publishing for $RID ==="
        OUTPUT_DIR="$OUTPUT_DIR" CONFIGURATION="$CONFIG" VERSION="$VERSION" just publish-executable "$RID" || {
            echo "⚠ Warning: Failed to publish for $RID (cross-compilation may not be supported)"
            continue
        }
    done
    
    echo ""
    echo "All single-file executables published to $OUTPUT_DIR"

# Publish single-file executable without AOT (managed .NET runtime) with trimming
# Usage: just publish-single-file <RID> [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/single-file]
# This creates a self-contained single-file executable that runs as a managed .NET application
# Trimming is enabled for size optimization (TrimMode=partial for safety)
# Compatible with dependencies that aren't AOT-compatible (e.g., Wolverine)
publish-single-file RID:
    #!/usr/bin/env bash
    ./scripts/publish-single-file.sh "{{RID}}" "${CONFIGURATION:-Release}" "${VERSION:-}" "${OUTPUT_DIR:-./artifacts/single-file}"

# Publish single-file executable without AOT and without trimming (baseline)
# Usage: just publish-single-file-untrimmed <RID> [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/single-file-untrimmed]
# This creates a baseline for size comparison
publish-single-file-untrimmed RID:
    #!/usr/bin/env bash
    ./scripts/publish-single-file-untrimmed.sh "{{RID}}" "${CONFIGURATION:-Release}" "${VERSION:-}" "${OUTPUT_DIR:-./artifacts/single-file-untrimmed}"

# Run end-to-end tests against Morphir executables (BDD/Gherkin)
# Usage: just test-e2e [EXECUTABLE_TYPE=all] [CONFIGURATION=Release]
#   EXECUTABLE_TYPE: aot, trimmed, untrimmed, or all (default)
#   CONFIGURATION: Build configuration (default: Release)
# This builds executables if needed and runs E2E tests
test-e2e EXECUTABLE_TYPE="all":
    #!/usr/bin/env bash
    ./scripts/run-e2e-tests.sh "${EXECUTABLE_TYPE}" "${CONFIGURATION:-Release}"

# Publish library NuGet packages to NuGet.org
# Usage: just publish-libs [NUGET_SOURCE=https://api.nuget.org/v3/index.json] [API_KEY=] [OUTPUT_DIR=./artifacts/packages]
publish-libs:
    #!/usr/bin/env bash
    set -e
    NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
    API_KEY="${API_KEY:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/packages}"
    
    if [ -z "$API_KEY" ]; then
        echo "Error: API_KEY environment variable is required for publishing"
        exit 1
    fi
    
    # Use find to locate packages (more reliable than glob patterns)
    CORE_PACKAGE=$(find "$OUTPUT_DIR" -name "Morphir.Core.*.nupkg" -type f | head -1)
    if [ -z "$CORE_PACKAGE" ]; then
        echo "Error: Morphir.Core package not found in $OUTPUT_DIR"
        echo "Contents of $OUTPUT_DIR:"
        ls -la "$OUTPUT_DIR" 2>/dev/null || echo "Directory does not exist"
        exit 1
    fi
    echo "Publishing Morphir.Core: $CORE_PACKAGE"
    dotnet nuget push "$CORE_PACKAGE" --source "$NUGET_SOURCE" --api-key "$API_KEY" --skip-duplicate
    
    TOOLING_PACKAGE=$(find "$OUTPUT_DIR" -name "Morphir.Tooling.*.nupkg" -type f | head -1)
    if [ -z "$TOOLING_PACKAGE" ]; then
        echo "Error: Morphir.Tooling package not found in $OUTPUT_DIR"
        echo "Contents of $OUTPUT_DIR:"
        ls -la "$OUTPUT_DIR" 2>/dev/null || echo "Directory does not exist"
        exit 1
    fi
    echo "Publishing Morphir.Tooling: $TOOLING_PACKAGE"
    dotnet nuget push "$TOOLING_PACKAGE" --source "$NUGET_SOURCE" --api-key "$API_KEY" --skip-duplicate

# Publish the Morphir CLI tool package to NuGet.org
# Usage: just publish-tool [NUGET_SOURCE=https://api.nuget.org/v3/index.json] [API_KEY=] [OUTPUT_DIR=./artifacts/packages]
publish-tool:
    #!/usr/bin/env bash
    NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
    API_KEY="${API_KEY:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/packages}"
    
    if [ -z "$API_KEY" ]; then
        echo "Error: API_KEY environment variable is required for publishing"
        exit 1
    fi
    
    # Find the Morphir tool package (exclude .Core and .Tooling packages)
    TOOL_PACKAGE=$(find "$OUTPUT_DIR" -name "Morphir.*.nupkg" ! -name "*Morphir.Core*" ! -name "*Morphir.Tooling*" | head -1)
    
    if [ -z "$TOOL_PACKAGE" ]; then
        echo "Error: Morphir tool package not found in $OUTPUT_DIR"
        exit 1
    fi
    
    echo "Publishing Morphir CLI tool: $TOOL_PACKAGE"
    dotnet nuget push "$TOOL_PACKAGE" --source "$NUGET_SOURCE" --api-key "$API_KEY" --skip-duplicate

# Publish all packages (libraries and tool)
# Usage: just publish-all [NUGET_SOURCE=https://api.nuget.org/v3/index.json] [API_KEY=] [OUTPUT_DIR=./artifacts/packages]
publish-all: publish-libs publish-tool
    @echo "All packages published successfully"

# Publish library packages to a local NuGet source
# Usage: just publish-local-libs [LOCAL_SOURCE=./artifacts/local-feed] [OUTPUT_DIR=./artifacts/packages]
publish-local-libs:
    #!/usr/bin/env bash
    LOCAL_SOURCE="${LOCAL_SOURCE:-./artifacts/local-feed}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/packages}"
    
    mkdir -p "$LOCAL_SOURCE"
    
    # Check if source already exists, if not add it
    if ! dotnet nuget list source | grep -q "$LOCAL_SOURCE"; then
        echo "Adding local NuGet source: $LOCAL_SOURCE"
        dotnet nuget add source "$LOCAL_SOURCE" --name local-feed || true
    fi
    
    echo "Publishing Morphir.Core to local feed..."
    dotnet nuget push "$OUTPUT_DIR"/*.Morphir.Core.*.nupkg --source "$LOCAL_SOURCE" --skip-duplicate || true
    
    echo "Publishing Morphir.Tooling to local feed..."
    dotnet nuget push "$OUTPUT_DIR"/*.Morphir.Tooling.*.nupkg --source "$LOCAL_SOURCE" --skip-duplicate || true
    
    echo "Libraries published to local feed: $LOCAL_SOURCE"

# Install the Morphir CLI tool locally from the package
# Usage: just publish-local-tool [OUTPUT_DIR=./artifacts/packages] [GLOBAL=false]
publish-local-tool:
    #!/usr/bin/env bash
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/packages}"
    GLOBAL="${GLOBAL:-false}"
    
    # Find the Morphir tool package (exclude .Core and .Tooling packages)
    TOOL_PACKAGE=$(find "$OUTPUT_DIR" -name "Morphir.*.nupkg" ! -name "*Morphir.Core*" ! -name "*Morphir.Tooling*" | head -1)
    
    if [ -z "$TOOL_PACKAGE" ]; then
        echo "Error: Morphir tool package not found in $OUTPUT_DIR"
        echo "Please run 'just pack-tool' first"
        exit 1
    fi
    
    if [ "$GLOBAL" = "true" ]; then
        echo "Installing Morphir CLI tool globally from: $TOOL_PACKAGE"
        dotnet tool install --global --add-source "$OUTPUT_DIR" Morphir || \
        dotnet tool update --global --add-source "$OUTPUT_DIR" Morphir
    else
        echo "Installing Morphir CLI tool locally from: $TOOL_PACKAGE"
        dotnet tool install --add-source "$OUTPUT_DIR" Morphir || \
        dotnet tool update --add-source "$OUTPUT_DIR" Morphir
    fi
    
    echo "Morphir CLI tool installed successfully"

# Publish all packages locally (libraries to local feed, tool installed locally)
# Usage: just publish-local-all [LOCAL_SOURCE=./artifacts/local-feed] [OUTPUT_DIR=./artifacts/packages] [GLOBAL=false]
publish-local-all: publish-local-libs publish-local-tool
    @echo "All packages published locally successfully"

