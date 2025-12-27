# Pluggable Pipeline Architecture - API Design

**Task**: Task 2.2 - Pipeline Architecture Design (Issue #320)
**Created**: 2025-12-26
**Related ADR**: ADR-026 - Pluggable Pipeline Architecture
**Status**: Proposed

## Table of Contents

1. [Overview](#overview)
2. [Core Types](#core-types)
3. [MorphirFile API](#morphirfile-api)
4. [MorphirProcessor API](#morphirprocessor-api)
5. [Plugin API](#plugin-api)
6. [Computation Expression Builders](#computation-expression-builders)
7. [Usage Examples](#usage-examples)
8. [Migration Guide](#migration-guide)

---

## 1. Overview

### Architecture

```
Input File → [Parser] → IR Tree → [Plugin Chain] → Modified IR → [Compiler] → Output File
                ↓                       ↓                              ↓
           MorphirFile          Diagnostic Messages          MorphirFile
```

### Key Components

| Component | Purpose | Language |
|-----------|---------|----------|
| `MorphirFile` | Diagnostic accumulation container | F# + C# |
| `MorphirProcessor` | Pipeline orchestrator | F# + C# |
| `Plugin` | Transformation unit | F# record + C# interface |
| `PipelineBuilder` | Computation expression | F# only |
| `TransformerBuilder` | Computation expression | F# only |
| `VisitorBuilder` | Computation expression | F# only |

---

## 2. Core Types

### 2.1 Message Types

#### F# Definition

```fsharp
namespace Morphir.IR

/// Severity level for diagnostic messages
type MessageSeverity =
    | Info      // Informational message
    | Warning   // Non-fatal issue
    | Error     // Fatal issue, pipeline continues
    | Fatal     // Fatal issue, pipeline should halt

/// Source position in original file
type SourcePosition = {
    Line: int
    Column: int
    Offset: int option
}

/// Source range (half-open: [start, end))
type SourceRange = {
    Start: SourcePosition
    End: SourcePosition
}

/// Diagnostic message
type MorphirMessage = {
    Severity: MessageSeverity
    Message: string
    Position: SourceRange option
    Source: string option      // e.g., "morphir-validate"
    RuleId: string option      // e.g., "no-undefined-types"
}
```

#### C# Definition

```csharp
namespace Morphir.IR;

/// <summary>
/// Severity level for diagnostic messages
/// </summary>
public enum MessageSeverity
{
    Info,     // Informational message
    Warning,  // Non-fatal issue
    Error,    // Fatal issue, pipeline continues
    Fatal     // Fatal issue, pipeline should halt
}

/// <summary>
/// Source position in original file
/// </summary>
public record SourcePosition(
    int Line,
    int Column,
    int? Offset = null
);

/// <summary>
/// Source range (half-open: [start, end))
/// </summary>
public record SourceRange(
    SourcePosition Start,
    SourcePosition End
);

/// <summary>
/// Diagnostic message
/// </summary>
public record MorphirMessage(
    MessageSeverity Severity,
    string Message,
    SourceRange? Position = null,
    string? Source = null,      // e.g., "morphir-validate"
    string? RuleId = null       // e.g., "no-undefined-types"
);
```

### 2.2 File Type

#### F# Definition

```fsharp
namespace Morphir.IR

open System.Collections.Immutable

/// Virtual file with diagnostic accumulation
type MorphirFile = {
    Content: IRNode option
    Path: string option
    History: string list
    Messages: MorphirMessage list
    Data: ImmutableDictionary<string, obj>
}
```

#### C# Definition

```csharp
namespace Morphir.IR;

using System.Collections.Immutable;

/// <summary>
/// Virtual file with diagnostic accumulation
/// </summary>
public record MorphirFile(
    IRNode? Content = null,
    string? Path = null,
    ImmutableList<string>? History = null,
    ImmutableList<MorphirMessage>? Messages = null,
    ImmutableDictionary<string, object>? Data = null
)
{
    public ImmutableList<string> History { get; init; } = History ?? ImmutableList<string>.Empty;
    public ImmutableList<MorphirMessage> Messages { get; init; } = Messages ?? ImmutableList<MorphirMessage>.Empty;
    public ImmutableDictionary<string, object> Data { get; init; } = Data ?? ImmutableDictionary<string, object>.Empty;
}
```

---

## 3. MorphirFile API

### 3.1 Constructors

#### F# API

```fsharp
module MorphirFile =
    /// Create empty file
    let empty : MorphirFile

    /// Create from path
    let fromPath (path: string) : MorphirFile

    /// Create from content
    let fromContent (content: IRNode) : MorphirFile

    /// Create from path and content
    let create (path: string option) (content: IRNode option) : MorphirFile
```

#### C# API

```csharp
public static class MorphirFileExtensions
{
    /// <summary>
    /// Create empty file
    /// </summary>
    public static MorphirFile Empty() => new();

    /// <summary>
    /// Create from path
    /// </summary>
    public static MorphirFile FromPath(string path) =>
        new(Path: path);

    /// <summary>
    /// Create from content
    /// </summary>
    public static MorphirFile FromContent(IRNode content) =>
        new(Content: content);
}
```

### 3.2 Diagnostic Methods

#### F# API

```fsharp
module MorphirFile =
    /// Add informational message
    let info (message: string) (file: MorphirFile) : MorphirFile

    /// Add warning with optional position
    let warn (message: string) (position: SourceRange option) (file: MorphirFile) : MorphirFile

    /// Add error with optional position
    let error (message: string) (position: SourceRange option) (file: MorphirFile) : MorphirFile

    /// Add fatal error with optional position
    let fail (message: string) (position: SourceRange option) (file: MorphirFile) : MorphirFile

    /// Add message with custom source/ruleId
    let message (severity: MessageSeverity) (msg: string) (position: SourceRange option)
                (source: string option) (ruleId: string option) (file: MorphirFile) : MorphirFile

    /// Check if file has errors
    let hasErrors (file: MorphirFile) : bool

    /// Check if file has fatal errors
    let hasFatals (file: MorphirFile) : bool

    /// Get all messages of severity
    let messagesOfSeverity (severity: MessageSeverity) (file: MorphirFile) : MorphirMessage list

    /// Get all errors
    let errors (file: MorphirFile) : MorphirMessage list

    /// Get all warnings
    let warnings (file: MorphirFile) : MorphirMessage list
```

#### C# API

```csharp
public static class MorphirFileExtensions
{
    /// <summary>
    /// Add informational message
    /// </summary>
    public static MorphirFile Info(this MorphirFile file, string message);

    /// <summary>
    /// Add warning with optional position
    /// </summary>
    public static MorphirFile Warn(this MorphirFile file, string message, SourceRange? position = null);

    /// <summary>
    /// Add error with optional position
    /// </summary>
    public static MorphirFile Error(this MorphirFile file, string message, SourceRange? position = null);

    /// <summary>
    /// Add fatal error with optional position
    /// </summary>
    public static MorphirFile Fail(this MorphirFile file, string message, SourceRange? position = null);

    /// <summary>
    /// Add message with custom source/ruleId
    /// </summary>
    public static MorphirFile Message(
        this MorphirFile file,
        MessageSeverity severity,
        string message,
        SourceRange? position = null,
        string? source = null,
        string? ruleId = null);

    /// <summary>
    /// Check if file has errors
    /// </summary>
    public static bool HasErrors(this MorphirFile file);

    /// <summary>
    /// Check if file has fatal errors
    /// </summary>
    public static bool HasFatals(this MorphirFile file);

    /// <summary>
    /// Get all messages of severity
    /// </summary>
    public static IEnumerable<MorphirMessage> MessagesOfSeverity(
        this MorphirFile file,
        MessageSeverity severity);

    /// <summary>
    /// Get all errors
    /// </summary>
    public static IEnumerable<MorphirMessage> Errors(this MorphirFile file);

    /// <summary>
    /// Get all warnings
    /// </summary>
    public static IEnumerable<MorphirMessage> Warnings(this MorphirFile file);
}
```

### 3.3 Data Methods

#### F# API

```fsharp
module MorphirFile =
    /// Set data value
    let setData (key: string) (value: obj) (file: MorphirFile) : MorphirFile

    /// Get data value
    let getData (key: string) (file: MorphirFile) : obj option

    /// Get typed data value
    let getDataAs<'T> (key: string) (file: MorphirFile) : 'T option

    /// Remove data value
    let removeData (key: string) (file: MorphirFile) : MorphirFile
```

#### C# API

```csharp
public static class MorphirFileExtensions
{
    /// <summary>
    /// Set data value
    /// </summary>
    public static MorphirFile SetData(this MorphirFile file, string key, object value);

    /// <summary>
    /// Get data value
    /// </summary>
    public static object? GetData(this MorphirFile file, string key);

    /// <summary>
    /// Get typed data value
    /// </summary>
    public static T? GetDataAs<T>(this MorphirFile file, string key) where T : class;

    /// <summary>
    /// Remove data value
    /// </summary>
    public static MorphirFile RemoveData(this MorphirFile file, string key);
}
```

---

## 4. MorphirProcessor API

### 4.1 Processor Type

#### F# Definition

```fsharp
namespace Morphir.IR

type Parser = MorphirFile -> Result<IRNode, string>
type Compiler = IRNode -> MorphirFile -> MorphirFile

type MorphirProcessor = {
    Parsers: Parser list
    Plugins: Plugin list
    Compilers: Compiler list
    Frozen: bool
    Data: ImmutableDictionary<string, obj>
}
```

#### C# Definition

```csharp
namespace Morphir.IR;

public delegate Result<IRNode, string> Parser(MorphirFile file);
public delegate MorphirFile Compiler(IRNode node, MorphirFile file);

public record MorphirProcessor(
    ImmutableList<Parser>? Parsers = null,
    ImmutableList<IPlugin>? Plugins = null,
    ImmutableList<Compiler>? Compilers = null,
    bool Frozen = false,
    ImmutableDictionary<string, object>? Data = null
)
{
    public ImmutableList<Parser> Parsers { get; init; } = Parsers ?? ImmutableList<Parser>.Empty;
    public ImmutableList<IPlugin> Plugins { get; init; } = Plugins ?? ImmutableList<IPlugin>.Empty;
    public ImmutableList<Compiler> Compilers { get; init; } = Compilers ?? ImmutableList<Compiler>.Empty;
    public ImmutableDictionary<string, object> Data { get; init; } = Data ?? ImmutableDictionary<string, object>.Empty;
}
```

### 4.2 Processor Methods

#### F# API

```fsharp
module MorphirProcessor =
    /// Create empty processor
    let empty : MorphirProcessor

    /// Add parser
    let parse (parser: Parser) (proc: MorphirProcessor) : MorphirProcessor

    /// Add plugin
    let plugin (plugin: Plugin) (proc: MorphirProcessor) : MorphirProcessor

    /// Add compiler
    let stringify (compiler: Compiler) (proc: MorphirProcessor) : MorphirProcessor

    /// Freeze processor (makes it immutable template)
    let freeze (proc: MorphirProcessor) : MorphirProcessor

    /// Check if processor is frozen
    let isFrozen (proc: MorphirProcessor) : bool

    /// Process file through pipeline
    let processFile (file: MorphirFile) (proc: MorphirProcessor) : MorphirFile

    /// Process file from path
    let processPath (path: string) (proc: MorphirProcessor) : MorphirFile
```

#### C# API

```csharp
public static class MorphirProcessorExtensions
{
    /// <summary>
    /// Create empty processor
    /// </summary>
    public static MorphirProcessor Empty() => new();

    /// <summary>
    /// Add parser
    /// </summary>
    public static MorphirProcessor Parse(this MorphirProcessor processor, Parser parser);

    /// <summary>
    /// Add plugin
    /// </summary>
    public static MorphirProcessor Plugin(this MorphirProcessor processor, IPlugin plugin);

    /// <summary>
    /// Add compiler
    /// </summary>
    public static MorphirProcessor Stringify(this MorphirProcessor processor, Compiler compiler);

    /// <summary>
    /// Freeze processor (makes it immutable template)
    /// </summary>
    public static MorphirProcessor Freeze(this MorphirProcessor processor);

    /// <summary>
    /// Check if processor is frozen
    /// </summary>
    public static bool IsFrozen(this MorphirProcessor processor);

    /// <summary>
    /// Process file through pipeline
    /// </summary>
    public static MorphirFile Process(this MorphirProcessor processor, MorphirFile file);

    /// <summary>
    /// Process file from path
    /// </summary>
    public static MorphirFile ProcessPath(this MorphirProcessor processor, string path);
}
```

---

## 5. Plugin API

### 5.1 Plugin Type

#### F# Definition

```fsharp
namespace Morphir.IR

type Plugin = {
    Name: string
    Configure: MorphirProcessor -> MorphirProcessor
    Transform: IRNode -> MorphirFile -> (IRNode option * MorphirFile)
}
```

#### C# Definition

```csharp
namespace Morphir.IR;

/// <summary>
/// Plugin interface for IR transformations
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Plugin name for diagnostics
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configure processor (add metadata, etc.)
    /// </summary>
    MorphirProcessor Configure(MorphirProcessor processor);

    /// <summary>
    /// Transform IR node, accumulate diagnostics
    /// </summary>
    (IRNode? Node, MorphirFile File) Transform(IRNode node, MorphirFile file);
}
```

### 5.2 Plugin Helpers

#### F# API

```fsharp
module Plugin =
    /// Create simple plugin (no configuration)
    let create (name: string) (transform: IRNode -> MorphirFile -> (IRNode option * MorphirFile)) : Plugin

    /// Create plugin with configuration
    let createConfigurable (name: string)
                           (configure: MorphirProcessor -> MorphirProcessor)
                           (transform: IRNode -> MorphirFile -> (IRNode option * MorphirFile)) : Plugin

    /// Create plugin that only modifies file (no transform)
    let createDiagnosticOnly (name: string) (diagnose: IRNode -> MorphirFile -> MorphirFile) : Plugin

    /// Create plugin that only transforms node (no diagnostics)
    let createTransformOnly (name: string) (transform: IRNode -> IRNode option) : Plugin
```

#### C# API

```csharp
public static class PluginHelpers
{
    /// <summary>
    /// Create simple plugin (no configuration)
    /// </summary>
    public static IPlugin Create(
        string name,
        Func<IRNode, MorphirFile, (IRNode? Node, MorphirFile File)> transform);

    /// <summary>
    /// Create plugin with configuration
    /// </summary>
    public static IPlugin CreateConfigurable(
        string name,
        Func<MorphirProcessor, MorphirProcessor> configure,
        Func<IRNode, MorphirFile, (IRNode? Node, MorphirFile File)> transform);

    /// <summary>
    /// Create plugin that only modifies file (no transform)
    /// </summary>
    public static IPlugin CreateDiagnosticOnly(
        string name,
        Func<IRNode, MorphirFile, MorphirFile> diagnose);

    /// <summary>
    /// Create plugin that only transforms node (no diagnostics)
    /// </summary>
    public static IPlugin CreateTransformOnly(
        string name,
        Func<IRNode, IRNode?> transform);
}
```

---

## 6. Computation Expression Builders

### 6.1 Pipeline Builder

**F# Only**:

```fsharp
type PipelineBuilder() =
    member _.Yield(_) = MorphirProcessor.empty

    [<CustomOperation("parse")>]
    member _.Parse(proc: MorphirProcessor, parser: Parser) =
        MorphirProcessor.parse parser proc

    [<CustomOperation("plugin")>]
    member _.Plugin(proc: MorphirProcessor, plugin: Plugin) =
        MorphirProcessor.plugin plugin proc

    [<CustomOperation("stringify")>]
    member _.Stringify(proc: MorphirProcessor, compiler: Compiler) =
        MorphirProcessor.stringify compiler proc

    [<CustomOperation("freeze")>]
    member _.Freeze(proc: MorphirProcessor) =
        MorphirProcessor.freeze proc

let pipeline = PipelineBuilder()
```

### 6.2 Transformer Builder

**F# Only**:

```fsharp
type TransformerBuilder() =
    member _.Bind(file: MorphirFile, f: MorphirFile -> MorphirFile) = f file
    member _.Return(value: 'a) = value
    member _.ReturnFrom(file: MorphirFile) = file
    member _.Zero() = MorphirFile.empty

    [<CustomOperation("info")>]
    member _.Info(file: MorphirFile, message: string) =
        MorphirFile.info message file

    [<CustomOperation("warn")>]
    member _.Warn(file: MorphirFile, message: string, ?pos: SourceRange) =
        MorphirFile.warn message pos file

    [<CustomOperation("error")>]
    member _.Error(file: MorphirFile, message: string, ?pos: SourceRange) =
        MorphirFile.error message pos file

let transformer = TransformerBuilder()
```

### 6.3 Visitor Builder

**F# Only**:

```fsharp
type VisitorRule<'Node> = {
    Pattern: 'Node -> bool
    Action: 'Node -> VisitorAction
}

type VisitorBuilder() =
    member _.Yield(_) = []

    member _.Run(rules: VisitorRule<IRNode> list) =
        fun (node: IRNode) ->
            rules
            |> List.tryPick (fun rule ->
                if rule.Pattern node then Some (rule.Action node)
                else None)
            |> Option.defaultValue VisitorAction.Continue

    [<CustomOperation("on")>]
    member _.On(rules, pattern, action) = (* ... *)

    [<CustomOperation("when")>]
    member _.When(rules, condition, action) = (* ... *)

let visitor = VisitorBuilder()
```

---

## 7. Usage Examples

### 7.1 Basic Pipeline

#### F# with Computation Expression

```fsharp
open Morphir.IR

// Define plugins
let validatePlugin = Plugin.create "validate" (fun node file ->
    // Validation logic
    Some node, file.Info("Validation complete")
)

let optimizePlugin = Plugin.create "optimize" (fun node file ->
    // Optimization logic
    Some (optimizedNode), file.Info("Optimization complete")
)

// Build pipeline
let irProcessor = pipeline {
    parse IR.JsonParser.parse
    plugin validatePlugin
    plugin optimizePlugin
    stringify IR.JsonSerializer.stringify
    freeze
}

// Process file
let result = irProcessor |> MorphirProcessor.processPath "input.json"

// Check for errors
if MorphirFile.hasErrors result then
    result
    |> MorphirFile.errors
    |> List.iter (printfn "%O")
else
    printfn "Success: %d warnings" (List.length (MorphirFile.warnings result))
```

#### C# with Fluent API

```csharp
using Morphir.IR;

// Define plugins
var validatePlugin = PluginHelpers.Create("validate", (node, file) =>
{
    // Validation logic
    return (node, file.Info("Validation complete"));
});

var optimizePlugin = PluginHelpers.Create("optimize", (node, file) =>
{
    // Optimization logic
    var optimized = Optimize(node);
    return (optimized, file.Info("Optimization complete"));
});

// Build pipeline
var irProcessor = MorphirProcessorExtensions.Empty()
    .Parse(IR.JsonParser.Parse)
    .Plugin(validatePlugin)
    .Plugin(optimizePlugin)
    .Stringify(IR.JsonSerializer.Stringify)
    .Freeze();

// Process file
var result = irProcessor.ProcessPath("input.json");

// Check for errors
if (result.HasErrors())
{
    foreach (var error in result.Errors())
    {
        Console.WriteLine(error);
    }
}
else
{
    Console.WriteLine($"Success: {result.Warnings().Count()} warnings");
}
```

### 7.2 Advanced: Transformer Builder

#### F# Only

```fsharp
let validateTypeReferences = transformer {
    info "Validating type references"

    // Collect all type references
    let! refs = collectTypeReferences

    // Validate each reference
    for ref in refs do
        let! exists = typeExists ref
        if not exists then
            error $"Type not found: {ref}" (getPosition ref)

    info "Type reference validation complete"
}
```

### 7.3 Advanced: Visitor Builder

#### F# Only

```fsharp
let collectVariables = visitor {
    on<Type.Variable> (fun (attr, name) ->
        variables.Add(name)
        VisitorAction.Continue)

    on<Type.Reference> (fun (attr, fqn, args) ->
        if isCached fqn then
            VisitorAction.Skip  // Skip cached references
        else
            VisitorAction.Continue)

    when isDeprecated (fun node ->
        warn $"Deprecated type: {node}"
        VisitorAction.Continue)
}
```

---

## 8. Migration Guide

### 8.1 From Result<T, Error> to MorphirFile

**Before**:
```fsharp
let validate (ir: IR) : Result<IR, string> =
    if isValid ir then
        Ok ir
    else
        Error "Invalid IR"
```

**After**:
```fsharp
let validatePlugin = Plugin.create "validate" (fun node file ->
    if isValid node then
        Some node, file.Info("Validation passed")
    else
        None, file.Error("Invalid IR", None)
)
```

### 8.2 From Function Composition to Pipeline

**Before**:
```fsharp
let processIR ir =
    ir
    |> validate
    |> Result.bind normalize
    |> Result.bind optimize
```

**After**:
```fsharp
let processor = pipeline {
    plugin validatePlugin
    plugin normalizePlugin
    plugin optimizePlugin
}

let result = processor |> MorphirProcessor.processFile file
```

### 8.3 Gradual Migration Strategy

1. **Phase 1**: Wrap existing functions as plugins
2. **Phase 2**: Replace Result<T,E> with MorphirFile in plugins
3. **Phase 3**: Adopt computation expressions for new code
4. **Phase 4**: Deprecate old APIs (with long deprecation period)

---

## Appendix A: Type Hierarchy

```
IRNode (base interface/type)
├── Type
│   ├── Variable
│   ├── Reference
│   ├── Function
│   ├── Tuple
│   └── Record
├── Value
│   ├── Literal
│   ├── Constructor
│   ├── Apply
│   └── Lambda
└── Definition
    ├── TypeAlias
    ├── CustomType
    └── ValueDef
```

## Appendix B: Error Codes

| Code | Severity | Description |
|------|----------|-------------|
| `IR001` | Error | Undefined type reference |
| `IR002` | Error | Type arity mismatch |
| `IR003` | Warning | Deprecated type usage |
| `IR004` | Error | Circular type dependency |
| `IR005` | Warning | Unused type parameter |

---

**Status**: Proposed
**Next Steps**: Review with team, implement Phase 1 (MorphirFile)
**Estimated Effort**: 5 weeks (5 phases)
