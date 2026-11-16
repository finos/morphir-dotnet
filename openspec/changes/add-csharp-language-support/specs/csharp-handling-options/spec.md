# Spec Delta

## Purpose

Lets a user choose how Morphir handles a C# model, in code with attributes or in `morphir.toml`, whichever the user prefers. Each option has two carriers with the same values and one effect, and one precedence order picks the value.

Example:

```csharp
[assembly: MorphirModel]
[MorphirEquality(FloatEquality.Ieee)]
public partial record Quote(double Value);   // NaN is not equal to NaN for this type
```

## ADDED Requirements

### Requirement: Each option has an attribute and a configuration key

Each handling option SHALL be settable with a C# attribute and with a configuration key, with the same values. The two carriers SHALL feed one option record, so one rule has one implementation. The options are: model project, loop lowering, traversal form, tail recursion form, and float equality. The key of a backend option SHALL be in `[codegen.csharp]` of `morphir.toml`, and the key of loop lowering SHALL be in `[frontend.csharp]`, which `morphir compile` gives to the frontend in the `extra` member of the MEP compile options. The key of the model project switch SHALL be the MSBuild property `MorphirModel`, because the analyzer runs in the build, which does not read `morphir.toml`.

#### Scenario: Same effect

- **WHEN** one project sets the traversal form with an attribute and a second project sets it with a key
- **THEN** the generated C# of the two is equal

#### Scenario: Same effect for a frontend option

- **WHEN** one project sets the loop lowering with `[assembly: MorphirLoop(LoopLowering.Recursion)]` and a second project sets `loop_lowering = "recursion"` in `[frontend.csharp]`
- **THEN** the IR of the two is equal

#### Scenario: Malformed frontend key

- **WHEN** `[frontend.csharp]` has `loop_lowering = "sideways"` or a key that the frontend does not know
- **THEN** `morphir compile` fails before the frontend runs, with exit code 2, and publishes no IR

#### Scenario: Model project by key

- **WHEN** a project sets the MSBuild property `MorphirModel` to `true` and has no `[assembly: MorphirModel]`
- **THEN** the analyzer reports in the project

### Requirement: The nearest carrier wins

The effective value SHALL be the first of: the command line, the attribute on the member, the attribute on the type, the attribute on the assembly, `morphir.toml`, the profile default.

#### Scenario: Member over type

- **WHEN** a type attribute sets IEEE equality and a member attribute sets plain equality
- **THEN** the member has plain equality and the rest of the type has IEEE equality

#### Scenario: Member over key

- **WHEN** `[frontend.csharp]` sets `loop_lowering = "recursion"` and a method has `[MorphirLoop(LoopLowering.Traversal)]`
- **THEN** the loop of the method is `List.foldl`, and a loop of another method is a `LetRecursion`

#### Scenario: Command line over attribute

- **WHEN** a command-line option sets a value and an attribute sets another
- **THEN** the command-line value is used

### Requirement: A frontend option changes the IR, a backend option does not

Loop lowering SHALL change the IR that the frontend gives. Traversal form, tail recursion form and float equality SHALL change only the C# text that the backend writes, and the IR SHALL stay the same.

#### Scenario: Backend option and IR

- **WHEN** a model is compiled twice with different traversal forms
- **THEN** the two models are equal and the generated C# differs

#### Scenario: Loop option and IR

- **WHEN** a loop has `[MorphirLoop(LoopLowering.Recursion)]`
- **THEN** the model has `LetRecursion` for the loop and not `List.foldl`

### Requirement: Backend options travel with the model

The frontend SHALL write a backend option as metadata of the declaration. The backend SHALL read it from the model and write the attribute back, so `morphir generate` on an IR file keeps the option.

#### Scenario: Round trip of an option

- **WHEN** C# with `[MorphirEmit(Traversal = TraversalForm.Functional)]` is compiled and generated back
- **THEN** the generated C# has the attribute and the functional form

### Requirement: An option does not make refused code accepted

An option SHALL select between handlings that the subset supports. A loop that the recognizer refuses SHALL stay refused under every option.

#### Scenario: Refused loop with an option

- **WHEN** a loop writes a field and has `[MorphirLoop(LoopLowering.Recursion)]`
- **THEN** the result has the same refusal as without the attribute

### Requirement: Equality is plain C# equality by default

Generated records and unions SHALL use the equality of plain C# unless the IEEE option is on. With the IEEE option, a record SHALL be not equal to itself when it holds `NaN`, and the backend SHALL add the equality members to the record or union.

#### Scenario: Default equality

- **WHEN** two generated records hold `NaN` and no option is set
- **THEN** the records are equal

#### Scenario: IEEE equality

- **WHEN** the IEEE option is set for the type
- **THEN** two records that hold `NaN` are not equal

### Requirement: Equality in authored code needs a partial type

An equality option on a type SHALL need `partial` on that type, because an attribute cannot change how compiled C# compares two records. A type that is not `partial` SHALL give a diagnostic.

#### Scenario: Type that is not partial

- **WHEN** `[MorphirEquality(FloatEquality.Ieee)]` is on a record that is not `partial`
- **THEN** the analyzer reports a diagnostic that asks for `partial`

### Requirement: Attributes need no configuration file

A project SHALL get every option behavior from attributes alone. The attribute types SHALL come from a small assembly of the package that targets `netstandard2.0`.

#### Scenario: No morphir.toml

- **WHEN** a project has attributes and no `morphir.toml`
- **THEN** the build applies the attribute values
