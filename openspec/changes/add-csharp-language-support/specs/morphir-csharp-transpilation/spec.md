# Spec Delta

## MODIFIED Requirements

### Requirement: Explicit language profiles

Generation SHALL support one language profile, C# 15, and SHALL default to it independently of the installed SDK. The profile SHALL declare the runtime support that it needs. A request for any other language version SHALL fail before emission with a diagnostic that names the supported version.

#### Scenario: Supported toolchains

- **WHEN** a supported model is generated
- **THEN** its sources compile and execute using the pinned .NET SDK 11 toolchain

#### Scenario: Unsupported version

- **WHEN** a request asks for C# 14
- **THEN** generation fails before emission and names C# 15 as the supported version

#### Scenario: No ambient preview

- **WHEN** the machine has a newer compiler or preview language features
- **THEN** generation uses C# 15 syntax only and does not enable unrelated preview syntax

#### Scenario: Public representation stability

- **WHEN** the same model is generated with the imperative and the functional profile
- **THEN** public type, case, member and payload identities remain equivalent rather than changing to a different representation

### Requirement: Immutable type translation

Generation SHALL preserve supported aliases, generic parameters, custom-type cases, tuples, closed records, function types, unit and visibility. Generated values SHALL be immutable. Generated records and unions SHALL use the equality of plain C# unless the IEEE equality option is on. Open extensible records SHALL fail explicitly.

#### Scenario: Generic data and nested collections

- **WHEN** a model exports generic records and cases containing SDK collections
- **THEN** the C# consumer constructs and compares values structurally without mutable backing data

#### Scenario: Alias and visibility fidelity

- **WHEN** a model uses transparent aliases and private helpers or constructors
- **THEN** supported aliases retain their semantic identity and inaccessible declarations remain inaccessible to a consumer

#### Scenario: Plain equality

- **WHEN** two generated records hold the same fields and no option is set
- **THEN** the records are equal with the equality of plain C#

### Requirement: Executable initial expressions

Generation SHALL support literals, variables, top-level references, self recursion and local recursive binding groups, constructors, tuples, lists, closed records, fields, application, lambdas, lets, conditionals, record updates and ordered patterns. It SHALL preserve scope, evaluation order and partial application.

#### Scenario: Function values and lexical capture

- **WHEN** a function is partially applied and a lambda captures a shadowed local name
- **THEN** executing the generated program uses the correct binding and argument order

#### Scenario: Pattern and record behavior

- **WHEN** a model uses constructor, tuple, list, wildcard, literal and alias patterns followed by a record update
- **THEN** the first matching branch executes and the original record remains unchanged

#### Scenario: Local recursion

- **WHEN** a model uses a local recursive binding group
- **THEN** the C# has a `while` loop for a tail call and a local recursive function otherwise, and the program gives the model result

#### Scenario: Unsupported local recursion

- **WHEN** a model uses an unmapped expression inside a local recursive binding group
- **THEN** generation reports its model location rather than omitting it or emitting a runtime stub

## ADDED Requirements

### Requirement: The backend writes syntax trees

The backend SHALL build each C# file as a Roslyn syntax tree and SHALL NOT build C# by text concatenation. It SHALL use no Roslyn Workspaces assembly. The output SHALL have stable layout.

#### Scenario: Native AOT

- **WHEN** the Native AOT `morphir` generates a model
- **THEN** the C# bytes equal those of `dotnet-morphir`

#### Scenario: Layout of a switch expression

- **WHEN** a model has a pattern match with three arms
- **THEN** each arm is on its own line and the closing brace is on its own line

### Requirement: Custom types take the closed form or the union form

A custom type in which each constructor has one argument that is a reference with the local name of the constructor SHALL be written as a C# `union`. Each other custom type SHALL be written as a `closed record` with `sealed record` cases. A custom type that mixes the two shapes SHALL take the closed form.

#### Scenario: Wrapper constructors

- **WHEN** the type `Shape` has constructors `Circle Circle` and `Rect Rect`
- **THEN** the C# has `union Shape(Circle, Rect)`

#### Scenario: Constructors with fields

- **WHEN** the type `Price` has the constructor `Quote Float`
- **THEN** the C# has a `closed record Price` and a `sealed record Quote(double Value) : Price`

#### Scenario: Name conflict in the mixed shape

- **WHEN** a mixed custom type has a case name equal to a type name of the module
- **THEN** generation reports an error that names both

### Requirement: Modules are namespaces and static classes

A module that has only types SHALL be written at namespace level. A module that has values SHALL be written as a static class with nested types. A module below a static-class module SHALL be a nested static class.

#### Scenario: Types only

- **WHEN** a module has only types
- **THEN** the C# has the types directly in the namespace

#### Scenario: Module with values

- **WHEN** a module has a value
- **THEN** the C# has a static class that holds the value

### Requirement: Equality members are written only for the IEEE option

The backend SHALL write the Morphir equality members to a record or a union only when the IEEE equality option is on for it. The members are `MorphirEquals`, `MorphirHashCode`, `Equals` and `GetHashCode`, and `==` and `!=` for a union.

#### Scenario: Option off

- **WHEN** no equality option is set
- **THEN** the generated records have no `MorphirEquals` member

#### Scenario: Option on

- **WHEN** the IEEE option is on for a record
- **THEN** the record has the four members and a `NaN` field makes two records unequal

### Requirement: Value classes of type variables

The backend SHALL write `where T : IComparable<T>` for a type variable of the class `comparable` and `where T : INumber<T>` for one of the class `number`. It SHALL take the class from the IR carrier, else from the first word of the name. A model with no carrier SHALL still generate.

#### Scenario: Generic comparable function

- **WHEN** a model has `maximum : comparable -> comparable -> comparable`
- **THEN** the C# method has `where TComparable : IComparable<TComparable>` and a program that calls it gives the model result

#### Scenario: Generic number function

- **WHEN** a model has `twice : number -> number` and calls it with an Int and with a Float
- **THEN** the C# method has `where TNumber : INumber<TNumber>`, each call gives the model result, and a call with a String gives `unsupported-numeric` at the call

### Requirement: Value classes are checked at each call

At each call of a generic function, the backend SHALL refuse a type that is not of the class of its type variable with `unsupported-comparable` or `unsupported-numeric`. A type variable of the class `number` SHALL also take the ordering operations. The backend SHALL refuse a generic function over `appendable` or `compappend` with `unsupported-appendable`, and a dictionary or a set with a key of a type variable with `unsupported-comparable`.

#### Scenario: Call with a type outside the class

- **WHEN** a model calls `maximum : comparable -> comparable -> comparable` with two Bool values
- **THEN** generation fails with `unsupported-comparable` at the call

#### Scenario: Generic appendable function

- **WHEN** a model has a function over the type variable `appendable`
- **THEN** generation fails with `unsupported-appendable`

### Requirement: Equality on function types fails at generation time

The backend SHALL infer equality for a type variable that reaches an equality, and SHALL refuse with `unsupported-equality` an equality on a type that holds a function, also through a type variable, a closed-base field or a union case.

#### Scenario: Function through a type variable

- **WHEN** a generic function compares two values of a type variable with `==`, and a caller gives it a record with a function field
- **THEN** generation fails with `unsupported-equality` at the call, and the message names the path to the function type

## REMOVED Requirements

### Requirement: Compiler-host reuse evidence

**Reason**: The source generator is now product code with its own requirements in `csharp-build-generation`, tested in the compilers of three SDKs. The claim "untested hosts are not claimed" moves to that capability, which names Visual Studio, Rider and VS Code as not tested.

**Migration**: Use the generator requirements of `csharp-build-generation`. The in-memory entry point stays callable from a test adapter.
