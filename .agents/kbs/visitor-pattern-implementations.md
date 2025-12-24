# Visitor Pattern Implementations Knowledge Base

**Task**: Task 1.2 - Language Design Pattern Research (Issue #316)
**Created**: 2025-12-23
**Purpose**: Comprehensive guide to visitor pattern variants for AST traversal and transformation in morphir-dotnet

## Table of Contents

1. [Classic Object-Oriented Visitor](#classic-object-oriented-visitor)
2. [Functional Visitor (Pattern Matching)](#functional-visitor-pattern-matching)
3. [Type-Safe Visitor with Records (F#)](#type-safe-visitor-with-records-f)
4. [Visitor with Default Behavior](#visitor-with-default-behavior)
5. [Transforming Visitor](#transforming-visitor)
6. [Accumulating Visitor](#accumulating-visitor)
7. [Context-Passing Visitor](#context-passing-visitor)
8. [Async Visitor](#async-visitor)
9. [Comparison and Selection Guide](#comparison-and-selection-guide)

---

## 1. Classic Object-Oriented Visitor

**Pattern**: GoF Visitor pattern with interface and double dispatch.

**When to Use**:
- Multiple unrelated operations on AST
- Operations added frequently, AST structure stable
- Object-oriented codebase (C#, Java)

### 1.1 Interface Definition

```csharp
// Visitor interface
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

// Visitor interface with parameter
public interface ITypeVisitor<TParam, TResult>
{
    TResult VisitVariable(Type.Variable variable, TParam param);
    TResult VisitReference(Type.Reference reference, TParam param);
    TResult VisitTuple(Type.Tuple tuple, TParam param);
    TResult VisitRecord(Type.Record record, TParam param);
    TResult VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord, TParam param);
    TResult VisitFunction(Type.Function function, TParam param);
    TResult VisitUnit(Type.Unit unit, TParam param);
}
```

### 1.2 AST Node Accept Method

```csharp
public abstract record Type
{
    public required Document Metadata { get; set; }

    public abstract TResult Accept<TResult>(ITypeVisitor<TResult> visitor);
    public abstract TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param);

    public sealed record Variable(Name Name) : Type
    {
        public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
            visitor.VisitVariable(this);

        public override TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param) =>
            visitor.VisitVariable(this, param);
    }

    public sealed record Reference(FqName TypeName, Seq<Type> TypeParameters) : Type
    {
        public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
            visitor.VisitReference(this);

        public override TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param) =>
            visitor.VisitReference(this, param);
    }

    public sealed record Tuple(Seq<Type> ElementTypes) : Type
    {
        public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
            visitor.VisitTuple(this);

        public override TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param) =>
            visitor.VisitTuple(this, param);
    }

    public sealed record Record(Seq<Field> FieldTypes) : Type
    {
        public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
            visitor.VisitRecord(this);

        public override TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param) =>
            visitor.VisitRecord(this, param);
    }

    public sealed record ExtensibleRecord(Name VariableName, Seq<Field> FieldTypes) : Type
    {
        public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
            visitor.VisitExtensibleRecord(this);

        public override TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param) =>
            visitor.VisitExtensibleRecord(this, param);
    }

    public sealed record Function(Type ParameterType, Type ReturnType) : Type
    {
        public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
            visitor.VisitFunction(this);

        public override TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param) =>
            visitor.VisitFunction(this, param);
    }

    public sealed record Unit() : Type
    {
        public override TResult Accept<TResult>(ITypeVisitor<TResult> visitor) =>
            visitor.VisitUnit(this);

        public override TResult Accept<TParam, TResult>(ITypeVisitor<TParam, TResult> visitor, TParam param) =>
            visitor.VisitUnit(this, param);
    }
}
```

### 1.3 Example Visitor: Type Size Calculator

```csharp
public class TypeSizeCalculator : ITypeVisitor<int>
{
    public int VisitVariable(Type.Variable variable) => 1;

    public int VisitReference(Type.Reference reference) =>
        1 + reference.TypeParameters.Sum(t => t.Accept(this));

    public int VisitTuple(Type.Tuple tuple) =>
        1 + tuple.ElementTypes.Sum(t => t.Accept(this));

    public int VisitRecord(Type.Record record) =>
        1 + record.FieldTypes.Sum(f => f.Type.Accept(this));

    public int VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord) =>
        1 + extensibleRecord.FieldTypes.Sum(f => f.Type.Accept(this));

    public int VisitFunction(Type.Function function) =>
        1 + function.ParameterType.Accept(this) + function.ReturnType.Accept(this);

    public int VisitUnit(Type.Unit unit) => 1;
}

// Usage
var type = new Type.Function(
    new Type.Variable(new Name("a")),
    new Type.Tuple(new[] {
        new Type.Variable(new Name("b")),
        new Type.Variable(new Name("c"))
    })
);

var sizeCalculator = new TypeSizeCalculator();
int size = type.Accept(sizeCalculator); // Returns 5
```

### 1.4 Example Visitor: Type Formatter

```csharp
public class TypeFormatter : ITypeVisitor<string>
{
    public string VisitVariable(Type.Variable variable) =>
        variable.Name.ToString();

    public string VisitReference(Type.Reference reference)
    {
        if (reference.TypeParameters.IsEmpty)
            return reference.TypeName.ToString();

        var typeParams = string.Join(", ", reference.TypeParameters.Select(t => t.Accept(this)));
        return $"{reference.TypeName}<{typeParams}>";
    }

    public string VisitTuple(Type.Tuple tuple)
    {
        var elements = string.Join(", ", tuple.ElementTypes.Select(t => t.Accept(this)));
        return $"({elements})";
    }

    public string VisitRecord(Type.Record record)
    {
        var fields = string.Join(", ", record.FieldTypes.Select(f => $"{f.Name}: {f.Type.Accept(this)}"));
        return $"{{ {fields} }}";
    }

    public string VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord)
    {
        var fields = string.Join(", ", extensibleRecord.FieldTypes.Select(f => $"{f.Name}: {f.Type.Accept(this)}"));
        return $"{{ {extensibleRecord.VariableName} | {fields} }}";
    }

    public string VisitFunction(Type.Function function)
    {
        var param = function.ParameterType.Accept(this);
        var ret = function.ReturnType.Accept(this);

        // Add parentheses for nested functions on left side
        if (function.ParameterType is Type.Function)
            param = $"({param})";

        return $"{param} -> {ret}";
    }

    public string VisitUnit(Type.Unit unit) => "()";
}

// Usage
var type = new Type.Function(
    new Type.Variable(new Name("Int")),
    new Type.Function(
        new Type.Variable(new Name("String")),
        new Type.Variable(new Name("Bool"))
    )
);

var formatter = new TypeFormatter();
string formatted = type.Accept(formatter); // "Int -> String -> Bool"
```

**Pros**:
- Type-safe: Compiler ensures all cases handled
- Open for extension: Easy to add new visitors
- Separation of concerns: Operations separated from AST structure
- IDE support: Good refactoring, navigation

**Cons**:
- Verbose: Requires Accept method in every AST node
- Closed hierarchy: Adding new AST nodes requires updating all visitors
- Boilerplate: Lots of repetitive code

---

## 2. Functional Visitor (Pattern Matching)

**Pattern**: Use exhaustive pattern matching instead of visitor interface.

**When to Use**:
- Functional codebase (F#, Elm, Haskell)
- Operations tied closely to AST structure
- Quick prototyping and exploration

### 2.1 F# Implementation

```fsharp
// No visitor interface needed - use pattern matching directly

module TypeOperations =
    // Type size calculator
    let rec typeSize = function
        | Variable _ -> 1
        | Reference (_, _, typeParams) ->
            1 + (typeParams |> List.sumBy typeSize)
        | Tuple (_, elementTypes) ->
            1 + (elementTypes |> List.sumBy typeSize)
        | Record (_, fieldTypes) ->
            1 + (fieldTypes |> List.sumBy (fun f -> typeSize f.Type))
        | ExtensibleRecord (_, _, fieldTypes) ->
            1 + (fieldTypes |> List.sumBy (fun f -> typeSize f.Type))
        | Function (_, param, ret) ->
            1 + typeSize param + typeSize ret
        | Unit _ -> 1

    // Type formatter
    let rec typeToString = function
        | Variable (_, name) -> name |> Name.toString
        | Reference (_, typeName, []) ->
            typeName |> FQName.toString
        | Reference (_, typeName, typeParams) ->
            sprintf "%s<%s>"
                (typeName |> FQName.toString)
                (typeParams |> List.map typeToString |> String.concat ", ")
        | Tuple (_, elementTypes) ->
            sprintf "(%s)" (elementTypes |> List.map typeToString |> String.concat ", ")
        | Record (_, fieldTypes) ->
            let fields = fieldTypes |> List.map (fun f -> sprintf "%s: %s" (f.Name |> Name.toString) (typeToString f.Type))
            sprintf "{ %s }" (fields |> String.concat ", ")
        | ExtensibleRecord (_, var, fieldTypes) ->
            let fields = fieldTypes |> List.map (fun f -> sprintf "%s: %s" (f.Name |> Name.toString) (typeToString f.Type))
            sprintf "{ %s | %s }" (var |> Name.toString) (fields |> String.concat ", ")
        | Function (_, param, ret) ->
            let paramStr =
                match param with
                | Function _ -> sprintf "(%s)" (typeToString param)
                | _ -> typeToString param
            sprintf "%s -> %s" paramStr (typeToString ret)
        | Unit _ -> "()"

// Usage
let myType = Function (
    (),
    Variable ((), Name "Int"),
    Function (
        (),
        Variable ((), Name "String"),
        Variable ((), Name "Bool")
    )
)

let size = TypeOperations.typeSize myType // 5
let formatted = TypeOperations.typeToString myType // "Int -> String -> Bool"
```

### 2.2 C# Pattern Matching (C# 9+)

```csharp
public static class TypeOperations
{
    // Type size calculator
    public static int TypeSize(Type type) => type switch
    {
        Type.Variable => 1,
        Type.Reference r => 1 + r.TypeParameters.Sum(TypeSize),
        Type.Tuple t => 1 + t.ElementTypes.Sum(TypeSize),
        Type.Record r => 1 + r.FieldTypes.Sum(f => TypeSize(f.Type)),
        Type.ExtensibleRecord er => 1 + er.FieldTypes.Sum(f => TypeSize(f.Type)),
        Type.Function f => 1 + TypeSize(f.ParameterType) + TypeSize(f.ReturnType),
        Type.Unit => 1,
        _ => throw new ArgumentException($"Unknown type: {type.GetType()}")
    };

    // Type formatter
    public static string TypeToString(Type type) => type switch
    {
        Type.Variable v => v.Name.ToString(),

        Type.Reference { TypeParameters.IsEmpty: true } r =>
            r.TypeName.ToString(),

        Type.Reference r =>
            $"{r.TypeName}<{string.Join(", ", r.TypeParameters.Select(TypeToString))}>",

        Type.Tuple t =>
            $"({string.Join(", ", t.ElementTypes.Select(TypeToString))})",

        Type.Record r =>
            $"{{ {string.Join(", ", r.FieldTypes.Select(f => $"{f.Name}: {TypeToString(f.Type)}"))} }}",

        Type.ExtensibleRecord er =>
            $"{{ {er.VariableName} | {string.Join(", ", er.FieldTypes.Select(f => $"{f.Name}: {TypeToString(f.Type)}"))} }}",

        Type.Function { ParameterType: Type.Function } f =>
            $"({TypeToString(f.ParameterType)}) -> {TypeToString(f.ReturnType)}",

        Type.Function f =>
            $"{TypeToString(f.ParameterType)} -> {TypeToString(f.ReturnType)}",

        Type.Unit => "()",

        _ => throw new ArgumentException($"Unknown type: {type.GetType()}")
    };
}
```

**Pros**:
- Concise: No visitor interface or Accept methods
- Direct: Straight-forward pattern matching
- Flexible: Easy to add local helper functions
- Natural in F#: Idiomatic functional style

**Cons**:
- Scattered logic: Operations spread across codebase
- No compiler enforcement: Adding AST nodes requires manual search for all pattern matches
- Limited IDE support: Harder to find all uses of a specific AST node

---

## 3. Type-Safe Visitor with Records (F#)

**Pattern**: Use F# records with function fields to represent visitors, enabling higher-order functions.

**When to Use**:
- Need composable visitors
- Want to pass visitors as values
- Functional codebase with emphasis on modularity

### 3.1 Visitor Record Definition

```fsharp
type TypeVisitor<'result> = {
    VisitVariable: Name -> 'result
    VisitReference: FQName -> Type<'a> list -> 'result
    VisitTuple: Type<'a> list -> 'result
    VisitRecord: Field<'a> list -> 'result
    VisitExtensibleRecord: Name -> Field<'a> list -> 'result
    VisitFunction: Type<'a> -> Type<'a> -> 'result
    VisitUnit: unit -> 'result
}
```

### 3.2 Accept Function

```fsharp
let rec acceptTypeVisitor (visitor: TypeVisitor<'result>) (typ: Type<'a>) : 'result =
    match typ with
    | Variable (_, name) ->
        visitor.VisitVariable name
    | Reference (_, typeName, typeParams) ->
        visitor.VisitReference typeName typeParams
    | Tuple (_, elementTypes) ->
        visitor.VisitTuple elementTypes
    | Record (_, fieldTypes) ->
        visitor.VisitRecord fieldTypes
    | ExtensibleRecord (_, var, fieldTypes) ->
        visitor.VisitExtensibleRecord var fieldTypes
    | Function (_, param, ret) ->
        visitor.VisitFunction param ret
    | Unit _ ->
        visitor.VisitUnit ()
```

### 3.3 Example Visitor: Type Size Calculator

```fsharp
let typeSizeVisitor: TypeVisitor<int> = {
    VisitVariable = fun _ -> 1
    VisitReference = fun _ typeParams ->
        1 + (typeParams |> List.sumBy (acceptTypeVisitor typeSizeVisitor))
    VisitTuple = fun elementTypes ->
        1 + (elementTypes |> List.sumBy (acceptTypeVisitor typeSizeVisitor))
    VisitRecord = fun fieldTypes ->
        1 + (fieldTypes |> List.sumBy (fun f -> acceptTypeVisitor typeSizeVisitor f.Type))
    VisitExtensibleRecord = fun _ fieldTypes ->
        1 + (fieldTypes |> List.sumBy (fun f -> acceptTypeVisitor typeSizeVisitor f.Type))
    VisitFunction = fun param ret ->
        1 + acceptTypeVisitor typeSizeVisitor param + acceptTypeVisitor typeSizeVisitor ret
    VisitUnit = fun () -> 1
}

// Usage
let myType = Function ((), Variable ((), Name "Int"), Variable ((), Name "String"))
let size = acceptTypeVisitor typeSizeVisitor myType // 3
```

### 3.4 Example Visitor: Type Formatter

```fsharp
let rec typeFormatterVisitor: TypeVisitor<string> = {
    VisitVariable = Name.toString
    VisitReference = fun typeName typeParams ->
        match typeParams with
        | [] -> FQName.toString typeName
        | _ ->
            sprintf "%s<%s>"
                (FQName.toString typeName)
                (typeParams |> List.map (acceptTypeVisitor typeFormatterVisitor) |> String.concat ", ")
    VisitTuple = fun elementTypes ->
        sprintf "(%s)" (elementTypes |> List.map (acceptTypeVisitor typeFormatterVisitor) |> String.concat ", ")
    VisitRecord = fun fieldTypes ->
        let fields = fieldTypes |> List.map (fun f ->
            sprintf "%s: %s" (Name.toString f.Name) (acceptTypeVisitor typeFormatterVisitor f.Type))
        sprintf "{ %s }" (fields |> String.concat ", ")
    VisitExtensibleRecord = fun var fieldTypes ->
        let fields = fieldTypes |> List.map (fun f ->
            sprintf "%s: %s" (Name.toString f.Name) (acceptTypeVisitor typeFormatterVisitor f.Type))
        sprintf "{ %s | %s }" (Name.toString var) (fields |> String.concat ", ")
    VisitFunction = fun param ret ->
        let paramStr =
            match param with
            | Function _ -> sprintf "(%s)" (acceptTypeVisitor typeFormatterVisitor param)
            | _ -> acceptTypeVisitor typeFormatterVisitor param
        sprintf "%s -> %s" paramStr (acceptTypeVisitor typeFormatterVisitor ret)
    VisitUnit = fun () -> "()"
}
```

### 3.5 Composing Visitors

```fsharp
// Combine two visitors into a tuple result
let combineVisitors (v1: TypeVisitor<'a>) (v2: TypeVisitor<'b>) : TypeVisitor<'a * 'b> = {
    VisitVariable = fun name ->
        (v1.VisitVariable name, v2.VisitVariable name)
    VisitReference = fun typeName typeParams ->
        (v1.VisitReference typeName typeParams, v2.VisitReference typeName typeParams)
    VisitTuple = fun elementTypes ->
        (v1.VisitTuple elementTypes, v2.VisitTuple elementTypes)
    VisitRecord = fun fieldTypes ->
        (v1.VisitRecord fieldTypes, v2.VisitRecord fieldTypes)
    VisitExtensibleRecord = fun var fieldTypes ->
        (v1.VisitExtensibleRecord var fieldTypes, v2.VisitExtensibleRecord var fieldTypes)
    VisitFunction = fun param ret ->
        (v1.VisitFunction param ret, v2.VisitFunction param ret)
    VisitUnit = fun () ->
        (v1.VisitUnit (), v2.VisitUnit ())
}

// Usage: Get both size and formatted string in one traversal
let combinedVisitor = combineVisitors typeSizeVisitor typeFormatterVisitor
let (size, formatted) = acceptTypeVisitor combinedVisitor myType
```

**Pros**:
- Composable: Visitors are first-class values
- Type-safe: Record fields enforce implementation
- Functional: Natural fit for higher-order functions
- Flexible: Easy to create specialized visitors

**Cons**:
- Recursive definitions: Need `rec` keyword and careful initialization
- Less IDE support: Harder to navigate than interfaces
- Unfamiliar: Not a common pattern in mainstream languages

---

## 4. Visitor with Default Behavior

**Pattern**: Provide base class with default implementation for common traversal logic.

**When to Use**:
- Many visitors share common traversal logic
- Only need to customize specific node types
- Want to reduce boilerplate

### 4.1 Base Visitor with Identity Transformation

```csharp
public abstract class TypeVisitorBase<TResult>
{
    public virtual TResult VisitVariable(Type.Variable variable) =>
        DefaultVisit(variable);

    public virtual TResult VisitReference(Type.Reference reference)
    {
        // Visit type parameters by default
        foreach (var typeParam in reference.TypeParameters)
            typeParam.Accept(this);
        return DefaultVisit(reference);
    }

    public virtual TResult VisitTuple(Type.Tuple tuple)
    {
        foreach (var elementType in tuple.ElementTypes)
            elementType.Accept(this);
        return DefaultVisit(tuple);
    }

    public virtual TResult VisitRecord(Type.Record record)
    {
        foreach (var field in record.FieldTypes)
            field.Type.Accept(this);
        return DefaultVisit(record);
    }

    public virtual TResult VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord)
    {
        foreach (var field in extensibleRecord.FieldTypes)
            field.Type.Accept(this);
        return DefaultVisit(extensibleRecord);
    }

    public virtual TResult VisitFunction(Type.Function function)
    {
        function.ParameterType.Accept(this);
        function.ReturnType.Accept(this);
        return DefaultVisit(function);
    }

    public virtual TResult VisitUnit(Type.Unit unit) =>
        DefaultVisit(unit);

    protected abstract TResult DefaultVisit(Type type);
}
```

### 4.2 Example: Variable Collector

```csharp
public class VariableCollector : TypeVisitorBase<object?>
{
    private readonly HashSet<Name> _variables = new();

    public IReadOnlySet<Name> Variables => _variables;

    public override object? VisitVariable(Type.Variable variable)
    {
        _variables.Add(variable.Name);
        return base.VisitVariable(variable);
    }

    protected override object? DefaultVisit(Type type) => null;
}

// Usage
var type = new Type.Function(
    new Type.Variable(new Name("a")),
    new Type.Tuple(new[] {
        new Type.Variable(new Name("b")),
        new Type.Reference(
            new FqName(/* ... */),
            new[] { new Type.Variable(new Name("c")) }
        )
    })
);

var collector = new VariableCollector();
type.Accept(collector);
// collector.Variables contains: "a", "b", "c"
```

### 4.3 Example: Type Replacer

```csharp
public class TypeReplacer : TypeVisitorBase<Type>
{
    private readonly Func<Type, Type?> _replacementFunc;

    public TypeReplacer(Func<Type, Type?> replacementFunc)
    {
        _replacementFunc = replacementFunc;
    }

    protected override Type DefaultVisit(Type type)
    {
        // Check if replacement function provides a replacement
        var replacement = _replacementFunc(type);
        return replacement ?? type;
    }

    public override Type VisitReference(Type.Reference reference)
    {
        // Check for replacement first
        var replacement = _replacementFunc(reference);
        if (replacement != null)
            return replacement;

        // Otherwise, visit type parameters
        var newTypeParams = reference.TypeParameters
            .Select(t => t.Accept(this))
            .ToSeq();

        // Return new reference if type parameters changed
        if (!newTypeParams.SequenceEqual(reference.TypeParameters))
            return reference with { TypeParameters = newTypeParams };

        return reference;
    }

    public override Type VisitFunction(Type.Function function)
    {
        var replacement = _replacementFunc(function);
        if (replacement != null)
            return replacement;

        var newParam = function.ParameterType.Accept(this);
        var newRet = function.ReturnType.Accept(this);

        if (newParam != function.ParameterType || newRet != function.ReturnType)
            return function with { ParameterType = newParam, ReturnType = newRet };

        return function;
    }

    // Similar implementations for other methods...
}

// Usage: Replace all variables named "a" with Int
var replacer = new TypeReplacer(type =>
    type is Type.Variable { Name: var name } && name.ToString() == "a"
        ? new Type.Reference(new FqName(/* path to Int */), Seq<Type>.Empty)
        : null
);

var newType = originalType.Accept(replacer);
```

**Pros**:
- Less boilerplate: Only override methods you care about
- Reusable: Common traversal logic shared
- Clear intent: Overridden methods show customization

**Cons**:
- Inheritance: Couples to base class
- Hidden complexity: Base class traversal not obvious
- Harder to compose: Can't easily combine multiple visitors

---

## 5. Transforming Visitor

**Pattern**: Visitor that returns a transformed AST (same or different type).

**When to Use**:
- AST transformations (optimization, normalization)
- Type-directed code generation
- Migration between IR versions

### 5.1 Generic Transformer Base

```csharp
public abstract class TypeTransformer : ITypeVisitor<Type>
{
    public virtual Type VisitVariable(Type.Variable variable) => variable;

    public virtual Type VisitReference(Type.Reference reference)
    {
        var newTypeParams = reference.TypeParameters
            .Select(t => t.Accept(this))
            .ToSeq();

        return newTypeParams.SequenceEqual(reference.TypeParameters)
            ? reference
            : reference with { TypeParameters = newTypeParams };
    }

    public virtual Type VisitTuple(Type.Tuple tuple)
    {
        var newElementTypes = tuple.ElementTypes
            .Select(t => t.Accept(this))
            .ToSeq();

        return newElementTypes.SequenceEqual(tuple.ElementTypes)
            ? tuple
            : tuple with { ElementTypes = newElementTypes };
    }

    public virtual Type VisitRecord(Type.Record record)
    {
        var newFieldTypes = record.FieldTypes
            .Select(f => f.Type.Accept(this))
            .Zip(record.FieldTypes, (newType, oldField) =>
                newType == oldField.Type ? oldField : oldField with { Type = newType })
            .ToSeq();

        return newFieldTypes.SequenceEqual(record.FieldTypes)
            ? record
            : record with { FieldTypes = newFieldTypes };
    }

    public virtual Type VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord)
    {
        var newFieldTypes = extensibleRecord.FieldTypes
            .Select(f => f.Type.Accept(this))
            .Zip(extensibleRecord.FieldTypes, (newType, oldField) =>
                newType == oldField.Type ? oldField : oldField with { Type = newType })
            .ToSeq();

        return newFieldTypes.SequenceEqual(extensibleRecord.FieldTypes)
            ? extensibleRecord
            : extensibleRecord with { FieldTypes = newFieldTypes };
    }

    public virtual Type VisitFunction(Type.Function function)
    {
        var newParam = function.ParameterType.Accept(this);
        var newRet = function.ReturnType.Accept(this);

        return newParam == function.ParameterType && newRet == function.ReturnType
            ? function
            : function with { ParameterType = newParam, ReturnType = newRet };
    }

    public virtual Type VisitUnit(Type.Unit unit) => unit;
}
```

### 5.2 Example: Type Simplifier

```csharp
// Simplifies nested tuples: (a, (b, c)) -> (a, b, c)
public class TupleFlattener : TypeTransformer
{
    public override Type VisitTuple(Type.Tuple tuple)
    {
        // First, transform nested types
        var transformedElements = tuple.ElementTypes
            .Select(t => t.Accept(this))
            .ToList();

        // Then, flatten any nested tuples
        var flattenedElements = new List<Type>();
        foreach (var element in transformedElements)
        {
            if (element is Type.Tuple nestedTuple)
                flattenedElements.AddRange(nestedTuple.ElementTypes);
            else
                flattenedElements.Add(element);
        }

        return flattenedElements.SequenceEqual(tuple.ElementTypes)
            ? tuple
            : new Type.Tuple(flattenedElements.ToSeq()) { Metadata = tuple.Metadata };
    }
}

// Usage
var nested = new Type.Tuple(new[] {
    new Type.Variable(new Name("a")),
    new Type.Tuple(new[] {
        new Type.Variable(new Name("b")),
        new Type.Variable(new Name("c"))
    })
});

var flattener = new TupleFlattener();
var flattened = nested.Accept(flattener);
// Result: Tuple(Variable("a"), Variable("b"), Variable("c"))
```

### 5.3 Example: Type Variable Renamer

```csharp
public class TypeVariableRenamer : TypeTransformer
{
    private readonly Dictionary<Name, Name> _renameMap;

    public TypeVariableRenamer(Dictionary<Name, Name> renameMap)
    {
        _renameMap = renameMap;
    }

    public override Type VisitVariable(Type.Variable variable)
    {
        return _renameMap.TryGetValue(variable.Name, out var newName)
            ? variable with { Name = newName }
            : variable;
    }
}

// Usage: Rename 'a' -> 'x', 'b' -> 'y'
var renamer = new TypeVariableRenamer(new Dictionary<Name, Name>
{
    [new Name("a")] = new Name("x"),
    [new Name("b")] = new Name("y")
});

var original = new Type.Function(
    new Type.Variable(new Name("a")),
    new Type.Variable(new Name("b"))
);

var renamed = original.Accept(renamer);
// Result: Function(Variable("x"), Variable("y"))
```

**Pros**:
- Structural sharing: Unchanged subtrees preserved
- Type-safe: Returns valid AST
- Composable: Chain multiple transformers

**Cons**:
- Boilerplate: Lots of traversal code
- Performance: May create unnecessary intermediate objects

---

## 6. Accumulating Visitor

**Pattern**: Visitor that accumulates results while traversing (fold/reduce).

**When to Use**:
- Collecting information from AST (variables, types, dependencies)
- Computing aggregate values (size, depth, metrics)
- Building summaries or reports

### 6.1 Generic Accumulator

```csharp
public abstract class TypeAccumulator<TState> : ITypeVisitor<TState, TState>
{
    public abstract TState VisitVariable(Type.Variable variable, TState state);
    public abstract TState VisitReference(Type.Reference reference, TState state);
    public abstract TState VisitTuple(Type.Tuple tuple, TState state);
    public abstract TState VisitRecord(Type.Record record, TState state);
    public abstract TState VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord, TState state);
    public abstract TState VisitFunction(Type.Function function, TState state);
    public abstract TState VisitUnit(Type.Unit unit, TState state);
}
```

### 6.2 Example: Depth Calculator

```csharp
public class DepthCalculator : TypeAccumulator<int>
{
    public override int VisitVariable(Type.Variable variable, int state) => state;

    public override int VisitReference(Type.Reference reference, int state)
    {
        var maxDepth = reference.TypeParameters
            .Select(t => t.Accept(this, state + 1))
            .DefaultIfEmpty(state)
            .Max();
        return maxDepth;
    }

    public override int VisitTuple(Type.Tuple tuple, int state)
    {
        var maxDepth = tuple.ElementTypes
            .Select(t => t.Accept(this, state + 1))
            .DefaultIfEmpty(state)
            .Max();
        return maxDepth;
    }

    public override int VisitRecord(Type.Record record, int state)
    {
        var maxDepth = record.FieldTypes
            .Select(f => f.Type.Accept(this, state + 1))
            .DefaultIfEmpty(state)
            .Max();
        return maxDepth;
    }

    public override int VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord, int state)
    {
        var maxDepth = extensibleRecord.FieldTypes
            .Select(f => f.Type.Accept(this, state + 1))
            .DefaultIfEmpty(state)
            .Max();
        return maxDepth;
    }

    public override int VisitFunction(Type.Function function, int state)
    {
        var paramDepth = function.ParameterType.Accept(this, state + 1);
        var retDepth = function.ReturnType.Accept(this, state + 1);
        return Math.Max(paramDepth, retDepth);
    }

    public override int VisitUnit(Type.Unit unit, int state) => state;
}

// Usage
var type = new Type.Function(
    new Type.Tuple(new[] {
        new Type.Variable(new Name("a")),
        new Type.Reference(
            new FqName(/* ... */),
            new[] { new Type.Variable(new Name("b")) }
        )
    }),
    new Type.Variable(new Name("c"))
);

var depthCalculator = new DepthCalculator();
int depth = type.Accept(depthCalculator, 1); // Returns 3
```

### 6.3 Example: Dependency Collector

```csharp
public class DependencyCollector : TypeAccumulator<HashSet<FqName>>
{
    public override HashSet<FqName> VisitVariable(Type.Variable variable, HashSet<FqName> state) =>
        state;

    public override HashSet<FqName> VisitReference(Type.Reference reference, HashSet<FqName> state)
    {
        state.Add(reference.TypeName);
        foreach (var typeParam in reference.TypeParameters)
            typeParam.Accept(this, state);
        return state;
    }

    public override HashSet<FqName> VisitTuple(Type.Tuple tuple, HashSet<FqName> state)
    {
        foreach (var elementType in tuple.ElementTypes)
            elementType.Accept(this, state);
        return state;
    }

    public override HashSet<FqName> VisitRecord(Type.Record record, HashSet<FqName> state)
    {
        foreach (var field in record.FieldTypes)
            field.Type.Accept(this, state);
        return state;
    }

    public override HashSet<FqName> VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord, HashSet<FqName> state)
    {
        foreach (var field in extensibleRecord.FieldTypes)
            field.Type.Accept(this, state);
        return state;
    }

    public override HashSet<FqName> VisitFunction(Type.Function function, HashSet<FqName> state)
    {
        function.ParameterType.Accept(this, state);
        function.ReturnType.Accept(this, state);
        return state;
    }

    public override HashSet<FqName> VisitUnit(Type.Unit unit, HashSet<FqName> state) =>
        state;
}

// Usage
var type = new Type.Function(
    new Type.Reference(new FqName(/* path to List */), new[] {
        new Type.Reference(new FqName(/* path to String */), Seq<Type>.Empty)
    }),
    new Type.Reference(new FqName(/* path to Int */), Seq<Type>.Empty)
);

var collector = new DependencyCollector();
var dependencies = type.Accept(collector, new HashSet<FqName>());
// dependencies contains: FqName for List, String, Int
```

**Pros**:
- Efficient: Single traversal for accumulation
- Clear state management: State parameter explicit
- Composable: Easy to combine with other patterns

**Cons**:
- Mutable state: Typically uses mutable accumulator
- Extra parameter: All visit methods need state parameter

---

## 7. Context-Passing Visitor

**Pattern**: Pass contextual information down the tree during traversal.

**When to Use**:
- Need parent/ancestor information during traversal
- Building symbol tables or environments
- Type checking with scoped variables

### 7.1 Context-Aware Visitor Interface

```csharp
public interface ITypeVisitorWithContext<TContext, TResult>
{
    TResult VisitVariable(Type.Variable variable, TContext context);
    TResult VisitReference(Type.Reference reference, TContext context);
    TResult VisitTuple(Type.Tuple tuple, TContext context);
    TResult VisitRecord(Type.Record record, TContext context);
    TResult VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord, TContext context);
    TResult VisitFunction(Type.Function function, TContext context);
    TResult VisitUnit(Type.Unit unit, TContext context);
}
```

### 7.2 Example: Type Variable Depth Tracker

```csharp
public class VariableDepthTracker : ITypeVisitorWithContext<int, Dictionary<Name, int>>
{
    public Dictionary<Name, int> VisitVariable(Type.Variable variable, int depth)
    {
        return new Dictionary<Name, int> { [variable.Name] = depth };
    }

    public Dictionary<Name, int> VisitReference(Type.Reference reference, int depth)
    {
        var result = new Dictionary<Name, int>();
        foreach (var typeParam in reference.TypeParameters)
        {
            var nested = typeParam.Accept(this, depth + 1);
            foreach (var (name, nestedDepth) in nested)
            {
                if (!result.ContainsKey(name) || result[name] < nestedDepth)
                    result[name] = nestedDepth;
            }
        }
        return result;
    }

    public Dictionary<Name, int> VisitTuple(Type.Tuple tuple, int depth)
    {
        var result = new Dictionary<Name, int>();
        foreach (var elementType in tuple.ElementTypes)
        {
            var nested = elementType.Accept(this, depth + 1);
            foreach (var (name, nestedDepth) in nested)
            {
                if (!result.ContainsKey(name) || result[name] < nestedDepth)
                    result[name] = nestedDepth;
            }
        }
        return result;
    }

    public Dictionary<Name, int> VisitRecord(Type.Record record, int depth)
    {
        var result = new Dictionary<Name, int>();
        foreach (var field in record.FieldTypes)
        {
            var nested = field.Type.Accept(this, depth + 1);
            foreach (var (name, nestedDepth) in nested)
            {
                if (!result.ContainsKey(name) || result[name] < nestedDepth)
                    result[name] = nestedDepth;
            }
        }
        return result;
    }

    public Dictionary<Name, int> VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord, int depth)
    {
        var result = new Dictionary<Name, int> { [extensibleRecord.VariableName] = depth };
        foreach (var field in extensibleRecord.FieldTypes)
        {
            var nested = field.Type.Accept(this, depth + 1);
            foreach (var (name, nestedDepth) in nested)
            {
                if (!result.ContainsKey(name) || result[name] < nestedDepth)
                    result[name] = nestedDepth;
            }
        }
        return result;
    }

    public Dictionary<Name, int> VisitFunction(Type.Function function, int depth)
    {
        var result = new Dictionary<Name, int>();

        var paramVars = function.ParameterType.Accept(this, depth + 1);
        foreach (var (name, nestedDepth) in paramVars)
        {
            if (!result.ContainsKey(name) || result[name] < nestedDepth)
                result[name] = nestedDepth;
        }

        var retVars = function.ReturnType.Accept(this, depth + 1);
        foreach (var (name, nestedDepth) in retVars)
        {
            if (!result.ContainsKey(name) || result[name] < nestedDepth)
                result[name] = nestedDepth;
        }

        return result;
    }

    public Dictionary<Name, int> VisitUnit(Type.Unit unit, int depth) =>
        new Dictionary<Name, int>();
}

// Usage
var type = new Type.Function(
    new Type.Variable(new Name("a")),
    new Type.Tuple(new[] {
        new Type.Variable(new Name("b")),
        new Type.Reference(
            new FqName(/* ... */),
            new[] { new Type.Variable(new Name("c")) }
        )
    })
);

var tracker = new VariableDepthTracker();
var depths = type.Accept(tracker, 0);
// depths: { "a" => 1, "b" => 2, "c" => 3 }
```

### 7.3 Example: Type Validator with Scope

```csharp
public class TypeScope
{
    private readonly TypeScope? _parent;
    private readonly HashSet<Name> _boundVariables = new();

    public TypeScope(TypeScope? parent = null)
    {
        _parent = parent;
    }

    public TypeScope WithVariable(Name name)
    {
        var newScope = new TypeScope(_parent);
        newScope._boundVariables.Add(name);
        foreach (var v in _boundVariables)
            newScope._boundVariables.Add(v);
        return newScope;
    }

    public bool IsInScope(Name name) =>
        _boundVariables.Contains(name) || _parent?.IsInScope(name) == true;
}

public class TypeValidator : ITypeVisitorWithContext<TypeScope, Result<Unit, string>>
{
    public Result<Unit, string> VisitVariable(Type.Variable variable, TypeScope scope)
    {
        return scope.IsInScope(variable.Name)
            ? Result.Ok(Unit.Value)
            : Result.Error($"Unbound type variable: {variable.Name}");
    }

    public Result<Unit, string> VisitReference(Type.Reference reference, TypeScope scope)
    {
        // Validate all type parameters
        foreach (var typeParam in reference.TypeParameters)
        {
            var result = typeParam.Accept(this, scope);
            if (result.IsError)
                return result;
        }
        return Result.Ok(Unit.Value);
    }

    public Result<Unit, string> VisitExtensibleRecord(Type.ExtensibleRecord extensibleRecord, TypeScope scope)
    {
        // Extensible records bind their row variable
        var newScope = scope.WithVariable(extensibleRecord.VariableName);

        foreach (var field in extensibleRecord.FieldTypes)
        {
            var result = field.Type.Accept(this, newScope);
            if (result.IsError)
                return result;
        }
        return Result.Ok(Unit.Value);
    }

    // ... other methods similar to TypeVariableDepthTracker
}
```

**Pros**:
- Context propagation: Natural way to thread context down
- Scoped information: Can model nested scopes
- Clear data flow: Context parameter makes dependencies explicit

**Cons**:
- Manual threading: Must pass context explicitly
- Immutable context overhead: May need to copy context for each branch

---

## 8. Async Visitor

**Pattern**: Visitor that performs asynchronous operations during traversal.

**When to Use**:
- I/O operations during traversal (e.g., loading external types)
- Concurrent processing of independent subtrees
- Long-running operations (e.g., type inference, proof search)

### 8.1 Async Visitor Interface

```csharp
public interface IAsyncTypeVisitor<TResult>
{
    Task<TResult> VisitVariableAsync(Type.Variable variable);
    Task<TResult> VisitReferenceAsync(Type.Reference reference);
    Task<TResult> VisitTupleAsync(Type.Tuple tuple);
    Task<TResult> VisitRecordAsync(Type.Record record);
    Task<TResult> VisitExtensibleRecordAsync(Type.ExtensibleRecord extensibleRecord);
    Task<TResult> VisitFunctionAsync(Type.Function function);
    Task<TResult> VisitUnitAsync(Type.Unit unit);
}
```

### 8.2 Accept Method

```csharp
public abstract record Type
{
    // ... existing code ...

    public abstract Task<TResult> AcceptAsync<TResult>(IAsyncTypeVisitor<TResult> visitor);
}

public sealed record Variable(Name Name) : Type
{
    public override Task<TResult> AcceptAsync<TResult>(IAsyncTypeVisitor<TResult> visitor) =>
        visitor.VisitVariableAsync(this);
}

// Similar for other types...
```

### 8.3 Example: External Type Resolver

```csharp
public class ExternalTypeResolver : IAsyncTypeVisitor<Type>
{
    private readonly ITypeRepository _repository;

    public ExternalTypeResolver(ITypeRepository repository)
    {
        _repository = repository;
    }

    public Task<Type> VisitVariableAsync(Type.Variable variable) =>
        Task.FromResult<Type>(variable);

    public async Task<Type> VisitReferenceAsync(Type.Reference reference)
    {
        // Resolve type parameters concurrently
        var resolvedParams = await Task.WhenAll(
            reference.TypeParameters.Select(t => t.AcceptAsync(this))
        );

        // Check if this is an external type that needs loading
        var resolvedType = await _repository.ResolveTypeAsync(reference.TypeName);

        if (resolvedType != null)
            return resolvedType;

        // Return updated reference with resolved parameters
        return reference with { TypeParameters = resolvedParams.ToSeq() };
    }

    public async Task<Type> VisitTupleAsync(Type.Tuple tuple)
    {
        var resolvedElements = await Task.WhenAll(
            tuple.ElementTypes.Select(t => t.AcceptAsync(this))
        );

        return tuple with { ElementTypes = resolvedElements.ToSeq() };
    }

    public async Task<Type> VisitRecordAsync(Type.Record record)
    {
        var resolvedFields = await Task.WhenAll(
            record.FieldTypes.Select(async f => f with {
                Type = await f.Type.AcceptAsync(this)
            })
        );

        return record with { FieldTypes = resolvedFields.ToSeq() };
    }

    public async Task<Type> VisitExtensibleRecordAsync(Type.ExtensibleRecord extensibleRecord)
    {
        var resolvedFields = await Task.WhenAll(
            extensibleRecord.FieldTypes.Select(async f => f with {
                Type = await f.Type.AcceptAsync(this)
            })
        );

        return extensibleRecord with { FieldTypes = resolvedFields.ToSeq() };
    }

    public async Task<Type> VisitFunctionAsync(Type.Function function)
    {
        var resolvedParam = await function.ParameterType.AcceptAsync(this);
        var resolvedRet = await function.ReturnType.AcceptAsync(this);

        return function with {
            ParameterType = resolvedParam,
            ReturnType = resolvedRet
        };
    }

    public Task<Type> VisitUnitAsync(Type.Unit unit) =>
        Task.FromResult<Type>(unit);
}

// Usage
var type = new Type.Reference(/* external type reference */);
var resolver = new ExternalTypeResolver(typeRepository);
var resolvedType = await type.AcceptAsync(resolver);
```

**Pros**:
- Natural async/await: Works with .NET async patterns
- Concurrent processing: Can parallelize independent operations
- Composable: Can chain async visitors

**Cons**:
- Complexity: Adds async overhead
- Error handling: Must handle exceptions from async operations
- Testing: Harder to test async code

---

## 9. Comparison and Selection Guide

### 9.1 Feature Matrix

| Pattern | Type Safety | Boilerplate | Composability | Performance | AOT-Friendly |
|---------|-------------|-------------|---------------|-------------|--------------|
| Classic OO Visitor | ✅ Excellent | ❌ High | ⚠️ Medium | ✅ Good | ✅ Yes |
| Functional (Pattern Matching) | ✅ Excellent (F#) / ⚠️ Good (C#) | ✅ Low | ✅ Excellent | ✅ Excellent | ✅ Yes |
| Record Visitor (F#) | ✅ Excellent | ⚠️ Medium | ✅ Excellent | ✅ Good | ✅ Yes |
| Visitor with Defaults | ✅ Good | ✅ Low | ❌ Poor | ✅ Good | ✅ Yes |
| Transforming Visitor | ✅ Excellent | ⚠️ Medium | ✅ Good | ⚠️ Medium | ✅ Yes |
| Accumulating Visitor | ✅ Excellent | ⚠️ Medium | ✅ Good | ✅ Excellent | ✅ Yes |
| Context-Passing Visitor | ✅ Excellent | ⚠️ Medium | ✅ Good | ⚠️ Medium | ✅ Yes |
| Async Visitor | ✅ Good | ❌ High | ⚠️ Medium | ❌ Poor | ⚠️ Possible |

### 9.2 Selection Guide

**Choose Classic OO Visitor when**:
- Working in C# with sealed record hierarchies
- Need clear separation between AST and operations
- Want excellent IDE support (navigation, refactoring)
- AST structure is stable, operations change frequently

**Choose Functional Pattern Matching when**:
- Working in F# with discriminated unions
- Operations are tightly coupled to AST structure
- Want minimal boilerplate
- Prototyping or exploratory programming

**Choose Record Visitor (F#) when**:
- Need visitors as first-class values
- Want to compose or pass visitors as parameters
- Building visitor combinators or higher-order functions

**Choose Visitor with Defaults when**:
- Most visitors share common traversal logic
- Only need to customize specific node types
- Want to minimize repetitive code

**Choose Transforming Visitor when**:
- Performing AST transformations (optimization, normalization)
- Migrating between IR versions
- Want structural sharing for performance

**Choose Accumulating Visitor when**:
- Collecting information from AST (variables, dependencies)
- Computing aggregate values (size, depth, metrics)
- Building summaries or reports

**Choose Context-Passing Visitor when**:
- Need parent/ancestor information during traversal
- Building symbol tables or environments
- Type checking with scoped variables

**Choose Async Visitor when**:
- Performing I/O operations during traversal
- Need concurrent processing of subtrees
- Working with external resources or services

### 9.3 Morphir-dotnet Recommendations

**For C# Modern IR (src/Morphir.Core/IR)**:
1. **Primary**: Classic OO Visitor with Accept methods
   - Excellent IDE support
   - Type-safe and extensible
   - Familiar to C# developers

2. **Secondary**: Pattern matching for simple queries
   - Use switch expressions for straightforward operations
   - Avoid for complex transformations

**For F# Classic IR (src/Morphir.Models/IR/Classic)**:
1. **Primary**: Functional pattern matching
   - Idiomatic F# style
   - Minimal boilerplate
   - Natural exhaustiveness checking

2. **Secondary**: Record visitors for composable operations
   - Use when building visitor combinators
   - Good for plugin architectures

**For Morphir Application Architect Skill**:
1. **Code generation**: Transforming visitors
   - Structural preservation important
   - Clear transformation logic

2. **Analysis**: Accumulating visitors
   - Collect metrics, dependencies, patterns
   - Build knowledge graphs

3. **Validation**: Context-passing visitors
   - Track scopes and environments
   - Type checking and constraint validation

---

## Summary

This knowledge base documents 8 visitor pattern variants for AST traversal and transformation:

1. **Classic Object-Oriented Visitor**: Interface-based with double dispatch
2. **Functional Visitor**: Pattern matching without visitor interface
3. **Type-Safe Visitor with Records (F#)**: First-class visitor values
4. **Visitor with Default Behavior**: Base class with shared traversal logic
5. **Transforming Visitor**: Returns transformed AST
6. **Accumulating Visitor**: Fold/reduce pattern for aggregation
7. **Context-Passing Visitor**: Thread contextual information during traversal
8. **Async Visitor**: Asynchronous operations during traversal

Each pattern has distinct trade-offs in type safety, boilerplate, composability, and performance. The selection guide provides clear criteria for choosing the appropriate pattern for specific use cases in morphir-dotnet.

---

**Related Documents**:
- [Language Design Patterns](./language-design-patterns.md)
- [Computation Expressions for AST Modeling](./computation-expressions-for-ast.md)
- [Compiler Services and Metaprogramming](./compiler-services-metaprogramming.md)
- [Ecosystem Knowledge Base](./ecosystem-knowledge-base.md)
