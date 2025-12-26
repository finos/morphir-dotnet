# Unified.js to .NET Adaptation Knowledge Base

**Task**: Task 2.1 - Unified.js Architecture Research (Issue #316)
**Created**: 2025-12-26
**Purpose**: Concrete adaptation strategies for applying unified.js patterns to morphir-dotnet IR transformation pipeline

## Table of Contents

1. [Adaptation Strategy](#adaptation-strategy)
2. [Processor Pattern in .NET](#processor-pattern-in-net)
3. [Plugin Architecture](#plugin-architecture)
4. [MorphirFile Pattern](#morphirfile-pattern)
5. [Visitor Utilities](#visitor-utilities)
6. [Computation Expression Builders](#computation-expression-builders)
7. [Bridge Plugins](#bridge-plugins)
8. [Decision Trees](#decision-trees)
9. [Implementation Roadmap](#implementation-roadmap)

---

## 1. Adaptation Strategy

### 1.1 Core Principles

**Type Safety First**:
- JavaScript dynamic typing → F#/C# static typing
- String `type` fields → Discriminated unions / sealed records
- Runtime checks → Compile-time verification

**Idiomatic .NET**:
- JavaScript prototypes → F# modules / C# extension methods
- Callback functions → Delegates / Func<T>
- Mutable state → Immutable records with `with` expressions

**Functional-First (F#)**:
- JavaScript objects → F# records
- Class-based → Function-based
- Middleware chains → Computation expressions

**Object-Oriented (C#)**:
- Interfaces for extensibility
- LINQ for collection operations
- Records with init-only properties

### 1.2 Pattern Mapping

| Unified.js Pattern | F# Adaptation | C# Adaptation |
|-------------------|---------------|---------------|
| **Processor** | Record with plugin list | Immutable class with builder |
| **Plugin (attacher)** | Record with `Configure` field | Interface `IPlugin` |
| **Transformer** | Function `MorphirFile -> MorphirFile` | Method `Transform(file)` |
| **VFile** | Record `MorphirFile` | Record `MorphirFile` |
| **Message** | Record `MorphirMessage` | Record `MorphirMessage` |
| **Visitor** | Module with functions | Static class or extension |
| **Control Flow** | Discriminated union | Enum or sealed records |

### 1.3 What to Adopt

**Architectural Patterns**:
- ✅ Parse → Transform → Stringify pipeline
- ✅ Processor with plugin registry
- ✅ VFile pattern for diagnostics
- ✅ Visitor pattern with control flow
- ✅ Bridge pattern for cross-format transformation

**Design Decisions**:
- ✅ Separate position tracking from tree structure
- ✅ Optional position for generated nodes
- ✅ Message accumulation across plugins
- ✅ Immutable updates with history tracking

### 1.4 What to Adapt

**JavaScript-Specific Patterns**:
- ⚠️ `this` context in attachers → Explicit parameters
- ⚠️ Prototype mutation → Extension methods / modules
- ⚠️ Dynamic typing → Generic type parameters
- ⚠️ Callback-based async → async/await

**API Surface**:
- ⚠️ `.use(plugin, options)` → `.Use(plugin)` with options in plugin
- ⚠️ String type tests → Type-safe pattern matching
- ⚠️ Untyped `data` object → Typed dictionary or discriminated union

### 1.5 What to Avoid

**Incompatible Patterns**:
- ❌ Runtime type checking (use static types)
- ❌ Untyped value fields (use typed properties)
- ❌ Global mutable state (use immutable records)
- ❌ Implicit context passing (use explicit parameters)

---

## 2. Processor Pattern in .NET

### 2.1 F# Implementation

```fsharp
type ProcessorPhase =
    | Parse
    | Transform
    | Stringify

type ProcessorContext = {
    Data: Map<string, obj>
}

type Plugin = {
    Name: string
    Phase: ProcessorPhase
    Configure: ProcessorContext -> unit
    Transform: MorphirFile -> MorphirFile
}

type MorphirProcessor = {
    Plugins: Plugin list
    Context: ProcessorContext
    IsFrozen: bool
}
with
    static member Create() = {
        Plugins = []
        Context = { Data = Map.empty }
        IsFrozen = false
    }

    member this.Use(plugin: Plugin) =
        if this.IsFrozen then
            failwith "Cannot add plugin to frozen processor"

        // Configure plugin
        plugin.Configure this.Context

        { this with Plugins = this.Plugins @ [plugin] }

    member this.Freeze() =
        { this with IsFrozen = true }

    member this.Process(file: MorphirFile) : MorphirFile =
        if not this.IsFrozen then
            failwith "Processor must be frozen before processing"

        this.Plugins
        |> List.fold (fun f plugin ->
            try
                plugin.Transform f
            with ex ->
                f |> MorphirFile.error
                    (sprintf "[%s] %s" plugin.Name ex.Message)
                    None
                    (Some plugin.Name)
        ) file
```

**Usage**:
```fsharp
let processor =
    MorphirProcessor.Create()
        .Use(parsePlugin)
        .Use(typeInferencePlugin)
        .Use(optimizationPlugin)
        .Use(codegenPlugin)
        .Freeze()

let result = processor.Process(inputFile)
```

### 2.2 C# Implementation

```csharp
public enum ProcessorPhase { Parse, Transform, Stringify }

public interface IPlugin
{
    string Name { get; }
    ProcessorPhase Phase { get; }
    void Configure(ProcessorContext context);
    MorphirFile Transform(MorphirFile file);
}

public class ProcessorContext
{
    public ImmutableDictionary<string, object> Data { get; init; }
        = ImmutableDictionary<string, object>.Empty;

    public ProcessorContext WithData(string key, object value) =>
        this with { Data = Data.SetItem(key, value) };
}

public class MorphirProcessor
{
    private readonly ImmutableList<IPlugin> _plugins;
    private readonly ProcessorContext _context;
    private readonly bool _isFrozen;

    private MorphirProcessor(
        ImmutableList<IPlugin> plugins,
        ProcessorContext context,
        bool isFrozen)
    {
        _plugins = plugins;
        _context = context;
        _isFrozen = isFrozen;
    }

    public static MorphirProcessor Create() =>
        new(
            ImmutableList<IPlugin>.Empty,
            new ProcessorContext(),
            isFrozen: false
        );

    public MorphirProcessor Use(IPlugin plugin)
    {
        if (_isFrozen)
            throw new InvalidOperationException("Cannot add plugin to frozen processor");

        plugin.Configure(_context);

        return new MorphirProcessor(
            _plugins.Add(plugin),
            _context,
            _isFrozen
        );
    }

    public MorphirProcessor Freeze() =>
        new(_plugins, _context, isFrozen: true);

    public MorphirFile Process(MorphirFile file)
    {
        if (!_isFrozen)
            throw new InvalidOperationException("Processor must be frozen before processing");

        return _plugins.Aggregate(file, (f, plugin) =>
        {
            try
            {
                return plugin.Transform(f);
            }
            catch (Exception ex)
            {
                return f.Error(
                    $"[{plugin.Name}] {ex.Message}",
                    position: null,
                    source: plugin.Name
                );
            }
        });
    }
}
```

**Usage**:
```csharp
var processor = MorphirProcessor.Create()
    .Use(new ParsePlugin())
    .Use(new TypeInferencePlugin())
    .Use(new OptimizationPlugin())
    .Use(new CodegenPlugin())
    .Freeze();

var result = processor.Process(inputFile);
```

### 2.3 Processor Inheritance

**F#**:
```fsharp
let baseProcessor =
    MorphirProcessor.Create()
        .Use(parsePlugin)
        .Use(typeInferencePlugin)
        .Freeze()

// Create specialized processors
let csharpProcessor =
    baseProcessor  // Call to create unfrozen copy
        .Use(csharpCodegenPlugin)
        .Freeze()

let scalaProcessor =
    baseProcessor
        .Use(scalaCodegenPlugin)
        .Freeze()
```

**C#**:
```csharp
var baseProcessor = MorphirProcessor.Create()
    .Use(new ParsePlugin())
    .Use(new TypeInferencePlugin())
    .Freeze();

// Inherit by calling frozen processor (creates copy)
var csharpProcessor = baseProcessor.Clone()  // New method
    .Use(new CSharpCodegenPlugin())
    .Freeze();

var scalaProcessor = baseProcessor.Clone()
    .Use(new ScalaCodegenPlugin())
    .Freeze();
```

---

## 3. Plugin Architecture

### 3.1 F# Plugin Examples

**Type Inference Plugin**:
```fsharp
let typeInferencePlugin = {
    Name = "type-inference"
    Phase = ProcessorPhase.Transform
    Configure = fun ctx ->
        // Configure type environment
        ()
    Transform = fun file ->
        match file.Content with
        | None -> file
        | Some ir ->
            try
                let inferredIR = TypeInference.infer ir
                { file with Content = Some inferredIR }
            with ex ->
                file |> MorphirFile.error ex.Message None (Some "type-inference")
}
```

**Optimization Plugin**:
```fsharp
let optimizationPlugin = {
    Name = "optimization"
    Phase = ProcessorPhase.Transform
    Configure = fun ctx -> ()
    Transform = fun file ->
        match file.Content with
        | None -> file
        | Some ir ->
            let optimizedIR =
                ir
                |> Optimizer.inlineConstants
                |> Optimizer.eliminateDeadCode
                |> Optimizer.simplifyExpressions

            file
            |> MorphirFile.info "Applied optimizations" None (Some "optimization")
            |> fun f -> { f with Content = Some optimizedIR }
}
```

**Validation Plugin**:
```fsharp
let validationPlugin = {
    Name = "validation"
    Phase = ProcessorPhase.Transform
    Configure = fun ctx -> ()
    Transform = fun file ->
        match file.Content with
        | None -> file
        | Some ir ->
            let mutable updatedFile = file

            IR.visit
                (fun node ->
                    match node with
                    | Type.Variable(pos, name) when not (isValidName name) ->
                        updatedFile <- updatedFile
                            |> MorphirFile.error
                                (sprintf "Invalid type variable: %s" (Name.toString name))
                                (Some pos)
                                (Some "validation:type-variable")
                    | _ -> ()
                )
                ir

            updatedFile
}
```

### 3.2 C# Plugin Examples

**Type Inference Plugin**:
```csharp
public class TypeInferencePlugin : IPlugin
{
    public string Name => "type-inference";
    public ProcessorPhase Phase => ProcessorPhase.Transform;

    public void Configure(ProcessorContext context)
    {
        // Configure type environment
    }

    public MorphirFile Transform(MorphirFile file)
    {
        if (file.Content == null)
            return file;

        try
        {
            var inferredIR = TypeInference.Infer(file.Content);
            return file with { Content = inferredIR };
        }
        catch (Exception ex)
        {
            return file.Error(ex.Message, source: Name);
        }
    }
}
```

**Optimization Plugin**:
```csharp
public class OptimizationPlugin : IPlugin
{
    public string Name => "optimization";
    public ProcessorPhase Phase => ProcessorPhase.Transform;

    public void Configure(ProcessorContext context) { }

    public MorphirFile Transform(MorphirFile file)
    {
        if (file.Content == null)
            return file;

        var optimizedIR = file.Content
            .InlineConstants()
            .EliminateDeadCode()
            .SimplifyExpressions();

        return file
            .Info("Applied optimizations", source: Name)
            .With(content: optimizedIR);
    }
}
```

### 3.3 Plugin Composition

**Combining Plugins**:
```fsharp
let combinedPlugin = {
    Name = "combined-validation"
    Phase = ProcessorPhase.Transform
    Configure = fun ctx -> ()
    Transform = fun file ->
        file
        |> typeValidationPlugin.Transform
        |> nameValidationPlugin.Transform
        |> structureValidationPlugin.Transform
}
```

**Conditional Plugin**:
```fsharp
let conditionalOptimization = {
    Name = "conditional-optimization"
    Phase = ProcessorPhase.Transform
    Configure = fun ctx ->
        ctx.Data <- ctx.Data.Add("optimizationLevel", 2)
    Transform = fun file ->
        match file.Data.TryFind("optimizationLevel") with
        | Some (:? int as level) when level > 1 ->
            optimizationPlugin.Transform file
        | _ ->
            file
}
```

---

## 4. MorphirFile Pattern

### 4.1 Complete F# Implementation

```fsharp
type SourcePosition = {
    Line: int
    Column: int
    Offset: int option
}

type SourceRange = {
    Start: SourcePosition
    End: SourcePosition
}

type MessageSeverity =
    | Info
    | Warning
    | Error

type MorphirMessage = {
    Reason: string
    Severity: MessageSeverity
    Position: SourceRange option
    Source: string option
    RuleId: string option
    Note: string option
}

type MorphirFile = {
    Content: IR option
    Cwd: string
    Path: string option
    History: string list
    Messages: MorphirMessage list
    Data: Map<string, obj>
}

module MorphirFile =
    let create (content: IR option) = {
        Content = content
        Cwd = System.IO.Directory.GetCurrentDirectory()
        Path = None
        History = []
        Messages = []
        Data = Map.empty
    }

    let withPath path file =
        { file with
            Path = Some path
            History = file.History @ [path]
        }

    let withContent content file =
        { file with Content = Some content }

    let withData key value file =
        { file with Data = file.Data.Add(key, value) }

    let message reason severity position source ruleId note file =
        let msg = {
            Reason = reason
            Severity = severity
            Position = position
            Source = source
            RuleId = ruleId
            Note = note
        }
        { file with Messages = file.Messages @ [msg] }

    let info reason position source file =
        message reason Info position source None None file

    let warn reason position source file =
        message reason Warning position source None None file

    let error reason position source file =
        message reason Error position source None None file

    let fail reason position source file =
        let updated = error reason position source file
        failwithf "Error: %s" reason

    let hasErrors file =
        file.Messages |> List.exists (fun m -> m.Severity = Error)

    let hasWarnings file =
        file.Messages |> List.exists (fun m -> m.Severity = Warning)
```

### 4.2 Complete C# Implementation

```csharp
public record SourcePosition(int Line, int Column, int? Offset = null);

public record SourceRange(SourcePosition Start, SourcePosition End);

public enum MessageSeverity { Info, Warning, Error }

public record MorphirMessage(
    string Reason,
    MessageSeverity Severity,
    SourceRange? Position = null,
    string? Source = null,
    string? RuleId = null,
    string? Note = null
);

public record MorphirFile(
    IR? Content,
    string Cwd,
    string? Path,
    ImmutableList<string> History,
    ImmutableList<MorphirMessage> Messages,
    ImmutableDictionary<string, object> Data
)
{
    public static MorphirFile Create(IR? content = null) =>
        new(
            Content: content,
            Cwd: Directory.GetCurrentDirectory(),
            Path: null,
            History: ImmutableList<string>.Empty,
            Messages: ImmutableList<MorphirMessage>.Empty,
            Data: ImmutableDictionary<string, object>.Empty
        );

    public MorphirFile WithPath(string path) =>
        this with
        {
            Path = path,
            History = History.Add(path)
        };

    public MorphirFile WithContent(IR content) =>
        this with { Content = content };

    public MorphirFile WithData(string key, object value) =>
        this with { Data = Data.SetItem(key, value) };

    private MorphirFile AddMessage(MorphirMessage message) =>
        this with { Messages = Messages.Add(message) };

    public MorphirFile Message(
        string reason,
        MessageSeverity severity,
        SourceRange? position = null,
        string? source = null,
        string? ruleId = null,
        string? note = null) =>
        AddMessage(new MorphirMessage(reason, severity, position, source, ruleId, note));

    public MorphirFile Info(string reason, SourceRange? position = null, string? source = null) =>
        Message(reason, MessageSeverity.Info, position, source);

    public MorphirFile Warn(string reason, SourceRange? position = null, string? source = null) =>
        Message(reason, MessageSeverity.Warning, position, source);

    public MorphirFile Error(string reason, SourceRange? position = null, string? source = null) =>
        Message(reason, MessageSeverity.Error, position, source);

    public MorphirFile Fail(string reason, SourceRange? position = null, string? source = null)
    {
        var updated = Error(reason, position, source);
        throw new InvalidOperationException(reason);
    }

    public bool HasErrors =>
        Messages.Any(m => m.Severity == MessageSeverity.Error);

    public bool HasWarnings =>
        Messages.Any(m => m.Severity == MessageSeverity.Warning);
}
```

---

## 5. Visitor Utilities

### 5.1 F# Visitor with Control Flow

```fsharp
type VisitorAction =
    | Continue
    | Skip
    | Exit

type VisitorResult =
    | Action of VisitorAction
    | ActionWithIndex of VisitorAction * int

exception ExitTraversal

module IR =
    let rec visit
        (test: IR -> bool)
        (visitor: IR -> int option -> IR option -> VisitorResult)
        (tree: IR)
        : unit =

        let rec visitNode (node: IR) (index: int option) (parent: IR option) =
            if test node then
                match visitor node index parent with
                | Action Continue -> visitChildren node
                | Action Skip -> ()
                | Action Exit -> raise ExitTraversal
                | ActionWithIndex(Continue, newIndex) ->
                    // Continue from new index
                    visitChildren node
                | ActionWithIndex(Skip, _) -> ()
                | ActionWithIndex(Exit, _) -> raise ExitTraversal
            else
                visitChildren node

        and visitChildren (parent: IR) =
            match parent with
            | Type.Tuple(_, elements) ->
                elements |> List.iteri (fun i child ->
                    visitNode child (Some i) (Some parent))

            | Type.Function(_, param, ret) ->
                visitNode param None (Some parent)
                visitNode ret None (Some parent)

            | Value.Apply(_, func, arg) ->
                visitNode func None (Some parent)
                visitNode arg None (Some parent)

            | _ -> ()

        try
            visitNode tree None None
        with
        | ExitTraversal -> ()
```

**Usage**:
```fsharp
// Find all type variables
let variables = ResizeArray<Name>()
IR.visit
    (function | Type.Variable _ -> true | _ -> false)
    (fun node _ _ ->
        match node with
        | Type.Variable(_, name) ->
            variables.Add(name)
            Action Continue
        | _ -> Action Continue)
    myType
```

### 5.2 C# Visitor Implementation

```csharp
public enum VisitorAction { Continue, Skip, Exit }

public abstract record VisitorResult
{
    public sealed record Action(VisitorAction Value) : VisitorResult;
    public sealed record ActionWithIndex(VisitorAction Value, int Index) : VisitorResult;
}

public static class IRVisitor
{
    public delegate VisitorResult Visitor(IR node, int? index, IR? parent);

    public static void Visit(
        IR tree,
        Func<IR, bool> test,
        Visitor visitor)
    {
        void VisitNode(IR node, int? index, IR? parent)
        {
            if (!test(node))
            {
                VisitChildren(node);
                return;
            }

            var result = visitor(node, index, parent);
            switch (result)
            {
                case VisitorResult.Action { Value: VisitorAction.Continue }:
                    VisitChildren(node);
                    break;

                case VisitorResult.Action { Value: VisitorAction.Skip }:
                    break;

                case VisitorResult.Action { Value: VisitorAction.Exit }:
                    throw new ExitTraversalException();

                case VisitorResult.ActionWithIndex { Value: VisitorAction.Continue }:
                    VisitChildren(node);
                    break;

                case VisitorResult.ActionWithIndex { Value: VisitorAction.Skip }:
                    break;

                case VisitorResult.ActionWithIndex { Value: VisitorAction.Exit }:
                    throw new ExitTraversalException();
            }
        }

        void VisitChildren(IR parent)
        {
            switch (parent)
            {
                case Type.Tuple tuple:
                    for (int i = 0; i < tuple.Elements.Count; i++)
                        VisitNode(tuple.Elements[i], i, parent);
                    break;

                case Type.Function func:
                    VisitNode(func.Parameter, null, parent);
                    VisitNode(func.Return, null, parent);
                    break;

                case Value.Apply apply:
                    VisitNode(apply.Function, null, parent);
                    VisitNode(apply.Argument, null, parent);
                    break;
            }
        }

        try
        {
            VisitNode(tree, null, null);
        }
        catch (ExitTraversalException)
        {
            // Expected exit
        }
    }

    private class ExitTraversalException : Exception { }
}
```

**Usage**:
```csharp
var variables = new List<Name>();
IRVisitor.Visit(
    myType,
    test: node => node is Type.Variable,
    visitor: (node, index, parent) =>
    {
        if (node is Type.Variable { Name: var name })
            variables.Add(name);
        return new VisitorResult.Action(VisitorAction.Continue);
    }
);
```

---

## 6. Computation Expression Builders

### 6.1 Overview

F# computation expressions provide an ergonomic, declarative API for the unified.js pipeline pattern. This section explores three key builders that make the pipeline fluent and type-safe.

**Benefits**:
- **Declarative**: Express intent, not implementation details
- **Type-safe**: Compiler-verified transformations
- **Composable**: Nest and combine builders
- **Familiar**: Standard F# idiom for complex workflows

**Key Builders**:
1. **Pipeline Builder**: Fluent processor construction
2. **Transformer Builder**: Diagnostic-aware transformations
3. **Visitor Builder**: Declarative tree traversal

### 6.2 Pipeline Builder

**Purpose**: Fluent construction of MorphirProcessor pipelines.

**Implementation**:

```fsharp
type PipelineBuilder() =
    member _.Yield(_) = MorphirProcessor.empty

    [<CustomOperation("parse")>]
    member _.Parse(proc: MorphirProcessor, parser: Parser) =
        { proc with Parsers = proc.Parsers @ [parser] }

    [<CustomOperation("use")>]
    member _.Use(proc: MorphirProcessor, plugin: Plugin) =
        { proc with Plugins = proc.Plugins @ [plugin] }

    [<CustomOperation("stringify")>]
    member _.Stringify(proc: MorphirProcessor, compiler: Compiler) =
        { proc with Compilers = proc.Compilers @ [compiler] }

    [<CustomOperation("freeze")>]
    member _.Freeze(proc: MorphirProcessor) =
        { proc with Frozen = true }

let pipeline = PipelineBuilder()
```

**Usage**:

```fsharp
// Define a complete IR transformation pipeline
let irProcessor = pipeline {
    parse irJsonParser
    use validateIRPlugin
    use normalizeTypesPlugin
    use optimizePlugin
    stringify irJsonSerializer
    freeze
}

// Create variant with additional plugins
let enhancedProcessor = pipeline {
    parse irJsonParser
    use validateIRPlugin
    use normalizeTypesPlugin
    use optimizePlugin
    use inlinePlugin  // Additional transformation
    stringify irJsonSerializer
}

// Execute pipeline
let result = irProcessor.Process(inputFile)
```

**Compared to Direct API**:

```fsharp
// Without CE - imperative
let processor =
    MorphirProcessor.empty
    |> MorphirProcessor.parse irJsonParser
    |> MorphirProcessor.use validateIRPlugin
    |> MorphirProcessor.use normalizeTypesPlugin
    |> MorphirProcessor.use optimizePlugin
    |> MorphirProcessor.stringify irJsonSerializer
    |> MorphirProcessor.freeze

// With CE - declarative
let processor = pipeline {
    parse irJsonParser
    use validateIRPlugin
    use normalizeTypesPlugin
    use optimizePlugin
    stringify irJsonSerializer
    freeze
}
```

### 6.3 Transformer Builder

**Purpose**: Build transformations that accumulate diagnostics in MorphirFile.

**Implementation**:

```fsharp
type TransformerBuilder() =
    member _.Bind(file: MorphirFile, f: MorphirFile -> MorphirFile) = f file
    member _.Return(value: 'a) = value
    member _.ReturnFrom(file: MorphirFile) = file
    member _.Zero() = MorphirFile.empty

    // Custom operations for diagnostics
    [<CustomOperation("info")>]
    member _.Info(file: MorphirFile, message: string) =
        file.Info(message)

    [<CustomOperation("warn")>]
    member _.Warn(file: MorphirFile, message: string, ?pos: SourceRange) =
        match pos with
        | Some p -> file.Warn(message, p)
        | None -> file.Warn(message)

    [<CustomOperation("error")>]
    member _.Error(file: MorphirFile, message: string, ?pos: SourceRange) =
        match pos with
        | Some p -> file.Error(message, p)
        | None -> file.Error(message)

    [<CustomOperation("transform")>]
    member _.Transform(file: MorphirFile, f: IRNode -> IRNode option) =
        match file.Content with
        | Some node ->
            match f node with
            | Some transformed -> { file with Content = Some transformed }
            | None -> file
        | None -> file

let transformer = TransformerBuilder()
```

**Usage**:

```fsharp
// Define a type reference validator with diagnostics
let validateTypeReference (fqn: FQName) (args: Type list) (file: MorphirFile) =
    transformer {
        info $"Validating type reference: {fqn}"

        // Check if type exists
        let! typeExists = checkTypeExists fqn
        if not typeExists then
            error $"Type not found: {fqn}" (getPosition fqn)

        // Validate argument count
        let! expectedArity = getTypeArity fqn
        if args.Length <> expectedArity then
            error $"Type {fqn} expects {expectedArity} args, got {args.Length}"

        // Validate each argument
        for arg in args do
            do! validateType arg

        info $"Type reference valid: {fqn}"
    }

// Use in plugin
let typeValidatorPlugin = {
    Name = "type-validator"
    Configure = id
    Transform = fun node file ->
        match node with
        | Type.Reference(attr, fqn, args) ->
            let file' = validateTypeReference fqn args file
            Some node, file'
        | _ -> Some node, file
}
```

**Compared to Manual Threading**:

```fsharp
// Without CE - manual threading
let validateTypeReference fqn args file =
    let file1 = file.Info($"Validating type reference: {fqn}")
    let typeExists = checkTypeExists fqn
    let file2 =
        if not typeExists then
            file1.Error($"Type not found: {fqn}", getPosition fqn)
        else
            file1
    let expectedArity = getTypeArity fqn
    let file3 =
        if args.Length <> expectedArity then
            file2.Error($"Type {fqn} expects {expectedArity} args, got {args.Length}")
        else
            file2
    let file4 = args |> List.fold (fun f arg -> validateType arg f) file3
    file4.Info($"Type reference valid: {fqn}")

// With CE - automatic threading
let validateTypeReference fqn args file =
    transformer {
        info $"Validating type reference: {fqn}"
        // ... (same as above, but file threading is automatic)
    }
```

### 6.4 Visitor Builder

**Purpose**: Declarative tree traversal with type-safe pattern matching and control flow.

**Implementation**:

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
                if rule.Pattern node then
                    Some (rule.Action node)
                else
                    None)
            |> Option.defaultValue VisitorAction.Continue

    [<CustomOperation("on")>]
    member _.On(rules: VisitorRule<IRNode> list, pattern: 'a -> bool, action: 'a -> VisitorAction) =
        let rule = {
            Pattern = fun node ->
                match node with
                | :? 'a as n -> pattern n
                | _ -> false
            Action = fun node ->
                match node with
                | :? 'a as n -> action n
                | _ -> VisitorAction.Continue
        }
        rules @ [rule]

    [<CustomOperation("when")>]
    member _.When(rules: VisitorRule<IRNode> list, condition: IRNode -> bool, action: IRNode -> VisitorAction) =
        let rule = {
            Pattern = condition
            Action = action
        }
        rules @ [rule]

let visitor = VisitorBuilder()
```

**Usage**:

```fsharp
// Define a visitor that collects type variables and skips cached references
let collectTypeVariables = visitor {
    on<Type.Variable> (fun (attr, name) ->
        variables.Add(name)
        VisitorAction.Continue)

    on<Type.Reference> (fun (attr, fqn, args) ->
        if isCached fqn then
            VisitorAction.Skip  // Skip children
        else
            VisitorAction.Continue)

    on<Type.Function> (fun (attr, input, output) ->
        // Custom processing
        processFunction input output
        VisitorAction.Continue)

    when (isDeprecated) (fun node ->
        warn $"Deprecated type: {node}"
        VisitorAction.Continue)
}

// Apply visitor to tree
let result = IRVisitor.visit collectTypeVariables irTree
```

**Compared to Manual Pattern Matching**:

```fsharp
// Without CE - manual recursion
let rec collectTypeVariables node =
    match node with
    | Type.Variable(attr, name) ->
        variables.Add(name)
        VisitorAction.Continue
    | Type.Reference(attr, fqn, args) ->
        if isCached fqn then
            VisitorAction.Skip
        else
            VisitorAction.Continue
    | Type.Function(attr, input, output) ->
        processFunction input output
        VisitorAction.Continue
    | _ when isDeprecated node ->
        warn $"Deprecated type: {node}"
        VisitorAction.Continue
    | _ -> VisitorAction.Continue

// With CE - declarative rules
let collectTypeVariables = visitor {
    on<Type.Variable> (fun (attr, name) ->
        variables.Add(name)
        VisitorAction.Continue)
    // ... (more readable, less boilerplate)
}
```

### 6.5 Combined Example

**Complete pipeline using all three builders**:

```fsharp
// Define transformation plugin with transformer CE
let optimizeTypesPlugin = {
    Name = "optimize-types"
    Configure = id
    Transform = fun node file ->
        transformer {
            info "Optimizing type definitions"

            // Apply visitor CE for optimization
            let! optimized = visitor {
                on<Type.Function> (fun (attr, input, output) ->
                    // Inline simple functions
                    if isSimple input && isSimple output then
                        transform (inlineFunction input output)
                        VisitorAction.Skip
                    else
                        VisitorAction.Continue)

                on<Type.Tuple> (fun (attr, items) ->
                    // Flatten nested tuples
                    if hasNestedTuples items then
                        transform (flattenTuples items)
                        VisitorAction.Continue
                    else
                        VisitorAction.Continue)
            }

            info "Type optimization complete"
            return optimized
        }
}

// Build complete pipeline with pipeline CE
let irPipeline = pipeline {
    parse irJsonParser

    // Validation phase
    use validateIRPlugin
    use typeCheckerPlugin

    // Optimization phase
    use normalizeTypesPlugin
    use optimizeTypesPlugin
    use inlinePlugin

    // Output phase
    stringify irJsonSerializer
    freeze
}

// Execute pipeline
let processIR inputPath outputPath =
    let file = MorphirFile.fromPath inputPath
    let result = irPipeline.Process(file)

    match result.Messages |> List.filter (fun m -> m.Severity = Error) with
    | [] ->
        // No errors, write output
        MorphirFile.writeTo outputPath result
        Ok result
    | errors ->
        // Report errors
        errors |> List.iter (printfn "%O")
        Error errors
```

### 6.6 Advanced Patterns

**6.6.1 Nested Transformers**:

```fsharp
let complexTransform node file =
    transformer {
        info "Starting complex transformation"

        // Nested transformer for sub-tree
        let! subtree = transformer {
            info "Processing subtree"
            transform optimizeSubtree
            warn "Subtree optimization applied"
        }

        // Continue with main transformation
        transform (combineResults subtree)
        info "Complex transformation complete"
    }
```

**6.6.2 Conditional Visitors**:

```fsharp
let conditionalVisitor enableOptimizations = visitor {
    // Always collect variables
    on<Type.Variable> (fun (attr, name) ->
        collectVariable name
        VisitorAction.Continue)

    // Conditional optimization
    if enableOptimizations then
        on<Type.Function> (fun (attr, input, output) ->
            optimizeFunction input output
            VisitorAction.Continue)

    // Always warn on deprecated
    when isDeprecated (fun node ->
        warn $"Deprecated: {node}"
        VisitorAction.Continue)
}
```

**6.6.3 Pipeline Composition**:

```fsharp
// Base pipeline
let basePipeline = pipeline {
    parse irJsonParser
    use validateIRPlugin
    stringify irJsonSerializer
}

// Enhanced pipeline (adds to frozen base)
let enhancedPipeline = pipeline {
    parse irJsonParser
    use validateIRPlugin
    use optimizePlugin  // Additional step
    stringify irJsonSerializer
}

// Production pipeline (different output)
let productionPipeline = pipeline {
    parse irJsonParser
    use validateIRPlugin
    use optimizePlugin
    stringify irBinarySerializer  // Different serializer
}
```

### 6.7 Benefits Summary

**Type Safety**:
- Compiler verifies all operations
- No runtime type errors
- IntelliSense support

**Readability**:
- Declarative intent
- Less boilerplate
- Clear data flow

**Composability**:
- Nest builders
- Combine pipelines
- Reuse rules

**Maintainability**:
- Centralized logic
- Easy to extend
- Clear separation of concerns

### 6.8 Implementation Priority

**Phase 1** (Week 2): Pipeline Builder
- Most impactful for API ergonomics
- Simplifies common use case
- Foundation for other builders

**Phase 2** (Week 3): Transformer Builder
- Improves diagnostic threading
- Reduces boilerplate significantly
- Builds on Phase 1

**Phase 3** (Week 4): Visitor Builder
- Advanced pattern
- Most complex implementation
- Highest payoff for complex traversals

---

## 7. Bridge Plugins

### 7.1 IR Version Migration

**V2 to V3 Bridge**:
```fsharp
let v2ToV3Bridge = {
    Name = "v2-to-v3-bridge"
    Phase = ProcessorPhase.Transform
    Configure = fun ctx -> ()
    Transform = fun file ->
        match file.Content with
        | None -> file
        | Some (:? IRv2 as v2IR) ->
            try
                let v3IR = IRMigration.migrateV2ToV3 v2IR
                file
                |> MorphirFile.info "Migrated IR v2 → v3" None (Some "v2-to-v3-bridge")
                |> fun f -> { f with Content = Some (v3IR :> IR) }
            with ex ->
                file |> MorphirFile.error
                    (sprintf "Migration failed: %s" ex.Message)
                    None
                    (Some "v2-to-v3-bridge")
        | Some _ ->
            file |> MorphirFile.warn
                "Content is not IRv2, skipping migration"
                None
                (Some "v2-to-v3-bridge")
}
```

### 6.2 Format Conversion Bridges

**IR to TypeScript AST**:
```fsharp
let irToTypeScriptBridge = {
    Name = "ir-to-typescript"
    Phase = ProcessorPhase.Stringify
    Configure = fun ctx -> ()
    Transform = fun file ->
        match file.Content with
        | None -> file
        | Some ir ->
            let tsAST = TypeScriptCodegen.generateAST ir
            file
            |> MorphirFile.withData "typescript-ast" tsAST
            |> MorphirFile.info "Generated TypeScript AST" None (Some "ir-to-typescript")
}
```

---

## 8. Decision Trees

### 8.1 When to Use Processor Pattern

```
Need pluggable transformation pipeline?
├── Yes → Use MorphirProcessor
│   └── Need multiple phases (parse, transform, codegen)?
│       ├── Yes → Separate plugins by phase
│       └── No → Single-phase processor
└── No → Use direct transformation functions
```

### 7.2 When to Use MorphirFile

```
Need diagnostic accumulation?
├── Yes → Use MorphirFile
│   └── Need position tracking?
│       ├── Yes → Include SourceRange in messages
│       └── No → Use None for position
└── No → Pass IR directly
```

### 7.3 Plugin Selection

```
What does your plugin do?
├── Parse input → Phase = Parse
├── Transform IR → Phase = Transform
│   └── Type inference, optimization, validation
└── Generate output → Phase = Stringify
    └── Code generation, formatting
```

---

## 9. Implementation Roadmap

### Phase 1: Core Infrastructure (Week 1)

**Tasks**:
1. Implement `MorphirFile` record (F# and C#)
2. Implement `MorphirMessage` with severity levels
3. Implement `SourcePosition` and `SourceRange`
4. Create message creation API (Info/Warn/Error/Fail)

**Deliverables**:
- `src/Morphir.Core/Pipeline/MorphirFile.fs` (F#)
- `src/Morphir.Core/Pipeline/MorphirFile.cs` (C#)
- Unit tests for message accumulation

### Phase 2: Processor Pattern (Week 2)

**Tasks**:
1. Implement `MorphirProcessor` with plugin registry
2. Implement `IPlugin` interface / Plugin record
3. Add processor freezing mechanism
4. Create processor inheritance (clone)

**Deliverables**:
- `src/Morphir.Core/Pipeline/MorphirProcessor.fs` (F#)
- `src/Morphir.Core/Pipeline/MorphirProcessor.cs` (C#)
- Example plugins (validation, optimization)

### Phase 3: Visitor Utilities (Week 3)

**Tasks**:
1. Implement `IRVisitor.visit` with control flow
2. Add visitor actions (Continue, Skip, Exit)
3. Create common visitor utilities (map, filter, find)
4. Integrate with existing IR traversal code

**Deliverables**:
- `src/Morphir.Core/IR/Visitor.fs` (F#)
- `src/Morphir.Core/IR/IRVisitor.cs` (C#)
- Visitor utility library

### Phase 4: Bridge Plugins (Week 4)

**Tasks**:
1. Implement IR version migration bridges (v2→v3)
2. Create format conversion bridges (IR→TypeScript, IR→Scala)
3. Add bridge testing infrastructure
4. Document bridge plugin creation

**Deliverables**:
- `src/Morphir.Core/Pipeline/Bridges/` directory
- IR migration bridges
- Format conversion bridges

### Phase 5: Integration & Documentation (Week 5)

**Tasks**:
1. Integrate processor pattern into existing codebase
2. Migrate existing transformations to plugin architecture
3. Write comprehensive documentation
4. Create tutorial examples

**Deliverables**:
- Updated AGENTS.md with processor pattern guidance
- Tutorial: "Creating Morphir Transformation Plugins"
- Example application using processor pipeline

---

## Summary

**Key Adaptation Strategies**:

1. **Processor Pattern**: Immutable processor with plugin registry, frozen/unfrozen states
2. **Plugin Architecture**: Separate Configure/Transform, type-safe plugin interface
3. **MorphirFile**: Track content, diagnostics, metadata through pipeline
4. **Visitor Utilities**: Control flow actions (Continue/Skip/Exit) for traversal
5. **Bridge Plugins**: First-class support for IR version migration and format conversion

**Implementation Priorities**:
1. MorphirFile and message management (enables diagnostic accumulation)
2. MorphirProcessor and plugin registry (enables pluggable pipeline)
3. Visitor utilities (enables advanced IR traversal)
4. Bridge plugins (enables cross-version and cross-format transformation)

**Type Safety Improvements**:
- JavaScript string `type` → F# discriminated union / C# sealed record
- Untyped `data` → Typed `ImmutableDictionary<string, object>` with helpers
- Runtime checks → Compile-time type verification
- Implicit context → Explicit parameters

**F# vs C# Approaches**:
- F#: Record-based, function-first, computation expressions
- C#: Interface-based, LINQ-friendly, fluent API
- Both: Immutable updates, type safety, extensibility

**Next Steps**:
1. Review and approve implementation roadmap
2. Create detailed design documents for each phase
3. Begin Phase 1 implementation (MorphirFile)
4. Set up continuous integration for new pipeline code

---

**Related Documents**:
- [Unified.js Architecture](./unified-js-architecture.md)
- [Unist Specification](./unist-specification.md)
- [VFile Pattern](./vfile-pattern.md)
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
