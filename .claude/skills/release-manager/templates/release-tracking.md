# Release v{VERSION}

**Release Type**: [ ] Stable / [ ] Pre-release (Alpha/Beta/RC) / [ ] Hotfix
**Target Date**: {DATE}
**Release Manager**: @{GITHUB_USERNAME}
**Status**: 🟡 In Progress

---

## 🔄 Automated Retrospective System

This release tracking issue integrates with the automated retrospective and feedback system:

- **Failure Feedback**: If the release fails, `monitor-release.fsx` will prompt for retrospective feedback and add it to this issue
- **Success Feedback**: After 3+ consecutive successful releases, `validate-release.fsx` will prompt for improvement ideas
- **Process Changes**: `prepare-release.fsx` detects changes to release processes and prompts for playbook updates

**Release History**: This release will be tracked in `.release-history.json` to enable pattern detection and consecutive success/failure counting.

---

## Release Information

- **Version**: `{VERSION}`
- **Previous Version**: `{PREV_VERSION}`
- **Branch**: `{BRANCH}`
- **Changelog**: [CHANGELOG.md](../CHANGELOG.md#v{VERSION_ANCHOR})
- **Deployment Workflow**: [View Run](https://github.com/finos/morphir-dotnet/actions/workflows/deployment.yml)

---

## 📋 Release Checklist

### Phase 1: Preparation

- [ ] Pre-flight checks completed (`prepare-release.fsx`)
  - [ ] Remote CI passing on main branch
  - [ ] CHANGELOG.md [Unreleased] section populated
  - [ ] Version validated (semantic versioning)
  - [ ] Version available (not on NuGet, not a git tag)
  - [ ] Local state assessed (advisory only)
- [ ] Version confirmed: `{VERSION}`
- [ ] Release tracking issue created (this issue)
- [ ] CHANGELOG.md updated
  - [ ] Moved [Unreleased] → [{VERSION}]
  - [ ] Added release date
  - [ ] Updated comparison links
  - [ ] Created new [Unreleased] section
  - [ ] Changes properly categorized
  - [ ] Breaking changes marked
- [ ] Changelog committed (if applicable)

### Phase 2: Execution

- [ ] Deployment workflow triggered
  - **Run ID**: {RUN_ID}
  - **Run URL**: {RUN_URL}
  - **Triggered at**: {TIMESTAMP}
- [ ] Monitoring started (`monitor-release.fsx`)
- [ ] Workflow stages completed:
  - [ ] Version validation
  - [ ] Build executables
    - [ ] linux-x64
    - [ ] linux-arm64
    - [ ] win-x64
    - [ ] osx-arm64
    - [ ] osx-x64
  - [ ] E2E tests (all platforms)
  - [ ] Pack packages
  - [ ] Publish to NuGet
  - [ ] CD aggregation

### Phase 3: Verification

- [ ] Package validation completed (`validate-release.fsx`)
- [ ] Packages on NuGet.org:
  - [ ] [Morphir.Core v{VERSION}](https://www.nuget.org/packages/Morphir.Core/{VERSION})
  - [ ] [Morphir.Tooling v{VERSION}](https://www.nuget.org/packages/Morphir.Tooling/{VERSION})
  - [ ] [Morphir v{VERSION}](https://www.nuget.org/packages/Morphir/{VERSION})
  - [ ] [Morphir.Tool v{VERSION}](https://www.nuget.org/packages/Morphir.Tool/{VERSION})
- [ ] Installation tests passed
  - [ ] Tool installs: `dotnet tool install -g Morphir.Tool --version {VERSION}`
  - [ ] Tool executes: `dotnet-morphir --version`
  - [ ] Libraries reference correctly
- [ ] QA Tester sign-off (issue #{QA_ISSUE})
  - [ ] Smoke tests passed
  - [ ] Functional tests passed
  - [ ] Regression tests passed
  - [ ] No critical issues found

### Phase 4: Documentation

- [ ] GitHub release created
  - **Release URL**: {RELEASE_URL}
  - [ ] Release notes added
  - [ ] Pre-release flag set (if applicable)
  - [ ] Binaries attached (if applicable)
- [ ] "What's New" document created
  - [ ] File: `docs/content/docs/whats-new/v{VERSION}.md`
  - [ ] Highlights extracted from changelog
  - [ ] Breaking changes documented with migration guide
  - [ ] Examples included
- [ ] Documentation site updated
  - [ ] Navigation updated
  - [ ] Version selector updated
- [ ] README.md updated (if needed)
- [ ] Announcement prepared
  - [ ] GitHub Discussions post
  - [ ] Community channels (if applicable)

### Phase 5: Post-Release

- [ ] Release playbook updated
  - [ ] Issues encountered documented
  - [ ] Solutions recorded
  - [ ] Process improvements noted
- [ ] Metrics recorded
  - **Time to release**: {DURATION}
  - **Manual interventions**: {COUNT}
  - **Issues encountered**: {COUNT}
- [ ] Retrospective notes added (see below)
- [ ] Release tracking issue closed

---

## 📊 Release Metrics

| Metric | Value |
|--------|-------|
| **Preparation Time** | {PREP_TIME} |
| **Execution Time** | {EXEC_TIME} |
| **Verification Time** | {VERIFY_TIME} |
| **Documentation Time** | {DOC_TIME} |
| **Total Time** | {TOTAL_TIME} |
| **Manual Interventions** | {INTERVENTIONS} |
| **Failures/Retries** | {FAILURES} |
| **QA Issues Found** | {QA_ISSUES} |

---

## 🔗 Links

### Workflow Runs
- Deployment Workflow: {DEPLOYMENT_RUN_URL}
- CI Workflow (pre-release): {CI_RUN_URL}

### Packages
- Morphir.Core: https://www.nuget.org/packages/Morphir.Core/{VERSION}
- Morphir.Tooling: https://www.nuget.org/packages/Morphir.Tooling/{VERSION}
- Morphir: https://www.nuget.org/packages/Morphir/{VERSION}
- Morphir.Tool: https://www.nuget.org/packages/Morphir.Tool/{VERSION}

### Documentation
- GitHub Release: {GITHUB_RELEASE_URL}
- What's New: {WHATS_NEW_URL}
- Changelog: {CHANGELOG_URL}

### Issues
- QA Tracking Issue: #{QA_ISSUE}
- Related Issues: {RELATED_ISSUES}

---

## 📝 Notes

### Issues Encountered

{Document any issues encountered during the release process}

**Example:**
- **Issue**: Platform build timeout on osx-arm64
- **Cause**: GitHub Actions runner resource contention
- **Solution**: Re-ran workflow
- **Prevention**: Consider using retry logic in workflow

### Process Improvements

{Document improvements discovered during this release}

**Example:**
- Added monitor-release.fsx script to reduce manual monitoring
- Improved changelog validation in prepare-release.fsx
- Updated pre-flight checks to validate remote CI status

### Breaking Changes

{List any breaking changes in this release with migration guidance}

**Example:**
- **Change**: Removed deprecated `OldApi.Method()`
- **Migration**: Use `NewApi.Method()` instead
- **Documentation**: See migration guide in What's New

### Special Thanks

{Acknowledge contributors to this release}

**Example:**
- @contributor1 - Implemented feature X
- @contributor2 - Fixed critical bug Y
- @qa-tester - Comprehensive testing and validation

---

## 🔄 Resumption Information

**For use with resume-release.fsx if release fails**

- **Last Successful Phase**: {LAST_PHASE}
- **Last Successful Step**: {LAST_STEP}
- **Failure Point**: {FAILURE_POINT}
- **Can Resume**: [ ] Yes / [ ] No
- **Resume Command**:
  ```bash
  dotnet fsi .claude/skills/release-manager/resume-release.fsx \
    --version {VERSION} \
    --issue {ISSUE_NUMBER}
  ```

---

## 📚 Retrospective

### What Went Well ✅

{Automatically collected via validation success feedback after 3+ consecutive releases}

{List things that went smoothly during this release}

### What Could Be Improved 📈

{Automatically collected via monitor failure feedback when releases fail}

{List areas for improvement discovered during this release}

### Process Changes Detected 🔄

{Automatically detected via prepare-release.fsx}

{List any changes to release processes, workflows, or scripts}

### Feedback Summary 💬

**Failure Retrospective** (if applicable):
{Feedback collected by monitor-release.fsx on failure}

**Success Improvement Ideas** (if applicable):
{Feedback collected by validate-release.fsx after consecutive successes}

**Process Update Suggestions** (if applicable):
{Feedback collected by prepare-release.fsx when process changes detected}

### Action Items 🎯

{Convert feedback into concrete action items for future releases}

- [ ] Action item 1 (from failure feedback)
- [ ] Action item 2 (from success feedback)
- [ ] Action item 3 (from process changes)

### Lessons Learned 📖

{High-level takeaways to inform future releases}

1. **What we learned about [topic]:** {insight}
2. **How we can prevent [issue]:** {solution}
3. **How we can replicate [success]:** {approach}

---

## Commands Reference

### Preparation
```bash
# Pre-flight checks
dotnet fsi .claude/skills/release-manager/prepare-release.fsx

# Create tracking issue (use gh CLI)
gh issue create --title "Release v{VERSION}" \
  --body-file .claude/skills/release-manager/templates/release-tracking.md \
  --label release,tracking
```

### Execution
```bash
# Trigger deployment
gh workflow run deployment.yml \
  --ref main \
  --field release-version={VERSION} \
  --field configuration=Release

# Monitor release
dotnet fsi .claude/skills/release-manager/monitor-release.fsx \
  --version {VERSION} \
  --issue {ISSUE_NUMBER} \
  --update-issue
```

### Verification
```bash
# Validate release
dotnet fsi .claude/skills/release-manager/validate-release.fsx \
  --version {VERSION} \
  --smoke-tests \
  --issue {ISSUE_NUMBER} \
  --update-issue

# Test installation
dotnet tool install -g Morphir.Tool --version {VERSION}
dotnet-morphir --version
```

### Recovery
```bash
# Resume failed release
dotnet fsi .claude/skills/release-manager/resume-release.fsx \
  --version {VERSION} \
  --issue {ISSUE_NUMBER}
```

---

**Release Manager**: This issue is managed by the Release Manager skill
**Automation**: Updates via monitor-release.fsx and validate-release.fsx
**QA Integration**: Coordinated with QA Tester skill

/cc @{TEAM_MEMBERS}
