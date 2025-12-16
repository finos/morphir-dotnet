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

# Pack the Morphir CLI as a dotnet tool
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

# Pack all projects (libraries and tool)
# Usage: just pack-all [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
pack-all: pack-libs pack-tool
    @echo "All packages created successfully"

# Publish library NuGet packages to NuGet.org
# Usage: just publish-libs [NUGET_SOURCE=https://api.nuget.org/v3/index.json] [API_KEY=] [OUTPUT_DIR=./artifacts/packages]
publish-libs:
    #!/usr/bin/env bash
    NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
    API_KEY="${API_KEY:-}"
    OUTPUT_DIR="${OUTPUT_DIR:-./artifacts/packages}"
    
    if [ -z "$API_KEY" ]; then
        echo "Error: API_KEY environment variable is required for publishing"
        exit 1
    fi
    
    echo "Publishing Morphir.Core..."
    dotnet nuget push "$OUTPUT_DIR"/*.Morphir.Core.*.nupkg --source "$NUGET_SOURCE" --api-key "$API_KEY" --skip-duplicate
    
    echo "Publishing Morphir.Tooling..."
    dotnet nuget push "$OUTPUT_DIR"/*.Morphir.Tooling.*.nupkg --source "$NUGET_SOURCE" --api-key "$API_KEY" --skip-duplicate

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

