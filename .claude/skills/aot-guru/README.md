# AOT Guru Skill

Single-file trimmed executable and Native AOT optimization expert for morphir-dotnet.

## Quick Start

This skill is automatically activated when you mention:
- "single-file" or "trimmed executable"
- "AOT" or "Native AOT"
- "trimming" or "PublishTrimmed"
- "size optimization"
- "IL2026", "IL3050" (trimming/AOT warnings)
- "reflection error"
- "source generator" or "Myriad"

## What This Skill Does

The AOT Guru helps with:

1. **Single-File Trimmed Executables** (Primary Focus) - Produce optimized deployments today
2. **AOT Readiness** - Guide code toward eventual Native AOT support
3. **Trimming Diagnostics** - Identify and resolve trimming issues
4. **Size Optimization** - Reduce binary size through configuration
5. **F# and Myriad Expertise** - Compile-time code generation for F#
6. **Knowledge Base** - Maintain and evolve best practices
7. **Testing Automation** - Create and run test matrices
8. **Continuous Improvement** - Learn from issues and update documentation

## Current Focus: Single-File Trimmed Executables

The primary focus is on **single-file trimmed executables** which are:
- ✅ Available now (no blockers)
- ✅ Significantly smaller than untrimmed (30-50% reduction)
- ✅ Easy to deploy (single file)
- ✅ No .NET runtime dependency
- ✅ Fast enough for CLI tools

Native AOT is the **future goal**, but not immediately achievable due to:
- ❌ Reflection usage in existing code
- ❌ Some dependency compatibility issues
- ❌ Dynamic code patterns

**The AOT Guru guides you to make code AOT-ready even while using trimmed executables today.**

## Common Use Cases

### "I'm getting IL2026 warnings"

**What it means**: Code is using reflection (not compatible with trimming or AOT)

**AOT Guru will**:
1. Analyze the warning details
2. Identify the reflection usage
3. Suggest source generators (C#) or Myriad (F#)
4. Show code examples
5. Explain why this prepares for future AOT
6. Update documentation if it's a new pattern

### "My trimmed binary is 40 MB, can we reduce it?"

**AOT Guru will**:
1. Analyze project dependencies
2. Check optimization flags
3. Identify large dependencies
4. Suggest replacements or optimizations
5. Provide step-by-step size reduction plan
6. Explain current vs future AOT size targets

### "How do I make System.Text.Json work with trimming?"

**AOT Guru will**:
1. Explain source-generated serialization contexts
2. Show code examples
3. Create JsonSerializerContext for your types
4. Test the changes
5. Update documentation
6. Note that this also prepares for AOT

### "Should I use FSharp.SystemTextJson in F# code?"

**AOT Guru will**:
1. Explain that FSharp.SystemTextJson uses reflection
2. Not compatible with trimming or AOT
3. Recommend Myriad for compile-time generation
4. Or use manual parsing/serialization
5. Show examples of both approaches
1. Explain source-generated serialization contexts
2. Show code examples
3. Create JsonSerializerContext for your types
4. Test the changes
5. Update documentation

### "My trimmed build succeeds but crashes at runtime"

**AOT Guru will**:
1. Diagnose likely trimming issue (types/methods removed)
2. Check for MissingMethodException or TypeLoadException
3. Add DynamicDependency attributes
4. Test with PublishTrimmed first (easier to debug than AOT)
5. Document the issue for future reference

### "What's Myriad and should I use it for F# code?"

**AOT Guru will**:
1. Explain Myriad: F# compile-time code generation
2. Compare to C# source generators
3. Show when Myriad helps (avoiding reflection in F#)
4. Provide examples of Myriad usage
5. Link to Myriad documentation
6. Explain how it prepares for future AOT

## Incremental Path to AOT

The AOT Guru understands that Native AOT is not immediately achievable. Here's the recommended path:

### Phase 1: Single-File Trimmed (Now) ✅

**Focus**: Produce deployable executables today
- Configure PublishTrimmed + PublishSingleFile
- Fix trimming warnings
- Optimize size (15-35 MB range)
- Test thoroughly

### Phase 2: AOT-Ready Patterns (Ongoing) 🚧

**Focus**: Write new code that will work with AOT
- Use source generators (C#) or Myriad (F#)
- Avoid reflection in new code
- Choose AOT-compatible dependencies
- Mark non-AOT code with attributes

### Phase 3: Refactor Existing (Future) ⏳

**Focus**: Make existing code AOT-compatible
- Replace reflection with generators
- Update dependencies
- Refactor dynamic code

### Phase 4: Enable AOT (Future Goal) 🎯

**Focus**: Compile with PublishAot=true
- Enable Native AOT
- Achieve 5-12 MB target sizes
- Instant startup times

**Current Status**: Phase 1 (trimmed) is production-ready. Phase 2 (AOT-ready patterns) is ongoing. The AOT Guru helps you succeed at Phase 1 while preparing for Phase 4.

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

### Current Reality (Single-File Trimmed)
| Configuration | Target Size | Use Case |
|--------------|-------------|----------|
| Minimal CLI | 15-25 MB | Basic IR operations, trimmed |
| Feature-rich CLI | 25-35 MB | Full tooling features, trimmed |
| With Rich UI | 30-40 MB | Spectre.Console, trimmed |

### Future Goal (Native AOT)
| Configuration | Target Size | Use Case |
|--------------|-------------|----------|
| Minimal CLI | 5-8 MB | Basic IR operations, AOT + trimming |
| Feature-rich CLI | 8-12 MB | Full tooling, AOT + trimming |
| With Rich UI | 10-15 MB | Spectre.Console, AOT + trimming |

**Note**: Focus on achieving current targets with trimmed executables while guiding code toward future AOT targets.

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
