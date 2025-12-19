# AOT Optimization Guide for AI Agents

This guide provides AI agents with comprehensive Native AOT, trimming, and size optimization guidance for morphir-dotnet. It complements the user-facing [AOT/Trimming Guide](../docs/contributing/aot-trimming-guide.md) with agent-specific decision trees, diagnostic procedures, and automated workflows.

## Quick Links

- **User Documentation**: [AOT/Trimming Guide](../docs/contributing/aot-trimming-guide.md)
- **F# Guide**: [F# Coding Guide](../docs/contributing/fsharp-coding-guide.md)
- **Skill**: [AOT Guru Skill](./.claude/skills/aot-guru/)
- **Main Guidance**: [AGENTS.md](../AGENTS.md)

## Agent Responsibilities

When working with AOT/trimming:

1. **Design Phase**: Ensure new code is AOT-compatible from the start
2. **Implementation**: Use AOT-friendly patterns (source generators, no reflection)
3. **Testing**: Test with PublishAot=true before finalizing
4. **Documentation**: Update guides with new patterns
5. **Issue Tracking**: Document problems in issue database

## Decision Trees

### Decision Tree: "How do I make this code AOT-compatible?"

```
What type of code?
├── JSON Serialization
│   └── Use source-generated JsonSerializerContext
│       └── See: AOT/Trimming Guide § JSON Serialization in AOT
│
├── Configuration/Options
│   ├── Simple POCOs → Use source generators
│   └── Complex validation → Use FluentValidation (AOT-compatible)
│
├── Dependency Injection
│   ├── Simple services → Register explicitly
│   └── Auto-discovery (WolverineFx, etc.) → Explicit registration for AOT
│
├── Logging
│   └── Serilog console/file sinks (AOT-compatible)
│       └── Avoid reflection-based sinks
│
├── CLI Parsing
│   └── System.CommandLine (AOT-compatible)
│       └── Argu for F# scripts (AOT-compatible)
│
└── Plugin/Extension System
    └── ❌ NOT AOT-compatible (Assembly.Load, reflection)
        └── Use compile-time known types only
```

### Decision Tree: "I have an AOT compilation error"

```
Error Type?
├── IL2026: RequiresUnreferencedCode
│   ├── Is it System.Text.Json?
│   │   └── YES → Create JsonSerializerContext with [JsonSerializable]
│   ├── Is it third-party library?
│   │   ├── Check if library has AOT support
│   │   ├── If yes → Update to latest version
│   │   └── If no → Find alternative or mark with [RequiresUnreferencedCode]
│   └── Is it your code?
│       ├── Can you avoid reflection?
│       │   └── YES → Refactor to use compile-time known types
│       └── Must use reflection?
│           └── Add [DynamicDependency] or [DynamicallyAccessedMembers]
│
├── IL3050: RequiresDynamicCode
│   ├── LINQ Expression trees?
│   │   └── Replace with Func<> delegates
│   ├── Reflection.Emit?
│   │   └── Use source generators instead
│   └── Dynamic types?
│       └── Use compile-time known types
│
├── IL2087: Type parameter incompatibility
│   └── Add [DynamicallyAccessedMembers] to generic parameter
│
└── Runtime error (MissingMethodException, TypeLoadException)
    ├── Build with PublishTrimmed=true first (easier to debug)
    ├── Enable trim warnings: <TrimmerSingleWarn>false</TrimmerSingleWarn>
    ├── Find what's being trimmed: Check trim warnings
    └── Preserve types:
        ├── Option 1: [DynamicDependency] attribute
        ├── Option 2: TrimmerRootDescriptor XML
        └── Option 3: <TrimmerRootAssembly>
```

### Decision Tree: "My binary is too large"

```
Current Size?
├── > 20 MB (Too large)
│   └── Check dependencies:
│       ├── Run: dotnet list package
│       ├── Look for heavy libraries:
│       │   ├── Newtonsoft.Json → Replace with System.Text.Json
│       │   ├── Entity Framework → Consider lighter alternative
│       │   └── Heavy ORMs → Use Dapper or manual SQL
│       └── Profile with dotnet-size-analyzer
│
├── 12-20 MB (Large but acceptable for feature-rich)
│   └── Verify optimizations:
│       ├── IlcOptimizationPreference=Size
│       ├── InvariantGlobalization=true
│       ├── DebugType=none
│       ├── EventSourceSupport=false
│       └── All feature switches disabled
│
├── 8-12 MB (Target for feature-rich CLI)
│   └── ✓ Good size
│       └── Monitor for regressions
│
└── 5-8 MB (Target for minimal CLI)
    └── ✓ Excellent size
        └── Document configuration for others
```

## Diagnostic Procedures

### Procedure: Diagnose AOT Issue in New Feature

**When**: Before merging any new feature PR

**Steps**:
1. Run AOT diagnostics:
   ```bash
   dotnet fsi .claude/skills/aot-guru/aot-diagnostics.fsx src/Morphir/Morphir.csproj
   ```

2. Build with AOT:
   ```bash
   dotnet publish -c Release -r linux-x64 /p:PublishAot=true 2>&1 | tee build.log
   ```

3. Analyze warnings:
   ```bash
   dotnet fsi .claude/skills/aot-guru/aot-analyzer.fsx build.log
   ```

4. Run smoke tests:
   ```bash
   ./bin/Release/net10.0/linux-x64/publish/morphir --version
   ./bin/Release/net10.0/linux-x64/publish/morphir --help
   ```

5. Document issues:
   - If new pattern → Update AOT/Trimming Guide
   - If blocking issue → Create aot-issue-report.md
   - If workaround needed → Create aot-workaround.md

### Procedure: Size Regression Investigation

**When**: Binary size increases unexpectedly

**Steps**:
1. Compare current vs baseline:
   ```bash
   ls -lh bin/Release/net10.0/linux-x64/publish/morphir
   # Compare with documented baseline
   ```

2. Run test matrix:
   ```bash
   dotnet fsi .claude/skills/aot-guru/aot-test-runner.fsx --runtime linux-x64
   ```

3. Analyze what changed:
   ```bash
   git log --oneline -10
   git diff HEAD~1 -- "*.csproj" "Directory.*.props"
   ```

4. Check for new dependencies:
   ```bash
   dotnet list package
   ```

5. Profile with tools:
   ```bash
   # Use ILSpy or dotnet-size-analyzer
   ```

6. Document findings:
   - Update size targets if intentional
   - Create issue if regression
   - Add size test to prevent future regressions

### Procedure: Third-Party Library AOT Compatibility Check

**When**: Before adding any new dependency

**Steps**:
1. Check library's AOT support:
   - README mentions AOT/trimming
   - Issues/discussions about AOT
   - Source generator support
   - Reflection usage

2. Test locally:
   ```bash
   # Add package to test project
   dotnet add package <PackageName>
   
   # Try AOT build
   dotnet publish -c Release -r linux-x64 /p:PublishAot=true
   ```

3. Review warnings:
   ```bash
   # Check for IL2XXX, IL3XXX warnings from the library
   ```

4. Document findings:
   - If compatible → Note in PR description
   - If issues → Document workaround or find alternative
   - If blocking → Choose different library

## Common Patterns

### Pattern: Source-Generated JSON Serialization

**Use Case**: Any System.Text.Json serialization in AOT build

**Implementation**:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

// Define all types that need serialization
[JsonSerializable(typeof(MyResult))]
[JsonSerializable(typeof(MyCommand))]
[JsonSerializable(typeof(List<MyItem>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AppJsonContext : JsonSerializerContext
{
}

// Usage
var json = JsonSerializer.Serialize(result, AppJsonContext.Default.MyResult);
var obj = JsonSerializer.Deserialize(json, AppJsonContext.Default.MyResult);
```

**Where**: Any feature that outputs JSON (--json flag)

### Pattern: WolverineFx with AOT

**Use Case**: WolverineFx handler registration in AOT builds

**Implementation**:
```csharp
builder.Services.AddWolverine(opts =>
{
    // Disable auto-discovery for AOT
    opts.Discovery.DisableConventionalDiscovery();
    
    // Explicitly register handlers
    opts.Handlers.AddHandler<VerifyIRHandler>();
    opts.Handlers.AddHandler<OtherHandler>();
    
    // Or scan specific assembly
    opts.Discovery.IncludeAssembly(typeof(VerifyIRHandler).Assembly);
});
```

**Where**: `src/Morphir.Tooling/Program.cs`

### Pattern: Embedded Resources in AOT

**Use Case**: Loading JSON schemas or other embedded resources

**Implementation**:
```csharp
var assembly = Assembly.GetExecutingAssembly();

// Use fully qualified resource name
var resourceName = "Morphir.Tooling.schemas.v3.json";

using var stream = assembly.GetManifestResourceStream(resourceName);

if (stream == null)
{
    // For debugging: list available resources
    var available = assembly.GetManifestResourceNames();
    throw new FileNotFoundException(
        $"Resource '{resourceName}' not found. Available: {string.Join(", ", available)}");
}

using var reader = new StreamReader(stream);
var content = reader.ReadToEnd();
```

**Where**: `src/Morphir.Tooling/Infrastructure/JsonSchema/SchemaLoader.cs`

### Pattern: Avoiding Assembly.GetTypes()

**Use Case**: Type discovery in AOT builds

**Implementation**:
```csharp
// ❌ BAD: Gets trimmed types
var types = Assembly.GetExecutingAssembly().GetTypes();

// ✅ GOOD: Explicit list
private static readonly Type[] KnownHandlers =
[
    typeof(VerifyIRHandler),
    typeof(MigrateIRHandler),
    typeof(FormatIRHandler)
];

// ✅ ALSO GOOD: Source generator for type lists
[assembly: KnownHandlersSource]

[Generator]
public class KnownHandlersGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        // Generate KnownHandlers list at compile time
    }
}
```

## Size Optimization Checklist

When optimizing binary size, apply these in order:

### 1. Baseline Configuration (Required)

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <IlcOptimizationPreference>Size</IlcOptimizationPreference>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>
</PropertyGroup>
```

**Expected Savings**: Baseline for AOT

### 2. Disable Debug Symbols

```xml
<PropertyGroup>
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
  <IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
</PropertyGroup>
```

**Expected Savings**: ~1-2 MB

### 3. Invariant Globalization

```xml
<PropertyGroup>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

**Expected Savings**: ~5 MB
**Trade-off**: No culture-specific formatting

### 4. Disable Event Source

```xml
<PropertyGroup>
  <EventSourceSupport>false</EventSourceSupport>
  <UseSystemResourceKeys>true</UseSystemResourceKeys>
</PropertyGroup>
```

**Expected Savings**: ~500 KB

### 5. Disable Other Features

```xml
<PropertyGroup>
  <HttpActivityPropagationSupport>false</HttpActivityPropagationSupport>
  <MetadataUpdaterSupport>false</MetadataUpdaterSupport>
  <EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
</PropertyGroup>
```

**Expected Savings**: ~1 MB combined

### 6. Dependency Review

- Replace Newtonsoft.Json with System.Text.Json: ~3 MB savings
- Remove unused NuGet packages: Varies
- Use lighter alternatives for heavy libraries: Varies

### 7. Code Review

- Remove unused code paths: Varies
- Lazy-load optional features: Improves startup, may not reduce size much
- Extract rarely-used features to plugins (if not AOT): Not applicable for AOT

## Testing Strategy

### Pre-Merge Testing

Every PR that touches code should:

1. **Build with AOT analyzers**:
   ```bash
   dotnet build -c Release /p:EnableAotAnalyzer=true /p:EnableTrimAnalyzer=true
   ```

2. **Check for new warnings**:
   ```bash
   # Compare warning count before/after
   ```

3. **Test AOT build locally**:
   ```bash
   dotnet publish -c Release -r linux-x64 /p:PublishAot=true
   ```

4. **Run smoke tests**:
   ```bash
   ./bin/Release/net10.0/linux-x64/publish/morphir --version
   ./bin/Release/net10.0/linux-x64/publish/morphir --help
   ```

### BDD Test Scenarios

Create BDD tests for AOT functionality:

```gherkin
Feature: Native AOT Compatibility
  As a CLI developer
  I want to ensure AOT compatibility
  So that users get fast, small binaries

  Scenario: Build succeeds with PublishAot
    Given a clean build environment
    When I build with PublishAot=true
    Then the build should succeed
    And no IL2XXX warnings should be present

  Scenario: JSON serialization works in AOT
    Given an AOT-built morphir executable
    When I run a command with --json flag
    Then the output should be valid JSON
    And no serialization errors should occur

  Scenario: All commands work in AOT
    Given an AOT-built morphir executable
    When I run each CLI command
    Then all commands should execute successfully
```

### Size Regression Testing

Add size checks to CI:

```bash
#!/bin/bash
# .github/workflows/aot-size-check.sh

MAX_SIZE_MB=12  # Feature-rich target

SIZE_BYTES=$(stat -c%s bin/Release/net10.0/linux-x64/publish/morphir)
SIZE_MB=$((SIZE_BYTES / 1024 / 1024))

echo "Executable size: ${SIZE_MB} MB (max: ${MAX_SIZE_MB} MB)"

if [ "$SIZE_MB" -gt "$MAX_SIZE_MB" ]; then
    echo "❌ Size exceeds threshold"
    exit 1
else
    echo "✓ Size within threshold"
    exit 0
fi
```

## Known Issues Database

### How to Document Issues

When you encounter an AOT issue:

1. Create issue report using template:
   ```bash
   cp .claude/skills/aot-guru/templates/aot-issue-report.md \
      .claude/skills/aot-guru/templates/known-issues/issue-YYYY-MM-DD-description.md
   ```

2. Fill in all sections
3. Link from AOT/Trimming Guide if pattern should be documented
4. Update diagnostic scripts if issue is detectable

### Current Known Issues

*This section will be populated as issues are discovered*

#### Issue 1: System.Text.Json Reflection (RESOLVED)
- **Status**: Resolved with source generators
- **Pattern**: Use JsonSerializerContext
- **Documentation**: AOT/Trimming Guide § JSON Serialization in AOT

#### Issue 2: WolverineFx Auto-Discovery (WORKAROUND AVAILABLE)
- **Status**: Workaround available
- **Pattern**: Explicit handler registration
- **Documentation**: AGENTS.md § Phase 1 Implementation Patterns

## Maintenance and Evolution

### Quarterly Review Tasks

Every quarter (or when new .NET version releases):

1. **Review Known Issues**
   - Close resolved issues
   - Update workarounds that became obsolete
   - Check if new .NET features provide better solutions

2. **Update Size Targets**
   - Review actual sizes achieved
   - Adjust targets based on reality
   - Document size trends

3. **Update Documentation**
   - Sync AOT/Trimming Guide with new .NET features
   - Add new patterns discovered
   - Remove obsolete patterns

4. **Update Diagnostic Scripts**
   - Add checks for new issue patterns
   - Improve accuracy of existing checks
   - Add new analyzers

5. **Review Dependencies**
   - Check for AOT support in dependencies
   - Update to versions with better AOT support
   - Remove or replace problematic dependencies

### Learning from Issues

When an AOT issue is resolved:

1. **Document the pattern** in AOT/Trimming Guide
2. **Create diagnostic check** in aot-diagnostics.fsx
3. **Add BDD test** to prevent regression
4. **Share with community** (blog post, discussion, etc.)

## References

### Primary Documentation
- [AOT/Trimming Guide](../docs/contributing/aot-trimming-guide.md) - User-facing guide
- [F# Coding Guide](../docs/contributing/fsharp-coding-guide.md) - F# AOT patterns
- [AGENTS.md](../AGENTS.md) - Project-wide agent guidance

### Microsoft Documentation
- [Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Prepare .NET Libraries for Trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
- [Introduction to AOT Warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/)
- [Trimming Options](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options)

### Tools
- [ILSpy](https://github.com/icsharpcode/ILSpy) - .NET assembly browser
- [dotnet-size-analyzer](https://github.com/MichalStrehovsky/sizoscope) - Size analysis tool

### Community Resources
- [Awesome .NET AOT](https://github.com/natemcmaster/awesome-dotnet-aot)
- [.NET AOT Compatibility List](https://aot.github.io/)

---

**Remember**: AOT compatibility should be a first-class concern from the design phase, not an afterthought. Design with AOT in mind, test early, and document patterns for others.
