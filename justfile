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
# Uses C# script for cross-platform compatibility
test:
    #!/usr/bin/env bash
    CONFIG="${CONFIGURATION:-Release}"
    dotnet run scripts/run-tests.cs "$CONFIG"

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
    dotnet run scripts/build-tool-dll.cs "${CONFIGURATION:-Release}" "${OUTPUT_DIR:-./artifacts/tool-dll}"

# Pack the Morphir CLI as a dotnet tool (managed DLL only)
# Usage: just pack-tool-platform [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
# This packages the managed DLL as a dotnet tool named 'dotnet-morphir'
# The managed DLL will run on any platform with .NET installed (no platform-specific executables needed)
pack-tool-platform:
    #!/usr/bin/env bash
    dotnet run scripts/pack-tool-platform.cs "${CONFIGURATION:-Release}" "${VERSION:-}" "${OUTPUT_DIR:-./artifacts/packages}"

# Pack all projects (libraries and tool)
# Usage: just pack-all [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
pack-all: pack-libs pack-tool
    @echo "All packages created successfully"

# Publish the Native AOT executable (the native-aot profile: no debug symbols, no documentation files)
# Usage: just publish-executable <RID> [CONFIGURATION=Release] [OUTPUT_DIR=./artifacts/executables]
# Runs the Nuke target PublishExecutable. The version comes from CHANGELOG.md. No .NET is needed to run the result.
# Common RIDs: linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64
publish-executable RID:
    #!/usr/bin/env bash
    ./build.sh --target PublishExecutable --rid "{{RID}}" --configuration "${CONFIGURATION:-Release}" --executables-dir "${OUTPUT_DIR:-./artifacts/executables}"

# Publish the Native AOT executable for all platforms
# Usage: just publish-executables [CONFIGURATION=Release] [OUTPUT_DIR=./artifacts/executables]
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

# Publish the legacy single-file release asset: managed, trimmed (TrimMode=partial), self-contained, no F# frontend
# Usage: just publish-single-file <RID> [CONFIGURATION=Release] [OUTPUT_DIR=./artifacts/single-file]
# Runs the Nuke target PublishSingleFile. The version comes from CHANGELOG.md.
publish-single-file RID:
    #!/usr/bin/env bash
    ./build.sh --target PublishSingleFile --rid "{{RID}}" --configuration "${CONFIGURATION:-Release}" --single-file-dir "${OUTPUT_DIR:-./artifacts/single-file}"

# Publish single-file executable without AOT and without trimming (baseline for size comparison, never released)
# Usage: just publish-single-file-untrimmed <RID> [CONFIGURATION=Release] [OUTPUT_DIR=./artifacts/single-file-untrimmed]
# Runs the Nuke target PublishSingleFileUntrimmed. The version comes from CHANGELOG.md.
publish-single-file-untrimmed RID:
    #!/usr/bin/env bash
    ./build.sh --target PublishSingleFileUntrimmed --rid "{{RID}}" --configuration "${CONFIGURATION:-Release}" --single-file-untrimmed-dir "${OUTPUT_DIR:-./artifacts/single-file-untrimmed}"

# Build the black-box test project (tests/Morphir.BlackBox.Tests), with the hosts of the development build
# Usage: just build-e2e-tests [CONFIGURATION=Release]
build-e2e-tests:
    #!/usr/bin/env bash
    CONFIG="${CONFIGURATION:-Release}"
    dotnet restore tests/Morphir.BlackBox.Tests/Morphir.BlackBox.Tests.csproj
    dotnet build tests/Morphir.BlackBox.Tests/Morphir.BlackBox.Tests.csproj \
        --configuration "$CONFIG" \
        --no-restore

# Run each group of the black-box tests against a published or packaged target
# Usage: just test-e2e [EXECUTABLE_TYPE=all] [CONFIGURATION=Release]
#   EXECUTABLE_TYPE: a value of MORPHIR_TEST_TARGET (aot, trimmed, untrimmed, native-aot, ...), tool, all or offerings
#   CONFIGURATION: Build configuration (default: Release)
# This publishes or packs a target that has no artifact, and runs the black-box tests against it
# Dependencies: build-e2e-tests ensures the test project is built
test-e2e EXECUTABLE_TYPE="all": build-e2e-tests
    #!/usr/bin/env bash
    dotnet run scripts/run-e2e-tests.cs "${EXECUTABLE_TYPE}" "${CONFIGURATION:-Release}"

# Build and test a single-file executable for a specific platform
# Usage: just build-and-test <RID> [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/single-file]
#   RID: Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)
#   CONFIGURATION: Build configuration (default: Release)
#   VERSION: Version string for the executable (optional)
#   OUTPUT_DIR: Output directory for the executable (default: ./artifacts/single-file)
# This builds the executable and then runs E2E tests against it
# Dependencies: publish-single-file builds the executable, build-e2e-tests ensures tests are ready
build-and-test RID: build-e2e-tests
    #!/usr/bin/env bash
    CONFIG="${CONFIGURATION:-Release}"
    VERSION="${VERSION:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/single-file}"
    
    echo "================================================"
    echo "Building and testing executable for {{RID}}"
    echo "================================================"
    echo ""
    
    # Build the executable
    echo "Building executable for {{RID}}..."
    just publish-single-file "{{RID}}"
    
    # Set executable permissions for Unix platforms
    if [[ "{{RID}}" != win-* ]]; then
        EXE_PATH="$OUTPUT_DIR/{{RID}}/morphir"
        if [ -f "$EXE_PATH" ]; then
            chmod +x "$EXE_PATH"
            echo "✓ Executable permissions set for {{RID}}/morphir"
        fi
    fi
    
    # Run E2E tests against the trimmed executable
    echo "Running E2E tests for {{RID}}..."
    dotnet run scripts/run-e2e-tests.cs "trimmed" "$CONFIG"
    
    echo ""
    echo "✓ Build and test completed successfully for {{RID}}"

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

