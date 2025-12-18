# AOT, Trimming, and Single-File Executables Guide

This guide provides comprehensive guidance for building Native AOT, trimmed, and single-file executables for morphir-dotnet CLI tools.

## Table of Contents

1. [Overview](#overview)
2. [Native AOT Compilation](#native-aot-compilation)
3. [Assembly Trimming](#assembly-trimming)
4. [Single-File Executables](#single-file-executables)
5. [Size Optimization Strategies](#size-optimization-strategies)
6. [Reflection and Dynamic Code](#reflection-and-dynamic-code)
7. [JSON Serialization in AOT](#json-serialization-in-aot)
8. [Common Gotchas and Workarounds](#common-gotchas-and-workarounds)
9. [Testing AOT/Trimmed Builds](#testing-aottrimmed-builds)
10. [Best Practices Checklist](#best-practices-checklist)

---

## Overview

### What is Native AOT?

Native AOT (Ahead-of-Time) compilation produces native executables that:
- Start instantly (no JIT compilation)
- Use less memory
- Are self-contained (no .NET runtime required)
- Are platform-specific (separate builds for Linux, Windows, macOS)

### What is Trimming?

Trimming removes unused code from assemblies:
- Reduces deployment size
- Removes unused dependencies
- Can break code that uses reflection
- Required for Native AOT

### Single-File Executables

Single-file executables bundle everything into one file:
- Simplified deployment
- Can be combined with AOT or regular .NET
- Platform-specific

### Trade-offs

| Feature | Pros | Cons |
|---------|------|------|
| Native AOT | Fast startup, small memory, self-contained | Larger size, no dynamic code, platform-specific |
| Trimming | Smaller size, faster deployment | May break reflection, requires testing |
| Single-File | Simple deployment | Larger initial size, extraction overhead |

---

## Native AOT Compilation

### Enabling Native AOT

```xml
<!-- In .csproj -->
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <IlcOptimizationPreference>Size</IlcOptimizationPreference>
  <IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
</PropertyGroup>
```

### AOT-Compatible Code Patterns

#### ✅ Good: Static Methods

```csharp
// ✅ AOT-friendly
public static class Calculator
{
    public static int Add(int a, int b) => a + b;
}
```

#### ✅ Good: Sealed Classes

```csharp
// ✅ Sealed classes optimize better
public sealed class Config
{
    public required string Host { get; init; }
    public required int Port { get; init; }
}
```

#### ❌ Avoid: Reflection.Emit

```csharp
// ❌ Not supported in Native AOT
var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
var moduleBuilder = assemblyBuilder.DefineDynamicModule("DynamicModule");
```

#### ✅ Good: Source Generators Instead

```csharp
// ✅ Use source generators for code generation
[JsonSerializable(typeof(Config))]
public partial class ConfigJsonContext : JsonSerializerContext { }
```

### AOT Warnings

Enable and address AOT warnings:

```xml
<PropertyGroup>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
</PropertyGroup>
```

Common AOT warnings:
- `IL2026`: Using members annotated with `RequiresUnreferencedCode`
- `IL2087`: Target parameter type not compatible with source type
- `IL3050`: Using dynamic types in AOT

---

## Assembly Trimming

### Enabling Trimming

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>
</PropertyGroup>
```

### Trim Modes

1. **`copyUsed`** (default): Copy entire assemblies if any part is used
2. **`link`**: Remove unused members from assemblies (more aggressive)

```xml
<!-- Use link for maximum size reduction -->
<PropertyGroup>
  <TrimMode>link</TrimMode>
</PropertyGroup>
```

### Preserving Code from Trimming

#### Method 1: Dynamic Dependency Attribute

```csharp
using System.Diagnostics.CodeAnalysis;

public class ConfigLoader
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Config))]
    public static Config Load(string json)
    {
        return JsonSerializer.Deserialize<Config>(json)!;
    }
}
```

#### Method 2: Trimmer Root Assembly

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="Morphir.Core" />
</ItemGroup>
```

#### Method 3: Trimmer Root Descriptor

```xml
<!-- Create TrimmerRoots.xml -->
<linker>
  <assembly fullname="Morphir.Core">
    <type fullname="Morphir.IR.Package" preserve="all" />
    <type fullname="Morphir.IR.Module" preserve="all" />
  </assembly>
</linker>

<!-- Reference in .csproj -->
<ItemGroup>
  <TrimmerRootDescriptor Include="TrimmerRoots.xml" />
</ItemGroup>
```

### F# Trimming Considerations

F# code can be trimmed, but requires careful handling:

```xml
<!-- F# project with trimming -->
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>
  <!-- Preserve F# reflection metadata -->
  <IlcDisableReflection>false</IlcDisableReflection>
</PropertyGroup>
```

---

## Single-File Executables

### Enabling Single-File Publishing

```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
</PropertyGroup>
```

### Single-File with AOT

```bash
# Publish Native AOT single-file executable
dotnet publish -c Release -r linux-x64 /p:PublishAot=true /p:PublishSingleFile=true
```

### Embedded Resources in Single-File

```xml
<!-- Embed files as resources -->
<ItemGroup>
  <EmbeddedResource Include="schemas/**/*.json" />
</ItemGroup>
```

```csharp
// Access embedded resources
var assembly = Assembly.GetExecutingAssembly();
using var stream = assembly.GetManifestResourceStream("Morphir.schemas.v3.json");
using var reader = new StreamReader(stream);
var schemaJson = reader.ReadToEnd();
```

---

## Size Optimization Strategies

### 1. Enable All Size Optimizations

```xml
<PropertyGroup>
  <!-- Native AOT size optimizations -->
  <PublishAot>true</PublishAot>
  <IlcOptimizationPreference>Size</IlcOptimizationPreference>
  <IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>

  <!-- Trimming -->
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>

  <!-- Remove debugging symbols -->
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>

  <!-- Invariant globalization (saves ~5MB) -->
  <InvariantGlobalization>true</InvariantGlobalization>

  <!-- Disable event source tracing -->
  <EventSourceSupport>false</EventSourceSupport>

  <!-- Use minimal HttpClient -->
  <UseSystemResourceKeys>true</UseSystemResourceKeys>
</PropertyGroup>
```

### 2. Minimize Dependencies

```csharp
// ❌ Avoid: Heavy dependencies
using Newtonsoft.Json;  // Large library

// ✅ Good: Use built-in alternatives
using System.Text.Json;  // Built-in, trims better
```

### 3. Use Feature Switches

```xml
<PropertyGroup>
  <!-- Disable unused features -->
  <EventSourceSupport>false</EventSourceSupport>
  <UseSystemResourceKeys>true</UseSystemResourceKeys>
  <EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
  <HttpActivityPropagationSupport>false</HttpActivityPropagationSupport>
  <MetadataUpdaterSupport>false</MetadataUpdaterSupport>
</PropertyGroup>
```

### 4. Size Comparison Table

| Configuration | Typical Size (Linux x64) |
|--------------|--------------------------|
| Framework-dependent | ~200 KB |
| Self-contained | ~70 MB |
| Self-contained + Trimmed | ~30 MB |
| Native AOT (no optimizations) | ~15 MB |
| Native AOT + Size optimizations | ~8-10 MB |
| Native AOT + Trimmed + Size opts | ~5-8 MB |

### 5. Measure and Analyze Size

```bash
# Publish and analyze
dotnet publish -c Release -r linux-x64 /p:PublishAot=true

# Check size
ls -lh bin/Release/net10.0/linux-x64/publish/

# Analyze trimmed assemblies (if not using AOT)
dotnet run --project $(dotnet tool run dotnet-ilverify) \
  bin/Release/net10.0/linux-x64/publish/Morphir.dll
```

---

## Reflection and Dynamic Code

### Problem: Reflection Breaks in AOT/Trimmed Builds

```csharp
// ❌ This breaks in AOT
Type type = Type.GetType("Morphir.IR.Package");
var instance = Activator.CreateInstance(type);
```

### Solution 1: Source Generators

```csharp
// ✅ Use source generators
[JsonSerializable(typeof(Package))]
[JsonSerializable(typeof(Module))]
public partial class MorphirJsonContext : JsonSerializerContext { }

// Usage
var package = JsonSerializer.Deserialize(json, MorphirJsonContext.Default.Package);
```

### Solution 2: Dynamic Dependency Attributes

```csharp
using System.Diagnostics.CodeAnalysis;

public class PackageLoader
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Package))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Module))]
    public static void LoadTypes()
    {
        // Ensures types are preserved
    }
}
```

### Solution 3: RequiresUnreferencedCode Annotation

```csharp
using System.Diagnostics.CodeAnalysis;

// Mark methods that use reflection
[RequiresUnreferencedCode("Uses reflection to load plugins")]
public static void LoadPlugins(string path)
{
    var assemblies = Directory.GetFiles(path, "*.dll")
        .Select(Assembly.LoadFrom);

    foreach (var asm in assemblies)
    {
        // Reflection code here
    }
}
```

### Solution 4: Avoid Reflection Entirely

```csharp
// ❌ Avoid: Reflection-based deserialization
var type = Type.GetType(typeName);
var deserializer = typeof(JsonSerializer)
    .GetMethod("Deserialize")
    .MakeGenericMethod(type);

// ✅ Good: Compile-time known types with source generators
var result = typeName switch
{
    "Package" => JsonSerializer.Deserialize(json, MorphirJsonContext.Default.Package),
    "Module" => JsonSerializer.Deserialize(json, MorphirJsonContext.Default.Module),
    _ => throw new NotSupportedException($"Unknown type: {typeName}")
};
```

---

## JSON Serialization in AOT

### Problem: System.Text.Json Uses Reflection

By default, `System.Text.Json` uses reflection for serialization, which doesn't work in Native AOT.

### Solution: Source-Generated Serialization Context

#### C# Example

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

// Define all types that need serialization
[JsonSerializable(typeof(VerifyIRResult))]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(Package))]
[JsonSerializable(typeof(Module))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class MorphirJsonContext : JsonSerializerContext
{
}

// Usage
var result = new VerifyIRResult(
    IsValid: true,
    SchemaVersion: "3",
    FilePath: "test.json",
    Errors: [],
    Timestamp: DateTime.UtcNow
);

var json = JsonSerializer.Serialize(result, MorphirJsonContext.Default.VerifyIRResult);
var deserialized = JsonSerializer.Deserialize(json, MorphirJsonContext.Default.VerifyIRResult);
```

#### F# Example

F# can also use source generators:

```fsharp
open System.Text.Json
open System.Text.Json.Serialization

// Define serialization context
[<JsonSerializable(typeof<ScriptResult>)>]
[<JsonSerializable(typeof<Config>)>]
[<JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)>]
type MorphirJsonContext() =
    inherit JsonSerializerContext()

// Usage
let result = { Success = true; Version = Some "1.0.0"; Errors = []; ExitCode = 0 }
let json = JsonSerializer.Serialize(result, MorphirJsonContext.Default.ScriptResult)
```

### F# with FSharp.SystemTextJson and AOT

`FSharp.SystemTextJson` doesn't fully support Native AOT. Options:

1. **Use source generators** (as shown above)
2. **Use simpler types** (records without unions/options for AOT builds)
3. **Mark AOT-incompatible code** with `RequiresUnreferencedCode`

```fsharp
open System.Diagnostics.CodeAnalysis

[<RequiresUnreferencedCode("FSharp.SystemTextJson uses reflection")>]
let serializeWithFSharpJson (value: 'T) : string =
    let options = JsonSerializerOptions()
    options.Converters.Add(JsonFSharpConverter())
    JsonSerializer.Serialize(value, options)
```

---

## Common Gotchas and Workarounds

### 1. Assembly.GetTypes() Fails

**Problem**: `Assembly.GetTypes()` returns incomplete list in trimmed builds.

**Workaround**: Use explicit type lists or source generators.

```csharp
// ❌ Breaks with trimming
var types = Assembly.GetExecutingAssembly().GetTypes();

// ✅ Use explicit list
private static readonly Type[] KnownTypes =
[
    typeof(Package),
    typeof(Module),
    typeof(TypeDefinition)
];
```

### 2. LINQ Expression Trees Fail in AOT

**Problem**: Expression trees use `Reflection.Emit`.

**Workaround**: Replace with delegates or source generators.

```csharp
// ❌ Fails in AOT
Expression<Func<int, int>> expr = x => x * 2;
var compiled = expr.Compile();

// ✅ Use delegates directly
Func<int, int> func = x => x * 2;
```

### 3. Type.GetType() Returns Null

**Problem**: Types are trimmed away.

**Workaround**: Use `DynamicDependency` or explicit type references.

```csharp
[DynamicDependency(DynamicallyAccessedMemberTypes.All, "Morphir.IR.Package", "Morphir.Core")]
public static Type GetPackageType()
{
    return Type.GetType("Morphir.IR.Package, Morphir.Core");
}
```

### 4. WolverineFx and AOT

**Issue**: WolverineFx uses reflection for handler discovery.

**Workaround**: Explicitly register handlers in AOT builds.

```csharp
// AOT-compatible WolverineFx setup
builder.Services.AddWolverine(opts =>
{
    // Explicitly register handlers instead of auto-discovery
    opts.Discovery.DisableConventionalDiscovery();
    opts.Handlers.AddHandler<VerifyIRHandler>();
    opts.Handlers.AddHandler<OtherHandler>();
});
```

### 5. Serilog Sinks May Use Reflection

**Issue**: Some Serilog sinks use reflection.

**Workaround**: Use console/file sinks which are AOT-compatible.

```csharp
// ✅ AOT-compatible logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        standardErrorFromLevel: LogEventLevel.Verbose,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

### 6. Spectre.Console and AOT

**Issue**: Spectre.Console generally works with AOT, but some features use reflection.

**Workaround**: Avoid reflection-based features, test thoroughly.

```csharp
// ✅ AOT-compatible Spectre.Console usage
AnsiConsole.MarkupLine("[green]✓ Success[/]");

var table = new Table();
table.AddColumn("Name");
table.AddColumn("Value");
table.AddRow("Version", "1.0.0");
AnsiConsole.Write(table);
```

### 7. Embedded Resources in AOT

**Issue**: `Assembly.GetManifestResourceStream()` works, but resource names change.

**Workaround**: Use fully qualified names and test.

```csharp
// ✅ Correct resource naming
var resourceName = "Morphir.Tooling.schemas.v3.json";
using var stream = Assembly.GetExecutingAssembly()
    .GetManifestResourceStream(resourceName);

if (stream == null)
{
    // List available resources for debugging
    var available = Assembly.GetExecutingAssembly()
        .GetManifestResourceNames();
    throw new FileNotFoundException(
        $"Resource '{resourceName}' not found. Available: {string.Join(", ", available)}");
}
```

---

## Testing AOT/Trimmed Builds

### 1. Test Matrix

Test all build configurations:

```bash
# Framework-dependent
dotnet build -c Release

# Self-contained
dotnet publish -c Release -r linux-x64 --self-contained

# Trimmed
dotnet publish -c Release -r linux-x64 --self-contained /p:PublishTrimmed=true

# Native AOT
dotnet publish -c Release -r linux-x64 /p:PublishAot=true
```

### 2. Automated Testing Script

```bash
#!/bin/bash
set -euo pipefail

echo "Testing AOT build..."

# Build Native AOT
dotnet publish -c Release -r linux-x64 /p:PublishAot=true

# Run basic smoke tests
./bin/Release/net10.0/linux-x64/publish/morphir --version
./bin/Release/net10.0/linux-x64/publish/morphir --help

# Test IR verification (example)
./bin/Release/net10.0/linux-x64/publish/morphir ir verify tests/TestData/valid-ir-v3.json

echo "AOT build tests passed!"
```

### 3. Size Regression Testing

```bash
#!/bin/bash
# Check executable size doesn't exceed threshold

MAX_SIZE_MB=10
EXECUTABLE="bin/Release/net10.0/linux-x64/publish/morphir"

SIZE_BYTES=$(stat -f%z "$EXECUTABLE" 2>/dev/null || stat -c%s "$EXECUTABLE")
SIZE_MB=$((SIZE_BYTES / 1024 / 1024))

if [ "$SIZE_MB" -gt "$MAX_SIZE_MB" ]; then
    echo "❌ Executable size ($SIZE_MB MB) exceeds threshold ($MAX_SIZE_MB MB)"
    exit 1
else
    echo "✅ Executable size: $SIZE_MB MB (threshold: $MAX_SIZE_MB MB)"
fi
```

### 4. Trim Warnings Analysis

```xml
<PropertyGroup>
  <!-- Treat trim warnings as errors in CI -->
  <TrimmerSingleWarn>false</TrimmerSingleWarn>
  <IlcTreatWarningsAsErrors>true</IlcTreatWarningsAsErrors>
</PropertyGroup>
```

---

## Best Practices Checklist

### Design Phase
- [ ] Avoid reflection and dynamic code generation
- [ ] Use source generators for JSON serialization
- [ ] Design with trimming in mind (explicit dependencies)
- [ ] Plan for platform-specific builds
- [ ] Consider size vs. feature trade-offs

### Implementation Phase
- [ ] Use `sealed` classes where possible
- [ ] Use static methods when appropriate
- [ ] Prefer compile-time known types over dynamic types
- [ ] Add `[DynamicDependency]` attributes where needed
- [ ] Use source-generated JSON contexts
- [ ] Avoid LINQ expression trees in critical paths
- [ ] Use `InvariantGlobalization` if localization not needed

### Testing Phase
- [ ] Test with `PublishTrimmed=true`
- [ ] Test with `PublishAot=true`
- [ ] Run full test suite on AOT builds
- [ ] Check executable size
- [ ] Verify embedded resources load correctly
- [ ] Test on all target platforms (Linux, Windows, macOS)
- [ ] Performance test startup time and memory usage

### Configuration Phase
- [ ] Enable AOT/trim analyzers
- [ ] Configure size optimizations
- [ ] Add trimmer root descriptors if needed
- [ ] Document platform-specific requirements
- [ ] Set up CI for multi-platform builds

### Documentation Phase
- [ ] Document AOT limitations
- [ ] List unsupported features (if any)
- [ ] Provide platform-specific instructions
- [ ] Document size expectations per platform

---

## Summary

### Key Principles

1. **Design for AOT from the start** - Retrofitting is harder
2. **Avoid reflection** - Use source generators instead
3. **Test early and often** - AOT issues appear late
4. **Measure size** - Optimize incrementally
5. **Use explicit types** - Don't rely on runtime type discovery
6. **Document limitations** - Be clear about what doesn't work

### Quick Reference: AOT-Compatible Patterns

| Pattern | Status | Alternative |
|---------|--------|-------------|
| Source generators | ✅ Supported | - |
| Static methods | ✅ Supported | - |
| Sealed classes | ✅ Supported | - |
| System.Text.Json (with source gen) | ✅ Supported | - |
| Embedded resources | ✅ Supported | Test names carefully |
| Serilog (console/file) | ✅ Supported | Avoid reflection-based sinks |
| Spectre.Console (basic) | ✅ Supported | Avoid advanced features |
| Reflection.Emit | ❌ Not supported | Use source generators |
| Dynamic types | ❌ Not supported | Use explicit types |
| Assembly.GetTypes() | ⚠️ Limited | Use explicit type lists |
| LINQ expressions | ⚠️ Limited | Use delegates |
| FSharp.SystemTextJson | ⚠️ Limited | Use source generators |

### Common Size Targets (Linux x64)

- **Minimal CLI tool**: 5-8 MB (AOT + trimming + size opts)
- **Feature-rich CLI**: 8-12 MB (AOT + trimming)
- **With rich UI**: 10-15 MB (AOT + Spectre.Console)

---

## References

- [Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Trim Self-Contained Deployments](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained)
- [Single-File Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Source Generation for JSON](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [AOT Warnings (IL2XXX, IL3XXX)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/)
- [.NET Size Optimization](https://devblogs.microsoft.com/dotnet/app-trimming-in-dotnet-5/)
- [AGENTS.md](../../AGENTS.md) - Project-wide agent guidance
