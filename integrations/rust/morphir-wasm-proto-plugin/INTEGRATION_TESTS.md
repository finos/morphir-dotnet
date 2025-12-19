# Proto Plugin Integration Tests

This document describes the BDD-style integration tests for the Morphir proto plugin.

## Prerequisites

- `proto` CLI installed (https://moonrepo.dev/proto)
- Built WASM plugin at `target/wasm32-wasip1/release/morphir_wasm_proto_plugin.wasm`

## Test Scenarios

### Feature: Proto Plugin Registration

#### Scenario: Plugin can be registered with proto
```gherkin
Given the Morphir WASM plugin is built
When I add the plugin using "proto plugin add morphir-test"
Then the plugin should appear in "proto plugin list"
And the plugin should be named "Morphir"
```

**Test Command:**
```bash
./test-integration.sh
```

**Expected:** Exit code 0, all tests pass

---

### Feature: Platform Detection

#### Scenario: Plugin detects Linux x64 platform
```gherkin
Given the plugin is running on Linux x64
When proto requests the download URL
Then the URL should contain "linux-x64"
And the executable name should be "morphir"
```

#### Scenario: Plugin detects macOS ARM64 platform
```gherkin
Given the plugin is running on macOS ARM64
When proto requests the download URL
Then the URL should contain "osx-arm64"
And the executable name should be "morphir"
```

#### Scenario: Plugin detects Windows x64 platform
```gherkin
Given the plugin is running on Windows x64
When proto requests the download URL
Then the URL should contain "win-x64"
And the executable name should be "morphir.exe"
```

#### Scenario: Plugin rejects unsupported platform
```gherkin
Given the plugin is running on an unsupported platform
When proto requests the download URL
Then the plugin should return an error
And the error should mention "Unsupported platform"
```

---

### Feature: Version Resolution

#### Scenario: Plugin resolves specific version
```gherkin
Given a valid version "1.0.0"
When proto requests version resolution
Then the plugin should accept the version
And prepare download URL for version "1.0.0"
```

#### Scenario: Plugin handles pre-release versions
```gherkin
Given a pre-release version "1.0.0-rc.1"
When proto requests version resolution
Then the plugin should accept the version
And prepare download URL for version "1.0.0-rc.1"
```

---

### Feature: Download URL Generation

#### Scenario: Plugin generates correct GitHub Release URL
```gherkin
Given version "1.0.0" and platform "linux-x64"
When proto requests the download URL
Then the URL should be:
  "https://github.com/finos/morphir-dotnet/releases/download/v1.0.0/morphir-linux-x64-v1.0.0.tar.gz"
```

#### Scenario Outline: All platforms generate correct URLs
```gherkin
Given version "2.0.0" and platform "<platform>"
When proto requests the download URL
Then the URL should contain "morphir-<platform>-v2.0.0.tar.gz"

Examples:
  | platform     |
  | linux-x64    |
  | linux-arm64  |
  | osx-x64      |
  | osx-arm64    |
  | win-x64      |
```

---

### Feature: Executable Location

#### Scenario: Plugin locates executable on Unix
```gherkin
Given the plugin is on a Unix platform (Linux/macOS)
When proto requests executable location
Then the plugin should return "morphir" (no extension)
```

#### Scenario: Plugin locates executable on Windows
```gherkin
Given the plugin is on Windows
When proto requests executable location
Then the plugin should return "morphir.exe"
```

---

### Feature: Post-Install Hook

#### Scenario: Plugin sets executable permissions on Unix
```gherkin
Given the plugin just installed Morphir on Unix
When the post-install hook runs
Then the executable should have permissions 0o755 (rwxr-xr-x)
```

#### Scenario: Plugin post-install completes on Windows
```gherkin
Given the plugin just installed Morphir on Windows
When the post-install hook runs
Then the hook should complete without error
And no permission changes should be attempted
```

---

### Feature: End-to-End Installation (Manual Test)

#### Scenario: Full installation workflow
```gherkin
Given proto is installed
And the Morphir plugin is added
When I run "proto install morphir <version>"
Then proto should download the correct platform executable
And the executable should be placed in ~/.proto/tools/morphir/<version>/
And the executable should be in PATH
And "morphir --version" should work
```

**Note:** This scenario requires a published GitHub release and cannot be fully automated in CI without network access.

---

## Running Integration Tests

### Automated Tests (Smoke Tests)
```bash
cd integrations/rust/morphir-wasm-proto-plugin
./test-integration.sh
```

This runs:
1. Plugin registration
2. Plugin listing
3. Basic smoke tests

### Manual End-to-End Test
```bash
# 1. Build plugin
cargo build --release --target wasm32-wasip1

# 2. Add plugin locally
proto plugin add morphir-test "source:file://$(pwd)/target/wasm32-wasip1/release/morphir_wasm_proto_plugin.wasm"

# 3. Verify registration
proto plugin list | grep morphir-test

# 4. Install Morphir (requires published release)
proto install morphir-test <published-version>

# 5. Test executable
morphir-test --version

# 6. Cleanup
proto plugin remove morphir-test
```

---

## Coverage Report

### Unit Tests Coverage
- ✅ Plugin name validation
- ✅ Platform RID mapping (5 platforms)
- ✅ Executable name logic (Windows vs Unix)
- ✅ URL generation format
- ✅ Archive naming
- ✅ Version parsing (stable, pre-release, build metadata)
- ✅ Unix permission bits (0o755)
- ✅ Edge cases (invalid versions, special characters)

### Integration Tests Coverage
- ✅ Plugin registration with proto
- ✅ Plugin appears in list
- ⚠️ Actual installation (requires published release)
- ⚠️ Executable download (requires network)
- ⚠️ Post-install permissions (requires Unix system)

---

## Future Enhancements

### Automated Integration Tests
Consider adding full integration tests using:
- GitHub Actions matrix for all platforms
- Mock HTTP server for download URLs
- Temporary proto installation for testing

### Contract Tests
- Validate plugin adheres to proto PDK schema
- Test JSON serialization/deserialization
- Verify all plugin_fn functions have correct signatures

### Performance Tests
- Plugin load time
- Version resolution speed
- Download URL generation performance
