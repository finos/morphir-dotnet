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

# Full CI pipeline: restore, build, test, and check
# Usage: just ci [CONFIGURATION=Release]
ci: restore build test check
    @echo "CI pipeline completed successfully"

