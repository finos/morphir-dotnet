# Skills Troubleshooting Guide

This guide helps diagnose and resolve common issues with Claude Code skills in the morphir-dotnet project.

## Common Issues

### 1. Skill Not Loading / "Unknown skill" Error

**Symptom**: When trying to invoke a skill with `@skill skill-name`, you get an error: `Unknown skill: skill-name`

**Root Causes**:

#### A. Incorrect Filename Casing
**Problem**: The skill definition file is named `skill.md` instead of `SKILL.md`

**Solution**:
```bash
# Check current filename
ls .claude/skills/your-skill-name/

# Should be SKILL.md (uppercase), not skill.md (lowercase)
# If wrong, rename:
git mv .claude/skills/your-skill-name/skill.md .claude/skills/your-skill-name/SKILL.md
```

**Why**: Claude Code requires the filename to be **exactly `SKILL.md`** with uppercase letters. The filename is **case-sensitive**.

**Reference**: [Claude Code Skills Documentation](https://code.claude.com/docs/en/skills.md)

#### B. Skill Not Committed to Repository
**Problem**: The skill exists locally but hasn't been committed to git

**Solution**:
```bash
# Check if skill is in git
git ls-files .claude/skills/your-skill-name/

# If empty, the skill isn't tracked
git add .claude/skills/your-skill-name/
git commit -m "feat: add your-skill-name skill"
```

#### C. Session Not Restarted
**Problem**: Skills may not be loaded in the current Claude Code session

**Solution**:
- **Restart your Claude Code session** (required for new skills)
- Skills are loaded **only when Claude Code starts**
- **New skills will NOT be discovered** until you restart
- Changes to existing skills may also require a restart to take effect

**Important**: Creating a new skill directory and SKILL.md file is not enough - you **must restart Claude Code** for it to be recognized.

### 2. Skill Not Triggering Automatically

**Symptom**: Skill exists but isn't activated when expected

**Root Causes**:

#### A. Weak Description Triggers
**Problem**: The `description` field in YAML frontmatter doesn't include clear trigger words

**Solution**:
Update your SKILL.md frontmatter with explicit triggers:

```yaml
---
name: your-skill-name
description: Specialized tool for X. Use when user asks to Y, Z. Triggers include "keyword1", "keyword2", "phrase to watch for".
---
```

**Good Example**:
```yaml
description: Specialized QA testing for morphir-dotnet. Use when user asks to create test plans, run tests, validate packages, report bugs, perform regression testing, or verify PR completion. Triggers include "test plan", "QA", "regression", "validate", "bug report", "test this", "verify implementation".
```

**Bad Example**:
```yaml
description: QA testing skill
```

#### B. Incorrect Skill Name Format
**Problem**: Skill name contains invalid characters

**Solution**:
Skill names must follow these rules:
- Lowercase letters only
- Numbers allowed
- Hyphens allowed (recommended for multi-word names)
- Max 64 characters
- No spaces, underscores, or special characters

```yaml
# Good
name: qa-tester
name: aot-guru
name: release-manager

# Bad
name: QA-Tester      # uppercase
name: qa_tester      # underscore
name: "QA Tester"    # spaces
```

### 3. Skill Invocation Syntax Issues

**Symptom**: Syntax error when trying to invoke skill

**Common Mistakes**:

```bash
# Wrong - using spaces in name
@skill QA Tester

# Wrong - using display name
@skill "QA Tester"

# Correct - using skill name from YAML
@skill qa-tester
```

**Solution**: Always use the exact `name` value from the skill's YAML frontmatter.

### 4. Skills Not Showing in Available Skills

**Symptom**: `<available_skills>` section is empty or missing your skill

**Diagnostic Steps**:

```bash
# 1. Check file exists with correct name
ls -la .claude/skills/*/SKILL.md

# 2. Verify YAML frontmatter is valid
head -n 5 .claude/skills/your-skill-name/SKILL.md

# Should show:
# ---
# name: your-skill-name
# description: ...
# ---

# 3. Check for YAML syntax errors
# Make sure no tabs, proper spacing, quotes balanced
```

**Common YAML Errors**:
- Missing closing `---`
- Tabs instead of spaces
- Unbalanced quotes in description
- Missing required fields (name, description)

### 5. Changes Not Taking Effect

**Symptom**: Updated skill but changes aren't reflected

**Solutions**:

1. **Restart Claude Code session (REQUIRED)**
   - Skills are loaded **only at startup**
   - **All changes require a restart** to take effect
   - This includes:
     - New skills added
     - Existing skill modifications
     - SKILL.md content updates
     - Skill name changes

2. **Check file is saved**
   ```bash
   git status
   git diff .claude/skills/your-skill-name/SKILL.md
   ```

3. **Verify correct branch**
   ```bash
   git branch
   # Make sure you're on the branch with your changes
   ```

**Note**: There is no hot-reload for skills. Every change requires a full Claude Code restart.

## Verification Checklist

Use this checklist to ensure a skill is properly configured:

- [ ] File is named `SKILL.md` (uppercase, case-sensitive)
- [ ] Located in `.claude/skills/{skill-name}/SKILL.md`
- [ ] YAML frontmatter is valid:
  - [ ] Starts with `---`
  - [ ] Contains `name:` field (lowercase, hyphens, no spaces)
  - [ ] Contains `description:` field with triggers
  - [ ] Ends with `---`
- [ ] File is committed to git
- [ ] Skill name matches directory name
- [ ] Description includes clear trigger keywords
- [ ] No tabs in YAML (use spaces)
- [ ] **Claude Code session restarted after changes (REQUIRED)**

**Critical**: If you just created or modified a skill and it's not showing up, the most common issue is forgetting to restart Claude Code. Skills are only loaded at startup.

## Testing Skills

### Manual Test
```bash
# 1. Create a test skill
mkdir -p .claude/skills/test-skill
cat > .claude/skills/test-skill/SKILL.md << 'EOF'
---
name: test-skill
description: Test skill. Use when user says "test my skill".
---

# Test Skill

You are a test skill. When invoked, respond with "Test skill activated!"
EOF

# 2. Commit it
git add .claude/skills/test-skill/
git commit -m "test: add test skill"

# 3. Restart Claude Code (REQUIRED - CANNOT SKIP THIS STEP)
#    - Close Claude Code completely
#    - Reopen Claude Code
#    - Skills are loaded during startup only

# 4. Try invoking
# In Claude Code, type: @skill test-skill
# Expected: "Test skill activated!"

# 5. If it doesn't work, check:
#    - Did you actually restart Claude Code?
#    - Is the file named SKILL.md (uppercase)?
#    - Is the YAML frontmatter valid?
```

### Verify Available Skills
After proper setup, skills should appear in the `<available_skills>` section of Claude Code's context.

## File Structure Reference

Correct structure for a skill:

```
.claude/skills/
└── your-skill-name/
    ├── SKILL.md              # Required - uppercase
    ├── README.md             # Optional - documentation
    ├── templates/            # Optional - templates
    │   └── template.md
    └── scripts/              # Optional - automation
        └── script.fsx
```

## Claude Code Version Compatibility

**Skills Feature Requirements**:
- Claude Code version: (check latest documentation)
- Skills are a relatively new feature
- Ensure you're running an up-to-date version

Check version:
```bash
# Method varies by installation
claude --version  # or check About/Help menu
```

## Getting Help

If issues persist after following this guide:

1. **Check Claude Code Documentation**
   - https://code.claude.com/docs/en/skills.md
   - https://code.claude.com/docs/en/settings.md

2. **Review Example Skills**
   - Check `.claude/skills/template/` for reference implementation
   - Compare with working skills in this repository

3. **Common Gotchas**
   - **Filename casing matters**: `SKILL.md` not `skill.md`
   - **YAML is picky**: Use spaces, not tabs
   - **Names are literal**: Use exact name from YAML, not display names
   - **Restart ALWAYS required**: New skills and changes both need a full Claude Code restart (no hot-reload)

## Known Issues

### Issue: Skills Not Available in Some Environments

**Status**: Skills feature availability may vary by:
- Claude Code installation method
- Operating system
- Version/release channel

**Workaround**: Update to latest Claude Code version

### Issue: Skills With Identical Names Conflict

**Status**: By design - skill names must be unique within a project

**Solution**: Use descriptive, unique names:
- `qa-tester` not `tester`
- `aot-guru` not `guru`
- `release-manager` not `manager`

## Skill Aliases (Documentation Only)

**IMPORTANT**: Skill aliases are **NOT a supported feature** of Claude Code. Our approach is a workaround for documentation purposes only.

### How We Document Aliases

Skills can document commonly used aliases in their YAML frontmatter:

```yaml
---
name: qa-tester
description: Specialized QA testing for morphir-dotnet...
# Aliases (NOT supported by Claude Code - documentation only)
# Common short forms: qa, test, tester
---
```

**What this means:**
- Aliases are **documentation only** - they help users understand alternative names
- Claude Code **does not recognize** these aliases
- The `@skill` command only works with the official `name` field
- Aliases serve as a reference for what users might expect to type

### Why Document Aliases?

1. **User expectations**: Users might naturally type `@skill qa` instead of `@skill qa-tester`
2. **Discoverability**: Shows what short forms are commonly understood
3. **Consistency**: Teams can agree on preferred short forms in documentation
4. **Future-proofing**: If Claude Code adds alias support, we're ready

### Limitations

- **Not functional**: Typing `@skill qa` will NOT work (only `@skill qa-tester` works)
- **Documentation only**: Aliases are purely informational
- **No validation**: Claude Code ignores the alias field completely
- **Workaround status**: This is our convention, not an official feature

### When to Document Aliases

**DO document aliases when:**
- The skill name is long and commonly abbreviated
- Users might naturally expect a shorter form
- Multiple common names exist for the same concept

**Example:**
```yaml
name: qa-tester
# Common short forms: qa, test, tester
```

**DON'T document aliases when:**
- The skill name is already short (e.g., `template`)
- No obvious abbreviations exist
- It would cause confusion

## Recent Fixes (History)

### 2025-12-19: Filename Casing Issue
**Problem**: All skills were named `skill.md` instead of `SKILL.md`
**Impact**: Skills not loading, "Unknown skill" errors
**Fix**: Renamed all skill definition files to uppercase `SKILL.md`
**Files Changed**:
- `.claude/skills/qa-tester/skill.md` → `SKILL.md`
- `.claude/skills/aot-guru/skill.md` → `SKILL.md`
- `.claude/skills/release-manager/skill.md` → `SKILL.md`
- `.claude/skills/template/skill.md` → `SKILL.md`
- Updated all documentation references

**Commit**: `fix/skills-filename-casing` branch

## Additional Resources

- [Creating Skills (Official Docs)](https://code.claude.com/docs/en/skills.md)
- [Project Settings](https://code.claude.com/docs/en/settings.md)
- [Slash Commands](https://code.claude.com/docs/en/slash-commands.md)
- [Skills Template](.claude/skills/template/README.md)

---

**Last Updated**: 2025-12-19
**Maintainers**: morphir-dotnet team
**Related**: CLAUDE.md, AGENTS.md, .claude/skills/template/
