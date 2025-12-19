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
- Restart your Claude Code session
- Skills are loaded when Claude Code starts
- Changes to skills may require a restart to take effect

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

1. **Restart Claude Code session**
   - Skills are loaded at startup
   - Changes require restart

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
- [ ] Claude Code session restarted after changes

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

# 3. Restart Claude Code

# 4. Try invoking
# In Claude Code, type: @skill test-skill
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
   - **Restart required**: Changes need session restart

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
