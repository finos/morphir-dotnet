# AOT Guru Skill

Native AOT, trimming, and optimization expert for morphir-dotnet.

## Quick Start

This skill is automatically activated when you mention:
- "AOT" or "Native AOT"
- "trimming" or "PublishTrimmed"
- "size optimization"
- "IL2026", "IL3050" (AOT warnings)
- "reflection error"
- "source generator"

## What This Skill Does

The AOT Guru helps with:

1. **Diagnosing AOT/Trimming Issues** - Identify and resolve compilation errors
2. **Size Optimization** - Reduce binary size to target ranges
3. **Reflection Workarounds** - Replace reflection with AOT-compatible patterns
4. **Knowledge Base** - Maintain and evolve AOT best practices
5. **Testing Automation** - Create and run AOT test matrices
6. **Continuous Improvement** - Learn from issues and update documentation

## Common Use Cases

### "I'm getting IL2026 warnings"

**What it means**: Code is using reflection (not fully supported in AOT)

**AOT Guru will**:
1. Analyze the warning details
2. Identify the reflection usage
3. Suggest source generators or DynamicDependency attributes
4. Show code examples
5. Update documentation if it's a new pattern

### "My AOT binary is 25 MB, target is 8 MB"

**AOT Guru will**:
1. Analyze project dependencies
2. Check optimization flags
3. Identify large dependencies
4. Suggest replacements or optimizations
5. Provide step-by-step size reduction plan

### "How do I make System.Text.Json work with AOT?"

**AOT Guru will**:
1. Explain source-generated serialization contexts
2. Show code examples
3. Create JsonSerializerContext for your types
4. Test the changes
5. Update documentation

### "My AOT build succeeds but crashes at runtime"

**AOT Guru will**:
1. Diagnose likely trimming issue
2. Check for MissingMethodException or TypeLoadException
3. Add DynamicDependency attributes
4. Test with PublishTrimmed first to isolate
5. Document the issue for future reference

## Tools Provided

### Diagnostic Scripts (.fsx)

Located in `.claude/skills/aot-guru/`:

1. **aot-diagnostics.fsx** - Comprehensive project analysis
   ```bash
   dotnet fsi aot-diagnostics.fsx <project-path>
   ```
   - Checks PublishAot configuration
   - Identifies reflection usage
   - Analyzes dependencies
   - Reports AOT compatibility issues

2. **aot-analyzer.fsx** - Build output analysis
   ```bash
   dotnet fsi aot-analyzer.fsx <build-log>
   ```
   - Categorizes AOT warnings
   - Groups by severity
   - Suggests fixes
   - Tracks trends

3. **aot-test-runner.fsx** - Test matrix runner
   ```bash
   dotnet fsi aot-test-runner.fsx --runtime linux-x64
   ```
   - Tests multiple configurations
   - Measures binary sizes
   - Runs smoke tests
   - Generates comparison report

### Issue Templates

Located in `templates/`:

1. **aot-issue-report.md** - For documenting new AOT issues
2. **aot-workaround.md** - For documenting workarounds
3. **known-issues/** - Database of all encountered issues

## Knowledge Base

The AOT Guru maintains and updates:

1. **AOT/Trimming Guide** (`docs/contributing/aot-trimming-guide.md`)
   - Comprehensive patterns and examples
   - User-facing documentation
   - Updated with new .NET releases

2. **AOT Optimization Guide** (`.agents/aot-optimization.md`)
   - Agent-specific guidance
   - Decision trees
   - Issue resolution workflows

3. **Issue Database** (`templates/known-issues/`)
   - Catalog of all AOT issues
   - Resolution status
   - Patterns and trends

## Size Targets

Based on morphir-dotnet requirements:

| Configuration | Target Size | Use Case |
|--------------|-------------|----------|
| Minimal CLI | 5-8 MB | Basic IR operations only |
| Feature-rich CLI | 8-12 MB | Full tooling features |
| With Rich UI | 10-15 MB | Spectre.Console for terminal UI |

## Example Workflow

### Making a Feature AOT-Compatible

1. **Assessment**
   ```
   You: "I need to make the VerifyIR feature AOT-compatible"
   
   AOT Guru:
   - Analyzes VerifyIR code
   - Identifies JSON serialization usage
   - Checks for reflection patterns
   - Reviews dependencies (WolverineFx, System.Text.Json)
   ```

2. **Planning**
   ```
   AOT Guru provides:
   - List of changes needed
   - Priority order
   - Estimated effort
   - Potential risks
   ```

3. **Implementation**
   ```
   AOT Guru:
   - Creates source-generated JsonSerializerContext
   - Adds DynamicDependency attributes where needed
   - Updates WolverineFx configuration for AOT
   - Shows code examples
   ```

4. **Testing**
   ```
   AOT Guru:
   - Builds with PublishAot=true
   - Runs smoke tests
   - Measures binary size
   - Compares against targets
   ```

5. **Documentation**
   ```
   AOT Guru:
   - Updates AOT/Trimming Guide with new patterns
   - Documents any issues encountered
   - Adds BDD test scenarios
   ```

## Decision Trees

### "I have an AOT error"

```
Error Type?
├── IL2026 (RequiresUnreferencedCode)
│   ├── System.Text.Json → Use source generators
│   └── Other reflection → Add DynamicDependency or refactor
│
├── IL3050 (RequiresDynamicCode)
│   ├── LINQ expressions → Replace with delegates
│   └── Reflection.Emit → Use source generators
│
├── IL2087 (Type incompatibility)
│   └── Add [DynamicallyAccessedMembers] attributes
│
└── Runtime error (MissingMethodException)
    └── Add DynamicDependency or TrimmerRootDescriptor
```

### "My binary is too large"

```
Size vs Target?
├── > 20 MB → Check dependencies (major issue)
│   ├── Run: dotnet list package
│   ├── Look for: Newtonsoft.Json, heavy ORMs
│   └── Replace with lighter alternatives
│
├── 12-20 MB → Check optimization flags
│   ├── IlcOptimizationPreference=Size
│   ├── InvariantGlobalization=true
│   └── Enable all feature switches
│
├── 8-12 MB → Feature-rich target (acceptable)
│   └── Document feature set and size
│
└── < 8 MB → Minimal/optimal (excellent)
    └── Track for size regression
```

## Integration with Other Skills

### With QA Tester
- AOT Guru provides test matrices
- QA Tester executes and validates
- Share issue reports and regression data

### With Release Manager
- AOT Guru ensures AOT builds before release
- Release Manager includes AOT binaries in release
- Track binary sizes across releases

## Continuous Improvement

The AOT Guru learns and improves by:

1. **Pattern Recognition** - Identifies recurring issues
2. **Automation** - Creates diagnostic scripts for common problems
3. **Documentation** - Updates guides with new patterns
4. **Community** - Shares findings with broader .NET community

### Quarterly Review

Every quarter, the AOT Guru reviews:
- All documented issues
- Size trends
- New .NET AOT features
- Community best practices
- Documentation accuracy

## Getting Help

If the AOT Guru encounters something it can't solve:
1. Documents the issue thoroughly
2. Researches .NET community solutions
3. Escalates to maintainers with full context
4. Updates knowledge base with resolution

## References

- [AOT/Trimming Guide](../../../docs/contributing/aot-trimming-guide.md) - User-facing documentation
- [F# Coding Guide](../../../docs/contributing/fsharp-coding-guide.md) - F# AOT patterns
- [AGENTS.md](../../../AGENTS.md) - Project guidance
- [Microsoft AOT Docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)

---

**Philosophy**: The best AOT support is proactive, not reactive. Design for AOT from the start, document every issue, automate diagnostics, and make AOT easier for everyone over time.
