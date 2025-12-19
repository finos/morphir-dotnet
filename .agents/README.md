# AI Coding Agent Resources

This directory contains guidance and resources for AI coding agents working on morphir-dotnet.

## Purpose

While [AGENTS.md](../AGENTS.md) in the project root provides comprehensive guidance for all agents, this directory contains:
- **Specialized topic guides** for specific domains (QA, deployment, etc.)
- **Cross-agent compatible resources** that work with any AI coding assistant
- **Reusable templates and scripts** for common tasks
- **Agent-agnostic documentation** that doesn't depend on specific tool features

## Structure

```
.agents/
├── README.md           # This file
├── qa-testing.md       # QA testing guidance (all agents)
└── [future topics]     # Additional domain-specific guides
```

## For AI Coding Agents

### Primary Guidance
Always start with [AGENTS.md](../AGENTS.md) - it's the authoritative source for:
- Project overview and architecture
- Coding conventions and standards
- Testing strategy (TDD)
- Build and deployment procedures
- Decision-making framework

### Specialized Guidance
This directory provides deep-dive guidance for specific areas:

| Topic | File | When to Use |
|-------|------|-------------|
| QA Testing | [qa-testing.md](qa-testing.md) | Creating test plans, running tests, reporting bugs |
| AOT Optimization | [aot-optimization.md](aot-optimization.md) | Native AOT compilation, trimming, size optimization |
| [Future] Deployment | deployment.md | Publishing packages, releases, CI/CD |
| [Future] Documentation | documentation.md | Writing docs, ADRs, PRDs |

### Claude Code Users
If you're using Claude Code, you also have access to:
- **Claude Skills**: [.claude/skills/](../.claude/skills/) - Specialized prompts and scripts
  - [qa-tester](../.claude/skills/qa-tester/) - QA testing skill with F# scripts
  - [aot-guru](../.claude/skills/aot-guru/) - AOT diagnostics and optimization skill
  - [release-manager](../.claude/skills/release-manager/) - Release management skill

See [CLAUDE.md](../CLAUDE.md) for Claude Code-specific guidance.

## For Human Contributors

This directory helps you understand:
- What guidance AI agents are following
- What templates and processes are standardized
- How to contribute agent-friendly documentation

### Adding New Agent Guidance

When adding new specialized guidance:

1. **Create focused topic file**: `{topic}.md` in this directory
2. **Use agent-agnostic language**: Avoid tool-specific features
3. **Provide templates**: Include reusable templates and checklists
4. **Link from AGENTS.md**: Add reference in main AGENTS.md
5. **Test with multiple agents**: Verify guidance works across tools

### Template for New Guidance Files

```markdown
# [Topic] Guidance for AI Coding Agents

**Audience**: All AI coding agents (Claude Code, GitHub Copilot, Cursor, etc.)
**Purpose**: [Brief description]

## Quick Reference

[Table of common tasks and actions]

## [Section 1]

[Detailed guidance]

## Templates

[Reusable templates]

## Scripts

[References to scripts]

## Best Practices

[Do's and don'ts]

## Agent-Specific Notes

[Any tool-specific variations]

## Resources

[Links to related docs]

## Updates

**Last Updated**: YYYY-MM-DD
**Maintained By**: [Maintainers]
```

## Relationship to Other Documentation

```
morphir-dotnet/
├── AGENTS.md              # Primary agent guidance (START HERE)
├── CLAUDE.md              # Claude Code-specific guidance
├── README.md              # Project README
├── .agents/               # Specialized agent guidance (THIS DIRECTORY)
│   ├── README.md          # This file
│   ├── qa-testing.md      # QA testing guide
│   └── aot-optimization.md # AOT and size optimization guide
├── .claude/               # Claude Code-specific resources
│   └── skills/            # Claude skills
│       ├── qa-tester/     # QA skill with F# scripts
│       ├── aot-guru/      # AOT optimization skill
│       └── release-manager/ # Release management skill
└── docs/                  # User-facing documentation
    ├── content/
    │   └── contributing/
    │       ├── aot-trimming-guide.md  # User-facing AOT guide
    │       └── fsharp-coding-guide.md # F# patterns including AOT
    └── spec/
```

**Navigation**:
- New to project? → Read `AGENTS.md`
- Need specialized guidance? → Check `.agents/{topic}.md`
- Using Claude Code? → See `CLAUDE.md` and `.claude/skills/`
- Writing docs? → See `docs/`

## Available Guidance

### QA Testing ([qa-testing.md](qa-testing.md))

Comprehensive QA testing guidance including:
- Pre-commit checklist
- PR verification checklist
- Test plan template
- Bug report template
- Regression testing playbook
- Package validation playbook
- Test scripts (F# and C#)
- Coverage requirements
- BDD testing with Reqnroll
- Unit testing with TUnit

**When to use**: Anytime you're creating test plans, running tests, or reporting issues.

### AOT Optimization ([aot-optimization.md](aot-optimization.md))

Native AOT, trimming, and size optimization guidance including:
- Decision trees for AOT compatibility
- Diagnostic procedures and automated testing
- Common patterns and workarounds
- Size optimization strategies
- Known issues database
- Continuous improvement processes
- BDD test scenarios for AOT

**When to use**: When working with Native AOT, troubleshooting compilation, optimizing binary size, or adding new features that must be AOT-compatible.

**Related Resources**:
- User-facing: [AOT/Trimming Guide](../docs/contributing/aot-trimming-guide.md)
- Skill: [AOT Guru](./.claude/skills/aot-guru/)
- F# patterns: [F# Coding Guide](../docs/contributing/fsharp-coding-guide.md)

### [Future] Deployment Guidance

Coming soon: Guidance for:
- Publishing packages to NuGet
- Creating GitHub releases
- Managing versions
- CI/CD workflows
- Deployment checklists

### [Future] Documentation Guidance

Coming soon: Guidance for:
- Writing documentation
- Creating ADRs (Architecture Decision Records)
- Writing PRDs (Product Requirements Documents)
- API documentation standards
- Examples and tutorials

## Contributing

To improve agent guidance:

1. **Identify gaps**: What tasks are agents struggling with?
2. **Create focused guides**: One topic per file
3. **Test with multiple agents**: Ensure cross-compatibility
4. **Provide examples**: Show, don't just tell
5. **Keep updated**: Revise as project evolves

## Feedback

Found issues with agent guidance?
- File an issue: https://github.com/finos/morphir-dotnet/issues
- Tag with: `agent-guidance`, `documentation`

## Version History

- **2025-12-19**: Added AOT Optimization guidance and AOT Guru skill
- **2025-12-18**: Initial creation with QA testing guidance
- [Future updates]

## See Also

- [AGENTS.md](../AGENTS.md) - Main agent guidance
- [CLAUDE.md](../CLAUDE.md) - Claude Code guidance
- [Contributing Guide](../docs/content/contributing/) - For all contributors
- [FINOS Code of Conduct](https://www.finos.org/code-of-conduct)
