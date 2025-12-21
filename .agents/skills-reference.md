# Skills Reference

> **Cross-Agent Accessibility**: This guide documents all specialized skills (gurus) available in the morphir-dotnet project, making them discoverable to all AI coding agents (Claude Code, GitHub Copilot, Cursor, Windsurf, Aider, etc.).

## Overview

The morphir-dotnet project provides specialized expert skills for domain-specific tasks. Each skill combines deep domain knowledge, automation scripts, and review capabilities to help agents deliver higher quality results more efficiently.

**Key Features:**
- **Domain Expertise**: Specialized knowledge in QA testing, AOT optimization, release management, technical documentation, vulnerability management, and Elm-to-F# migration
- **Automation Scripts**: F# scripts that save agent tokens and accelerate common tasks
- **Review Capabilities**: Built-in quality checks and continuous monitoring
- **Cross-Agent Compatible**: Accessible via documentation and scripts regardless of your agent

## How to Use This Guide

**For Claude Code Users:**
- Skills are available as interactive tools via `@skill {skill-name}`
- Examples: `@skill qa-tester`, `@skill aot-guru`, `@skill release-manager`, `@skill technical-writer`, `@skill vulnerability-resolver`, `@skill elm-to-fsharp-guru`
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

@skill technical-writer
Audit documentation for consistency and completeness

@skill vulnerability-resolver
Scan for CVEs and help resolve security vulnerabilities

@skill elm-to-fsharp-guru
Migrate Morphir.IR.Type module from Elm to F#
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

#### Copilot Usage Guide

- **Discover skills**: Ask "What skills are available in this project?" → Copilot should reference this file and list QA Tester, AOT Guru, Release Manager with SKILL.md paths.
- **Invoke guidance**: Use natural language, e.g., "Use the QA Tester skill to create a test plan for PR #123" → Copilot reads `.claude/skills/qa-tester/skill.md` and follows the Test Plan playbook.
- **Run scripts**: Provide and run commands, e.g., `dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx`, `dotnet fsi .claude/skills/aot-guru/scripts/aot-diagnostics.fsx`.
- **Follow playbooks**: Ask "Walk me through the regression testing playbook" → Copilot enumerates steps with commands and validation criteria.
- **Aliases**: `@skill` and aliases are Claude-specific. In Copilot, reference full skill names and SKILL.md files.

See the test plan for BDD scenarios and acceptance criteria: [docs/content/contributing/qa/copilot-skill-emulation-test-plan.md](../docs/content/contributing/qa/copilot-skill-emulation-test-plan.md).

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

### 4. Technical Writer

**Full Documentation**: [.claude/skills/technical-writer/SKILL.md](../.claude/skills/technical-writer/SKILL.md)

#### Scope and Purpose
Expert in documentation, Hugo static site generator, Docsy theme, and visual communication. Creates and maintains high-quality documentation with compelling diagrams, consistent style, and excellent user experience.

#### Core Competencies

1. **Hugo Static Site Generator Mastery**
   - Configuration management (hugo.toml)
   - Module and theme integration
   - Shortcode usage and creation
   - Build troubleshooting and optimization
   - Live preview and development workflow

2. **Docsy Theme Expertise**
   - Content organization and navigation
   - Customization patterns (SCSS, layouts)
   - Component usage (alerts, cards, tabs)
   - Never modify theme directly philosophy

3. **Mermaid Diagram Creation**
   - Flowcharts for processes
   - Sequence diagrams for interactions
   - Class diagrams for architecture
   - State diagrams for workflows
   - ER diagrams for data models
   - Gantt charts for timelines

4. **PlantUML Expertise**
   - C4 architecture diagrams
   - Component diagrams
   - Deployment diagrams
   - Complex system visualization

5. **Markdown Mastery**
   - Hugo-flavored markdown
   - Frontmatter optimization
   - Cross-referencing and links
   - Table formatting

6. **API Documentation**
   - XML doc comments
   - API reference generation
   - Code example quality
   - Developer experience

7. **Style Guide Enforcement**
   - Consistent voice and tone
   - Terminology standardization
   - Brand identity maintenance
   - Accessibility compliance

#### Review Capability

**Documentation Quality Review**
- Validates link integrity across all docs
- Checks Hugo build health
- Verifies diagram syntax correctness
- Ensures style guide compliance
- Monitors content freshness

**Review Output:**
- Link validation report (broken links, redirects)
- Hugo build diagnostics
- Diagram syntax validation
- Style compliance score
- Content gap analysis

**Review Triggers:**
- Pre-release documentation validation
- Quarterly documentation audit
- After major feature additions
- PR reviews for docs changes

#### Automation Scripts

Location: `.claude/skills/technical-writer/scripts/` (to be created)

**link-validator.fsx**
- Validate all internal/external links
- Generate broken link report
- Suggest fixes for common issues
- **Token Savings**: ~800 tokens (vs manual link checking)

**hugo-doctor.fsx**
- Diagnose Hugo build issues
- Check configuration validity
- Verify module compatibility
- **Token Savings**: ~600 tokens (vs manual troubleshooting)

**diagram-validator.fsx**
- Validate Mermaid/PlantUML syntax
- Check diagram rendering
- Identify broken diagrams
- **Token Savings**: ~400 tokens (vs manual diagram validation)

**content-auditor.fsx**
- Analyze content coverage
- Detect stale documentation
- Identify missing sections
- **Token Savings**: ~700 tokens (vs manual content analysis)

**style-checker.fsx**
- Enforce terminology consistency
- Check heading hierarchy
- Validate frontmatter
- **Token Savings**: ~500 tokens (vs manual style review)

**release-notes-generator.fsx**
- Parse changelog
- Generate What's New document
- Create release announcement
- **Token Savings**: ~600 tokens (vs manual document creation)

**screenshot-taker.fsx**
- Capture UI screenshots (with Playwright MCP)
- Update outdated screenshots
- Validate visual documentation
- **Token Savings**: ~900 tokens (vs manual screenshot process)

#### Manual Workflow for Non-Claude Agents

**To validate documentation links:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/technical-writer/scripts/link-validator.fsx

# Option 2: Manual steps
cd docs && hugo --gc --minify
# Check build output for broken link warnings
```

**To diagnose Hugo issues:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/technical-writer/scripts/hugo-doctor.fsx

# Option 2: Manual troubleshooting
cd docs && hugo version
hugo config
hugo server --disableFastRender
```

**To validate diagrams:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/technical-writer/scripts/diagram-validator.fsx

# Option 2: Manual check
# Build Hugo and check browser console for Mermaid errors
cd docs && hugo server
```

**To use Playwright MCP for live verification:**
```
# If Playwright MCP is available, use browser tools
1. browser_navigate to documentation URL
2. browser_snapshot to capture accessibility tree
3. browser_take_screenshot for visual verification
```

#### Decision Trees

**"What type of diagram should I use?"**
```
What are you visualizing?
  ├─ Process/workflow → Mermaid Flowchart
  ├─ Time-ordered interactions → Mermaid Sequence Diagram
  ├─ Class relationships → Mermaid Class Diagram
  ├─ State transitions → Mermaid State Diagram
  ├─ Data model → Mermaid ER Diagram
  ├─ Project timeline → Mermaid Gantt Chart
  ├─ System architecture (C4) → PlantUML
  ├─ Component relationships → PlantUML Component
  └─ Deployment topology → PlantUML Deployment
```

**"Hugo build is failing"**
```
1. What type of error?
   ├─ "module not found" → Check go.mod, run hugo mod tidy
   ├─ "shortcode not found" → Verify shortcode path/name
   ├─ "template not found" → Check layouts directory
   ├─ "frontmatter error" → Validate YAML syntax
   ├─ "content error" → Check markdown formatting
   └─ "theme error" → Never modify theme, use overrides

2. After fix:
   → Test with hugo server --disableFastRender
   → Document issue if recurring
```

**"What type of documentation should I create?"**
```
What's the user need?
  ├─ Getting started → Tutorial (step-by-step)
  ├─ How to accomplish X → How-To Guide (task-oriented)
  ├─ Technical details → Reference (comprehensive)
  ├─ Why/background → Explanation (conceptual)
  └─ Quick lookup → API Reference (auto-generated)
```

#### Pattern Catalog

**Mermaid Patterns:**
```markdown
# Flowchart
flowchart LR
    A[Start] --> B{Decision}
    B -->|Yes| C[Action]
    B -->|No| D[Other]

# Sequence
sequenceDiagram
    participant A
    participant B
    A->>B: Request
    B-->>A: Response
```

**Hugo Frontmatter:**
```yaml
---
title: "Page Title"
linkTitle: "Nav Title"
weight: 10
description: "Brief description for SEO"
---
```

**Docsy Shortcodes:**
```markdown
{{< alert title="Warning" color="warning" >}}
Important content here
{{< /alert >}}

{{< tabpane >}}
{{< tab header="C#" >}}
// C# code
{{< /tab >}}
{{< tab header="F#" >}}
// F# code
{{< /tab >}}
{{< /tabpane >}}
```

#### Integration with Other Skills

- **Release Manager**: Create release notes and What's New documents
- **QA Tester**: Document test procedures and results
- **AOT Guru**: Document AOT patterns and troubleshooting guides

---

### 5. Vulnerability Resolver

**Full Documentation**: [.claude/skills/vulnerability-resolver/SKILL.md](../.claude/skills/vulnerability-resolver/SKILL.md)

#### Scope and Purpose
Specialized security vulnerability management for morphir-dotnet. Helps developers efficiently triage, fix, and document security vulnerabilities detected by OWASP Dependency-Check, maintaining a clear audit trail of all security decisions.

#### Core Competencies

1. **Vulnerability Scanning**
   - Trigger dependency-check scans on any branch
   - Configure CVSS thresholds
   - Monitor workflow progress
   - Download and analyze reports

2. **Vulnerability Analysis**
   - Parse dependency-check reports (HTML, JSON, XML)
   - Categorize by severity (Critical, High, Medium, Low)
   - Assess fix availability and false positive likelihood
   - Identify transitive dependency issues

3. **Resolution Guidance**
   - Interactive fix vs. suppress decision prompts
   - Evidence-based false positive detection
   - Clear resolution options with trade-offs
   - Research assistance with NVD links

4. **Suppression Management**
   - Create documented suppressions for false positives
   - Follow OWASP suppression file schema
   - Required metadata (reason, reviewer, date, review date)
   - Support for expiration dates

5. **Fix Automation**
   - Generate package update commands
   - Verify fix effectiveness with re-scan
   - Handle transitive dependency upgrades
   - Detect breaking changes

6. **Security Documentation**
   - Resolution summaries for audit trail
   - Suppression rationale with evidence
   - Quarterly review reminders
   - PR descriptions for security fixes

#### Review Capability

**Suppression Review**
- Quarterly review of all active suppressions
- Check if fixes have become available
- Validate suppression rationale still applies
- Update expiration dates

**Security Posture Assessment**
- Unresolved vulnerability tracking
- Suppression quality checks
- New fix availability detection
- Documentation completeness

**Review Triggers:**
- Quarterly scheduled review
- Before releases (pre-release gate)
- After dependency updates
- Manual via `@skill vulnerability-resolver review`

#### Automation Scripts

Location: `.claude/skills/vulnerability-resolver/scripts/` (to be created)

**scan-branch.fsx**
- Trigger CVE scan on specified branch
- Configure CVSS threshold and suppression settings
- **Token Savings**: ~500 tokens (vs manual workflow triggering)

**parse-report.fsx**
- Parse dependency-check HTML/JSON report
- Extract CVE details and categorize
- **Token Savings**: ~2000 tokens (vs manual report reading)

**create-suppression.fsx**
- Generate properly formatted suppression XML
- Include required metadata
- **Token Savings**: ~300 tokens (vs manual XML creation)

**verify-fixes.fsx**
- Verify package updates resolve CVEs
- Re-run scan after fix
- **Token Savings**: ~400 tokens (vs manual verification)

#### Manual Workflow for Non-Claude Agents

**To scan for vulnerabilities:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/vulnerability-resolver/scripts/scan-branch.fsx [branch]

# Option 2: Manual trigger
gh workflow run cve-scanning.yml
gh workflow run cve-scanning.yml --ref feature/my-branch
gh workflow run cve-scanning.yml -f fail-cvss=9
```

**To monitor a scan:**
```bash
gh run list --workflow=cve-scanning.yml --limit 3
gh run watch <run-id>
gh run download <run-id> -n "Depcheck report"
```

**To create a suppression:**
```bash
# Option 1: Use automation script
dotnet fsi .claude/skills/vulnerability-resolver/scripts/create-suppression.fsx \
  --cve CVE-2023-4914 --reason "Package name confusion"

# Option 2: Use template
cp .claude/skills/vulnerability-resolver/templates/suppression-entry.xml .
# Fill in template and add to dependency-check-suppressions.xml
```

#### Decision Trees

**"A CVE scan failed, what do I do?"**
```
1. Download the report artifact
   gh run download <run-id> -n "Depcheck report"

2. For each vulnerability:
   A. Is CVSS >= 7 (High/Critical)?
      YES → Prioritize resolution
      NO → Can address in normal sprint

   B. Is update available?
      YES → Check if breaking change, apply update
      NO → Investigate if false positive

   C. False positive indicators present?
      - Version mismatch (assembly vs package)
      - Package name confusion
      - Stale CVE (age > 5 years)
      - CVE targets different technology

      YES → Create documented suppression with evidence
      NO → Track upstream, document workaround
```

**"Should I suppress or fix?"**
```
Can I update the package?
├─ YES, minor/patch update → FIX (preferred)
├─ YES, but major update
│  └─ CVSS >= 9? → FIX with migration
│  └─ CVSS < 9 → Suppress temporarily + plan upgrade
└─ NO, no fix available
   └─ False positive?
      ├─ YES → SUPPRESS with documentation
      └─ NO → Document risk, track upstream
```

#### Pattern Catalog

**False Positive Patterns:**

| Pattern | Detection | Example |
|---------|-----------|---------|
| Version Misidentification | Unusual version format | Azure.Identity@1.1700.x (assembly version, not package) |
| Package Name Confusion | CVE mentions different tech | Cecil (SSG) vs Mono.Cecil (.NET library) |
| Stale CVE | CVE date >> package date | CVE-2012-2055 for Octokit@14.0.0 |
| Already Fixed Transitive | Lock file shows newer version | Transitive at 1.17.1 but reported as 1.10.0 |

**Suppression Best Practices:**
- Always include detailed rationale
- Provide evidence for false positive claim
- Set expiration dates for time-limited suppressions
- Review suppressions quarterly
- Never suppress without documentation

#### Integration with Other Skills

- **QA Tester**: Run regression tests after dependency updates
- **Release Manager**: Pre-release security verification gate
- **AOT Guru**: Verify dependency updates don't break AOT compatibility

---

### 6. Elm-to-F# Guru

**Full Documentation**: [.claude/skills/elm-to-fsharp-guru/SKILL.md](../.claude/skills/elm-to-fsharp-guru/SKILL.md)

#### Scope and Purpose
Expert in converting Elm code from finos/morphir-elm to idiomatic F# for morphir-dotnet. Combines deep expertise in both Elm and F# ecosystems with specific understanding of Morphir IR concepts, functional domain modeling patterns, .NET integration requirements, and compile-time code generation strategies.

#### Core Philosophy
**Logical Compatibility Over Literal Translation**: Prioritize idiomatic F# patterns and .NET ecosystem integration over literal Elm-to-F# mapping. Focus on behavioral equivalence verified through testing, not syntactic similarity.

**Compile-Time Code Generation First**: Reflection is a last resort. Always explore Myriad plugins and build-time code generation before accepting runtime reflection. This ensures AOT compatibility and optimal performance.

#### Core Competencies

1. **Language Translation**
   - Convert Elm syntax, types, and patterns to idiomatic F#
   - Map Elm's type system to F# while preserving safety
   - Handle Elm constraints (no typeclasses, Dict limitations)
   - Apply F# idioms and conventions

2. **Type System Mapping**
   - Custom types → Discriminated unions
   - Type aliases → Records or type abbreviations
   - Opaque types → Phantom types with smart constructors
   - Maybe/Result → Option/Result
   - Extensible records → Interfaces or explicit fields

3. **Compile-Time Code Generation**
   - Use Myriad for F# code generation (AOT-compatible)
   - Use C# source generators for interop scenarios
   - Identify code generation opportunities (5+ repetitive types)
   - Create custom Myriad plugins when needed
   - Integrate with MSBuild pipeline

4. **JSON Serialization Migration**
   - System.Text.Json source generators (C# interop)
   - Myriad-generated codecs (pure F#, AOT-safe)
   - Manual codecs (simple types, full control)
   - Decision matrix based on scenario

5. **Test Migration**
   - Extract test cases from Elm doc comments
   - Generate BDD scenarios (Reqnroll)
   - Generate unit tests (TUnit)
   - Generate property tests (FsCheck)
   - Create compatibility tests (Elm vs F# output)

6. **UI Architecture Translation**
   - Elm Architecture → Fun.Blazor
   - Model-Msg-Update-View pattern
   - MudBlazor component integration
   - State management patterns
   - Blazor Server vs WASM decisions

7. **Behavioral Verification**
   - Ensure Elm-F# behavioral equivalence
   - JSON roundtrip testing
   - Cross-implementation output comparison
   - Document intentional divergences

8. **Pattern Catalog Maintenance**
   - Build and maintain translation pattern library
   - Document new patterns as discovered
   - Identify automation opportunities
   - Share learnings across migrations

#### Review Capability

**Migration Quality Review**
- Check for literal translation anti-patterns
- Verify F# idiom compliance
- Detect reflection usage (AOT incompatibility)
- Identify Myriad plugin opportunities
- Assess test coverage completeness

**Review Triggers:**
- After each module migration completes
- When pattern appears 3+ times (automation candidate)
- Manual request for migration review

**Review Output:**
- Migration quality assessment
- F# idiom compliance report
- AOT compatibility check
- Pattern catalog updates needed
- Myriad plugin recommendations
- Test coverage verification

#### Automation Scripts

Location: `.claude/skills/elm-to-fsharp-guru/scripts/`

**analyze-elm-module.fsx**
- Analyze Elm module structure, dependencies, types, functions
- Identify code generation opportunities
- Report complexity metrics
- **Token Savings**: ~800 tokens

**extract-elm-tests.fsx**
- Extract test cases from Elm doc comments
- Generate BDD scenarios (Reqnroll .feature files)
- **Token Savings**: ~600 tokens

**verify-compatibility.fsx**
- Compare Elm and F# JSON outputs
- Verify behavioral equivalence
- Report differences
- **Token Savings**: ~700 tokens

**migration-metrics.fsx**
- Track migration progress (modules completed vs pending)
- Report test coverage per module
- Calculate feature parity percentage
- **Token Savings**: ~400 tokens

**generate-myriad-plugin.fsx**
- Scaffold custom Myriad plugin projects
- Generate template implementation
- Set up MSBuild integration
- **Token Savings**: ~900 tokens

**codegen-helpers.fsx**
- Build-time code generation utilities
- Commands: json-codec, visitor, lenses
- **Token Savings**: ~500 tokens

#### Manual Workflow for Non-Claude Agents

**To analyze an Elm module:**
```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/analyze-elm-module.fsx \
    path/to/morphir-elm/src/Morphir/IR/Type.elm
```

**To extract tests from Elm docs:**
```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/extract-elm-tests.fsx \
    path/to/elm/module.elm \
    tests/Morphir.Core.Tests/ModuleTests.feature
```

**To verify Elm-F# compatibility:**
```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/verify-compatibility.fsx \
    tests/fixtures/
```

**To scaffold a Myriad plugin:**
```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/generate-myriad-plugin.fsx \
    MorphirJsonCodec
```

#### Decision Trees

**When to Use Myriad vs Manual:**
```
Is the pattern repetitive (3+ types)?
├─ YES → Consider code generation
│   ├─ Existing Myriad plugin available?
│   │   ├─ YES → Use existing plugin
│   │   └─ NO → Worth writing custom plugin (5+ types)?
│   │       ├─ YES → Write custom Myriad plugin
│   │       └─ NO → Use build script or manual
│   └─ For C# interop?
│       ├─ YES → Use C# source generators
│       └─ NO → Myriad is appropriate
└─ NO → Write manually
```

**Which JSON Serialization Approach:**
```
What's the primary use case?
├─ C# Interop Heavy → System.Text.Json + Source Generators
├─ Pure F# Library
│   ├─ Complex types (10+ fields) → Myriad-Generated Codecs
│   └─ Simple types (< 5 fields) → Manual Implementation
└─ Prototyping → Manual Implementation
```

**UI Migration Path:**
```
Elm UI Component
├─ Server-side rendering? → Blazor Server + Fun.Blazor
├─ Rich client app? → Blazor WASM + Fun.Blazor
├─ Desktop app? → Avalonia.FuncUI
├─ Complex state? → Use Elmish (TEA for .NET)
└─ Material Design? → Add MudBlazor components
```

#### Pattern Catalog

Location: `.claude/skills/elm-to-fsharp-guru/patterns/`

**Core Patterns:**
1. **custom-types.md** - Elm custom types → F# discriminated unions
2. **encoders-decoders.md** - JSON serialization approaches
3. **opaque-types.md** - Smart constructors and phantom types
4. **maybe-result.md** - Option/Result equivalence
5. **dict-limitations.md** - Working around Elm Dict restrictions
6. **myriad-basics.md** - Using Myriad for code generation
7. **custom-myriad-plugins.md** - Writing custom Myriad plugins
8. **fun-blazor-basics.md** - Elm Architecture to Fun.Blazor

#### Migration Workflow

**Phase 1: Analysis & Planning**
1. Identify Elm module to migrate
2. Analyze dependencies
3. Extract test cases with automation
4. Identify code generation opportunities
5. Create migration task from template
6. Estimate effort

**Phase 2: Implementation**
1. Set up code generation (Myriad/build script)
2. Create F# types (following patterns)
3. Implement functions (F# idioms)
4. Generate or create JSON serialization
5. Write tests (TDD: unit, BDD, property)

**Phase 3: Verification**
1. Verify no reflection warnings
2. Test with PublishTrimmed=true
3. Run compatibility tests
4. Verify JSON roundtrip
5. Compare with Elm output
6. Document divergences
7. Get reviews (AOT Guru, QA Tester)

**Phase 4: Documentation**
1. Update migration tracking
2. Add new patterns to catalog
3. Document code generation approach
4. Update compatibility matrix
5. Document learnings

#### Integration with Other Skills

- **AOT Guru**: Review generated F# code for AOT safety and reflection issues
- **QA Tester**: Verify test coverage and BDD scenario quality
- **Release Manager**: Track feature parity milestones for releases
- **Technical Writer**: Document new patterns and migration guides

#### Getting Started

**Your First Migration:**
1. Choose a simple Elm module (< 100 lines, few dependencies)
2. Analyze with `analyze-elm-module.fsx`
3. Extract tests with `extract-elm-tests.fsx`
4. Create migration task from template
5. Translate types using pattern catalog
6. Implement functions with F# idioms
7. Generate codecs (Myriad or manual)
8. Write tests (TDD)
9. Verify compatibility with `verify-compatibility.fsx`
10. Get reviews from AOT Guru and QA Tester
11. Document learnings

**Success Criteria:**
- ✅ Types encode same invariants as Elm
- ✅ Functions are behaviorally equivalent
- ✅ JSON roundtrip tests pass
- ✅ No reflection warnings
- ✅ Test coverage >= 80%
- ✅ BDD scenarios cover user flows
- ✅ Code is idiomatic F#
- ✅ Patterns documented
- ✅ AOT Guru review passed
- ✅ QA Tester coverage verified

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
   - [Technical Writer](../.claude/skills/technical-writer/SKILL.md)
   - [Vulnerability Resolver](../.claude/skills/vulnerability-resolver/SKILL.md)

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
