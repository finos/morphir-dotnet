# Skills Reference

> **Cross-Agent Accessibility**: This guide documents all specialized skills (gurus) available in the morphir-dotnet project, making them discoverable to all AI coding agents (Claude Code, GitHub Copilot, Cursor, Windsurf, Aider, etc.).

## Overview

The morphir-dotnet project provides specialized expert skills for domain-specific tasks. Each skill combines deep domain knowledge, automation scripts, and review capabilities to help agents deliver higher quality results more efficiently.

**Key Features:**
- **Domain Expertise**: Specialized knowledge in QA testing, AOT optimization, and release management
- **Automation Scripts**: F# scripts that save agent tokens and accelerate common tasks
- **Review Capabilities**: Built-in quality checks and continuous monitoring
- **Cross-Agent Compatible**: Accessible via documentation and scripts regardless of your agent

## How to Use This Guide

**For Claude Code Users:**
- Skills are available as interactive tools via `@skill {skill-name}`
- Examples: `@skill qa-tester`, `@skill aot-guru`, `@skill release-manager`
- Skills can run automation scripts and provide guided assistance
- **Note**: Some skills document common aliases, but these are **not supported** by Claude Code (documentation only)

**For Other Agents (Copilot, Cursor, Windsurf, Aider):**
- Read skill descriptions to understand capabilities
- Run automation scripts directly: `dotnet fsi .claude/skills/{skill}/scripts/{script-name}.fsx`
- Follow decision trees and playbooks for guided workflows
- Reference review capabilities for quality assurance

## Cross-Platform Skill Invocation

Skills work across all AI coding agents, though invocation methods differ. This section provides detailed guidance for each platform.

### Claude Code (Native `@skill` Support)

**Configuration**: [CLAUDE.md](../CLAUDE.md) + [.claude/skills/](./.claude/skills/)

**Invocation**: Use `@skill {skill-name}` command
```
@skill qa-tester
Create a test plan for PR #123

@skill aot-guru
Diagnose trimming warnings in src/Morphir.Tool

@skill release-manager
Prepare release for version 1.0.0
```

**Features**:
- Interactive skills run automatically
- Automation scripts executed by skills
- Review capabilities trigger on appropriate actions
- Alias documentation: Skills may document common short forms (e.g., "qa", "tester") but these are **not functional** - only `@skill {official-name}` works

**Testing**: Skills native to Claude Code (no emulation testing needed)

---

### GitHub Copilot (Documentation-Based Emulation)

**Configuration**: [.github/copilot-instructions.md](../.github/copilot-instructions.md)

**Invocation**: Use natural language referencing skills
```
"Use the QA Tester skill to create a test plan for PR #123"
"Apply AOT Guru guidance to optimize this code"
"Follow Release Manager playbook for version 1.0.0"
```

**How It Works**:
1. Ask Copilot to use a specific skill
2. Copilot reads `.claude/skills/{skill-name}/SKILL.md`
3. Copilot applies skill guidance manually
4. Run automation scripts: `dotnet fsi .claude/skills/{skill}/scripts/{script}.fsx`

**Discovering Skills**:
Ask: "What skills are available?" → Copilot reads `.agents/skills-reference.md`

**Testing**: See issue #266 for comprehensive Copilot skill emulation tests

---

### Cursor (`.cursorrules` + @-Mentions)

**Configuration**: [.cursorrules](../.cursorrules) file references skills

**Invocation Method 1 - Automatic (via .cursorrules)**:
```
"Help me with testing"  → Cursor recognizes "testing" trigger, applies QA Tester
"Optimize binary size"  → Cursor applies AOT Guru guidance
```

**Invocation Method 2 - @-Mention**:
```
@.claude/skills/qa-tester/SKILL.md Create test plan for PR #123
@.agents/skills-reference.md What skills are available?
```

**Invocation Method 3 - Composer Mode**:
Add multiple skill files to Composer for multi-skill workflows

**Testing**: See issue #267 for comprehensive Cursor skill emulation tests

---

### Windsurf (Cascade AI Auto-Discovery)

**Configuration**: [.windsurf/rules.md](../.windsurf/rules.md) (if present)

**Invocation**: Natural language - Cascade discovers skills automatically
```
"Use QA Tester to validate this PR"
"Apply AOT Guru best practices to this code"
"Prepare release with Release Manager"
```

**How It Works**:
- Cascade AI indexes codebase and discovers skills automatically
- Context-aware questions trigger relevant skills
- Multi-file editing coordinates multiple skills

**Features**:
- **Automatic discovery**: Ask domain questions, Cascade finds skills
- **Inline edit**: Select code → "Apply {skill} best practices"
- **Multi-skill coordination**: Complex tasks engage multiple skills

**Testing**: See issue #268 for comprehensive Windsurf skill emulation tests

---

### JetBrains AI Assistant (Custom Prompts)

**Configuration**: Custom prompts in `.jetbrains/prompts/` (to be created)

**Invocation Method 1 - Custom Prompts (Recommended)**:
Create custom prompts that reference skill files:
```
Name: "QA Tester - Create Test Plan"
Prompt: Read .claude/skills/qa-tester/SKILL.md and create test plan for $SELECTION
```

**Invocation Method 2 - AI Chat**:
```
"Use QA Tester skill to create test plan for PR #123"
```

**Invocation Method 3 - Context Actions**:
Select code → Invoke context action → AI may reference skills

**Features**:
- Custom prompts library for each skill
- F1 for quick documentation
- Multi-turn chat maintains context

**Testing**: See issue #269 for comprehensive JetBrains AI skill emulation tests

---

### Universal Skill Access Patterns

**For All Agents**:

1. **Skill Discovery**:
   - Read `.agents/skills-reference.md` for complete skill list
   - Each skill has: description, capabilities, scripts, playbooks

2. **Skill Documentation**:
   - Primary: `.claude/skills/{skill-name}/SKILL.md`
   - Includes: responsibilities, competencies, playbooks, decision trees, patterns

3. **Automation Scripts**:
   - Location: `.claude/skills/{skill-name}/scripts/`
   - Run directly: `dotnet fsi {script-path}`
   - Token efficient alternative to manual steps

4. **Skill Aliases** (Documentation Only):
   - Skills may document common short forms in YAML frontmatter
   - Example: `# Common short forms: qa, test, tester`
   - **Not functional** - only for user understanding
   - Only Claude Code's `@skill` command works; alternatives must reference full skill name

### Agent Capabilities Comparison

| Feature                    | Claude Code | Copilot | Cursor | Windsurf | JetBrains |
|---------------------------|-------------|---------|---------|----------|-----------|
| Native skill invocation   | ✅ `@skill`  | ❌      | ❌      | ❌       | ❌        |
| Documentation-based       | ✅          | ✅      | ✅      | ✅       | ✅        |
| Automation scripts        | ✅ (auto)    | ✅ (manual) | ✅ (manual) | ✅ (manual) | ✅ (manual) |
| Automatic skill discovery | ✅          | ❌      | ⚠️ (.cursorrules) | ✅ (Cascade) | ⚠️ (custom prompts) |
| Multi-skill coordination  | ✅          | ⚠️      | ✅ (Composer) | ✅ (Cascade) | ⚠️        |
| Review capabilities       | ✅          | ❌      | ❌      | ❌       | ❌        |

**Legend**: ✅ Full support | ⚠️ Partial/requires setup | ❌ Not available

See [capabilities-matrix.md](./capabilities-matrix.md) for detailed comparison.

## Available Skills

### 1. QA Tester Guru

**Full Documentation**: [.claude/skills/qa-tester/skill.md](../.claude/skills/qa-tester/skill.md)

#### Scope and Purpose
Comprehensive QA testing and verification for morphir-dotnet. Ensures quality through test plan design, execution, regression testing, and issue reporting.

#### Core Competencies

1. **Test Plan Development**
   - Create comprehensive test plans from issues/PRs
   - Cover happy paths, edge cases, error conditions
   - Organize by priority (Critical, High, Medium, Low)
   - Include manual and automated procedures

2. **Regression Testing**
   - Identify areas affected by changes
   - Run existing test suites
   - Verify no functionality breaks
   - Validate backwards compatibility

3. **End-to-End Testing**
   - Test complete user workflows
   - Verify all components integrate correctly
   - Validate output formats
   - Check error handling

4. **Issue Reporting**
   - Clear, reproducible bug reports
   - Steps to reproduce with expected vs actual behavior
   - Environment details and relevant logs
   - Suggested severity and potential root cause

5. **Test Automation**
   - Create and maintain test scripts
   - Build reusable test fixtures
   - Automate repetitive testing tasks

6. **Coverage Monitoring**
   - Track test coverage trends
   - Identify coverage gaps
   - Ensure >= 80% overall coverage

#### Review Capability

**Continuous Coverage Scanning**
- Monitors test coverage across commits and PRs
- Detects ignored tests and skipped scenarios
- Identifies coverage gaps in new features
- Tracks coverage trends over time

**Review Output:**
- Coverage trend analysis (increasing/decreasing)
- Gap identification (untested code paths)
- Testing debt report (ignored tests, TODOs)
- Compliance checks (BDD coverage, unit test quality)

**Review Triggers:**
- Automatic on PR verification workflow
- Manual via smoke test and regression test playbooks
- Pre-release validation

#### Automation Scripts

Location: `.claude/skills/qa-tester/scripts/` (to be created)

**smoke-test.fsx**
- Quick validation of core functionality
- Runs in 5-10 minutes
- Tests: Build, Unit Tests, Package Generation
- **Token Savings**: ~500 tokens (vs manual test execution and reporting)

**regression-test.fsx**
- Comprehensive regression validation
- Runs in 30-45 minutes
- Tests: Full CI workflow, E2E tests, Tool installation
- **Token Savings**: ~1000 tokens (vs manual multi-step validation)

**validate-packages.fsx**
- Package structure and metadata validation
- Inspects NuGet packages
- Verifies tool configuration (DotnetToolSettings.xml)
- **Token Savings**: ~300 tokens (vs manual package inspection)

#### Manual Workflow for Non-Claude Agents

**To run a smoke test:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx

# Option 2: Manual steps
./build.sh Compile
./build.sh Test
./build.sh PackAll
ls artifacts/packages/*.nupkg | wc -l  # Should be 4
```

**To perform regression testing:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/qa-tester/scripts/regression-test.fsx

# Option 2: Follow playbook
# See .claude/skills/qa-tester/skill.md "Regression Testing Playbook"
```

**To validate packages:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/qa-tester/scripts/validate-packages.fsx

# Option 2: Manual inspection
unzip -l artifacts/packages/Morphir.Tool.*.nupkg
unzip -p artifacts/packages/Morphir.Tool.*.nupkg tools/net10.0/any/DotnetToolSettings.xml
```

#### Decision Trees

**"Should I create a test plan?"**
```
Is there a PR or issue with acceptance criteria?
  YES → Create test plan
    ├─ Read acceptance criteria
    ├─ Review implementation decisions
    ├─ Design test cases (happy path, edge cases, errors)
    ├─ Organize by priority
    └─ Include execution scripts

  NO → Is there a feature request or bug report?
    YES → Identify test scenarios from description
    NO → Consult with team for requirements
```

**"What type of testing is needed?"**
```
What changed?
  ├─ New feature → Feature testing playbook
  ├─ Bug fix → Regression testing playbook  
  ├─ Build system → Build system testing playbook
  ├─ Package changes → Package testing playbook
  └─ Pre-release → Full regression + smoke tests
```

#### Pattern Catalog

**Test Design Patterns:**
- **AAA Pattern**: Arrange, Act, Assert for clear test structure
- **Test Data Builders**: Reusable fixtures for consistent test data
- **BDD Scenarios**: Given-When-Then for acceptance criteria
- **Test One Thing**: Each test validates a single behavior

**Test Organization Patterns:**
- **Suite Per Feature**: Group tests by feature area
- **Priority Tagging**: Critical, High, Medium, Low
- **Category Filtering**: Unit, Integration, E2E, Smoke
- **Naming Convention**: `Should_ExpectedBehavior_When_Condition`

#### Integration with Other Agents

- **Development Agents**: Request test plans before implementation
- **Documentation Agents**: Verify documented examples work
- **Deployment Agents**: Perform pre-deployment validation

---

### 2. AOT Guru

**Full Documentation**: [.claude/skills/aot-guru/skill.md](../.claude/skills/aot-guru/skill.md)

#### Scope and Purpose
Expert in Native AOT compilation, trimming, and binary size optimization for morphir-dotnet. Focuses on single-file trimmed executables with guidance toward eventual Native AOT support.

#### Core Competencies

1. **Single-File Trimmed Executables** (Primary Focus)
   - Produce optimized, trimmed single-file deployments
   - Enable size optimizations (InvariantGlobalization, etc.)
   - Test with PublishTrimmed=true
   - Measure and optimize binary size

2. **AOT Readiness Assessment**
   - Guide development toward AOT-compatible patterns
   - Identify reflection usage and dynamic code
   - Recommend source generators (C#) or Myriad (F#)
   - Evaluate dependency compatibility

3. **Trimming Diagnostics**
   - Analyze trim warnings (IL2026, IL2087, IL3050)
   - Identify reflection usage patterns
   - Check for dynamic code generation
   - Review dependencies for trimming compatibility

4. **Size Optimization Analysis**
   - Measure baseline and optimized sizes
   - Identify large dependencies
   - Analyze with tools (ilspy, dotnet-size-analyzer)
   - Target: 15-35 MB (trimmed), 5-12 MB (AOT goal)

5. **Reflection Workarounds**
   - Source generators for C# serialization
   - Myriad for F# compile-time generation
   - DynamicDependency attributes
   - Explicit type registration

6. **F# and Myriad Expertise**
   - Recommend Myriad for F# code generation
   - Avoid F# reflection features in library code
   - Use explicit type annotations
   - Design for trimming compatibility

#### Review Capability

**Quarterly Project Review**
- Scheduled comprehensive project scan every 3 months
- Identifies new reflection usage since last review
- Detects binary size creep and trends
- Recommends plugin or tool updates

**Review Output:**
- IL warning summary (IL2026, IL3050, etc.)
- Size trend analysis (per-component breakdown)
- Reflection hot spots (files, methods, dependencies)
- AOT readiness score (0-100%)
- Plugin recommendations (when compatibility issues found)

**Review Triggers:**
- Quarterly scheduled review (automated reminder)
- Before major releases
- After significant dependency updates
- When size targets are exceeded

#### Automation Scripts

Location: `.claude/skills/aot-guru/scripts/` (to be created)

**aot-diagnostics.fsx**
- Diagnose AOT issues in a project
- Checks: PublishAot config, reflection patterns, dependencies
- **Token Savings**: ~800 tokens (vs manual project analysis)

**aot-analyzer.fsx**
- Analyze build output for warnings
- Groups by category with suggested fixes
- **Token Savings**: ~600 tokens (vs manual log parsing)

**aot-test-runner.fsx**
- Run comprehensive AOT test matrix
- Tests: Framework-dependent, Self-contained, Trimmed, AOT
- Compares sizes and validates functionality
- **Token Savings**: ~1200 tokens (vs manual multi-config builds)

#### Manual Workflow for Non-Claude Agents

**To diagnose AOT issues:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/aot-guru/scripts/aot-diagnostics.fsx <project-path>

# Option 2: Manual checks
dotnet publish -c Release -r linux-x64 /p:PublishTrimmed=true
# Review warnings in build output
# Check for IL2026, IL3050, etc.
```

**To analyze build warnings:**
```bash
# Option 1: Use automation script
dotnet publish > build.log 2>&1
dotnet fsi .claude/skills/aot-guru/scripts/aot-analyzer.fsx build.log

# Option 2: Manual analysis
grep "IL2026\|IL3050\|IL2087" build.log
# Group and categorize manually
```

**To run AOT test matrix:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/aot-guru/scripts/aot-test-runner.fsx --runtime linux-x64

# Option 2: Manual execution
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r linux-x64 /p:PublishTrimmed=true
dotnet publish -c Release -r linux-x64 /p:PublishAot=true
ls -lh bin/Release/net10.0/*/publish/morphir
```

#### Decision Trees

**"I have an AOT compilation error"**
```
1. What type of error?
   A. IL2026 (RequiresUnreferencedCode)
      → Is this System.Text.Json?
         YES → Use source-generated JsonSerializerContext
         NO → Apply DynamicDependency or refactor to avoid reflection
   
   B. IL3050 (RequiresDynamicCode)
      → Is this LINQ expressions or Reflection.Emit?
         YES → Replace with delegates or source generators
         NO → Check third-party library compatibility
   
   C. IL2087 (Type incompatibility)
      → Add [DynamicallyAccessedMembers] attributes
      → Ensure generic constraints match
   
   D. Runtime error (MissingMethodException)
      → Check trimmer warnings
      → Add DynamicDependency or TrimmerRootDescriptor
      → Test with PublishTrimmed first to isolate issue

2. After fix:
   → Update aot-trimming-guide.md if new pattern
   → Add to known issues if recurring
```

**"My binary is too large"**
```
1. Current size vs target?
   > 35 MB → Check dependencies (likely issue)
   25-35 MB → Feature-rich target (acceptable)
   15-25 MB → Minimal target (good)
   < 15 MB → Excellent

2. For sizes > target:
   A. Check optimization flags
      → IlcOptimizationPreference=Size (for AOT)
      → InvariantGlobalization=true
      → DebugType=none
   
   B. Analyze dependencies
      → dotnet list package
      → Check for heavy libraries
      → Replace with lighter alternatives
   
   C. Check embedded resources
      → Are schemas embedded efficiently?
      → Can resources be external?
   
   D. Profile with tools
      → dotnet-size-analyzer
      → ILSpy size analysis
```

#### Pattern Catalog

**AOT-Ready Patterns** (use now):
- Source generators (C#) for code generation
- Myriad (F#) for compile-time type generation
- Explicit type registration (vs Assembly.GetTypes())
- System.Text.Json with source generators
- Compile-time known types for DI

**AOT-Incompatible Patterns** (avoid):
- Dynamic assembly loading (plugins)
- Reflection.Emit / DynamicMethod
- LINQ Expression compilation
- FSharp.SystemTextJson (uses reflection)
- Newtonsoft.Json (uses reflection)

**Size Optimization Techniques:**
```xml
<!-- Single-file trimmed configuration -->
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>
  <InvariantGlobalization>true</InvariantGlobalization>
  <DebugType>none</DebugType>
  <EventSourceSupport>false</EventSourceSupport>
</PropertyGroup>
```

#### Integration with Other Agents

- **Development Agents**: Design code with AOT patterns from the start
- **QA Tester**: Validate trimmed and AOT builds in E2E tests
- **Release Manager**: Ensure release executables are properly optimized

---

### 3. Release Manager

**Full Documentation**: [.claude/skills/release-manager/skill.md](../.claude/skills/release-manager/skill.md)

#### Scope and Purpose
Orchestrates the complete release lifecycle from preparation through verification. Ensures quality, consistency, and comprehensive documentation for all releases.

#### Core Competencies

1. **Version Management**
   - Parse CHANGELOG.md to analyze changes
   - Suggest version bumps (major/minor/patch)
   - Validate semantic versioning
   - Check version availability on NuGet

2. **Changelog Management**
   - Follow [Keep a Changelog](https://keepachangelog.com/) format
   - Categorize changes (Added, Changed, Fixed, etc.)
   - Move [Unreleased] to versioned section
   - Update comparison links

3. **Release Preparation**
   - Validate remote CI status (main branch)
   - Check for uncommitted changes (advisory)
   - Run pre-flight validation
   - Create release tracking issue

4. **Release Execution**
   - Trigger GitHub Actions deployment workflow
   - Monitor workflow progress (5 platform builds, E2E tests)
   - Handle failures with recovery procedures
   - Track progress in release issue

5. **Release Verification**
   - Validate packages published to NuGet
   - Test tool installation
   - Coordinate with QA Tester for smoke tests
   - Verify documentation updated

6. **Release Documentation**
   - Generate "What's New" documents
   - Create release notes with highlights
   - Document breaking changes with migration guides
   - Maintain release playbook

#### Review Capability

**Process Consistency Checks**
- Validates release follows documented playbook
- Detects deviations from standard workflow
- Ensures all steps completed (preparation, execution, verification, documentation)

**Changelog Quality Checks**
- Validates changelog format and structure
- Ensures proper categorization of changes
- Verifies breaking changes are clearly marked
- Checks comparison links are updated

**Version Verification**
- Confirms version follows semantic versioning
- Validates version doesn't already exist
- Ensures version aligns with change types
- Checks all packages have consistent versions

**Review Output:**
- Process compliance report (steps completed/skipped)
- Changelog quality score (format, completeness, categorization)
- Version consistency check (semver, NuGet, git tags)
- Documentation completeness (release notes, What's New, breaking changes)

**Review Triggers:**
- During release preparation (pre-flight checks)
- After release execution (verification phase)
- Post-release retrospective (quarterly)

#### Automation Scripts

Location: `.claude/skills/release-manager/scripts/`

**prepare-release.fsx**
- Automate pre-flight checks
- Parse and validate changelog
- Suggest version based on changes
- Check NuGet availability
- **Token Savings**: ~700 tokens (vs manual validation)

**monitor-release.fsx**
- Monitor GitHub Actions workflow
- Track job and step progress
- Update tracking issue automatically
- Alert on failures
- **Token Savings**: ~1000 tokens (vs continuous manual monitoring)

**monitor-pr.fsx**
- Monitor PR checks until completion
- Live progress display
- Optional auto-merge when checks pass (requires explicit confirmation)
- **Token Savings**: ~500 tokens (vs polling PR status)

**validate-release.fsx**
- Query NuGet for packages
- Test tool installation
- Run smoke tests
- Generate verification report
- **Token Savings**: ~600 tokens (vs manual package validation)

**resume-release.fsx**
- Resume failed release from checkpoint
- Read tracking issue for context
- Identify last successful step
- Prompt for confirmation and resume
- **Token Savings**: ~400 tokens (vs manual failure recovery)

#### Manual Workflow for Non-Claude Agents

**To prepare a release:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/release-manager/scripts/prepare-release.fsx

# Option 2: Manual steps (see Standard Release Playbook)
# 1. Check CI status: gh run list --workflow=ci.yml
# 2. Parse CHANGELOG.md manually
# 3. Determine version from changes
# 4. Search NuGet: https://nuget.org/packages/Morphir.Core
```

**To monitor a release:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/release-manager/scripts/monitor-release.fsx --version 1.0.0

# Option 2: Manual monitoring
gh run list --workflow=deployment.yml --limit 5
gh run watch <run-id>
```

**To monitor a PR:**
```bash
# Option 1: Use automation script (NO AUTO-MERGE without confirmation!)
dotnet fsi .claude/skills/release-manager/scripts/monitor-pr.fsx --pr 123

# Option 2: Manual monitoring
gh pr view 123 --json statusCheckRollup
gh pr checks 123 --watch
```

**To validate a release:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/release-manager/scripts/validate-release.fsx --version 1.0.0

# Option 2: Manual validation
dotnet tool install -g Morphir.Tool --version 1.0.0
dotnet-morphir --version
```

#### Decision Trees

**"Should I create a release?"**
```
Does [Unreleased] section in CHANGELOG.md have changes?
  YES → Analyze change types
    ├─ Breaking changes? → Major release (X.0.0)
    ├─ New features? → Minor release (x.Y.0)
    ├─ Bug fixes only? → Patch release (x.y.Z)
    └─ Pre-release needed? → Alpha/Beta/RC (x.y.z-alpha.N)
  
  NO → Not ready for release
    → Continue development
    → Update changelog as changes are made
```

**"Release failed, what should I do?"**
```
1. Identify failure point (which workflow stage?)
   ├─ Version validation → Fix semver, retry
   ├─ Build failure → Fix code, new release attempt
   ├─ E2E test failure → Fix tests, new release attempt
   ├─ NuGet publish timeout → Infrastructure issue, retry
   └─ Platform-specific failure → Check logs, fix or retry

2. Is it resumable?
   ├─ Transient/infrastructure → Use resume-release.fsx
   └─ Code/test issue → Fix and new release attempt

3. Document in tracking issue
   → What failed, why, how to prevent
```

#### Pattern Catalog

**Release Workflows:**
- **Standard Release**: Regular release from main branch (70-105 min)
- **Hotfix Release**: Critical bug fix from hotfix branch (45-60 min)
- **Pre-release**: Alpha/Beta/RC for testing (75-110 min)
- **Failed Release Recovery**: Resume from checkpoint (15-120 min variable)

**Changelog Patterns:**
```markdown
## [Unreleased]

### Added
- New feature X for use case Y (#123)

### Changed
- Improved performance of Z by 30% (#124)

### Fixed
- Bug in component A when condition B (#125)

### Breaking
- **BREAKING:** Renamed API method from old() to new() (#126)
```

**Version Selection Logic:**
- **Major (X.0.0)**: Breaking changes, major new features
- **Minor (x.Y.0)**: New features (backwards compatible)
- **Patch (x.y.Z)**: Bug fixes only
- **Pre-release (x.y.z-alpha.N)**: Alpha, beta, rc versions

#### Integration with Other Agents

- **QA Tester**: Hand off for smoke tests after release published
- **Development Agents**: Coordinate changelog updates during development
- **Documentation Agents**: Update release notes and "What's New" documents

---

## Cross-Agent Compatibility

See [capabilities-matrix.md](./capabilities-matrix.md) for detailed cross-agent compatibility information.

### Quick Reference

| Feature | Claude Code | Copilot | Cursor | Windsurf | Aider |
|---------|------------|---------|--------|----------|-------|
| Interactive skills (`@skill`) | ✅ | ❌ | ❌ | ❌ | ❌ |
| Automation scripts | ✅ | ✅ | ✅ | ✅ | ✅ |
| Review capabilities | ✅ | ✅† | ✅† | ✅† | ✅† |
| Decision trees | ✅ | ✅ | ✅ | ✅ | ✅ |
| Playbooks | ✅ | ✅ | ✅ | ✅ | ✅ |

† Review capabilities accessible via documentation and scripts

## How Scripts Save Agent Tokens

Automation scripts save significant agent tokens by:

1. **Pre-computed Analysis**: Scripts analyze code/configs and provide structured results
2. **Batch Operations**: Execute multiple related tasks in a single invocation
3. **Formatted Output**: Return clean, parseable results instead of raw tool output
4. **Error Handling**: Built-in error detection and categorization
5. **Idempotent**: Can be re-run safely without side effects

**Example Token Savings:**

**Without script** (manual approach):
```
Agent: Run git status
Agent: Parse output
Agent: Run git diff
Agent: Analyze changes
Agent: Run dotnet test
Agent: Parse test results
Agent: Format summary
Total: ~800-1000 tokens
```

**With script** (`smoke-test.fsx`):
```
Agent: Run dotnet fsi smoke-test.fsx
Agent: Parse structured JSON output
Total: ~200-300 tokens
Savings: ~500-700 tokens (60-70% reduction)
```

## Getting Started

### For Claude Code Users

1. **Invoke a skill** using `@skill {skill-name}`:
   ```
   @skill qa-tester
   Please create a test plan for PR #123
   ```

2. **Skills can run automation scripts** for you automatically

3. **Skills provide guided assistance** through complex workflows

### For Other Agent Users

1. **Read skill documentation** to understand capabilities:
   - [QA Tester](../.claude/skills/qa-tester/skill.md)
   - [AOT Guru](../.claude/skills/aot-guru/skill.md)
   - [Release Manager](../.claude/skills/release-manager/skill.md)

2. **Run automation scripts directly**:
   ```bash
   dotnet fsi .claude/skills/{skill}/scripts/{script-name}.fsx
   ```

3. **Follow playbooks** for complex workflows:
   - Each skill includes detailed playbooks
   - Step-by-step procedures
   - Common pitfalls and solutions

4. **Use decision trees** when uncertain:
   - Each skill provides decision trees for common scenarios
   - Flow charts guide you to the right approach

## Contributing

### Adding a New Skill

See [Guru Creation Guide](./guru-creation-guide.md) for comprehensive step-by-step instructions on creating new skills. A complete [skill template](./../.claude/skills/template/) is available to accelerate new guru development.

### Updating Existing Skills

When updating a skill:
1. Update the skill's `skill.md` file
2. Update automation scripts if changed
3. Update this `skills-reference.md` if scope/capabilities changed
4. Update `capabilities-matrix.md` if cross-agent compatibility changed
5. Update `AGENTS.md` if it affects general agent guidance

### Feedback

Found an issue or have a suggestion? Please:
1. Open an issue with label `agent-guidance`
2. Mention which skill and agent you're using
3. Describe the problem or suggestion
4. Provide examples if possible

## Skill Aliases (Documentation Only)

**IMPORTANT**: Skill aliases are **NOT a supported feature** of Claude Code. Some skills document commonly used short forms in their YAML frontmatter, but these are purely informational.

### What This Means

Skills may include alias documentation like:
```yaml
---
name: qa-tester
# Common short forms: qa, test, tester
---
```

**Reality:**
- Only `@skill qa-tester` works (the official `name` field)
- `@skill qa` will **NOT** work
- Aliases are documentation to help users understand alternative names
- This is our workaround/convention, not an official feature

### Why We Document Aliases

1. **User expectations**: Shows what short forms users might naturally expect
2. **Discoverability**: Makes skills easier to understand
3. **Consistency**: Teams can agree on common terminology
4. **Future-proofing**: If Claude Code adds alias support later, we're ready

**Full details**: See [TROUBLESHOOTING.md - Skill Aliases](../.claude/skills/TROUBLESHOOTING.md#skill-aliases-documentation-only)

## References

- **[AGENTS.md](../AGENTS.md)**: Primary guidance for all AI agents
- **[Guru Philosophy](./guru-philosophy.md)**: Core philosophy behind gurus
- **[Guru Creation Guide](./guru-creation-guide.md)**: Step-by-step guide for creating new gurus
- **[Skill Matrix](./skill-matrix.md)**: Maturity tracking for all gurus
- **[Capabilities Matrix](./capabilities-matrix.md)**: Cross-agent compatibility details
- **[QA Testing Guide](./qa-testing.md)**: Cross-agent QA practices
- **[AOT Optimization Guide](./aot-optimization.md)**: Cross-agent AOT guidance
- **[Skills Troubleshooting](../.claude/skills/TROUBLESHOOTING.md)**: Common issues and solutions (including aliases)
- **[Claude Skills Directory](../.claude/skills/)**: Full skill implementations
- **[Skill Template](../.claude/skills/template/)**: Template for creating new gurus

---

**Remember**: Skills exist to make your job easier and deliver higher quality results. Don't hesitate to use them, run their scripts, or follow their guidance. They represent accumulated expertise and lessons learned from real-world usage.
