# Deployment Workflow Testing Guide

This document provides instructions for testing the deployment workflow before pushing to the main repository.

## Overview

The deployment workflow (`.github/workflows/deployment.yml`) is triggered by:
1. **Tag push**: Pushing a tag matching `v*` (e.g., `v0.2.1`)
2. **Manual workflow dispatch**: Manually triggering via GitHub Actions UI

## Prerequisites

- [ ] CHANGELOG.md updated with the version you want to deploy
- [ ] All tests passing locally
- [ ] Version number follows semantic versioning (e.g., `0.2.1` or `0.2.1-rc.1`)

## Testing on a Fork (Recommended)

**IMPORTANT**: Always test on a fork first to avoid accidental releases to the main repository.

### Step 1: Fork the Repository

If you haven't already, fork the `finos/morphir-dotnet` repository to your GitHub account.

### Step 2: Clone Your Fork

```bash
git clone https://github.com/YOUR_USERNAME/morphir-dotnet.git
cd morphir-dotnet
git checkout -b test-deployment
```

### Step 3: Update CHANGELOG.md

Ensure your test version exists in `CHANGELOG.md`:

```markdown
## [0.0.0-test.1] - 2025-12-19

### Added
- Test deployment workflow
```

### Step 4: Push Changes to Your Fork

```bash
git add CHANGELOG.md
git commit -m "test: add test version to CHANGELOG"
git push origin test-deployment
```

### Step 5: Create a Test Tag

Create a lightweight test tag:

```bash
git tag v0.0.0-test.1
git push origin v0.0.0-test.1
```

### Step 6: Monitor Workflow Execution

1. Go to your fork on GitHub: `https://github.com/YOUR_USERNAME/morphir-dotnet`
2. Click on "Actions" tab
3. You should see the "Deployment" workflow running
4. Click on the workflow run to see detailed logs

### Step 7: Verify Steps

Check that each step completes successfully:

- ✅ `validate-version` job:
  - Extracts version from tag (`v0.0.0-test.1` → `0.0.0-test.1`)
  - Validates version format
  - Checks version exists in CHANGELOG.md
  
- ✅ `build-executables` job (runs in parallel for each platform):
  - linux-x64
  - linux-arm64
  - win-x64
  - osx-arm64
  - Each builds and uploads executable artifact
  
- ✅ `release` job:
  - Downloads executable artifacts
  - Packs NuGet packages
  - Runs TestAll (unit tests + build tests)
  - **Note**: Will fail at NuGet publish step if you don't have NUGET_KEY secret configured (this is expected)
  
- ✅ `create-github-release` job:
  - Downloads executables
  - Extracts release notes from CHANGELOG.md
  - Creates GitHub release with executables

### Step 8: Clean Up

After testing, delete the test tag and release:

```bash
# Delete local tag
git tag -d v0.0.0-test.1

# Delete remote tag
git push --delete origin v0.0.0-test.1

# Delete GitHub release (via GitHub UI or gh CLI)
gh release delete v0.0.0-test.1 --yes
```

## Testing with workflow_dispatch

You can also test using manual workflow dispatch:

### Step 1: Go to Actions Tab

Navigate to your fork's Actions tab: `https://github.com/YOUR_USERNAME/morphir-dotnet/actions`

### Step 2: Select Deployment Workflow

Click on "Deployment" workflow in the left sidebar.

### Step 3: Run Workflow

1. Click "Run workflow" button (top right)
2. Fill in the parameters:
   - **Branch**: Select your test branch
   - **Configuration**: `Release` (or `Debug` for testing)
   - **release-version**: Enter your test version (e.g., `0.0.0-test.1`)
   - **skip-git-release**: Check this if you want to skip GitHub release creation
3. Click "Run workflow"

### Step 4: Monitor Execution

Watch the workflow execution in real-time and verify all steps complete successfully.

## What Could Go Wrong?

### Version Not Found in CHANGELOG.md

**Error**: `Version [X.Y.Z] not found in CHANGELOG.md`

**Solution**: Add the version to CHANGELOG.md:

```markdown
## [X.Y.Z] - YYYY-MM-DD

### Added
- Your changes here
```

### Invalid Version Format

**Error**: `Invalid semantic version format: X.Y.Z`

**Solution**: Ensure version follows semantic versioning:
- Valid: `0.2.1`, `1.0.0`, `0.2.1-rc.1`, `1.0.0-alpha.1`
- Invalid: `v0.2.1`, `0.2`, `1.0.0-RC.1`

### Build Tests Fail

**Error**: `Build tests failed with exit code X`

**Solution**: 
1. Run tests locally first: `./build.sh TestAll`
2. Ensure packages are built: `./build.sh PackAll`
3. Fix any failing tests before pushing

### Executable Artifacts Not Found

**Error**: `No executable found in artifacts/executables/morphir-{rid}`

**Solution**:
1. Check that `build-executables` job completed successfully for all platforms
2. Verify artifact upload step in build-executables job
3. Check artifact retention settings (default: 1 day)

### NuGet Publish Fails

**Error**: `API_KEY is required for publishing`

**Solution**: 
- On fork: This is expected unless you configure NUGET_KEY secret
- On main repo: Ensure NUGET_KEY secret is set in repository settings
- For testing: Use `skip-git-release: true` to test everything except publishing

## Production Deployment Checklist

Before deploying to production (main repository):

### Pre-Deployment

- [ ] All code changes merged to main branch
- [ ] CHANGELOG.md updated with release notes
- [ ] Version number decided (e.g., `0.3.0`)
- [ ] All tests passing locally
- [ ] Tested on fork successfully

### Deployment Process

1. **Create Release Branch** (recommended):
   ```bash
   git checkout main
   git pull
   git checkout -b release/v0.3.0
   ```

2. **Update CHANGELOG.md**:
   ```bash
   ./build.sh PrepareRelease --release-version 0.3.0
   git commit -m "chore: prepare release 0.3.0"
   ```

3. **Push and Create PR**:
   ```bash
   git push origin release/v0.3.0
   gh pr create --title "chore: prepare release 0.3.0"
   ```

4. **After PR Merge, Create Tag**:
   ```bash
   git checkout main
   git pull
   git tag -a v0.3.0 -m "Release 0.3.0"
   git push origin v0.3.0
   ```

5. **Monitor Deployment**:
   - Watch GitHub Actions workflow
   - Verify packages published to NuGet.org
   - Verify GitHub release created
   - Test installation: `dotnet tool install -g Morphir.Tool`

### Post-Deployment

- [ ] Verify packages on NuGet.org
- [ ] Verify GitHub release has all executables
- [ ] Test install scripts on each platform
- [ ] Update documentation if needed
- [ ] Announce release

## Rollback Plan

If deployment fails:

1. **Delete Tag**:
   ```bash
   git push --delete origin vX.Y.Z
   git tag -d vX.Y.Z
   ```

2. **Delete GitHub Release** (if created):
   ```bash
   gh release delete vX.Y.Z --yes
   ```

3. **Unlist Packages from NuGet** (if published):
   - Go to NuGet.org
   - Sign in and navigate to package
   - Click "Unlist" (don't delete)
   - Packages will still be available to existing users but hidden from search

4. **Fix Issue**:
   - Fix the bug/issue
   - Create new version (patch bump)
   - Repeat deployment process

## Troubleshooting

### Workflow Not Triggering

**Issue**: Pushed tag but workflow didn't start

**Solutions**:
- Ensure tag matches pattern `v*` (must start with lowercase `v`)
- Check that workflow file is in `.github/workflows/` directory
- Verify workflow file has valid YAML syntax
- Check repository Actions settings (Actions must be enabled)

### Secrets Not Available

**Issue**: GITHUB_TOKEN or NUGET_KEY not found

**Solutions**:
- GITHUB_TOKEN: Automatically provided by GitHub Actions
- NUGET_KEY: Must be configured in repository secrets
  - Go to Settings > Secrets and variables > Actions
  - Add secret named `NUGET_KEY` with your NuGet API key

### Permission Denied

**Issue**: Cannot create release or push to repository

**Solutions**:
- Ensure GITHUB_TOKEN has write permissions
- Check repository settings > Actions > General > Workflow permissions
- Should be set to "Read and write permissions"

## Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [softprops/action-gh-release](https://github.com/softprops/action-gh-release)
- [Semantic Versioning](https://semver.org/)
- [Keep a Changelog](https://keepachangelog.com/)

## Questions?

If you encounter issues not covered here, please:
1. Check workflow logs for detailed error messages
2. Search existing issues in the repository
3. Create a new issue with:
   - Steps to reproduce
   - Workflow logs
   - Expected vs actual behavior
