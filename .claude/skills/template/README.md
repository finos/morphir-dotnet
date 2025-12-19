# {Guru Name} - Quick Reference

> **Template Instructions**: Replace all `{placeholder}` text with actual content. Remove this blockquote when done.

## What This Guru Does

{One paragraph overview of what this guru does, its primary responsibilities, and value proposition.}

**Example:**
The {Guru Name} is a specialized {domain} expert that {primary capability}. It provides {capability 1}, {capability 2}, and {capability 3} to help teams {value delivered}. With {N} automation scripts and a growing pattern catalog, this guru saves approximately {N} tokens per {unit} by automating {high-cost tasks}.

## When to Use This Guru

Use the {Guru Name} when you need to:

- **{Use Case 1}** - {Brief description}
- **{Use Case 2}** - {Brief description}
- **{Use Case 3}** - {Brief description}
- **{Use Case 4}** - {Brief description}
- **{Use Case 5}** - {Brief description}

**Trigger Keywords:** {keyword1}, {keyword2}, {keyword3}, {keyword4}

## Core Competencies

### Primary
1. **{Competency 1}** - {Brief description}
2. **{Competency 2}** - {Brief description}
3. **{Competency 3}** - {Brief description}

### Secondary
1. **{Competency 4}** - {Brief description}
2. **{Competency 5}** - {Brief description}

## Available Scripts

| Script | Purpose | Token Savings | Status |
|--------|---------|---------------|--------|
| `{script-1}.fsx` | {Purpose} | ~{N} tokens | {Status} |
| `{script-2}.fsx` | {Purpose} | ~{N} tokens | {Status} |
| `{script-3}.fsx` | {Purpose} | ~{N} tokens | {Status} |

**Total Token Savings:** ~{N} tokens per {unit}

### Quick Start with Scripts

```bash
# {Most common script usage}
dotnet fsi .claude/skills/{guru-id}/scripts/{script-1}.fsx

# {Second most common script usage}
dotnet fsi .claude/skills/{guru-id}/scripts/{script-2}.fsx [args]

# {Third most common script usage}
dotnet fsi .claude/skills/{guru-id}/scripts/{script-3}.fsx [args]
```

## Common Tasks

### Task 1: {Task Name}

**Quick How-To:**
1. {Step 1}
2. {Step 2}
3. {Step 3}

**Using Automation:**
```bash
dotnet fsi .claude/skills/{guru-id}/scripts/{relevant-script}.fsx
```

---

### Task 2: {Task Name}

**Quick How-To:**
1. {Step 1}
2. {Step 2}
3. {Step 3}

**Decision Tree:** See [SKILL.md - Decision Tree {N}](./SKILL.md#{anchor})

---

### Task 3: {Task Name}

**Quick How-To:**
1. {Step 1}
2. {Step 2}
3. {Step 3}

**Playbook:** See [SKILL.md - Playbook {N}](./SKILL.md#{anchor})

---

### Task 4: {Task Name}

**Quick How-To:**
1. {Step 1}
2. {Step 2}
3. {Step 3}

**Template:** Use `.claude/skills/{guru-id}/templates/{template-name}.md`

## Pattern Catalog

### Quick Pattern Index

| Pattern | Category | Complexity | Frequency |
|---------|----------|------------|-----------|
| [{Pattern 1}](./patterns/{pattern-1}.md) | {Category} | {Low/Med/High} | {Common/Occasional/Rare} |
| [{Pattern 2}](./patterns/{pattern-2}.md) | {Category} | {Low/Med/High} | {Common/Occasional/Rare} |
| [{Pattern 3}](./patterns/{pattern-3}.md) | {Category} | {Low/Med/High} | {Common/Occasional/Rare} |
| [{Pattern 4}](./patterns/{pattern-4}.md) | {Category} | {Low/Med/High} | {Common/Occasional/Rare} |
| [{Pattern 5}](./patterns/{pattern-5}.md) | {Category} | {Low/Med/High} | {Common/Occasional/Rare} |

**Total Patterns:** {N} (growing quarterly)

### Most Useful Patterns

**{Pattern Name 1}:**
```{language}
{Brief code snippet showing pattern}
```
Use when: {Scenario}

**{Pattern Name 2}:**
```{language}
{Brief code snippet showing pattern}
```
Use when: {Scenario}

**{Pattern Name 3}:**
```{language}
{Brief code snippet showing pattern}
```
Use when: {Scenario}

## Review Capability

### What This Guru Reviews

- **{Review Scope 1}** - {What it looks for}
- **{Review Scope 2}** - {What it looks for}
- **{Review Scope 3}** - {What it looks for}
- **{Review Scope 4}** - {What it looks for}

### Review Schedule

| Review Type | Frequency | Trigger | Output |
|-------------|-----------|---------|--------|
| {Type 1} | {Frequency} | {Trigger} | {Output} |
| {Type 2} | {Frequency} | {Trigger} | {Output} |
| {Type 3} | {Frequency} | {Trigger} | {Output} |

### Running a Manual Review

```bash
# Run full domain review
dotnet fsi .claude/skills/{guru-id}/scripts/{review-script}.fsx

# Review specific area
dotnet fsi .claude/skills/{guru-id}/scripts/{review-script}.fsx --scope={area}
```

**Output:** Markdown report with findings, trends, and recommendations

## Examples

### Example 1: {Scenario Name}

**Context:** {What was the situation}

**Task:** {What needed to be done}

**Solution:**
```{language}
{Code showing solution}
```

**Result:** {Outcome}

---

### Example 2: {Scenario Name}

**Context:** {What was the situation}

**Task:** {What needed to be done}

**Solution:**
```bash
# Used automation script
dotnet fsi .claude/skills/{guru-id}/scripts/{script}.fsx {args}
```

**Result:** {Outcome}

---

### Example 3: {Scenario Name}

**Context:** {What was the situation}

**Task:** {What needed to be done}

**Approach:** Followed [Playbook {N}](./SKILL.md#{anchor})

**Result:** {Outcome}

## Integration

### With Other Gurus

**Coordinates With:**
- **{Other Guru 1}** - {What gets passed back and forth}
- **{Other Guru 2}** - {What gets passed back and forth}
- **{Other Guru 3}** - {What gets passed back and forth}

### Example Workflow

```
{Guru Name} {action}
    ↓
Passes to {Other Guru 1} for {purpose}
    ↓
{Other Guru 1} returns {result}
    ↓
{Guru Name} {next action}
    ↓
Passes to {Other Guru 2} for {purpose}
    ↓
Complete
```

## Usage by Agent

### Claude Code
```
@skill {guru-id}
{Your request}
```

### GitHub Copilot
1. Read `.agents/{guru-id}.md` for guidance
2. Run scripts: `dotnet fsi .claude/skills/{guru-id}/scripts/{script}.fsx`
3. Follow playbooks in `SKILL.md`

### Other Agents (Cursor, Windsurf, Aider)
1. Read this README for quick reference
2. Read `SKILL.md` for comprehensive guidance
3. Run scripts directly
4. Use templates from `templates/`

## Quick Decision Trees

### "{When to make common decision}"

```
{Question}?
  ├─ {Option A} → {Action A}
  ├─ {Option B} → {Action B}
  └─ {Option C} → {Action C}
```

See [SKILL.md](./SKILL.md) for complete decision trees.

## Getting Help

### Built-in Resources
- **[SKILL.md](./SKILL.md)** - Comprehensive documentation
- **[MAINTENANCE.md](./MAINTENANCE.md)** - Evolution and improvement guide
- **[metadata.yaml](./metadata.yaml)** - Skill configuration
- **[scripts/](./scripts/)** - Automation scripts
- **[templates/](./templates/)** - Reusable templates
- **[patterns/](./patterns/)** - Detailed patterns

### Project Resources
- **[AGENTS.md](../../../AGENTS.md)** - Primary agent guidance
- **[Guru Philosophy](../../../.agents/guru-philosophy.md)** - Core principles
- **[Skills Reference](../../../.agents/skills-reference.md)** - All available gurus
- **[Skill Matrix](../../../.agents/skill-matrix.md)** - Maturity tracking

### External Resources
- {External resource 1}
- {External resource 2}
- {External resource 3}

## Feedback and Evolution

This guru improves quarterly through:
- Pattern discovery and cataloging
- Automation opportunity identification
- Playbook refinement
- Review capability enhancement

**Current Status:** {alpha/beta/stable}  
**Last Review:** {YYYY-MM-DD}  
**Next Review:** {YYYY-MM-DD}  
**Pattern Count:** {N} (target: 20+ for stable)  
**Token Savings:** ~{N} per {unit}

## Contributing

Found a new pattern? Discovered an issue? Have a suggestion?

1. Document the pattern/issue/suggestion
2. Submit via GitHub issue with label `guru-{guru-id}`
3. Tag: `@{maintainer}`
4. Pattern will be reviewed in quarterly update

## Quick Reference Card

```
{Guru Name} ({guru-id})
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Domain: {Domain}
Status: {Phase}
Scripts: {N}
Patterns: {N}
Token Savings: ~{N}/unit

Quick Start:
  dotnet fsi .claude/skills/{guru-id}/scripts/{main-script}.fsx

Top 3 Use Cases:
  1. {Use case 1}
  2. {Use case 2}
  3. {Use case 3}

Coordinates With:
  → {Guru 1}, {Guru 2}, {Guru 3}

Maintainer: {Maintainer}
Last Updated: {YYYY-MM-DD}
```

---

**Version:** {Semantic version}  
**Status:** {alpha/beta/stable}  
**Maintainer:** {Maintainer name/GitHub handle}  
**Last Updated:** {YYYY-MM-DD}
