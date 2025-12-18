# Windsurf Configuration for morphir-dotnet

This directory contains Windsurf-specific rules and configuration for the morphir-dotnet project.

## Files

### rules/morphir.md

Primary Windsurf rules file that:
- Points to [AGENTS.md](../AGENTS.md) as the authoritative guidance
- Links to [.agents/](../.agents/) for specialized topics
- Provides quick reference for common tasks
- Includes Windsurf-specific usage notes

## Usage

### Automatic Discovery

Windsurf will automatically discover and apply rules from this directory based on your configuration.

### Activation Modes

The morphir.md rules file supports multiple activation modes:

1. **Manual Invocation**: Reference explicitly in prompts
   ```
   "Using the morphir rules, implement feature X"
   ```

2. **Always-On**: Configure in Windsurf settings to apply to all prompts

3. **Model-Driven**: Let AI decide when rules are relevant based on context

4. **Glob Pattern**: Apply to specific subdirectories
   ```
   # In Windsurf settings
   rules:
     - file: .windsurf/rules/morphir.md
       pattern: "src/**/*.cs"
   ```

## Quick Start

When working with morphir-dotnet in Windsurf:

1. **First Time**: Read [rules/morphir.md](./rules/morphir.md)
2. **Every Task**: Follow the guidance in [AGENTS.md](../AGENTS.md)
3. **Testing**: Use [.agents/qa-testing.md](../.agents/qa-testing.md)
4. **Before Commit**: Run pre-commit checklist (see rules/morphir.md)

## Creating Windsurf Workflows

When creating reusable Windsurf workflows (saved as markdown prompts):

### Test Plan Creation Workflow
```markdown
# Create Test Plan

1. Read .agents/qa-testing.md for test plan template
2. Identify test cases from issue/PR description
3. Create test plan following template structure
4. Include BDD scenarios, test execution steps
5. Save to docs/content/contributing/qa/{feature}-test-plan.md
```

### Feature Implementation Workflow
```markdown
# Implement Feature with TDD

1. Read AGENTS.md Sections 5-6 for conventions
2. Write BDD scenario in tests/*/Features/*.feature
3. Write failing unit tests (RED)
4. Implement minimal code (GREEN)
5. Refactor while keeping tests green
6. Run: ./build.sh Format && ./build.sh Test
7. Verify coverage >= 80%
```

### Quality Check Workflow
```markdown
# Pre-Commit Quality Check

1. Format code: ./build.sh Format
2. Run linter: ./build.sh Lint
3. Run all tests: ./build.sh Test
4. Check coverage: dotnet test --collect:"XPlat Code Coverage"
5. Review changes: git diff
6. Verify no console logs in stdout (CLI tools)
```

## Integration with Project Structure

```
morphir-dotnet/
├── AGENTS.md                 # Primary guidance
├── .agents/                  # Specialized topics
│   └── qa-testing.md         # QA guidance
├── .windsurf/                # Windsurf config (YOU ARE HERE)
│   ├── README.md             # This file
│   └── rules/
│       └── morphir.md        # Windsurf rules
├── .cursorrules              # Cursor rules
├── .github/
│   └── copilot-instructions.md  # Copilot instructions
└── CLAUDE.md                 # Claude Code guidance
```

## Best Practices

### 1. Use Cascade Mode Effectively
- Read AGENTS.md before starting complex changes
- Break large tasks into smaller steps
- Run tests after each Cascade iteration
- Keep TDD cycle: RED → GREEN → REFACTOR

### 2. Leverage Rules File
- Reference rules explicitly for consistent behavior
- Create workflows that reference morphir.md
- Update rules as project conventions evolve

### 3. Test-Driven Development
- Always write tests first
- Use BDD scenarios for features
- Unit tests for components
- E2E tests for CLI functionality

### 4. Pre-Commit Checks
```bash
# Always run before committing
./build.sh Format
./build.sh Lint
./build.sh Test
```

### 5. Documentation
- Update AGENTS.md for universal changes
- Update .agents/qa-testing.md for testing changes
- Update this file for Windsurf-specific changes

## Troubleshooting

### Rules Not Being Applied
1. Check Windsurf settings for rules activation
2. Verify .windsurf/rules/morphir.md exists
3. Try explicit reference: "Using morphir rules, ..."

### Tests Failing
1. Read error messages carefully
2. Check .agents/qa-testing.md for testing guidance
3. Run specific test: `dotnet test --filter "TestName"`
4. Check test data in tests/*/TestData/

### Build Errors
1. Run `./build.sh Clean`
2. Run `./build.sh Restore`
3. Check for file locking issues (Windows)
4. See build targets: `./build.sh --help`

## Resources

- **AGENTS.md**: [../AGENTS.md](../AGENTS.md)
- **.agents/qa-testing.md**: [../.agents/qa-testing.md](../.agents/qa-testing.md)
- **Windsurf Docs**: Check Windsurf documentation for rules and workflows
- **AGENTS.md Standard**: https://agents.md

## Contributing

When updating Windsurf configuration:

1. Test changes with Windsurf
2. Ensure rules align with AGENTS.md
3. Update this README if structure changes
4. Keep rules file focused (point to detailed docs)

---

**Last Updated**: 2025-12-18
**Maintained By**: morphir-dotnet contributors
