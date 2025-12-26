# Compiler Services and Metaprogramming Knowledge Base

**Task**: Task 1.2 - Language Design Pattern Research (Issue #316)
**Created**: 2025-12-23
**Purpose**: Guide to F# Compiler Service, Roslyn, and metaprogramming patterns (Source Generators, Myriad, Type Providers) for morphir-dotnet

## Table of Contents

1. [F# Compiler Service (FCS)](#f-compiler-service-fcs)
2. [Roslyn C# Compiler](#roslyn-c-compiler)
3. [C# Source Generators](#c-source-generators)
4. [Myriad F# Code Generator](#myriad-f-code-generator)
5. [F# Type Providers](#f-type-providers)
6. [Morphir-dotnet Integration](#morphir-dotnet-integration)
7. [Selection Guide](#selection-guide)

---

## 1. F# Compiler Service (FCS)

### 1.1 Overview

FCS provides programmatic access to F# compiler functionality: parsing, type checking, symbol resolution, and code generation.

**Key Capabilities**:
- Parse F# source code into untyped AST
- Type check and produce typed AST
- Query symbols, types, and tooltips (IDE features)
- Compile to IL or emit assemblies

### 1.2 Untyped AST (SynTree)

**Fast, syntax-only operations**: parsing, formatting, simple transformations.

```fsharp
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

// Parse F# source to untyped AST
let sourceCode = """
module Example

let add x y = x + y
"""

let checker = FSharpChecker.Create()
let sourceText = SourceText.ofString sourceCode
let options = { FSharpParsingOptions.Default with SourceFiles = [|"Example.fs"|] }

let parseResults = checker.ParseFile("Example.fs", sourceText, options) |> Async.RunSynchronously

match parseResults.ParseTree with
| ParsedInput.ImplFile(implFile) ->
    // implFile: ParsedImplFileInput
    // Contains: SynModuleOrNamespace list
    for moduleOrNamespace in implFile.Contents do
        match moduleOrNamespace with
        | SynModuleOrNamespace(longId, isRecursive, kind, decls, _, _, _, _, _) ->
            printfn "Module: %A" longId
            for decl in decls do
                match decl with
                | SynModuleDecl.Let(_, bindings, _) ->
                    for binding in bindings do
                        printfn "Binding: %A" binding.Keyword
                | _ -> ()
| _ -> ()
```

**Common SynTree Types**:
- `SynModuleOrNamespace`: Top-level module/namespace
- `SynModuleDecl`: Module-level declarations (let, type, open, etc.)
- `SynExpr`: Expressions (application, lambda, match, etc.)
- `SynType`: Type expressions (function, tuple, app, etc.)
- `SynPat`: Patterns (named, tuple, record, etc.)

### 1.3 Typed AST (TypedTree)

**Semantic analysis**: type information, symbol resolution, overload resolution.

```fsharp
open FSharp.Compiler.CodeAnalysis

let sourceCode = """
module Example

let add (x: int) (y: int) : int = x + y

let result = add 10 20
"""

let checker = FSharpChecker.Create()
let projectOptions = // Create FSharpProjectOptions with references, etc.

// Parse and type check
let parseResults, checkResults =
    checker.ParseAndCheckFileInProject("Example.fs", 0, SourceText.ofString sourceCode, projectOptions)
    |> Async.RunSynchronously

match checkResults with
| FSharpCheckFileAnswer.Succeeded(checkFileResults) ->
    // Get typed implementation
    match checkFileResults.ImplementationFile with
    | Some implFile ->
        // implFile: FSharpImplementationFileContents
        for declaration in implFile.Declarations do
            match declaration with
            | FSharpImplementationFileDeclaration.Entity(entity, subDecls) ->
                printfn "Entity: %s, Type: %A" entity.DisplayName entity.Type
            | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(value, args, body) ->
                printfn "Value: %s, Type: %A" value.DisplayName value.FullType
            | _ -> ()
    | None -> printfn "No implementation file"
| _ -> printfn "Type checking failed"
```

**Use Cases**:
- IDE features: tooltips, go-to-definition, find references
- Static analysis: detect unused bindings, type errors
- Refactoring: rename symbols, extract functions
- Code generation: access full type information

### 1.4 Morphir Application

**Potential Use**: Parse F# Morphir SDK code to extract type signatures for documentation generation.

```fsharp
// Example: Extract function signatures from Morphir.SDK.List module
let extractFunctionSignatures moduleName =
    let sourceCode = File.ReadAllText($"src/Morphir.SDK/{moduleName}.fs")
    let parseResults, checkResults = parseAndCheckFile sourceCode

    match checkResults with
    | FSharpCheckFileAnswer.Succeeded(checkFileResults) ->
        checkFileResults.ImplementationFile
        |> Option.map (fun implFile ->
            implFile.Declarations
            |> List.choose (function
                | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(value, _, _) when value.IsModuleValueOrMember ->
                    Some (value.DisplayName, value.FullType.Format(FSharpDisplayContext.Empty))
                | _ -> None
            )
        )
    | _ -> None

// Result: [("map", "('a -> 'b) -> 'a list -> 'b list"); ("filter", "('a -> bool) -> 'a list -> 'a list"); ...]
```

---

## 2. Roslyn C# Compiler

### 2.1 Overview

Roslyn is the .NET compiler platform with four API layers:

1. **Compiler APIs**: Syntax trees, semantic models, compilation
2. **Workspaces APIs**: Projects, solutions, documents
3. **Diagnostic APIs**: Analyzers, code fixes
4. **Scripting APIs**: Eval C# expressions at runtime

### 2.2 Syntax Trees (Red-Green Trees)

**Immutable, persistent tree structure** with two layers:

- **Green nodes**: Immutable, cached, no parent pointers (memory-efficient)
- **Red nodes**: Mutable wrappers with parent/position info (user-facing API)

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Parse C# source to syntax tree
var sourceCode = @"
namespace Example
{
    public class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }
}
";

SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

// Traverse syntax tree
foreach (var member in root.Members)
{
    if (member is NamespaceDeclarationSyntax namespaceDecl)
    {
        Console.WriteLine($"Namespace: {namespaceDecl.Name}");

        foreach (var typeMember in namespaceDecl.Members)
        {
            if (typeMember is ClassDeclarationSyntax classDecl)
            {
                Console.WriteLine($"  Class: {classDecl.Identifier.Text}");

                foreach (var classMember in classDecl.Members)
                {
                    if (classMember is PropertyDeclarationSyntax property)
                    {
                        Console.WriteLine($"    Property: {property.Identifier.Text}, Type: {property.Type}");
                    }
                }
            }
        }
    }
}
```

**Benefits of Red-Green Trees**:
- Immutable green nodes shared across versions
- Incremental re-parsing: only changed parts re-parsed
- Memory-efficient for large files

### 2.3 Semantic Model

**Type information and symbol resolution** via semantic model.

```csharp
var compilation = CSharpCompilation.Create("MyCompilation")
    .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
    .AddSyntaxTrees(syntaxTree);

var semanticModel = compilation.GetSemanticModel(syntaxTree);

// Get symbol for a type
var classDecl = root.DescendantNodes()
    .OfType<ClassDeclarationSyntax>()
    .First();

var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
Console.WriteLine($"Class: {classSymbol.Name}, Namespace: {classSymbol.ContainingNamespace}");

// Get type info for a property
var propertyDecl = classDecl.Members
    .OfType<PropertyDeclarationSyntax>()
    .First();

var propertySymbol = semanticModel.GetDeclaredSymbol(propertyDecl) as IPropertySymbol;
Console.WriteLine($"Property: {propertySymbol.Name}, Type: {propertySymbol.Type.ToDisplayString()}");
```

**Use Cases**:
- Type inference and checking
- Overload resolution
- Find references and navigate symbols
- Code generation with type awareness

### 2.4 Morphir Application

**Potential Use**: Analyze C# code using Morphir SDK types for verification.

```csharp
// Example: Verify Morphir.Core.IR types are immutable records
var compilation = CreateCompilation("Morphir.Core.IR");
var syntaxTrees = compilation.SyntaxTrees;

foreach (var syntaxTree in syntaxTrees)
{
    var semanticModel = compilation.GetSemanticModel(syntaxTree);
    var root = syntaxTree.GetRoot();

    foreach (var recordDecl in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
    {
        var recordSymbol = semanticModel.GetDeclaredSymbol(recordDecl) as INamedTypeSymbol;

        // Check if all properties are init-only or readonly
        foreach (var member in recordSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.SetMethod != null && !member.SetMethod.IsInitOnly)
            {
                Console.WriteLine($"Warning: {recordSymbol.Name}.{member.Name} is mutable");
            }
        }
    }
}
```

---

## 3. C# Source Generators

### 3.1 Overview

**Compile-time code generation** that runs as part of the C# compiler pipeline.

**Benefits**:
- AOT-friendly: No runtime reflection
- Performance: Generated code optimized at compile time
- Type-safe: Generated code checked by compiler
- IDE support: IntelliSense for generated code

### 3.2 Incremental Generators (Recommended)

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator]
public class VisitorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Filter syntax nodes to only record declarations
        var recordDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is RecordDeclarationSyntax,
                transform: static (ctx, _) => (RecordDeclarationSyntax)ctx.Node
            )
            .Where(static record => record != null);

        // 2. Combine with compilation for semantic analysis
        var recordsWithCompilation = recordDeclarations
            .Combine(context.CompilationProvider);

        // 3. Generate code for each record
        context.RegisterSourceOutput(recordsWithCompilation,
            static (spc, source) => GenerateVisitor(spc, source.Left, source.Right));
    }

    static void GenerateVisitor(SourceProductionContext context, RecordDeclarationSyntax recordSyntax, Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(recordSyntax.SyntaxTree);
        var recordSymbol = semanticModel.GetDeclaredSymbol(recordSyntax) as INamedTypeSymbol;

        if (recordSymbol == null)
            return;

        // Find all derived sealed records
        var derivedRecords = FindDerivedRecords(recordSymbol, compilation);

        // Generate visitor interface
        var code = GenerateVisitorInterface(recordSymbol, derivedRecords);

        context.AddSource($"{recordSymbol.Name}Visitor.g.cs", code);
    }

    static string GenerateVisitorInterface(INamedTypeSymbol baseRecord, List<INamedTypeSymbol> derivedRecords)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {baseRecord.ContainingNamespace};");
        sb.AppendLine();
        sb.AppendLine($"public interface I{baseRecord.Name}Visitor<TResult>");
        sb.AppendLine("{");

        foreach (var derived in derivedRecords)
        {
            sb.AppendLine($"    TResult Visit{derived.Name}({baseRecord.Name}.{derived.Name} node);");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
```

### 3.3 Incremental Pipeline Benefits

**Caching**: Only re-execute changed parts of the pipeline.

```csharp
// Example: Cache expensive operations
var typesWithAttributes = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: static (node, _) => node is TypeDeclarationSyntax,
        transform: static (ctx, _) => GetTypeWithAttribute(ctx)
    )
    .Where(static type => type != null)
    .Collect(); // Cache results

// Only re-run generation when types with attributes change
context.RegisterSourceOutput(typesWithAttributes, Generate);
```

**Value Equality**: Use value types or override `Equals`/`GetHashCode` for cache hits.

```csharp
readonly record struct TypeInfo(string Name, string Namespace, List<string> Properties);

// Records provide value equality automatically
// Cache hits when TypeInfo instances are equal
```

### 3.4 Morphir Application

**Actual Usage in morphir-dotnet**: Generate visitor interfaces for IR types.

```csharp
// Proposed: Generate visitor interfaces for Type, Value, etc.
// Input: Morphir.Core.IR.Type (sealed record hierarchy)
// Output: ITypeVisitor<TResult> interface with VisitVariable, VisitReference, etc.

[Generator]
public class MorphirVisitorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all abstract record types in Morphir.Core.IR namespace
        var irRecords = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is RecordDeclarationSyntax record &&
                    record.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                transform: GetIRRecord
            )
            .Where(static r => r != null);

        context.RegisterSourceOutput(irRecords, GenerateVisitor);
    }

    // Generates visitor interface for each abstract record
}
```

---

## 4. Myriad F# Code Generator

### 4.1 Overview

**MSBuild-integrated F# code generator** using plugin architecture.

**Key Features**:
- Reads F# AST (via FCS)
- Generates F# code (outputAST)
- MSBuild integration: runs during build
- Plugin system: extensible generators

### 4.2 Plugin Architecture

```fsharp
// Myriad generator plugin interface
type IMyriadGenerator =
    abstract member Generate : Ast.SynModuleOrNamespace list -> Ast.SynModuleOrNamespace list
    abstract member ValidInputExtensions : string seq

// Example plugin: Generate lenses for records
[<MyriadGenerator("lenses")>]
type LensesGenerator() =
    interface IMyriadGenerator with
        member _.Generate(namespaces) =
            namespaces |> List.collect (fun ns ->
                // Find all records in namespace
                let records = findRecords ns

                // Generate lens functions for each record field
                records |> List.map generateLenses
            )

        member _.ValidInputExtensions = seq { ".fs" }
```

### 4.3 Configuration (myriad.toml)

```toml
[lenses]
inputFile = "Domain.fs"
outputFile = "Domain.Lenses.g.fs"
namespace = "MyApp.Domain.Lenses"

[visitors]
inputFile = "IR/Type.fs"
outputFile = "IR/Type.Visitors.g.fs"
namespace = "Morphir.Core.IR"
```

### 4.4 Morphir-dotnet Integration

**Actual Usage**: Planned for visitor pattern generation.

```fsharp
// File: src/Morphir.Internal.CodeGeneration/Generators/VisitorGenerator.fs
namespace Morphir.Internal.CodeGeneration.Generators

open Fantomas.Core.SyntaxOak
open Myriad.Core

[<MyriadGenerator("morphir-visitors")>]
type VisitorGenerator() =
    interface IMyriadGenerator with
        member _.Generate(namespaceDecls: SynModuleOrNamespaceNode list) =
            namespaceDecls |> List.collect (fun ns ->
                // Find discriminated unions in namespace
                let unions = findDiscriminatedUnions ns

                // Generate visitor record type for each union
                unions |> List.map (fun union ->
                    generateVisitorRecord union
                )
            )

        member _.ValidInputExtensions = seq { ".fs" }

// Generated output:
// type TypeVisitor<'result> = {
//     VisitVariable: Name -> 'result
//     VisitReference: FQName -> Type<'a> list -> 'result
//     VisitTuple: Type<'a> list -> 'result
//     // ...
// }
```

**Benefits for morphir-dotnet**:
- Reduce boilerplate: Auto-generate visitor records
- AOT-compatible: No runtime reflection
- Type-safe: Compiler checks generated code
- Maintainable: Single source of truth (IR types)

---

## 5. F# Type Providers

### 5.1 Overview

**Meta-programming mechanism** that provides types based on external schemas at compile-time.

**Two Kinds**:
1. **Erased**: Types exist only at compile-time, erased to base types at runtime
2. **Generative**: Types emitted to assembly, available at runtime

### 5.2 Erased Type Provider

**Example**: JsonProvider (FSharp.Data)

```fsharp
open FSharp.Data

// Provide types from JSON schema
type PersonJson = JsonProvider<"""
{
    "name": "John Doe",
    "age": 30,
    "address": {
        "street": "123 Main St",
        "city": "Springfield"
    }
}
""">

// Usage: Type-safe access to JSON
let json = """
{
    "name": "Jane Smith",
    "age": 25,
    "address": {
        "street": "456 Elm St",
        "city": "Shelbyville"
    }
}
"""

let person = PersonJson.Parse(json)
printfn "Name: %s, Age: %d" person.Name person.Age
printfn "City: %s" person.Address.City

// At runtime, 'person' is just IJsonDocument
// All type safety is compile-time only
```

**Benefits**:
- No runtime overhead: Types erased to interfaces
- Fast compilation: No code generation
- Flexible: Easy to update schema

**Drawbacks**:
- No reflection: Can't serialize/deserialize generated types
- No runtime types: Can't use in generic constraints
- Limited scenarios: Best for data access

### 5.3 Generative Type Provider

**Example**: SQL Type Provider (SQLProvider)

```fsharp
open FSharp.Data.Sql

// Provide types from database schema
type SQL = SqlDataProvider<
    ConnectionString = "Data Source=mydb.sqlite",
    DatabaseVendor = Common.DatabaseProviderTypes.SQLITE
>

let ctx = SQL.GetDataContext()

// Generated types: ctx.Main.Person, ctx.Main.Order, etc.
let people = ctx.Main.Person |> Seq.toList

for person in people do
    printfn "Person: %s %s" person.FirstName person.LastName

// Types are real .NET types at runtime
// Can use reflection, serialization, etc.
```

**Benefits**:
- Runtime types: Can use reflection, serialization
- Full .NET types: Work with any .NET library
- Persistent: Types emitted to assembly

**Drawbacks**:
- Slower compilation: Code generation required
- Larger assemblies: Generated types increase size
- Less flexible: Schema changes require recompilation

### 5.4 Morphir Application

**Potential Use**: Generate F# types from Morphir IR schema.

```fsharp
// Hypothetical: MorphirTypeProvider
open Morphir.TypeProvider

// Provide types from Morphir IR package
type MorphirSDK = MorphirTypeProvider<"morphir-ir.json">

// Generated types from IR:
// MorphirSDK.Morphir.SDK.List.map : ('a -> 'b) -> 'a list -> 'b list
// MorphirSDK.Morphir.SDK.Maybe.Maybe<'a> = Just of 'a | Nothing

let result = MorphirSDK.Morphir.SDK.List.map (fun x -> x * 2) [1; 2; 3]
// Type-safe access to Morphir SDK from F#
```

**Challenges**:
- Morphir IR is complex (types, values, modules, packages)
- Type parameters and constraints require careful mapping
- Generative provider needed for runtime use (larger effort)

---

## 6. Morphir-dotnet Integration

### 6.1 Current State

**F# Compiler Service**: Not currently used
**Roslyn**: Not currently used
**Source Generators**: Planned for visitor generation
**Myriad**: Stub implementation in [src/Morphir.Internal.CodeGeneration/Generators/VisitorGenerator.fs](../../src/Morphir.Internal.CodeGeneration/Generators/VisitorGenerator.fs)
**Type Providers**: Not planned

### 6.2 Myriad Visitor Generator

**Status**: Stubbed out, returns empty output

```fsharp
// Current implementation
[<MyriadGenerator("morphir-visitors")>]
type VisitorGenerator() =
    interface IMyriadGenerator with
        member _.Generate(input) =
            Output.Ast []  // TODO: Implement

        member _.ValidInputExtensions = seq { ".fs" }
```

**Planned Implementation**:

```fsharp
[<MyriadGenerator("morphir-visitors")>]
type VisitorGenerator() =
    let generateVisitorForUnion (unionType: SynTypeDefn) =
        // Extract union cases
        let cases = extractUnionCases unionType

        // Generate visitor record type
        let visitorFields = cases |> List.map (fun case ->
            let fieldName = $"Visit{case.Name}"
            let fieldType = generateFieldType case
            SynField(fieldName, fieldType)
        )

        SynTypeDefn.CreateRecord($"{unionType.Name}Visitor", visitorFields)

    interface IMyriadGenerator with
        member _.Generate(namespaces) =
            namespaces |> List.collect (fun ns ->
                // Find all discriminated unions
                let unions = ns.Decls |> List.choose (function
                    | SynModuleDecl.Type(typeDef, _) when isDiscriminatedUnion typeDef -> Some typeDef
                    | _ -> None
                )

                // Generate visitor for each union
                unions |> List.map generateVisitorForUnion
            )

        member _.ValidInputExtensions = seq { ".fs" }
```

### 6.3 Source Generator for Modern IR

**Planned**: Generate visitor interfaces for C# Modern IR.

```csharp
// Input: Morphir.Core.IR.Type (abstract record with sealed derived records)
public abstract record Type
{
    public sealed record Variable(Name Name) : Type;
    public sealed record Reference(FqName TypeName, Seq<Type> TypeParameters) : Type;
    // ...
}

// Generated output: ITypeVisitor<TResult> interface
public interface ITypeVisitor<TResult>
{
    TResult VisitVariable(Type.Variable variable);
    TResult VisitReference(Type.Reference reference);
    TResult VisitTuple(Type.Tuple tuple);
    TResult VisitRecord(Type.Record record);
    TResult VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord);
    TResult VisitFunction(Type.Function function);
    TResult VisitUnit(Type.Unit unit);
}

// Generated extension: Accept methods
public abstract partial record Type
{
    public abstract TResult Accept<TResult>(ITypeVisitor<TResult> visitor);
}

public partial record Variable
{
    public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
        visitor.VisitVariable(this);
}

// ... similar for other types
```

### 6.4 Morphir Architect Skill Usage

**Code Analysis**:
- Use Roslyn to analyze user C# code using Morphir types
- Validate immutability, detect anti-patterns
- Generate reports on Morphir SDK usage

**Code Generation**:
- Use source generators for boilerplate (visitors, lenses, serializers)
- Use Myriad for F# code generation
- Generate backend code (C#, TypeScript, Scala) from Morphir IR

**Refactoring**:
- Use FCS to parse F# Morphir SDK code
- Suggest refactorings based on Morphir best practices
- Auto-fix common issues (e.g., replace mutable vars with immutable lets)

---

## 7. Selection Guide

### 7.1 Feature Comparison

| Feature | FCS | Roslyn | Source Generators | Myriad | Type Providers |
|---------|-----|--------|-------------------|--------|----------------|
| **Language** | F# | C# | C# | F# | F# |
| **Use Case** | Analyze F# code | Analyze C# code | Generate C# code | Generate F# code | Provide types from schema |
| **Runtime** | Any | Any | Compile-time | Compile-time | Compile-time |
| **AOT-Friendly** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes | ⚠️ Generative only |
| **IDE Support** | ✅ Good | ✅ Excellent | ✅ Excellent | ⚠️ Medium | ⚠️ Medium |
| **Performance** | ⚠️ Slow (full analysis) | ⚠️ Slow (full analysis) | ✅ Fast (incremental) | ✅ Fast | ⚠️ Slow (schema load) |
| **Complexity** | ❌ High | ❌ High | ⚠️ Medium | ⚠️ Medium | ❌ High |

### 7.2 Morphir-dotnet Recommendations

**Use FCS when**:
- Analyzing F# Morphir SDK code
- Generating documentation from F# types
- Refactoring F# IR models
- NOT for runtime code generation (too slow)

**Use Roslyn when**:
- Analyzing user C# code using Morphir
- Building IDE analyzers for Morphir patterns
- Validating Morphir C# SDK usage
- NOT for F# code (use FCS instead)

**Use Source Generators when**:
- Generating C# visitor interfaces for Modern IR
- Generating serializers for IR types
- Creating boilerplate code (lenses, builders)
- AOT scenarios (no runtime reflection)

**Use Myriad when**:
- Generating F# visitor records for Classic IR
- Creating F# boilerplate from IR types
- MSBuild-integrated F# code generation
- Complementing FCS with codegen

**Avoid Type Providers when**:
- Need runtime types (erased providers won't work)
- AOT scenarios (generative providers increase size)
- Schema is complex (Morphir IR is very complex)
- Consider source generators or Myriad instead

### 7.3 Decision Matrix

| Scenario | Recommended Approach |
|----------|---------------------|
| Generate visitor interface for C# `Type` | **C# Source Generator** |
| Generate visitor record for F# `Type<'a>` | **Myriad** |
| Analyze F# SDK code for docs | **FCS** |
| Analyze user C# code for validation | **Roslyn** |
| Generate backend code from IR | **Custom template engine + Myriad/SourceGen** |
| Provide F# types from Morphir IR | **Maybe Type Provider (complex, low priority)** |

---

## Summary

This knowledge base covers five metaprogramming approaches:

**F# Compiler Service (FCS)**:
- Parse and analyze F# code
- Untyped AST (fast) and Typed AST (semantic)
- Use for IDE features, refactoring, documentation generation

**Roslyn C# Compiler**:
- Parse and analyze C# code
- Red-green immutable syntax trees
- Semantic model for type information
- Use for analyzers, code fixes, validation

**C# Source Generators**:
- Compile-time C# code generation
- Incremental pipeline with caching
- AOT-friendly, type-safe, IDE-supported
- Use for visitor interfaces, serializers, boilerplate

**Myriad F# Code Generator**:
- MSBuild-integrated F# code generation
- Plugin architecture using FCS
- Generate F# code from F# AST
- Use for visitor records, lenses, boilerplate

**F# Type Providers**:
- Provide types from external schemas
- Erased (compile-time only) vs Generative (runtime types)
- Complex to implement for Morphir IR
- Consider alternatives (source generators, Myriad)

**Morphir-dotnet Integration**:
- Planned: Source generators for C# Modern IR visitors
- Planned: Myriad for F# Classic IR visitors
- Potential: FCS for documentation generation
- Potential: Roslyn for user code analysis

---

**Related Documents**:
- [Language Design Patterns](./language-design-patterns.md)
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
- [Computation Expressions for AST Modeling](./computation-expressions-for-ast.md)
- [Ecosystem Knowledge Base](./ecosystem-knowledge-base.md)
- [Myriad Visitor Generator](../../src/Morphir.Internal.CodeGeneration/Generators/VisitorGenerator.fs)
