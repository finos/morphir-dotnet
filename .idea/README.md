# JetBrains IDE Configuration for morphir-dotnet

This directory contains JetBrains IDE-specific configuration files.

## Files

### ai-assistant-rules.md

JetBrains AI Assistant rules file that:
- Points to [AGENTS.md](../AGENTS.md) as authoritative guidance
- Links to [.agents/](../.agents/) for specialized topics
- Provides quick reference for TDD, coding standards, build commands
- Includes JetBrains-specific usage tips

## JetBrains AI Assistant Setup

### 1. Install AI Assistant

If not already installed:
1. Open Settings → Plugins
2. Search for "AI Assistant"
3. Install and restart IDE

### 2. Configure Rules

1. Go to Settings → AI Assistant → Rules
2. Click "+" to add new rule file
3. Select `.idea/ai-assistant-rules.md`
4. Set rule type to **"Always"** (recommended)
   - This ensures AI Assistant always considers morphir-dotnet conventions
5. Click OK to save

### 3. Alternative Rule Types

You can also configure as:
- **Manually**: Reference with @rule or #rule in chat
- **By model decision**: AI decides when relevant
- **By file patterns**: Applied for specific files (e.g., `src/**/*.cs`)

## Quick Start

When using JetBrains AI Assistant with morphir-dotnet:

1. **First Time**: Read [ai-assistant-rules.md](./ai-assistant-rules.md)
2. **Every Task**: Follow guidance in [AGENTS.md](../AGENTS.md)
3. **Testing**: Use [.agents/qa-testing.md](../.agents/qa-testing.md)
4. **Before Commit**: Run pre-commit checklist

## Using AI Assistant

### Chat with Context

In AI Assistant chat:
```
Using project rules, implement feature X following TDD
```

AI Assistant will automatically apply rules configured as "Always".

### Code Generation

When generating code:
1. AI Assistant considers:
   - Project rules (ai-assistant-rules.md)
   - ReSharper code analysis
   - .editorconfig settings
   - Language inspections
2. Generated code follows morphir-dotnet conventions
3. Includes tests (TDD approach)

### Custom Prompts

Create reusable prompts in Prompt Library:

**Create Test Plan**:
```
Create test plan following .agents/qa-testing.md template
for feature in [current selection/issue]
```

**Implement with TDD**:
```
Implement [feature] using TDD:
1. Write BDD scenario (Gherkin in tests/*/Features/)
2. Write unit test (TUnit, should fail)
3. Implement minimal code (make test pass)
4. Refactor while keeping tests green
Follow AGENTS.md Section 9.1
```

**Pre-Commit Check**:
```
Run quality checks:
./build.sh Format
./build.sh Lint
./build.sh Test
Verify coverage >= 80%
```

## Integration with Project Tools

### Build System
JetBrains IDEs recognize Nuke build targets:
- View targets: Build → Nuke → Show Targets
- Run target: Double-click target in Nuke tool window
- Or use terminal: `./build.sh {target}`

### Testing
- TUnit tests visible in test runner
- Reqnroll (BDD) features recognized in Rider
- Run tests: Ctrl+Shift+F10 (Windows/Linux), Ctrl+Shift+R (macOS)
- Debug tests: Shift+F9

### Code Analysis
- ReSharper inspections enabled
- .editorconfig settings applied
- Code style follows morphir-dotnet conventions

## Supported IDEs

This configuration works with:
- **IntelliJ IDEA** (with .NET plugin)
- **Rider** (.NET IDE)
- **WebStorm** (TypeScript support if needed)
- **Fleet** (JetBrains' new IDE)

All JetBrains IDEs with AI Assistant support can use these rules.

## Best Practices

### 1. Keep Rules Updated
- When AGENTS.md changes significantly, review this file
- Update JetBrains-specific tips as needed
- Keep quick reference commands current

### 2. Use Rules Consistently
- Configure as "Always" for consistent behavior
- Reference explicitly for critical tasks: "Using project rules, ..."
- Create custom prompts that reference rules

### 3. Leverage IDE Integration
- Use AI Assistant with code analysis (ReSharper/IntelliJ)
- Let AI fix inspections while following project rules
- Generate tests with TUnit/Reqnroll conventions

### 4. TDD Workflow
AI Assistant can help with TDD:
```
1. Ask: "Create BDD scenario for [feature]"
2. Ask: "Create unit test for [component]" (should fail)
3. Ask: "Implement [component] to pass test"
4. Ask: "Refactor [component] keeping tests green"
```

## Troubleshooting

### Rules Not Applied
1. Verify rule file added in Settings → AI Assistant → Rules
2. Check rule type (set to "Always" for automatic application)
3. Try explicit reference: "Using project rules, ..."

### AI Generates Non-Compliant Code
1. Check if rules are properly configured
2. Reference AGENTS.md explicitly: "Following AGENTS.md Section 5, ..."
3. Review generated code against conventions
4. Provide feedback to AI: "This doesn't follow immutability-first principle"

### Tests Not Generated
1. Ask explicitly: "Create tests using TDD approach"
2. Reference testing guide: "Following .agents/qa-testing.md, create tests"
3. Specify framework: "Create TUnit test for [component]"

## Migration from Other Tools

If migrating from:

### Cursor
JetBrains AI Assistant can read `.cursorrules` files. Your existing [.cursorrules](../.cursorrules) will work.

### GitHub Copilot
JetBrains AI Assistant can read `.github/copilot-instructions.md`. Your existing [copilot-instructions.md](../.github/copilot-instructions.md) will work.

### Claude Code
Your [CLAUDE.md](../CLAUDE.md) is compatible with JetBrains AI Assistant.

However, ai-assistant-rules.md is optimized for JetBrains and recommended.

## Resources

### Internal Documentation
- **AGENTS.md**: [../AGENTS.md](../AGENTS.md) - Primary guidance
- **.agents/qa-testing.md**: [../.agents/qa-testing.md](../.agents/qa-testing.md) - QA practices
- **Test Plan Example**: [../docs/content/contributing/qa/phase-1-test-plan.md](../docs/content/contributing/qa/phase-1-test-plan.md)

### JetBrains AI Assistant Documentation
- **Configure Rules**: https://www.jetbrains.com/help/ai-assistant/configure-project-rules.html
- **Settings Reference**: https://www.jetbrains.com/help/ai-assistant/settings-reference-rules.html
- **Getting Started**: https://www.jetbrains.com/help/ai-assistant/getting-started-with-ai-assistant.html
- **Prompt Library**: https://www.jetbrains.com/help/ai-assistant/prompt-library.html

### External Resources
- **AGENTS.md Standard**: https://agents.md
- **Morphir**: https://morphir.finos.org/
- **Reqnroll**: https://docs.reqnroll.net/
- **TUnit**: https://thomhurst.github.io/TUnit/
- **Nuke**: https://nuke.build/

## Contributing

When updating JetBrains configuration:

1. Test with Rider or IntelliJ IDEA
2. Ensure rules align with AGENTS.md
3. Update this README for structural changes
4. Keep rules file focused (detailed docs in AGENTS.md/.agents/)

---

**Last Updated**: 2025-12-18
**Maintained By**: morphir-dotnet contributors
