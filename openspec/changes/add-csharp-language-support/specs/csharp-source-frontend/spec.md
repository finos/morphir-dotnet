# Spec Delta

## Purpose

Turns an ordered set of C# 15 source documents into a Morphir library model for a defined subset of C#. Every construct outside the subset is refused with a precise, stable diagnostic, so the frontend never gives an approximate model. The frontend is a built-in of both hosts, including the Native AOT `morphir`.

Example: a namespace with types is one Morphir module, and a static class below it is a module of values.

```csharp
namespace Acme.Trading;                          // module acme.trading: types only
public closed record Order;
public sealed record Market(decimal Quantity) : Order;
public sealed record Limit(decimal Quantity, decimal Price) : Order;

public static class Pricing                      // module acme.trading.pricing: values
{
    public static decimal Notional(Order order, decimal last) => order switch
    {
        Market m => m.Quantity * last,
        Limit l => l.Quantity * l.Price,
    };
}
```

## ADDED Requirements

### Requirement: Ordered source documents form one package

The frontend SHALL compile an ordered list of C# documents as one package with a supplied package name. Each document SHALL keep its URI and text. The frontend SHALL NOT read a project file, and SHALL NOT read a document from disk.

#### Scenario: Two documents

- **WHEN** two documents and a package name are supplied
- **THEN** the model has the modules of both documents under that package, and each diagnostic refers to a document URI

### Requirement: Compilation uses fixed options and references

The frontend SHALL compile with language version 15, fixed nullable and implicit-using options, and a fixed reference set of the framework reference assemblies and `Morphir.SDK.CSharp`. The reference assemblies SHALL be inside the host, so the Native AOT `morphir` needs no runtime assembly file.

#### Scenario: Native AOT host

- **WHEN** the Native AOT `morphir` compiles a document
- **THEN** it binds the document with no framework assembly file on disk

### Requirement: C# compiler errors stop compilation

An error from the C# parser or binder SHALL become a `csharp.compiler` error diagnostic that keeps the C# message and the error id. When such an error exists, the frontend SHALL produce no model and SHALL NOT also report unsupported constructs.

#### Scenario: Type error

- **WHEN** a document assigns a `string` to an `int`
- **THEN** the result has a `csharp.compiler` error with id `CS0029` and no model

### Requirement: Warnings accompany a successful model

A successful compilation SHALL return its model together with every compiler diagnostic of severity `warning`, `info` or `hint`.

#### Scenario: Warning and model

- **WHEN** a document compiles with a nullable warning
- **THEN** the result has the model and the warning

### Requirement: Namespaces and static classes are modules

Types directly in a namespace SHALL belong to the module of that namespace. A static class SHALL be a module below its namespace and MAY hold values and nested types. A static class nested in a static class SHALL be a child module.

#### Scenario: Types and values

- **WHEN** a namespace has a record and a static class with a method
- **THEN** the model has the module of the namespace with the record, and a module below it with the value

#### Scenario: Nested static class

- **WHEN** a static class holds a nested static class
- **THEN** the model has a child module for the nested class

### Requirement: Supported type definitions

The frontend SHALL lower these to the matching Morphir types, keeping type parameters, field order and access: type-level aliases that C# has, enums, plain records, `closed record` hierarchies with `sealed record` cases, and unions. A plain record SHALL give a record type. An enum SHALL give a custom type with no-argument constructors.

#### Scenario: Closed record

- **WHEN** a `closed record` has two `sealed record` cases
- **THEN** the model has a custom type with one constructor for each case, with the case fields as arguments

#### Scenario: Enum

- **WHEN** a document declares `enum Side { Buy, Sell }`
- **THEN** the model has a custom type `Side` with constructors `Buy` and `Sell` and no arguments

### Requirement: Unions that can convert are accepted

The frontend SHALL accept a union whose case types each lower to a Morphir type reference. The union SHALL give a custom type with one constructor for each case. The constructor SHALL have the local name of the case type and that type as its one argument.

#### Scenario: Union of two records

- **WHEN** a document declares `union Shape(Circle, Rect)`
- **THEN** the model has the custom type `Shape` with constructors `Circle` and `Rect`

#### Scenario: Construction and match

- **WHEN** code converts a `Circle` to a `Shape` and matches `Circle c => ...` on a `Shape`
- **THEN** the model applies the constructor `Circle` and has a constructor pattern with an `as` pattern for `c`

### Requirement: Unions that cannot convert are refused

The frontend SHALL refuse a union whose case is a type parameter or a tuple, whose case type shares a constructor name with another union of the same module, or whose use reaches the state with no case: `default`, a `null` arm, the `Value` property and `IUnion`. Each refusal SHALL be one error diagnostic with the reason.

#### Scenario: Type parameter case

- **WHEN** a document declares `union Opt<T>(T, Nothing)`
- **THEN** the result has an error diagnostic that names the case that has no name

#### Scenario: Shared case type

- **WHEN** two unions of one module have the case `Circle`
- **THEN** the result has an error that names both unions

#### Scenario: Default of a union

- **WHEN** code uses `default` of a union
- **THEN** the result has an error at that expression

### Requirement: Supported value definitions

The frontend SHALL lower literals, variables, references, constructor application, tuples, lists, record construction, field access, `with` updates, application, lambdas, `let` bindings, conditionals, `switch` expressions and statements with exhaustive patterns, and calls of mapped SDK functions. It SHALL keep evaluation order, scope and branch order.

#### Scenario: Switch expression

- **WHEN** a method has a `switch` expression with one arm for each case of a `closed record`
- **THEN** the value is a pattern match with the arms in order

### Requirement: Unsupported constructs have specific diagnostics

Every construct outside the subset SHALL give an error with a stable code `csharp.unsupported_<construct>`, a message that names the construct, and the source range. The published rule catalog SHALL list each code.

#### Scenario: Class with a mutable field

- **WHEN** a document has a class with a settable property
- **THEN** the result has `csharp.unsupported_mutable_property` at the property

### Requirement: Names follow Morphir naming

C# identifiers SHALL map to Morphir names by splitting PascalCase and camelCase words into lowercase segments. An identifier that gives no Morphir word SHALL fail with `csharp.unsupported_name`.

#### Scenario: Pascal case

- **WHEN** a method is named `CalculateTotal`
- **THEN** the Morphir name is `calculate-total`

### Requirement: Value classes of type parameters

The frontend SHALL find the class of a type parameter from an explicit statement, from its .NET constraints, and from the type-checked uses in its body. It SHALL keep the name of the type parameter and SHALL write the class in the `extensions` of the type attributes of each type variable of the signature, as `{"morphir-dotnet": {"classes": [...]}}`.

#### Scenario: Constraints and uses

- **WHEN** a type parameter has `IEquatable<T>` or `IEqualityOperators<T, T, bool>`, or its values meet `==`, `Equals` or `EqualityComparer<T>.Default`
- **THEN** it has the class `equality`; `IComparable<T>`, `IComparisonOperators<T, T, bool>`, `CompareTo` and `Comparer<T>.Default` give `comparable`, and `INumber<T>` gives `number`

#### Scenario: Comparable constraint

- **WHEN** a method has `TKey Max<TKey>(TKey a, TKey b) where TKey : IComparable<TKey>`
- **THEN** the model has the type variable `key`, and its type attributes carry the class `comparable`

### Requirement: Values carry inferred types and source locations

Every lowered value and pattern node SHALL carry its C# type as a Morphir type and the source range from which it was lowered. A replacement node of a later pass SHALL keep that range.

#### Scenario: Location on a node

- **WHEN** a method body is lowered
- **THEN** each node has the start and end line and column of its source

### Requirement: Compilation is deterministic

The same documents, package settings and frontend version SHALL give the same model and diagnostics on every machine. Absolute paths outside the supplied URIs SHALL NOT appear in a model. The Native AOT `morphir` and `dotnet-morphir` SHALL give the same model for the same input.

#### Scenario: Native AOT equals JIT

- **WHEN** the same documents compile in the Native AOT `morphir` and in `dotnet-morphir`
- **THEN** the models and the diagnostics are equal
