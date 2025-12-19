# Fast-Follow Issue: Upgrade WolverineFx to 5.9.0

This document contains the content for creating a GitHub issue to track the upgrade of WolverineFx from 5.8.0 back to 5.9.0.

## How to Create the Issue

1. Go to https://github.com/finos/morphir-dotnet/issues/new
2. Copy the content below and paste it into the issue body
3. Set the title to: **Upgrade WolverineFx to 5.9.0 and Update E2E Test Log Filtering**
4. Add labels: `technical-debt`, `bug`, `priority:high`, `epic:deployment-architecture`
5. Link to PR #211 in the issue

---

## Issue Content

**Priority**: High  
**Type**: Technical Debt / Bug Fix  
**Part of Epic**: #208 (Deployment Architecture Refactor)  
**Depends On**: #211 (Phase 3 - Build Testing Infrastructure)

### Background

In PR #211, we temporarily downgraded WolverineFx from 5.9.0 to 5.8.0 to fix E2E test failures that were blocking deployment (deployment run #20360658509). The root cause was identified as WolverineFx 5.9.0 changing log message formats that the ExecutableRunner's infrastructure log filtering logic couldn't handle properly.

This is a temporary fix to unblock deployments. This issue tracks the proper investigation and upgrade back to WolverineFx 5.9.0.

### Problem

When WolverineFx was upgraded from 5.8.0 to 5.9.0 (PR #236), 20 E2E tests failed in the deployment workflow. The tests are in `tests/Morphir.E2E.Tests/` and validate CLI command execution through the ExecutableRunner infrastructure.

The ExecutableRunner (located at `tests/Morphir.E2E.Tests/Infrastructure/ExecutableRunner.cs`) filters infrastructure log messages to prevent them from contaminating test assertions. The filtering logic in the `IsInfrastructureLogMessage` method is based on string patterns that match WolverineFx 5.8.0 log output formats.

### Tasks

#### Investigation Phase
- [ ] Install WolverineFx 5.9.0 in a test branch
- [ ] Run E2E tests and capture actual log output
- [ ] Compare WolverineFx 5.9.0 log formats with 5.8.0
- [ ] Identify specific log message patterns that changed
- [ ] Document all log format changes in issue comments

#### Implementation Phase
- [ ] Update `IsInfrastructureLogMessage` in ExecutableRunner.cs to handle 5.9.0 patterns
- [ ] Add unit tests for log filtering with both 5.8.0 and 5.9.0 log formats
- [ ] Upgrade WolverineFx to 5.9.0 in Directory.Packages.props
- [ ] Run full E2E test suite locally and verify all tests pass
- [ ] Run deployment workflow in CI and verify E2E tests pass

#### Validation Phase
- [ ] All E2E tests passing with WolverineFx 5.9.0
- [ ] No test output contamination from infrastructure logs
- [ ] Deployment workflow succeeds end-to-end
- [ ] Add regression tests to prevent future log filtering issues

### Files to Modify

1. **tests/Morphir.E2E.Tests/Infrastructure/ExecutableRunner.cs**
   - Update `IsInfrastructureLogMessage` method with new log patterns
   - Consider making the filtering more robust/flexible

2. **Directory.Packages.props**
   - Change `WolverineFx` version from 5.8.0 to 5.9.0

3. **tests/Morphir.E2E.Tests/** (potentially)
   - Add unit tests for log filtering if they don't exist
   - Update any E2E tests that make assumptions about log output

### Expected Log Patterns to Handle

Based on existing filtering in ExecutableRunner.cs, ensure these patterns still work:
- Wolverine extension scanning messages
- Application startup/shutdown messages
- Hosting environment messages
- Open Telemetry metrics messages
- Code generation mode messages
- Assembly searching messages

And identify any new patterns introduced in 5.9.0.

### Definition of Done

- [ ] WolverineFx upgraded to version 5.9.0
- [ ] All E2E tests passing locally
- [ ] All E2E tests passing in CI (deployment workflow)
- [ ] Log filtering unit tests added
- [ ] No infrastructure log contamination in test output
- [ ] Deployment workflow completes successfully
- [ ] Code review completed
- [ ] PR merged

### Testing Strategy

1. **Local Testing**
   ```bash
   ./build.sh Compile
   ./build.sh PublishSingleFile --rid linux-x64
   ./build.sh TestE2E --executable-type trimmed
   ```

2. **CI Testing**
   - Trigger deployment workflow manually
   - Verify all matrix builds pass (linux-x64, linux-arm64, osx-arm64, win-x64)
   - Confirm E2E tests pass on all platforms

### References

- **Blocking Issue**: #211 (Phase 3 - Build Testing Infrastructure)
- **Original Dependency Bump**: PR #236
- **Failed Deployment Run**: #20360658509
- **WolverineFx Repository**: https://github.com/JasperFx/wolverine
- **WolverineFx 5.9.0 Release Notes**: Check for logging-related changes

### Notes

- This should be treated as a fast-follow to PR #211
- Do not merge this issue's PR until #211 is merged
- Consider creating a helper method or configuration for log patterns to make future updates easier
- May want to investigate if WolverineFx has configuration options to reduce log verbosity
