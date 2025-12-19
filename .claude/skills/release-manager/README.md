# Release Manager Skill

Specialized release management skill for the morphir-dotnet project.

## Overview

This skill provides comprehensive release lifecycle management capabilities including:
- Release preparation and validation
- Deployment workflow execution and monitoring
- Release verification and QA coordination
- Documentation generation ("What's New", release notes)
- Failed release recovery
- Release playbook maintenance
- **Automated retrospective and feedback collection**

## Features

### 🔄 Automated Retrospective System (NEW)

The release manager now includes automated prompts to capture feedback at critical moments:

1. **Failure Retrospective** - When a release fails, `monitor-release.fsx` prompts for feedback about what went wrong and how to improve
2. **Success Feedback** - After 3+ consecutive successful releases, `validate-release.fsx` prompts for improvement ideas
3. **Process Change Detection** - `prepare-release.fsx` detects changes to release processes and prompts for playbook updates

All feedback is automatically recorded in release tracking issues and used to improve future releases.

## Files

- **SKILL.md** - Main skill prompt with release playbooks and guidance
- **README.md** - This file
- **templates/release-tracking.md** - GitHub issue template for tracking releases (includes retrospective sections)
- **prepare-release.fsx** - F# script: Pre-release validation and preparation (with process change detection)
- **monitor-release.fsx** - F# script: Monitor GitHub Actions deployment workflow (with failure retrospective)
- **validate-release.fsx** - F# script: Post-release verification (with success feedback)
- **resume-release.fsx** - F# script: Resume failed releases from checkpoint
- **release-history.fsx** - F# module: Release history tracking and feedback utilities

## Quick Start

### Prerequisites

- .NET 10 SDK installed
- GitHub CLI (`gh`) installed and authenticated
- Maintainer permissions on morphir-dotnet repository
- Access to trigger GitHub Actions workflows

### Typical Release Workflow

```bash
# 1. Prepare release
dotnet fsi .claude/skills/release-manager/prepare-release.fsx

# 2. Create tracking issue
gh issue create \
  --title "Release v1.0.0" \
  --body-file .claude/skills/release-manager/templates/release-tracking.md \
  --label release,tracking

# 3. Update CHANGELOG.md (manual or via script)
# Move [Unreleased] → [1.0.0] - 2025-12-18

# 4. Trigger deployment
gh workflow run deployment.yml \
  --ref main \
  --field release-version=1.0.0 \
  --field configuration=Release

# 5. Monitor deployment
dotnet fsi .claude/skills/release-manager/monitor-release.fsx \
  --version 1.0.0 \
  --issue 219 \
  --update-issue

# 6. Validate release
dotnet fsi .claude/skills/release-manager/validate-release.fsx \
  --version 1.0.0 \
  --smoke-tests \
  --issue 219

# 7. Coordinate with QA Tester for verification
# 8. Create "What's New" documentation
# 9. Announce release
# 10. Update release playbook and close tracking issue
```

## Automation Scripts

### prepare-release.fsx

Pre-flight checks and release preparation automation.

**Features:**
- Validates remote CI status on main branch
- Parses and validates CHANGELOG.md
- Suggests version based on change types (major/minor/patch)
- Checks NuGet for version availability
- Checks git tags for version conflicts
- Provides local state advisory (non-blocking)
- Generates pre-flight checklist

**Usage:**
```bash
# Standard usage (human-readable output)
dotnet fsi .claude/skills/release-manager/prepare-release.fsx

# JSON output for automation
dotnet fsi .claude/skills/release-manager/prepare-release.fsx --json

# Specify version explicitly
dotnet fsi .claude/skills/release-manager/prepare-release.fsx --version 1.0.0

# JSON output with specific version
dotnet fsi .claude/skills/release-manager/prepare-release.fsx --version 1.0.0 --json

# Dry run mode (no side effects)
dotnet fsi .claude/skills/release-manager/prepare-release.fsx --dry-run

# Skip local state check entirely
dotnet fsi .claude/skills/release-manager/prepare-release.fsx --skip-local-check
```

**IMPORTANT**: All scripts follow CLI logging standards:
- Human-readable output: Logs to stderr, results to stdout
- `--json` flag: Only JSON to stdout, all logs to stderr
- Automation-friendly: `script.fsx --json | jq` works correctly

**Exit Codes:**
- `0`: Ready to release
- `1`: Not ready (blocking issues)
- `2`: Ready with warnings (non-blocking)

**JSON Output Format** (`--json`):
```json
{
  "ready": true,
  "version": {
    "suggested": "1.2.0",
    "specified": null,
    "type": "minor",
    "rationale": "New features added, no breaking changes"
  },
  "remoteState": {
    "ciPassing": true,
    "latestRun": 1234,
    "latestCommit": "abc1234",
    "commitMessage": "feat: add feature X"
  },
  "changelog": {
    "hasUnreleased": true,
    "changeCount": 12,
    "added": 5,
    "changed": 3,
    "fixed": 4,
    "breakingChanges": 0
  },
  "versionValidation": {
    "nugetAvailable": true,
    "tagAvailable": true,
    "conflicts": []
  },
  "localState": {
    "branch": "feature/my-feature",
    "clean": false,
    "modifiedFiles": 3,
    "blocking": false
  },
  "warnings": [],
  "errors": [],
  "exitCode": 0
}
```

**Human-Readable Output:**
```
=== Release Preparation ===

Remote State:
✅ CI passing on main branch (run #1234)
✅ Latest commit: abc1234 "feat: add feature X"

Changelog Analysis:
📝 [Unreleased] section found with 12 changes
   - Added: 5 features
   - Changed: 3 improvements
   - Fixed: 4 bug fixes

Version Suggestion:
📊 Suggested version: 1.2.0 (Minor)
   Rationale: New features added, no breaking changes

Version Validation:
✅ Version 1.2.0 available on NuGet
✅ Tag v1.2.0 does not exist

Local State (Advisory):
ℹ️  On branch: feature/my-feature
⚠️  Local changes detected (3 modified files)
   You can:
   - Stash: git stash save "WIP before release"
   - Commit: git add . && git commit -m "WIP"
   - Continue anyway (workflow runs on remote main)

Pre-Flight Checklist:
✅ Remote CI passing
✅ Changelog populated
✅ Version validated
✅ NuGet availability confirmed
✅ Git tag available
ℹ️  Local state: not clean (non-blocking)

Result: Ready to release v1.2.0
```

---

### monitor-release.fsx

**CRITICAL FOR TOKEN EFFICIENCY**: This script automates monitoring and dramatically reduces LLM token usage by handling workflow polling autonomously.

**Features:**
- Polls GitHub Actions workflow status automatically
- Displays live progress with rich formatting
- Detects completion/failure immediately
- Parses workflow logs for errors
- Updates release tracking issue automatically
- Generates detailed status reports
- Alerts on failures with diagnostics

**Usage:**
```bash
# Monitor specific version (human-readable, live updates)
dotnet fsi .claude/skills/release-manager/monitor-release.fsx --version 1.0.0

# JSON output for automation (status snapshots)
dotnet fsi .claude/skills/release-manager/monitor-release.fsx --version 1.0.0 --json

# Monitor latest deployment workflow run
dotnet fsi .claude/skills/release-manager/monitor-release.fsx --latest

# Monitor with tracking issue updates
dotnet fsi .claude/skills/release-manager/monitor-release.fsx \
  --version 1.0.0 \
  --issue 219 \
  --update-issue

# JSON output with issue updates (for automation pipelines)
dotnet fsi .claude/skills/release-manager/monitor-release.fsx \
  --version 1.0.0 \
  --issue 219 \
  --update-issue \
  --json

# Custom polling interval (default: 30 seconds)
dotnet fsi .claude/skills/release-manager/monitor-release.fsx \
  --version 1.0.0 \
  --interval 60

# Monitor specific workflow run ID
dotnet fsi .claude/skills/release-manager/monitor-release.fsx --run-id 12345678
```

**Note on JSON Mode**: When `--json` is used, the script outputs JSON status snapshots on each poll interval instead of live terminal UI. Logs still go to stderr.

**Exit Codes:**
- `0`: Workflow completed successfully
- `1`: Workflow failed
- `2`: Workflow cancelled
- `3`: Error monitoring workflow

**JSON Output Format** (`--json`):
```json
{
  "workflow": {
    "name": "Deployment",
    "runId": 12345678,
    "status": "completed",
    "conclusion": "success",
    "startedAt": "2025-12-18T14:30:00Z",
    "completedAt": "2025-12-18T15:15:32Z",
    "duration": "00:45:32"
  },
  "stages": [
    {
      "name": "Validate Version",
      "status": "completed",
      "conclusion": "success",
      "duration": "00:00:15"
    },
    {
      "name": "Build Executables",
      "status": "completed",
      "conclusion": "success",
      "duration": "00:35:20",
      "jobs": [
        {"name": "linux-x64", "status": "completed", "conclusion": "success", "duration": "00:08:23"},
        {"name": "linux-arm64", "status": "completed", "conclusion": "success", "duration": "00:09:15"},
        {"name": "win-x64", "status": "completed", "conclusion": "success", "duration": "00:07:45"},
        {"name": "osx-arm64", "status": "completed", "conclusion": "success", "duration": "00:10:32"},
        {"name": "osx-x64", "status": "completed", "conclusion": "success", "duration": "00:09:05"}
      ]
    },
    {
      "name": "E2E Tests",
      "status": "completed",
      "conclusion": "success",
      "duration": "00:08:15"
    },
    {
      "name": "Release",
      "status": "completed",
      "conclusion": "success",
      "duration": "00:01:30"
    },
    {
      "name": "CD",
      "status": "completed",
      "conclusion": "success",
      "duration": "00:00:12"
    }
  ],
  "trackingIssue": {
    "number": 219,
    "updated": true,
    "lastUpdate": "2025-12-18T15:15:45Z"
  },
  "exitCode": 0
}
```

**Human-Readable Output:**
```
=== Release Monitor v1.0.0 ===

Workflow: Deployment
Run ID: 12345678
Status: in_progress
Started: 2025-12-18 14:30:00 UTC
Elapsed: 00:15:32

╔══════════════════════════════════════════════════════════╗
║ Stage                  │ Status       │ Duration          ║
╠══════════════════════════════════════════════════════════╣
║ Validate Version       │ ✅ Complete  │ 00:00:15         ║
║ Build Executables      │ ⏳ Running   │ 00:12:00         ║
║   linux-x64            │ ✅ Complete  │ 00:08:23         ║
║   linux-arm64          │ ✅ Complete  │ 00:09:15         ║
║   win-x64              │ ✅ Complete  │ 00:07:45         ║
║   osx-arm64            │ ⏳ Running   │ 00:10:32         ║
║   osx-x64              │ ⏸️  Queued   │ --               ║
║ E2E Tests              │ ⏸️  Queued   │ --               ║
║ Release                │ ⏸️  Queued   │ --               ║
║ CD                     │ ⏸️  Queued   │ --               ║
╚══════════════════════════════════════════════════════════╝

Tracking Issue: #219
Last Updated: 2025-12-18 14:45:00 UTC

Polling every 30 seconds... Press Ctrl+C to stop.
```

**Tracking Issue Integration:**

When `--update-issue` is provided, the script automatically:
- Checks off completed checklist items
- Adds progress comments every 5 minutes
- Updates failure status with diagnostics
- Attaches workflow logs for failures
- Adds final summary when complete

---

### validate-release.fsx

Post-release verification and validation automation.

**Features:**
- Queries NuGet.org API for published packages
- Validates package metadata (version, license, README)
- Tests tool installation from NuGet
- Tests library package references
- Runs basic smoke tests (optional)
- Generates comprehensive verification report
- Updates tracking issue with results

**Usage:**
```bash
# Validate specific version (human-readable)
dotnet fsi .claude/skills/release-manager/validate-release.fsx --version 1.0.0

# JSON output for automation
dotnet fsi .claude/skills/release-manager/validate-release.fsx --version 1.0.0 --json

# Include smoke tests (runs smoke-test.fsx from QA skill)
dotnet fsi .claude/skills/release-manager/validate-release.fsx \
  --version 1.0.0 \
  --smoke-tests

# Update tracking issue with results
dotnet fsi .claude/skills/release-manager/validate-release.fsx \
  --version 1.0.0 \
  --issue 219 \
  --update-issue

# JSON output with all checks (for CI/CD pipelines)
dotnet fsi .claude/skills/release-manager/validate-release.fsx \
  --version 1.0.0 \
  --smoke-tests \
  --json \
  --update-issue
```

**Exit Codes:**
- `0`: All validations passed
- `1`: One or more validations failed
- `2`: Warnings (non-critical issues)

**JSON Output Format** (`--json`):
```json
{
  "version": "1.0.0",
  "valid": true,
  "packages": [
    {
      "name": "Morphir.Core",
      "version": "1.0.0",
      "available": true,
      "publishedAt": "2025-12-18T15:00:00Z",
      "metadata": {
        "hasLicense": true,
        "hasReadme": true,
        "repositoryUrl": "https://github.com/finos/morphir-dotnet",
        "projectUrl": "https://morphir.finos.org"
      }
    },
    {
      "name": "Morphir.Tooling",
      "version": "1.0.0",
      "available": true,
      "publishedAt": "2025-12-18T15:00:05Z",
      "metadata": {
        "hasLicense": true,
        "hasReadme": true,
        "repositoryUrl": "https://github.com/finos/morphir-dotnet",
        "projectUrl": "https://morphir.finos.org"
      }
    },
    {
      "name": "Morphir",
      "version": "1.0.0",
      "available": true,
      "publishedAt": "2025-12-18T15:00:10Z",
      "metadata": {
        "hasLicense": true,
        "hasReadme": true,
        "repositoryUrl": "https://github.com/finos/morphir-dotnet",
        "projectUrl": "https://morphir.finos.org"
      }
    },
    {
      "name": "Morphir.Tool",
      "version": "1.0.0",
      "available": true,
      "publishedAt": "2025-12-18T15:00:15Z",
      "metadata": {
        "hasLicense": true,
        "hasReadme": true,
        "repositoryUrl": "https://github.com/finos/morphir-dotnet",
        "projectUrl": "https://morphir.finos.org"
      }
    }
  ],
  "installation": {
    "toolInstalled": true,
    "toolVersion": "1.0.0",
    "librariesReferenced": true
  },
  "smokeTests": {
    "run": true,
    "passed": true,
    "results": {
      "buildSucceeded": true,
      "testsPassed": true,
      "packagingSucceeded": true,
      "packageCountCorrect": true
    }
  },
  "trackingIssue": {
    "number": 219,
    "updated": true
  },
  "summary": {
    "totalChecks": 16,
    "passed": 16,
    "failed": 0,
    "warnings": 0
  },
  "duration": "00:03:45",
  "exitCode": 0
}
```

**Human-Readable Output:**
```
=== Release Validation v1.0.0 ===

Package Availability:
✅ Morphir.Core 1.0.0 (published 2025-12-18 15:00:00 UTC)
✅ Morphir.Tooling 1.0.0 (published 2025-12-18 15:00:05 UTC)
✅ Morphir 1.0.0 (published 2025-12-18 15:00:10 UTC)
✅ Morphir.Tool 1.0.0 (published 2025-12-18 15:00:15 UTC)

Package Metadata:
✅ All packages have LICENSE.md
✅ All packages have README.md
✅ All packages have correct version
✅ Repository URL correct
✅ Project URL correct

Installation Tests:
✅ Tool installs successfully
   Command: dotnet tool install -g Morphir.Tool --version 1.0.0
✅ Tool executes correctly
   Output: Morphir.Tool 1.0.0
✅ Libraries can be referenced
   - Morphir.Core: OK
   - Morphir.Tooling: OK

Smoke Tests:
✅ Build succeeds
✅ Tests pass
✅ Packaging succeeds
✅ Package count correct (4 packages)

Verification Summary:
✅ All checks passed (16/16)
⏱️ Validation completed in 00:03:45

Tracking Issue: #219
Result: Release v1.0.0 validated successfully
```

---

### resume-release.fsx

Resume failed releases from checkpoint.

**Features:**
- Reads release tracking issue for context
- Identifies last successful workflow step
- Validates prerequisites for resume
- Interactive confirmation prompts
- Re-triggers workflow from appropriate point
- Updates tracking issue with resume details
- Dry-run mode for planning

**Usage:**
```bash
# Resume from tracking issue (reads issue for context)
dotnet fsi .claude/skills/release-manager/resume-release.fsx --issue 219

# JSON output for automation
dotnet fsi .claude/skills/release-manager/resume-release.fsx --issue 219 --json

# Resume specific version with issue reference
dotnet fsi .claude/skills/release-manager/resume-release.fsx \
  --version 1.0.0 \
  --issue 219

# Dry run (show what would be done without executing)
dotnet fsi .claude/skills/release-manager/resume-release.fsx \
  --issue 219 \
  --dry-run

# JSON output for dry run analysis
dotnet fsi .claude/skills/release-manager/resume-release.fsx \
  --issue 219 \
  --dry-run \
  --json

# Force resume (skip confirmation prompts - use with caution)
dotnet fsi .claude/skills/release-manager/resume-release.fsx \
  --issue 219 \
  --force
```

**Exit Codes:**
- `0`: Successfully resumed
- `1`: Cannot resume (fix required)
- `2`: User aborted
- `3`: Error analyzing failure

**JSON Output Format** (`--json`):
```json
{
  "version": "1.0.0",
  "trackingIssue": 219,
  "resumable": true,
  "failure": {
    "stage": "Build Executables",
    "job": "osx-arm64",
    "failedAt": "2025-12-18T14:45:23Z",
    "error": "Build timeout after 30 minutes",
    "logs": "MSBuild timeout during restore\nNetwork latency to NuGet.org detected",
    "type": "transient",
    "requiresCodeFix": false
  },
  "lastSuccessful": {
    "stage": "Build Executables",
    "job": "win-x64",
    "completedAt": "2025-12-18T14:40:15Z"
  },
  "resumePlan": {
    "action": "re-trigger-workflow",
    "estimatedTime": "00:15:00",
    "willReuse": ["linux-x64", "linux-arm64", "win-x64"],
    "willRetry": ["osx-arm64", "osx-x64"]
  },
  "prerequisites": {
    "trackingIssueAccessible": true,
    "originalWorkflowFound": true,
    "githubCliAuthenticated": true,
    "workflowPermissionsVerified": true,
    "allPassed": true
  },
  "dryRun": false,
  "resumed": true,
  "newWorkflowRun": {
    "id": 12345679,
    "url": "https://github.com/finos/morphir-dotnet/actions/runs/12345679"
  },
  "exitCode": 0
}
```

**Human-Readable Output:**
```
=== Resume Release v1.0.0 ===

Reading tracking issue #219...
✅ Issue found: "Release v1.0.0"
✅ Version: 1.0.0
✅ Original workflow run: #12345678

Failure Analysis:
❌ Failed at: Build Executables (osx-arm64)
📋 Last successful: win-x64 build completed
🕒 Failed at: 2025-12-18 14:45:23 UTC
📝 Error: Build timeout after 30 minutes

Failure Logs:
> MSBuild timeout during restore
> Network latency to NuGet.org detected

Resumability Assessment:
✅ Transient failure (infrastructure)
✅ No code changes required
✅ Can retry from failed step

Resume Plan:
1. Re-trigger deployment workflow
2. GitHub Actions will use cached artifacts from successful steps
3. Only failed jobs will re-run
4. Estimated time: ~15 minutes (vs 45 minutes full run)

Prerequisites Check:
✅ Tracking issue accessible
✅ Original workflow found
✅ GitHub CLI authenticated
✅ Workflow permissions verified

⚠️  Confirm Resume?
   Version: 1.0.0
   Issue: #219
   Action: Re-trigger deployment workflow

[Y/n]: y

▶ Triggering workflow...
✅ Workflow triggered (run #12345679)

▶ Updating tracking issue...
✅ Added resume comment to #219
✅ Marked resume in progress

Next Steps:
1. Monitor workflow:
   dotnet fsi .claude/skills/release-manager/monitor-release.fsx \
     --run-id 12345679 \
     --issue 219 \
     --update-issue

2. Or watch manually:
   gh run watch 12345679
```

---

## Using the Release Manager Skill

### Invoke in Claude Code

```
@skill release-manager
I want to create a new release for version 1.0.0
```

or

```
@skill release-manager
The deployment workflow failed for v1.0.0. Help me resume it.
```

or

```
@skill release-manager
Prepare a release with the changes currently in the Unreleased section
```

### Trigger Words

The skill automatically activates when the user mentions:
- "release"
- "deploy"
- "publish"
- "changelog"
- "version"
- "release notes"
- "what's new"

### Example Interactions

**1. Standard Release:**
```
User: Let's create a new release
Agent: I'll help you prepare a new release. Let me run pre-flight checks first.

[Runs prepare-release.fsx]

Based on the changelog analysis, I suggest version 1.2.0 (minor release).
The unreleased section contains 5 new features and 4 bug fixes.

Shall I proceed with:
1. Creating a release tracking issue
2. Updating CHANGELOG.md to version 1.2.0
3. Triggering the deployment workflow

Would you like to proceed with v1.2.0 or specify a different version?
```

**2. Failed Release Recovery:**
```
User: The release failed. Issue #219
Agent: Let me analyze the failure and determine if we can resume.

[Runs resume-release.fsx with --dry-run]

I've analyzed the failure in issue #219. The deployment failed during the
osx-arm64 build due to a network timeout (transient infrastructure issue).

This is resumable! The failure doesn't require code changes.

I can re-trigger the workflow which will:
- Reuse successful builds (linux-x64, linux-arm64, win-x64)
- Only retry the failed osx-arm64 and pending osx-x64 builds
- Save ~25 minutes compared to starting over

Shall I resume the release?
```

**3. Pre-release (Alpha):**
```
User: Create an alpha release for testing
Agent: I'll prepare an alpha release. Let me check the current state.

[Runs prepare-release.fsx]

Based on the changelog, I suggest 2.0.0-alpha.1 as this contains
breaking changes that will be part of version 2.0.0.

For an alpha release, I'll:
1. Mark the CHANGELOG entry as pre-release
2. Set the GitHub release as pre-release
3. Document known issues and testing requirements

Shall I proceed with v2.0.0-alpha.1?
```

---

## Integration with QA Tester

The Release Manager coordinates closely with the QA Tester skill:

**Handoff After Package Publishing:**

```
@skill qa-tester

Release v1.0.0 ready for verification.

**Packages:**
- Morphir.Core v1.0.0
- Morphir.Tooling v1.0.0
- Morphir v1.0.0
- Morphir.Tool v1.0.0

**Test Plan:**
1. Run smoke-test.fsx
2. Test tool installation: dotnet tool install -g Morphir.Tool --version 1.0.0
3. Verify key commands work
4. Check for regressions

**Tracking Issue:** #219

Please update tracking issue with results.
```

**QA Response Integration:**

QA Tester adds results to tracking issue, Release Manager:
1. Reviews QA findings
2. Addresses any issues found
3. Requires QA sign-off before closing release
4. Updates release documentation with any notes

---

## Best Practices

### 1. Always Use Tracking Issues
- Create tracking issue at start of release
- Update throughout process
- Provides audit trail
- Enables resumption if failure
- Documents learnings

### 2. Automate Monitoring
- Always use `monitor-release.fsx`
- Reduces token usage dramatically
- Catches failures immediately
- Frees you for other work
- Automatic issue updates

### 3. Flexible with Local State
- Workflow runs on remote GitHub Actions
- Local changes usually don't interfere
- Stash if concerned, but not required
- Use `--ref main` to ensure remote execution

### 4. Document Everything
- Update tracking issue continuously
- Record all issues encountered
- Document solutions applied
- Update playbook with learnings
- Help future releases

### 5. Coordinate with QA
- Don't skip verification
- Give QA time to test thoroughly
- Address all QA findings
- Get explicit sign-off
- Close release only after QA approval

### 6. Keep Playbook Current
- Update after every release
- Add new failure scenarios
- Document process improvements
- Share with team
- Continuous improvement

### 7. Leverage Automated Retrospective System
- **Respond to prompts** when they appear
  - Failure retrospective: Explain what went wrong and how to prevent it
  - Success feedback: Share improvement ideas after consecutive successes
  - Process changes: Note why release process files changed
- **Be specific and actionable** in feedback
  - Good: "E2E test timeout could be prevented by increasing timeout from 30s to 60s"
  - Poor: "Tests failed"
- **Review feedback in tracking issues** to inform future improvements
- **Update documentation** when prompted about process changes
- **Track patterns** using release history to identify recurring issues

---

## Retrospective and Feedback System

### Overview

The release manager includes an automated retrospective system that captures feedback at three critical moments:

1. **On Release Failure** - Immediate retrospective to understand what went wrong
2. **After Consecutive Successes** - Proactive improvement ideas when things are working well
3. **On Process Changes** - Documentation prompts when release processes evolve

### How It Works

**Release History Tracking:**
- All releases are tracked in `.release-history.json`
- Consecutive successes and failures are counted automatically
- Historical data enables pattern detection

**Feedback Collection:**
- Scripts prompt for feedback at appropriate moments
- Responses are collected interactively (can be skipped)
- Feedback is automatically added to release tracking issues
- Enables data-driven continuous improvement

### Usage Examples

**Example 1: Failure Retrospective**
```bash
# During monitoring, if workflow fails:
$ dotnet fsi monitor-release.fsx --version 1.0.0 --issue 219

# Script detects failure and prompts:
[FEEDBACK REQUEST]
We noticed the release failed. Are there any changes we could make 
to the release process to ensure future success?

Enter your feedback (or press Enter to skip):
> The E2E tests timed out on osx-arm64. We should increase the timeout 
> in the deployment workflow from 30 minutes to 45 minutes for that platform.

# Feedback is automatically added to issue #219
```

**Example 2: Success Feedback**
```bash
# After 3rd consecutive successful release:
$ dotnet fsi validate-release.fsx --version 1.3.0 --issue 225

# Script prompts:
[FEEDBACK REQUEST]
You've had 3 successful releases in a row! 🎉 Would you like to provide 
feedback on how we can further improve the release process?

Enter your feedback (or press Enter to skip):
> The monitoring script works great. Consider adding Slack notifications 
> for workflow completion so we don't have to watch it continuously.

# Feedback is added to issue #225
```

**Example 3: Process Change Detection**
```bash
# Before release, if workflow files changed:
$ dotnet fsi prepare-release.fsx

# Script detects changes and prompts:
[FEEDBACK REQUEST]
We see changes to 2 release process files. Would you like to update 
or add to our release playbooks based on these changes?

Enter your feedback (or press Enter to skip):
> Updated deployment.yml to use .NET 10. Updated SKILL.md to reflect 
> new version. No other playbook changes needed.

# Guidance is displayed for updating documentation
```

### Benefits

- **Captures insights when they're fresh** - Right after events happen
- **Builds institutional knowledge** - Feedback preserved in tracking issues
- **Identifies patterns** - Historical tracking reveals recurring issues
- **Drives improvement** - Actionable feedback leads to concrete changes
- **No extra effort** - Integrated into existing workflow
- **Skippable** - Press Enter to skip if no feedback at the moment

### Best Practices

1. **Be specific** - Include details about what/why/how
2. **Be actionable** - Suggest concrete improvements
3. **Be honest** - Note both successes and failures
4. **Be timely** - Provide feedback when prompted
5. **Follow up** - Convert feedback into action items
6. **Share widely** - Reference feedback in retrospectives and planning

---

## Troubleshooting

### Script Dependencies

All scripts use **Spectre.Console** for rich terminal output. Dependencies are automatically downloaded by F# Interactive.

If you encounter package errors:

```bash
# Option 1: Let F# Interactive handle it (usually automatic)
dotnet fsi .claude/skills/release-manager/prepare-release.fsx

# Option 2: Pre-restore packages
dotnet tool restore
dotnet restore

# Option 3: Clear NuGet cache and retry
dotnet nuget locals all --clear
dotnet fsi .claude/skills/release-manager/prepare-release.fsx
```

### GitHub CLI Authentication

Scripts require authenticated GitHub CLI:

```bash
# Check auth status
gh auth status

# Login if needed
gh auth login

# Verify permissions
gh auth refresh -h github.com -s write:packages,workflow
```

### Workflow Permissions

If workflow won't trigger:

1. Check branch protection rules
2. Verify GH_TOKEN has workflow permissions
3. Check repository settings → Actions → General
4. Ensure workflow file syntax is valid

### Common Issues

| Issue | Solution |
|-------|----------|
| "Cannot find workflow run" | Wait a few seconds after triggering, workflow takes time to appear |
| "Version already on NuGet" | Increment version or contact NuGet support to unlist |
| "E2E tests fail" | Check platform-specific logs, may need code fix |
| "NuGet publish timeout" | Check NuGet.org status, retry if transient |
| "Script hangs" | Check network connection, GitHub API may be slow |

---

## CLI Logging Standards and JSON Output

**CRITICAL**: All release manager scripts follow the morphir-dotnet CLI logging standards:

### Output Separation

1. **Human-Readable Mode** (default):
   - **stdout**: Final results, summaries, actionable output
   - **stderr**: Progress logs, diagnostics, informational messages
   - User sees both in terminal, but can redirect separately

2. **JSON Mode** (`--json` flag):
   - **stdout**: ONLY valid JSON (nothing else)
   - **stderr**: All logs, progress, diagnostics
   - Enables: `script.fsx --json | jq`
   - Enables: `script.fsx --json > result.json` (clean JSON file)

### Why This Matters

```bash
# ✅ GOOD: JSON mode allows clean piping
dotnet fsi prepare-release.fsx --json | jq '.version.suggested'
# Output: "1.2.0"

# ❌ BAD: If logs leaked to stdout
dotnet fsi prepare-release.fsx --json | jq '.version.suggested'
# Output: parse error (logs mixed with JSON)
```

### Implementation Pattern

All scripts use this pattern:

```fsharp
// Parse command line
let jsonOutput = args |> Array.contains "--json"

// Configure output functions
let logInfo msg =
    if not jsonOutput then
        eprintfn "[INFO] %s" msg  // Always to stderr

let logError msg =
    eprintfn "[ERROR] %s" msg  // Always to stderr

let output result =
    if jsonOutput then
        // ONLY JSON to stdout
        let json = JsonSerializer.Serialize(result)
        printfn "%s" json
    else
        // Human-readable to stdout
        printfn "=== Results ==="
        // ... format nicely
```

### Testing JSON Output

Before committing any script, verify:

```bash
# Test 1: JSON is valid
dotnet fsi script.fsx --json | jq . > /dev/null
# Should succeed with no errors

# Test 2: No log contamination
dotnet fsi script.fsx --json 2>/dev/null | jq .
# Should output only JSON

# Test 3: Logs still appear on stderr
dotnet fsi script.fsx --json 2>&1 > /dev/null | grep "\[INFO\]"
# Should show log messages
```

### JSON Schema Consistency

All scripts should include `exitCode` in JSON output:

```json
{
  "exitCode": 0,
  "...": "other fields"
}
```

This allows consumers to verify success without checking shell exit code.

---

## Dependencies

### Required
- .NET 10 SDK
- GitHub CLI (`gh`) authenticated
- Network access to GitHub API and NuGet.org
- Repository maintainer permissions

### Optional
- QA Tester skill (for verification handoff)
- Spectre.Console (auto-downloaded by scripts)

---

## File Structure

```
.claude/skills/release-manager/
├── SKILL.md                         # Main skill prompt
├── README.md                        # This file
├── prepare-release.fsx              # Pre-flight checks (~200 lines)
├── monitor-release.fsx              # Workflow monitoring (~400 lines)
├── validate-release.fsx             # Post-release validation (~300 lines)
├── resume-release.fsx               # Resume failed releases (~250 lines)
└── templates/
    └── release-tracking.md          # GitHub issue template
```

---

## Metrics

Track these metrics for continuous improvement:

- **Time to Release**: Target < 90 minutes for standard release
- **Failed Releases**: Target < 5% failure rate
- **Manual Interventions**: Target 0 (fully automated)
- **Documentation Completeness**: Target 100% (all releases documented)
- **QA Issues Post-Release**: Target 0 (catch before release)

---

## Future Enhancements

Planned improvements:

1. **Automated Changelog Generation**: Parse commits for auto-changelog
2. **Rollback Automation**: Quick rollback script for critical issues
3. **Multi-Environment Support**: Dev, staging, production releases
4. **Release Analytics**: Dashboard of release metrics
5. **Slack/Discord Notifications**: Alert channels on release events
6. **Automated Documentation**: Generate docs from code comments

---

## References

- **Skill Prompt**: [SKILL.md](./SKILL.md)
- **QA Tester Skill**: [../qa-tester/](../qa-tester/)
- **Deployment Workflow**: [.github/workflows/deployment.yml](../../../.github/workflows/deployment.yml)
- **AGENTS.md**: [../../../AGENTS.md](../../../AGENTS.md)
- **Keep a Changelog**: https://keepachangelog.com/
- **Semantic Versioning**: https://semver.org/
- **GitHub CLI**: https://cli.github.com/manual/

---

## Contributing

To improve this skill:

1. Test releases and document issues
2. Update scripts with improvements
3. Add new automation as needed
4. Keep playbook current
5. Share learnings with team
6. Submit PRs with enhancements

---

**Remember**: Releases are critical touchpoints with users. Automate what you can, monitor actively, coordinate with QA, document everything, and continuously improve the process.
