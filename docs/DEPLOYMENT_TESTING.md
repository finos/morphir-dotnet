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

## NuGet trusted publishing

Deployment uses GitHub OIDC and `NuGet/login` to obtain a temporary NuGet API key
immediately before publishing. It does not use the stored `NUGET_KEY` or
`NUGET_TOKEN` secrets. Only the `release` job has `id-token: write` permission.

Before running a release:

1. In NuGet.org, open **Trusted Publishing** under your personal user profile.
   Select the account or organization that owns the Morphir packages as the
   package owner. The current policy selects `Morphir`. Confirm that the GitHub
   policy trusts repository owner `finos`, repository `morphir-dotnet`, and
   workflow file `deployment.yml`, without the directory path.
2. Set the policy's environment restriction to `production`, matching
   `environment: production` on the workflow's `release` job.
3. Confirm the policy covers all package IDs in `LibraryPackages` in
   `build/Build.cs`, plus `Morphir.Tool`. It must allow new package creation as
   well as new versions for IDs that have not been published before. The package
   glob `Morphir*` covers all 15 packages, including the root `Morphir` package.
4. Set the repository Actions variable `NUGET_USER` to the personal NuGet username
   that creates the policy, even when an organization is selected as package
   owner. Use the NuGet username, not an email address, organization name or
   GitHub username. The current policy was created by `DamReev`. An existing
   Actions secret named `NUGET_USER` is also accepted. No permanent API key is needed.

The workflow checks for `NUGET_USER` before building packages. NuGet checks the
policy when `NuGet/login` exchanges the OIDC token. See the
[NuGet trusted publishing documentation](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
for policy ownership and package scopes.

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
  - **Note**: A fork without `NUGET_USER` fails the configuration check. A fork is not trusted by the main repository's NuGet policy, so OIDC login will fail even if a username is configured.
  
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

Check which step failed:

- **Validate trusted publishing configuration**: Set the Actions variable
  `NUGET_USER` to the personal NuGet username that created the trust policy.
- **Authenticate to NuGet with trusted publishing**: Check the policy's profile,
  repository owner, repository, workflow filename, environment restriction,
  activation status and package scopes against the configuration above.
- **Publish packages to NuGet**: Check the temporary credential's package scopes
  and whether the policy allows both new packages and new versions. Do not
  restore a stored API key as a workaround.

`skip-git-release: true` only skips creation of the GitHub release. It still
publishes NuGet packages, so it is not a publishing dry run. A fork without its
own trust policy cannot complete NuGet publishing.

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

### Publishing Credentials Not Available

GitHub provides `GITHUB_TOKEN` automatically. NuGet publishing requires the
`NUGET_USER` profile name and a matching trusted publishing policy, as described
above. `NuGet/login` supplies the temporary API key through its step output.
The old `NUGET_KEY` and `NUGET_TOKEN` secrets are no longer read by deployment.

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
