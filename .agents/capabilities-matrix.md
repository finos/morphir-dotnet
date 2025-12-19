# Cross-Agent Capabilities Matrix

> **Purpose**: This document shows which features and capabilities are available to which AI coding agents when working on morphir-dotnet.

## Overview

The morphir-dotnet project supports multiple AI coding agents:
- **Claude Code** (Anthropic) - Full skill support with interactive tools
- **GitHub Copilot** (Microsoft) - Documentation-based guidance
- **Cursor** (Anysphere) - Documentation and script-based
- **Windsurf** (Codeium) - Documentation and script-based
- **Aider** (Paul Gauthier) - CLI-based with documentation

This matrix helps you understand what's available for your agent and how to access it.

## Feature Availability Matrix

### Core Features

| Feature | Claude Code | GitHub Copilot | Cursor | Windsurf | Aider | Notes |
|---------|-------------|----------------|--------|----------|-------|-------|
| **Primary Guidance** (AGENTS.md) | ✅ | ✅ | ✅ | ✅ | ✅ | All agents read AGENTS.md |
| **Specialized Guides** (.agents/*.md) | ✅ | ✅ | ✅ | ✅ | ✅ | Topic-specific documentation |
| **Agent-Specific Instructions** | ✅ CLAUDE.md | ✅ copilot-instructions.md | ✅ .cursorrules | ✅ .windsurf/rules.md | ✅ .aider.conf.yml | Platform-specific guidance |
| **Automation Scripts** (.fsx) | ✅ | ✅ | ✅ | ✅ | ✅ | All agents can run F# scripts |
| **Decision Trees** | ✅ | ✅ | ✅ | ✅ | ✅ | In documentation |
| **Playbooks** | ✅ | ✅ | ✅ | ✅ | ✅ | Step-by-step procedures |

### Guru Skills

| Skill | Claude Code | GitHub Copilot | Cursor | Windsurf | Aider | Access Method |
|-------|-------------|----------------|--------|----------|-------|---------------|
| **QA Tester** | ✅ `@skill qa-tester` | ✅ Via docs | ✅ Via docs | ✅ Via docs | ✅ Via docs | Claude: Interactive<br>Others: Documentation |
| **AOT Guru** | ✅ `@skill aot-guru` | ✅ Via docs | ✅ Via docs | ✅ Via docs | ✅ Via docs | Claude: Interactive<br>Others: Documentation |
| **Release Manager** | ✅ `@skill release-manager` | ✅ Via docs | ✅ Via docs | ✅ Via docs | ✅ Via docs | Claude: Interactive<br>Others: Documentation |

### Review Capabilities

| Review Type | Claude Code | GitHub Copilot | Cursor | Windsurf | Aider | How to Invoke |
|-------------|-------------|----------------|--------|----------|-------|---------------|
| **QA Coverage Scanning** | ✅ Auto | ✅ Manual | ✅ Manual | ✅ Manual | ✅ Manual | Claude: Skill triggers automatically<br>Others: Run smoke-test.fsx |
| **AOT Quarterly Review** | ✅ Scheduled | ✅ Manual | ✅ Manual | ✅ Manual | ✅ Manual | Claude: Quarterly prompt<br>Others: Run aot-diagnostics.fsx |
| **Release Process Checks** | ✅ Auto | ✅ Manual | ✅ Manual | ✅ Manual | ✅ Manual | Claude: Skill triggers on release<br>Others: Run prepare-release.fsx |
| **Code Review** (code_review tool) | ✅ | ✅ | ✅ | ✅ | ✅ | All: Use code_review tool |
| **Security Scanning** (codeql_checker) | ✅ | ✅ | ✅ | ✅ | ✅ | All: Use codeql_checker tool |

### Automation Scripts

| Script Category | Claude Code | GitHub Copilot | Cursor | Windsurf | Aider | Script Location |
|-----------------|-------------|----------------|--------|----------|-------|-----------------|
| **QA Scripts** | ✅ | ✅ | ✅ | ✅ | ✅ | `.claude/skills/qa-tester/scripts/` |
| **AOT Scripts** | ✅ | ✅ | ✅ | ✅ | ✅ | `.claude/skills/aot-guru/scripts/` |
| **Release Scripts** | ✅ | ✅ | ✅ | ✅ | ✅ | `.claude/skills/release-manager/scripts/` |
| **Build Scripts** | ✅ | ✅ | ✅ | ✅ | ✅ | `scripts/*.fsx` and `build.sh` |

### Documentation Access

| Documentation | Claude Code | GitHub Copilot | Cursor | Windsurf | Aider | Location |
|---------------|-------------|----------------|--------|----------|-------|----------|
| **Skills Reference** | ✅ | ✅ | ✅ | ✅ | ✅ | `.agents/skills-reference.md` |
| **Capabilities Matrix** | ✅ | ✅ | ✅ | ✅ | ✅ | `.agents/capabilities-matrix.md` (this file) |
| **QA Testing Guide** | ✅ | ✅ | ✅ | ✅ | ✅ | `.agents/qa-testing.md` |
| **AOT Optimization Guide** | ✅ | ✅ | ✅ | ✅ | ✅ | `.agents/aot-optimization.md` |
| **Skill Details** | ✅ | ✅ | ✅ | ✅ | ✅ | `.claude/skills/*/skill.md` |

## Agent-Specific Details

### Claude Code (Anthropic)

**Special Features:**
- Interactive skill invocation with `@skill {skill-name}`
- Skills can run automation scripts automatically
- Built-in review capabilities trigger automatically
- Guided assistance through complex workflows

**How to Use:**
```
@skill qa-tester
Please create a test plan for PR #123

@skill aot-guru  
Help me diagnose trimming warnings in my build

@skill release-manager
Prepare a release for version 1.0.0
```

**Review Triggers:**
- Automatic on certain actions (PR verification, release preparation)
- Manual invocation via skill
- Scheduled (quarterly for AOT review)

### GitHub Copilot (Microsoft)

**Special Features:**
- Uses `copilot-instructions.md` for project-specific guidance
- Deep integration with GitHub features
- Can reference issues and PRs directly

**How to Use:**
1. **Read documentation** first (AGENTS.md, .agents/*.md)
2. **Run scripts directly** when needed:
   ```bash
   dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx
   ```
3. **Follow playbooks** for complex workflows
4. **Reference skills-reference.md** to understand guru capabilities

**Review Triggers:**
- Manual: Run automation scripts explicitly
- Pre-commit: Run via Husky hooks if configured
- CI/CD: Automated via GitHub Actions

### Cursor (Anysphere)

**Special Features:**
- Uses `.cursorrules` for project-specific configuration
- Terminal integration for script execution
- Can read multiple context files simultaneously

**How to Use:**
1. **Cursor reads** `.cursorrules` (references AGENTS.md and .agents/)
2. **Run scripts** via integrated terminal:
   ```bash
   dotnet fsi .claude/skills/aot-guru/scripts/aot-diagnostics.fsx src/Morphir
   ```
3. **Reference documentation** as needed
4. **Follow decision trees** in skills-reference.md

**Review Triggers:**
- Manual: Execute automation scripts in terminal
- Via tasks: Configure tasks.json to run scripts

### Windsurf (Codeium)

**Special Features:**
- Uses `.windsurf/rules.md` for project configuration
- Can execute commands directly
- Multi-file context awareness

**How to Use:**
1. **Windsurf reads** `.windsurf/rules.md` (references AGENTS.md)
2. **Run scripts** via command execution:
   ```bash
   dotnet fsi .claude/skills/release-manager/scripts/monitor-pr.fsx --pr 123
   ```
3. **Access documentation** from .agents/ directory
4. **Use playbooks** for guided workflows

**Review Triggers:**
- Manual: Execute scripts on demand
- Automated: Set up via shell scripts or make targets

### Aider (Paul Gauthier)

**Special Features:**
- CLI-based workflow
- Uses `.aider.conf.yml` for configuration
- Git-aware with automatic commit messages
- Can read documentation files as context

**How to Use:**
1. **Aider reads** `.aider.conf.yml` and can be pointed to documentation
2. **Provide context** by including relevant docs:
   ```bash
   aider --read AGENTS.md --read .agents/skills-reference.md
   ```
3. **Run scripts** from aider session:
   ```bash
   /run dotnet fsi .claude/skills/qa-tester/scripts/regression-test.fsx
   ```
4. **Reference playbooks** by pasting relevant sections

**Review Triggers:**
- Manual: Explicitly run automation scripts
- Git hooks: Configure pre-commit hooks to run scripts

## Script Portability

All automation scripts are F# scripts (.fsx) that work identically across all agents.

### Running Scripts

**Basic syntax:**
```bash
dotnet fsi <script-path> [options]
```

**Examples:**
```bash
# QA smoke test
dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx

# AOT diagnostics
dotnet fsi .claude/skills/aot-guru/scripts/aot-diagnostics.fsx src/Morphir

# Release preparation
dotnet fsi .claude/skills/release-manager/scripts/prepare-release.fsx --version 1.0.0

# Monitor PR checks
dotnet fsi .claude/skills/release-manager/scripts/monitor-pr.fsx --pr 123
```

### Script Output Formats

Most scripts support multiple output formats:

**JSON** (machine-readable):
```bash
dotnet fsi script.fsx --json
```

**Text** (human-readable, default):
```bash
dotnet fsi script.fsx
```

**Markdown** (for documentation):
```bash
dotnet fsi script.fsx --markdown
```

### Script Dependencies

All scripts require:
- .NET 10 SDK (`dotnet --version`)
- F# support (included with SDK)
- Project dependencies restored (`dotnet restore`)

Some scripts have additional requirements:
- **GitHub CLI** (`gh`) for release scripts
- **git** for version control operations
- **Spectre.Console** for rich output (auto-restored)

## Review Capability Details

### For Claude Code Users

Reviews are **automatic** - skills trigger at appropriate times:
- **QA Coverage**: Triggers on PR verification workflow
- **AOT Review**: Quarterly scheduled prompt + before releases
- **Release Process**: Triggers during release preparation

**Manual invocation:**
```
@skill qa-tester
Review test coverage for recent changes

@skill aot-guru
Run quarterly AOT review

@skill release-manager
Verify release process compliance
```

### For Other Agent Users

Reviews are **manual** - run automation scripts explicitly:

**QA Coverage Review:**
```bash
# Smoke test with coverage analysis
dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx

# Full regression test
dotnet fsi .claude/skills/qa-tester/scripts/regression-test.fsx
```

**AOT Review:**
```bash
# Diagnose AOT issues
dotnet fsi .claude/skills/aot-guru/scripts/aot-diagnostics.fsx src/Morphir

# Analyze build for warnings
dotnet publish > build.log 2>&1
dotnet fsi .claude/skills/aot-guru/scripts/aot-analyzer.fsx build.log

# Run full AOT test matrix
dotnet fsi .claude/skills/aot-guru/scripts/aot-test-runner.fsx --runtime linux-x64
```

**Release Process Review:**
```bash
# Prepare release with validation
dotnet fsi .claude/skills/release-manager/scripts/prepare-release.fsx

# Monitor PR checks
dotnet fsi .claude/skills/release-manager/scripts/monitor-pr.fsx --pr 123

# Validate released packages
dotnet fsi .claude/skills/release-manager/scripts/validate-release.fsx --version 1.0.0
```

## Common Workflows by Agent

### Claude Code Workflow

```
1. User: "Create a test plan for PR #123"
2. Agent: @skill qa-tester
3. Skill: Reads PR, creates comprehensive test plan
4. Skill: Runs smoke-test.fsx if needed
5. Agent: Presents test plan to user
```

### GitHub Copilot Workflow

```
1. User: "Create a test plan for PR #123"
2. Agent: Reads copilot-instructions.md → references skills-reference.md
3. Agent: Reads PR #123 content
4. Agent: Follows QA Tester patterns from skills-reference.md
5. Agent: Suggests running smoke-test.fsx
6. User: Runs script manually
7. Agent: Interprets results and presents test plan
```

### Cursor/Windsurf Workflow

```
1. User: "Create a test plan for PR #123"
2. Agent: Reads .cursorrules/.windsurf/rules.md → references AGENTS.md
3. Agent: Reads skills-reference.md for QA Tester guidance
4. Agent: Reviews PR content
5. Agent: Follows QA test plan patterns
6. Agent: Executes smoke-test.fsx via terminal
7. Agent: Presents comprehensive test plan with script results
```

### Aider Workflow

```
1. User: Start aider with context
   aider --read AGENTS.md --read .agents/skills-reference.md
2. User: "Create a test plan for PR #123"
3. Aider: References skills-reference.md QA Tester section
4. Aider: Suggests test plan structure
5. User: /run dotnet fsi .claude/skills/qa-tester/scripts/smoke-test.fsx
6. Aider: Incorporates script output into test plan
7. Aider: Presents complete test plan
```

## Token Usage Comparison

**Example: Creating a comprehensive test plan**

| Agent | Approach | Approximate Tokens |
|-------|----------|-------------------|
| Claude Code | `@skill qa-tester` (skill runs script) | ~800 tokens |
| GitHub Copilot | Read docs + suggest script + interpret results | ~1500 tokens |
| Cursor | Read rules + docs + run script + format | ~1400 tokens |
| Windsurf | Similar to Cursor | ~1400 tokens |
| Aider | Read context + run script + format | ~1300 tokens |

**Token savings with automation scripts:**
- Claude Code: ~500 token savings (skill integration)
- Others: ~300-500 token savings (avoid manual command construction)

## Migration Guide

### Moving from Claude-Only to Multi-Agent

If your project previously only supported Claude Code:

1. **Add cross-references** in existing documentation:
   - copilot-instructions.md → link to skills-reference.md
   - .cursorrules → reference AGENTS.md and .agents/
   - Create .windsurf/rules.md if needed
   - Create .aider.conf.yml if needed

2. **Document manual script execution** in playbooks:
   - Show how to run scripts without skill invocation
   - Include expected output formats
   - Document common parameters

3. **Extract decision trees** from skills into documentation:
   - Make decision logic accessible without skill interaction
   - Include in skills-reference.md

4. **Test with multiple agents**:
   - Verify documentation is clear for non-Claude users
   - Ensure scripts work independently
   - Validate playbooks are self-contained

### Supporting a New Agent

To add support for a new agent:

1. **Create agent-specific config** if needed (e.g., `.newagentrules`)
2. **Reference core documentation** (AGENTS.md, .agents/*.md)
3. **Test script execution** from agent's environment
4. **Document agent-specific features** in this capabilities matrix
5. **Add to workflow examples** above
6. **Update skills-reference.md** if agent has unique capabilities

## Troubleshooting

### Scripts Don't Run

**Problem**: `dotnet fsi` command not found

**Solution**: 
- Install .NET 10 SDK: https://dot.net/download
- Verify: `dotnet --version`

**Problem**: Script fails with missing dependencies

**Solution**:
```bash
cd /home/runner/work/morphir-dotnet/morphir-dotnet
dotnet restore
```

### Documentation Not Loading

**Problem**: Agent can't find documentation files

**Solution**:
- Ensure agent is in repository root
- Check file paths are correct (case-sensitive on Linux/macOS)
- For Aider: Explicitly provide files with `--read`

### Review Capabilities Not Working

**Problem**: Reviews not triggering automatically

**Solution**:
- **Claude Code**: Invoke skill explicitly if not auto-triggering
- **Other agents**: Reviews are manual - run scripts explicitly
- Check script permissions: `chmod +x .claude/skills/*/scripts/*.fsx`

### Token Usage Too High

**Problem**: Agent uses too many tokens for simple tasks

**Solution**:
1. **Use automation scripts** instead of manual commands
2. **Reference specific documentation sections** instead of entire files
3. **Use decision trees** to quickly navigate to right approach
4. **For Claude**: Use skills (`@skill`) instead of manual guidance

## Best Practices

### For All Agents

1. **Read AGENTS.md first** - Primary guidance document
2. **Use automation scripts** - Save tokens and ensure consistency
3. **Follow playbooks** - Proven workflows for common tasks
4. **Reference decision trees** - Quick guidance when uncertain
5. **Run code_review and codeql_checker** - Before finalizing changes

### For Claude Code

1. **Use skills** - More efficient than manual guidance
2. **Let skills run scripts** - Automatic execution and interpretation
3. **Trust skill recommendations** - Based on project best practices

### For Other Agents

1. **Keep skills-reference.md handy** - Quick access to guru capabilities
2. **Run scripts proactively** - Better than describing what to do
3. **Follow playbooks closely** - Compensates for lack of interactive skills
4. **Use decision trees** - Navigate complex scenarios confidently

## Future Enhancements

Planned improvements to cross-agent support:

- [ ] **Skill API** - REST/gRPC API for skills, accessible to all agents
- [ ] **GitHub Actions for Reviews** - Automated review comments on PRs
- [ ] **Agent Performance Metrics** - Track token usage and quality by agent
- [ ] **Interactive Playbooks** - Web-based guided workflows
- [ ] **Skill Marketplace** - Share and discover community skills

## Feedback and Contributions

### Report Issues

If a capability doesn't work as expected:
1. Open issue with label `agent-guidance`
2. Specify your agent (Claude, Copilot, Cursor, etc.)
3. Describe what you tried and what happened
4. Include relevant error messages or logs

### Suggest Improvements

Ideas for better cross-agent support?
1. Open issue with label `enhancement` and `agent-guidance`
2. Describe the problem you're trying to solve
3. Suggest specific improvements
4. Indicate which agents would benefit

### Contribute Scripts

Want to add new automation scripts?
1. See [skills-reference.md](./skills-reference.md) for guidelines
2. Ensure script works for all agents (test with multiple)
3. Document parameters and output formats
4. Update capabilities-matrix.md
5. Submit PR with label `automation`

## References

- **[AGENTS.md](../AGENTS.md)**: Primary agent guidance
- **[Skills Reference](./skills-reference.md)**: Detailed skill documentation
- **[QA Testing Guide](./qa-testing.md)**: Cross-agent QA practices
- **[AOT Optimization Guide](./aot-optimization.md)**: Cross-agent AOT guidance
- **[Claude Skills](../.claude/skills/)**: Interactive skill implementations

---

**Remember**: The goal is to provide the best possible experience for developers regardless of which AI agent they use. Automation scripts and clear documentation are the great equalizers - they work the same for everyone.
