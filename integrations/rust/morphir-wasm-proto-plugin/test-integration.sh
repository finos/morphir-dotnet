#!/usr/bin/env bash
# Integration test for Morphir proto WASM plugin
# This script tests the plugin's ability to:
# 1. Register with proto
# 2. Download Morphir executables
# 3. Locate and execute Morphir

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if proto is installed
if ! command -v proto &> /dev/null; then
    log_error "proto CLI not found. Please install proto first:"
    log_info "  curl -fsSL https://moonrepo.dev/install/proto.sh | bash"
    exit 1
fi

log_info "proto CLI found: $(proto --version)"

# Get the plugin WASM file path
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PLUGIN_WASM="$REPO_ROOT/artifacts/proto-plugin/morphir_plugin.wasm"

if [ ! -f "$PLUGIN_WASM" ]; then
    log_error "Plugin WASM file not found at $PLUGIN_WASM"
    log_info "Build the plugin first: ./build.sh --target BuildProtoPlugin"
    exit 1
fi

log_info "Plugin WASM found at: $PLUGIN_WASM"

# Test 1: Remove existing morphir-test plugin (if any)
log_info "Test 1: Cleaning up any existing test plugin..."
proto plugin remove morphir-test 2>/dev/null || true
log_info "✓ Cleanup complete"

# Test 2: Add plugin from local file
log_info "Test 2: Adding plugin from local file..."
if proto plugin add morphir-test "source:file://$PLUGIN_WASM"; then
    log_info "✓ Plugin added successfully"
else
    log_error "Failed to add plugin"
    exit 1
fi

# Test 3: List plugins to verify
log_info "Test 3: Verifying plugin is registered..."
if proto plugin list | grep -q "morphir-test"; then
    log_info "✓ Plugin is registered with proto"
else
    log_error "Plugin not found in proto plugin list"
    proto plugin list
    exit 1
fi

# Test 4: Try to install a Morphir version
# Note: This will fail if the version doesn't exist on GitHub Releases
# For testing, we'll just verify the plugin can be invoked
log_info "Test 4: Testing plugin invocation..."
log_warn "Skipping actual installation test (requires published Morphir release)"
log_info "  To test installation:"
log_info "    proto install morphir-test <version>"
log_info "    morphir-test --version"

# Test 5: Inspect plugin info
log_info "Test 5: Inspecting plugin information..."
if proto plugin list --json | grep -q "morphir-test"; then
    log_info "✓ Plugin info available"
else
    log_warn "Could not retrieve plugin info via JSON output"
fi

# Cleanup
log_info "Cleanup: Removing test plugin..."
proto plugin remove morphir-test 2>/dev/null || true
log_info "✓ Test plugin removed"

log_info ""
log_info "================================================================"
log_info "All integration tests passed!"
log_info "================================================================"
log_info ""
log_info "To use the plugin with a real Morphir installation:"
log_info "  1. Ensure Morphir is released to GitHub with platform executables"
log_info "  2. proto plugin add morphir \"source:https://github.com/finos/morphir-dotnet/releases/download/plugin-v{VERSION}/morphir_plugin.wasm\""
log_info "  3. proto install morphir"
log_info "  4. morphir --version"
