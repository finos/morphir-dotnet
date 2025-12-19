# Scripts

This directory contains utility scripts for the morphir-dotnet project.

## Available Scripts

### F# Scripts

#### `remove-claude-coauthor.fsx`

Removes "Co-Authored-By: Claude <noreply@anthropic.com>" from commit messages to ensure CLA (Contributor License Agreement) compliance.

**Usage:**
```bash
# Dry run - show what would be changed (default)
dotnet fsi scripts/remove-claude-coauthor.fsx

# Check last 5 commits
dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5

# Check specific branch
dotnet fsi scripts/remove-claude-coauthor.fsx --branch fix/my-branch

# Check all commits since divergence from main
dotnet fsi scripts/remove-claude-coauthor.fsx --since-main

# Apply changes (creates backup branch automatically)
dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5 --yes

# Verbose output
dotnet fsi scripts/remove-claude-coauthor.fsx --commits 5 --verbose
```

**Features:**
- ✅ Detects commits with Claude co-author violations
- ✅ Dry-run mode by default (safe preview)
- ✅ Creates automatic backup branch before rewriting
- ✅ Preserves all other commit metadata and co-authors
- ✅ Warns about pushed commits requiring force-push
- ✅ Requires user confirmation before applying changes

**Safety:**
- Requires clean working directory
- Creates backup: `backup/pre-coauthor-fix-{timestamp}`
- Confirmation prompt before applying (unless `--yes` flag)
- Clear guidance for force-push if needed

See: [AGENTS.md - Commit Messages](../AGENTS.md#11-review-and-contribution-rules), [CLAUDE.md - Commit Standards](../CLAUDE.md)

#### `validate-changelog.fsx`

Validates CHANGELOG.md against KeepAChangelog format requirements.

**Usage:**
```bash
dotnet fsi scripts/validate-changelog.fsx
dotnet fsi scripts/validate-changelog.fsx --file CHANGELOG.md
dotnet fsi scripts/validate-changelog.fsx --verbose
```

### C# Scripts

#### `generate-wolverine-code.cs`

Generates Wolverine framework code.

#### `publish-single-file.cs` / `publish-single-file-untrimmed.cs`

Publishes single-file executables (trimmed/untrimmed variants).

#### `run-e2e-tests.cs`

Runs end-to-end tests.

#### `run-tests.cs`

Runs unit tests.

### Shell Scripts

#### `install-linux.sh` / `install-macos.sh` / `install-windows.ps1`

Installation scripts for different platforms.

## Running Scripts

### F# Scripts
```bash
dotnet fsi scripts/{script-name}.fsx [options]
```

### C# Scripts  
```bash
dotnet run --project scripts/{script-name}.cs
```

### Shell Scripts
```bash
# Linux/macOS
./scripts/{script-name}.sh

# Windows (PowerShell)
.\scripts\{script-name}.ps1
```

## Contributing

When adding new scripts:
1. Follow existing patterns (see `validate-changelog.fsx` for F# scripts)
2. Include comprehensive header documentation with usage examples
3. Add entry to this README.md
4. Use Argu for CLI parsing (F# scripts)
5. Use Spectre.Console for formatted output (F# scripts)
6. Ensure cross-platform compatibility
