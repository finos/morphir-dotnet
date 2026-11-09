# fsharp-source-frontend Specification

## Purpose
Turns an ordered set of F# source documents into a Morphir library model for a defined core subset of F#. Every construct outside that subset is reported with a precise, stable diagnostic, so the frontend never produces an approximate model.

## Requirements

### Requirement: Ordered source documents form one package
The frontend SHALL compile an ordered list of F# source documents as one package with a supplied package name. Document order SHALL be the F# compilation order. Each document SHALL keep its URI and text, and diagnostics SHALL refer to that URI. An empty document list or a missing package name SHALL fail before analysis.

#### Scenario: Later file uses an earlier file
- **WHEN** `Rates.fs` defines `dailyRate` and the later `Pricing.fs` calls it
- **THEN** compilation succeeds and the `Pricing` value refers to the fully qualified `Rates` value

#### Scenario: Reversed order is an F# error
- **WHEN** the same two documents are supplied with `Pricing.fs` first
- **THEN** compilation fails with an `fsharp.compiler` error located in `Pricing.fs` and produces no model

#### Scenario: Missing package name
- **WHEN** documents are supplied without a package name
- **THEN** compilation fails before analysis with an invocation diagnostic naming the missing package name

### Requirement: F# compiler errors stop compilation
Errors from F# parsing or type checking SHALL become `fsharp.compiler` error diagnostics that keep the F# message and the error number. When any such error exists, the frontend SHALL produce no model and SHALL NOT also report unsupported-construct diagnostics for code the compiler rejected.

#### Scenario: Type error
- **WHEN** a value annotated as `int` is given a `string` literal
- **THEN** the result has one `fsharp.compiler` error whose data carries the F# error number (1), its range points to the literal, and there is no model

### Requirement: Warnings and information accompany a successful model
A successful compilation SHALL return its model together with every diagnostic of severity `warning`, `info` or `hint` that any stage produced. F# compiler warnings and messages SHALL become `fsharp.compiler` diagnostics with their F# severity and error number. Only an `error` diagnostic SHALL prevent a model.

#### Scenario: Incomplete match warning
- **WHEN** a value has a `match` that F# reports as incomplete (FS0025) and nothing else is wrong
- **THEN** compilation succeeds with a model, and the result has one `fsharp.compiler` warning whose data carries error number 25, at the match's range

#### Scenario: Warnings kept when a later error fails the run
- **WHEN** a source has an F# compiler warning and also a refused construct
- **THEN** the result has no model and holds both the warning and the refusal error

### Requirement: Supported type definitions
The frontend SHALL lower these to the matching Morphir type definitions, keeping type parameters, field order, constructor argument order and access: type abbreviations; records; unions with named or positional fields; generic parameters; tuples; functions; `unit`; the primitives `bool`, `int`, `int64`, `float`, `decimal`, `char` and `string`; and `option`, `Result` and `list`.

#### Scenario: Record and union
- **WHEN** a module defines `type Car = { Plate: string; Seats: int }` and `type Status = Available | Rented of days: int`
- **THEN** the model has a record type `car` with fields `plate` and `seats` in that order, and a custom type `status` with constructors `available` and `rented` taking one `Int` argument

#### Scenario: Standard generic types
- **WHEN** a field has type `Result<int option list, string>`
- **THEN** its Morphir type is SDK `Result` of `String` and SDK `List` of SDK `Maybe` of SDK `Int`, with the SDK `Result` argument order

### Requirement: Supported value definitions
The frontend SHALL lower these, keeping currying, evaluation structure, lexical scope and branch order: literals; variables; references; constructor application; tuples; lists; record construction; field access; record update; application; lambdas; `let`; `let rec` and mutually recursive `let rec ... and`; `if`/`then`/`else`; and `match`.

#### Scenario: Curried function with a local binding
- **WHEN** `let total rate days = let base = rate * days in base + 10` is compiled
- **THEN** the value has two inputs, and its body is a let-definition of `base` followed by an SDK `add` application, with no extra lambdas or bindings

#### Scenario: Record update
- **WHEN** a value evaluates `{ car with Seats = 4 }`
- **THEN** the body is a record update of `car` that sets only `seats`

### Requirement: Supported patterns
The frontend SHALL lower wildcard, variable, literal, tuple, constructor, empty-list, cons, `as` and unit patterns in `match` and in function and `let` parameters. It SHALL keep case order. Guards, OR patterns, active patterns, record patterns, array patterns and type tests SHALL be refused.

#### Scenario: Ordered match
- **WHEN** a `match` has cases `Rented 0`, `Rented n` and `Available` in that order
- **THEN** the Morphir pattern match has three cases in the same order with the same bindings

#### Scenario: Reduced-away and nested matches keep their source structure
- **WHEN** the compiler reduces a variable, tuple, literal or unit match, or changes the typed target ranges of a nested match
- **THEN** the model still has the original source match clauses in source order, with their original bindings and body structure, and compilation does not require a surviving decision-tree node or an exact target-range join

#### Scenario: Constructor field arity
- **WHEN** one union case has two fields `of int * string` and another has one tuple-valued field `of (int * string)`
- **THEN** their lowered patterns have two constructor arguments and one tuple constructor argument respectively, with compiler-supplied component types in source order

#### Scenario: Guard is refused
- **WHEN** a match case uses `when n > 3`
- **THEN** compilation fails with `fsharp.unsupported_pattern_guard` at the guard's range

### Requirement: SDK mapping is explicit
Operators and library functions SHALL be lowered only through a documented mapping to Morphir SDK names: arithmetic, comparison, equality, Boolean operators, `|>`, `<|`, `>>` and `<<`, and the mapped `List`, `Option`, `Result` and `String` functions. An unmapped external function, type or member SHALL be refused with `fsharp.unsupported_reference`, naming the F# symbol.

#### Scenario: Pipe and arithmetic
- **WHEN** `xs |> List.map (fun x -> x + 1)` is compiled
- **THEN** the body applies SDK `List.map` to a lambda whose body applies SDK `Basics.add`, and no pipe function remains

#### Scenario: Unmapped library call
- **WHEN** a value calls `System.Math.Round`
- **THEN** compilation fails with `fsharp.unsupported_reference` naming `System.Math.Round` at the call's range

#### Scenario: Boolean operators keep short-circuit evaluation
- **WHEN** `x <> 0 && 10 / x > 1` is compiled
- **THEN** the body is an if-then-else whose condition is `x <> 0`, whose then branch holds the division, and whose else branch is `false`

#### Scenario: Operand type selects the SDK operation
- **WHEN** `a + b` is compiled with `decimal` operands and `a / b` with `int` operands
- **THEN** the bodies apply SDK `Decimal.add` and SDK `Basics.integerDivide`, and `a + b` with `int64` operands fails with `fsharp.unsupported_reference` at the expression's range

### Requirement: SDK mapping entries are checked
Each SDK mapping entry SHALL be an operation of the reviewed .NET SDK inventory with the same arity, or SHALL name why it is outside that contract. The F# generator SHALL write each entry as F# that the frontend maps back to the same SDK name.

#### Scenario: Entry outside the SDK contract
- **WHEN** an entry maps `not` to SDK `Basics.not`, which the SDK inventory does not list
- **THEN** the entry names its reason, and the table tests fail if the reason is empty or if the inventory lists the operation

### Requirement: Unsupported constructs have specific diagnostics
Every construct outside the supported subset SHALL produce an error with a stable code `fsharp.unsupported_<construct>`, a message naming the construct, and the construct's source range. The published rule catalog SHALL list each code. All refusals in a run SHALL be reported together.

#### Scenario: Refused families
- **WHEN** the conformance corpus compiles one example each of: `mutable`, assignment and loops; classes, interfaces, members and object expressions; computation, sequence and comprehension expressions and arrays; exceptions; active patterns, units of measure and quotations; anonymous records, struct types and inline functions; attributes with semantic effect, type tests and casts; `#if` conditional compilation; and `.fsi` signature files
- **THEN** each example fails with its own catalog code at its own range, and no model is produced

#### Scenario: Several problems in one run
- **WHEN** one document contains `let mutable x = 0` and a class declaration
- **THEN** the result has one `fsharp.unsupported_mutable` error and one `fsharp.unsupported_class` error, each at its own range, and no model

#### Scenario: Conditional compilation
- **WHEN** a document contains `#if DEBUG`
- **THEN** compilation fails with `fsharp.unsupported_conditional_compilation` at the directive. Because defines are not chosen in this delivery, the frontend does not choose a branch silently.

#### Scenario: Catalog is complete
- **WHEN** the published coverage reference is compared with the frontend's rule catalog
- **THEN** every catalog code appears in the reference with an example, and every refused construct in the conformance corpus maps to exactly one catalog code

### Requirement: Diagnostics use the MEP diagnostic shape
Each diagnostic SHALL have:

- a severity of `error`, `warning`, `info` or `hint`;
- an optional stable code and a message;
- an optional location `{uri, range}`, whose positions are zero-based and whose `character` counts UTF-16 code units with an exclusive end;
- optional related locations, and optional structured data.

Diagnostics SHALL be in document order, then position order.

#### Scenario: Range after a non-BMP character
- **WHEN** the line `let s = "😀" + mutable` has an error on `mutable`
- **THEN** the start character counts the emoji as two UTF-16 units, and the line number is zero-based

### Requirement: Names follow Morphir naming
F# identifiers SHALL map to Morphir names by splitting camelCase and PascalCase words into lowercase segments. A module's path SHALL be its namespace and module names with the package path removed from the front; file paths SHALL NOT affect it. A module outside the package path SHALL fail with `fsharp.module_outside_package`. Names that collide in one scope SHALL fail with `fsharp.name_collision`.

#### Scenario: Name segments
- **WHEN** package `acme/car-rental` has namespace `Acme.CarRental` declaring module `Pricing` with value `dailyRateFor`
- **THEN** the package path is `[["acme"], ["car", "rental"]]`, the module path is `[["pricing"]]` and the value name is `["daily", "rate", "for"]`

#### Scenario: Module outside the package
- **WHEN** package `acme/car-rental` compiles a module declared as `Other.Pricing`
- **THEN** compilation fails with `fsharp.module_outside_package` at the module declaration

#### Scenario: Colliding names
- **WHEN** one module declares values `taxRate` and `TaxRate`
- **THEN** compilation fails with `fsharp.name_collision`, with related locations for both declarations

### Requirement: Identifiers without Morphir words are refused
An F# identifier that gives no Morphir name, such as a custom operator, SHALL fail with `fsharp.unsupported_name` at the identifier, unless the declaration has `[<MorphirName>]`.

#### Scenario: Custom operator
- **WHEN** a module defines `let (+++) a b = a + b`
- **THEN** compilation fails with `fsharp.unsupported_name` at the operator definition

### Requirement: MorphirName sets a Morphir name
A value, type, union case, record field or module with `[<Morphir.MorphirName(name)>]` from `Morphir.FSharp.Annotations` SHALL have the Morphir name parsed from `name` in canonical Morphir name syntax. A name that does not parse SHALL fail with `fsharp.invalid_morphir_name`. `[<CompiledName>]` SHALL NOT change a Morphir name.

#### Scenario: Named operator
- **WHEN** a module defines `[<MorphirName("triple-add")>] let (+++) a b = a + b`
- **THEN** the value's Morphir name is `["triple", "add"]`

#### Scenario: Invalid declared name
- **WHEN** a value has `[<MorphirName("Triple Add!")>]`
- **THEN** compilation fails with `fsharp.invalid_morphir_name` at the value

### Requirement: Access follows exposed modules and F# visibility
A module SHALL be public when the exposed-module list is absent or names it, and private otherwise. An empty list SHALL expose no module. A `private` or `internal` declaration SHALL be private. An exposed module name that matches no compiled module SHALL be a `compile.unknown_exposed_module` error.

#### Scenario: Selected exposure
- **WHEN** modules `Pricing` and `Rates` are compiled with exposed modules `["Pricing"]`
- **THEN** `Pricing` is public, `Rates` is private, and a `private` value inside `Pricing` is private

#### Scenario: Internal module
- **WHEN** `module internal Rates` declares a value without an access keyword and an `internal` value
- **THEN** `Rates` is private, the first value is public and the `internal` value is private, because a declaration takes its access from its own keyword and not from its module

### Requirement: Values carry inferred types and source locations
Every lowered value and pattern node SHALL carry its F# inferred type as a Morphir type. In IR v4, every node SHALL also carry the source range from which it was lowered. Classic IR has no place for source ranges. Generic F# types SHALL become Morphir type variables with stable names.

#### Scenario: Inferred type on a lambda
- **WHEN** `fun x -> x + 1` is compiled where `x` is inferred as `int`
- **THEN** in every IR version, the lambda's type is SDK `Int -> Int` and its argument pattern's type is SDK `Int`, and in IR v4 both nodes carry their source ranges

### Requirement: Analysis evidence preserves the original checked program
The frontend SHALL use original syntax for match structure and locations, and compiler-checked information for types and symbol identity. Analysis-only transformations SHALL preserve checked binding types, generalization, constraints, source-symbol resolution and evaluation structure. Generated witnesses SHALL NOT appear in emitted models or public source diagnostics.

#### Scenario: Polymorphic match result
- **WHEN** `let identity = match () with | () -> fun value -> value` is used at both `int` and `string`
- **THEN** compilation preserves its generic function type, and generated evidence bindings do not restrict it to one use type

### Requirement: Unavailable match evidence fails explicitly
Missing, ambiguous or changed match evidence SHALL fail with `fsharp.unsupported_match_shape` at the original match and no model. The frontend SHALL NOT guess types, reorder clauses or flatten matches to bypass unavailable evidence.

#### Scenario: Invalid match evidence
- **WHEN** required checked match evidence is missing, ambiguous or changes an original binding signature
- **THEN** compilation fails with `fsharp.unsupported_match_shape` located in the original document, emits no model and exposes no generated-source path

### Requirement: Frontend conformance is defined by implementation-independent cases
The frontend's conformance SHALL be defined by portable Morphir Compatibility Kit cases. Each case holds its sources, settings and independently written expectations: IR, diagnostics and call results. No expected result SHALL be produced by the implementation under test. Any implementation SHALL be able to run the cases through its command line or a MEP guest, and get a verdict report that names it and the case set.

#### Scenario: Another implementation is checked
- **WHEN** the `frontend-fsharp` cases are run against a different `morphir` executable through its command line
- **THEN** a verdict report is written that holds that executable's identity and SHA-256, the case-set SHA-256, and one verdict per case and IR version

#### Scenario: Expectations are not regenerated
- **WHEN** a frontend change alters the IR it produces for a case
- **THEN** the case fails until a reviewer edits the expected IR by hand. No tool rewrites expectations from the frontend's output.

### Requirement: Compilation is deterministic
The same documents, package settings and frontend version SHALL produce the same model and diagnostics, regardless of machine, current directory or environment. Absolute paths outside the supplied URIs SHALL NOT appear in a model or diagnostic.

#### Scenario: Repeat compilation
- **WHEN** the same input is compiled twice in different working directories
- **THEN** the serialized models and diagnostic lists are byte-identical
