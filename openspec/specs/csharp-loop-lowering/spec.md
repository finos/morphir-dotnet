# csharp-loop-lowering Specification

## Purpose
Lets C# authors write loops in a Morphir model. The frontend lowers a loop to a collection traversal or to recursion where it can, and refuses a loop that cannot lower, with the reason. The backend writes the matching loop or functional form by profile, so the model goes from C# to Morphir IR and back with the same IR.

Example: a `foreach` that writes one local is a fold, and a `while` that writes locals only is a local recursion.

```csharp
decimal sum = 0;
foreach (var p in prices) sum += p;      // List.foldl
```

## Requirements

### Requirement: Loops lower to traversals or recursion

The frontend SHALL lower a C# loop by its shape: a `foreach` that adds a projection to a list is `List.map`; one that adds an element under a condition is `List.filter`; one that adds a projection under a condition is `List.filterMap`; one that writes one local is `List.foldl`; one that writes two or more locals is `List.foldl` with a tuple state. A `while` or `for` loop that writes locals only SHALL be `LetRecursion`, and the written locals SHALL be its parameters.

#### Scenario: Map

- **WHEN** a `foreach` body is `result.Add(f(x))`
- **THEN** the model has `List.map` with the lowered `f`

#### Scenario: Fold with two locals

- **WHEN** a `foreach` writes `count` and `sum`
- **THEN** the model has `List.foldl` with a tuple of two as its state

#### Scenario: While loop

- **WHEN** a `while` loop writes `acc` and `n` and nothing else
- **THEN** the model has a `LetRecursion` whose parameters are `acc` and `n`

### Requirement: Exits become base cases

A loop with `break` or `return` SHALL lower to `LetRecursion`, and the exit SHALL become the base case. The number of exits SHALL come from control-flow analysis of the loop body.

#### Scenario: Break

- **WHEN** a `foreach` sums values and stops at the first negative value with `break`
- **THEN** the model has a `LetRecursion` with one base case for the break

### Requirement: Loops with outside effects are refused

A loop that writes a field, a property or an array element, or reads state that it does not declare, SHALL be refused with an error that names the effect and the source range.

#### Scenario: Array element

- **WHEN** a `for` loop writes `items[i]`
- **THEN** the result has an error that says the loop writes an array element

#### Scenario: Field

- **WHEN** a `foreach` increments a static field
- **THEN** the result has an error that names the field

### Requirement: One recognizer gives one answer

The analyzer diagnostic and the lowering SHALL use one recognizer. A loop that the analyzer reports as refused SHALL be refused by the frontend with the same code and reason, and a loop that the analyzer accepts SHALL lower.

#### Scenario: IDE and compile agree

- **WHEN** ten loops of the probe set run in the analyzer and in the frontend
- **THEN** the set of refused loops is the same, with the same reasons

### Requirement: The backend writes matching forms by profile

For each IR shape of the transform table, the backend SHALL write the loop form under the imperative profile and the functional form under the functional profile. The table has: `List.map`, `List.filter`, `List.filterMap`, `List.foldl`, `LetRecursion` in tail position, and other `LetRecursion`.

#### Scenario: Imperative profile

- **WHEN** a model with `List.map` is generated with the imperative profile
- **THEN** the C# has a `foreach` that adds to a list

#### Scenario: Functional profile

- **WHEN** the same model is generated with the functional profile
- **THEN** the C# has a `Select` call

#### Scenario: Filter map

- **WHEN** a model with `List.filterMap` is generated with the functional profile
- **THEN** the C# has one call to `FilterMap` of the SDK and not `Where` followed by `Select`

### Requirement: Tail recursion is a while loop

A tail-recursive `LetRecursion` SHALL be written as a `while` loop with state locals in every profile. A `LetRecursion` that is not tail-recursive SHALL be written as a local recursive function. A local recursive function for a tail call SHALL be written only when an option asks for it.

#### Scenario: Tail call under the functional profile

- **WHEN** a tail-recursive `LetRecursion` is generated with the functional profile
- **THEN** the C# has a `while` loop with state locals

#### Scenario: Not a tail call

- **WHEN** a `LetRecursion` calls itself in a non-tail position
- **THEN** the C# has a local recursive function

### Requirement: Round trip gives the same IR

For each row of the transform table and each profile, the IR that the frontend gives for the generated C# SHALL equal the IR that the backend received after the target-side passes, with source locations left out. The frontend SHALL also read `Select`, `Where`, `Aggregate` and local recursive functions as the same IR as the loop forms.

#### Scenario: Round trip of each row

- **WHEN** each row of the table is generated under each profile and read back
- **THEN** the IR is equal to the input

#### Scenario: Where then Select

- **WHEN** C# has `Where` followed by `Select`
- **THEN** the model has `List.map` over `List.filter`, and not `List.filterMap`
