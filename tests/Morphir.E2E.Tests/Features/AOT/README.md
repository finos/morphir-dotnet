# AOT (Ahead-of-Time) Test Suite

This directory contains BDD feature tests for validating AOT compilation and assembly trimming in Morphir.

## Test Suites

### Assembly Trimming (`AssemblyTrimming.feature`)
Tests for validating trimmed executables:
- Trimming modes (link, partial)
- Size reduction validation
- Feature switch support
- Resource preservation
- Type preservation with attributes
- Baseline comparisons

**11 scenarios** covering trimming effectiveness and correctness.

### Native AOT Compilation (`NativeAOTCompilation.feature`)
Tests for validating Native AOT compilation:
- Successful AOT builds
- Size optimizations
- Cross-platform builds (linux-x64, win-x64, osx-x64)
- Runtime command execution
- JSON output validation
- Performance metrics

**9 scenarios** covering AOT compilation and runtime behavior.

## Running AOT Tests

### Manual Workflow (Recommended)

AOT tests are tagged with `@manual-only` and run in a dedicated GitHub Actions workflow:

1. Go to **Actions** → **Manual AOT Testing**
2. Click **Run workflow**
3. Select:
   - **Configuration**: Release or Debug
   - **Platform**: linux-x64, osx-arm64, win-x64, etc.
   - **Test Suite**: both, trimming, or aot-compilation
4. Click **Run workflow**

The workflow will:
- Build the required executables (trimmed, untrimmed, AOT)
- Run the selected test suite
- Upload artifacts on failure

### Local Execution

To run AOT tests locally:

```bash
# Build executables first
./build.sh --target PublishSingleFile --rid linux-x64
./build.sh --target PublishSingleFileUntrimmed --rid linux-x64
./build.sh --target PublishExecutable --rid linux-x64

# Run trimming tests
cd tests/Morphir.E2E.Tests
MORPHIR_EXECUTABLE_TYPE=trimmed dotnet run -- --treenode-filter "*/Trimming*"

# Run AOT tests
MORPHIR_EXECUTABLE_TYPE=aot dotnet run -- --treenode-filter "*/AOT*"
```

### Excluding from Regular CI

AOT tests are excluded from regular CI runs because:
- They are long-running (1-5 minutes per scenario, ~30-100 minutes total)
- They are for awareness/preparation, not blocking
- They help determine if trimmed/AOT executables are safe to release

The `run-e2e-tests.cs` script automatically excludes tests matching `Trimming*` or `*AOT*` patterns unless `INCLUDE_MANUAL_TESTS=true` is set.

## Test Duration

- **Assembly Trimming**: ~15-30 minutes (builds trimmed + untrimmed executables)
- **Native AOT Compilation**: ~30-60 minutes (builds AOT executable, longer compile time)
- **Total**: ~45-90 minutes for both suites

Each scenario builds executables with specific MSBuild properties to validate the build process.

## Implementation Details

### Step Definitions
- `AssemblyTrimmingSteps.cs` - 537 lines, implements all trimming scenarios
- `NativeAOTCompilationSteps.cs` - 555 lines, implements all AOT scenarios

### Build Approach
- Tests invoke `dotnet publish` with scenario-specific properties
- Isolated builds in `artifacts/test-builds/{guid}` per scenario
- Reuses existing artifacts when available (AOT tests)
- Cross-platform RID detection

### Validations
- Exit code checks
- File size comparisons and ranges
- Build warning detection (IL2XXX)
- Runtime command execution
- JSON output validation

## When to Run

Run AOT tests:
- Before releasing a new version with trimmed/AOT executables
- After significant changes to CLI, dependencies, or build configuration
- To validate trimming/AOT compatibility after dependency updates
- For awareness of trimming warnings and size characteristics

## Troubleshooting

### AOT Build Failures
- Ensure .NET 10 SDK is installed
- Check for IL2XXX warnings indicating reflection or dynamic code
- Review `ILLink.Descriptors.xml` for required type preservation

### Test Failures
- Check uploaded artifacts for build logs
- Verify executable exists and is executable
- Check platform-specific size limits match actual build output

### Long Build Times
- AOT compilation is inherently slower than managed builds
- Consider running single platform or test suite
- Use faster hardware (CI runners have varied performance)
