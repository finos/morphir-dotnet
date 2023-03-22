module Morphir.IR.Value

open Morphir.IR.Type
open Morphir.IR.Literal
open Morphir.IR.Name
open Morphir.IR.FQName
open Morphir.SDK.List

[<AutoOpen>]
module Parameter =
    type Parameter<'TA, 'VA> = Name * 'VA * Type<'TA>

[<AutoOpen>]
module ParameterList =
    type ParameterList<'TA, 'VA> = List<Parameter<'TA, 'VA>>

    let map f (parameters: ParameterList<'TA, 'VA>) =
        parameters
        |> List.map f

/// Type that represents a value/expression.
type Value<'TA, 'VA> =
    /// A literal represents a fixed value in the IR. We only allow values of basic types: bool, char, string, int, float.
    | Literal of Attributes: 'VA * Value: Literal
    | Constructor of Attributes: 'VA * FullyQualifiedName: FQName
    | Tuple of Attributes: 'VA * Elements: Value<'TA, 'VA> list
    | List of Attributes: 'VA * Items: Value<'TA, 'VA> list
    | Record of Attributes: 'VA * Fields: (Name * Value<'TA, 'VA>) list
    | Variable of Attributes: 'VA * Name: Name
    | Reference of Attributes: 'VA * FullyQualifiedName: FQName
    | Field of Attributes: 'VA * SubjectValue: Value<'TA, 'VA> * FieldName: Name
    | FieldFunction of Attributes: 'VA * FieldName: Name
    | Apply of Attributes: 'VA * Function: Value<'TA, 'VA> * Argument: Value<'TA, 'VA>
    | Lambda of Attributes: 'VA * ArgumentPattern: Pattern<'VA> * Body: Value<'TA, 'VA>
    | LetDefinition of
        Attributes: 'VA *
        ValueName: Name *
        ValueDefinition: Definition<'TA, 'VA> *
        InValue: Value<'TA, 'VA>
    | LetRecursion of
        Attributes: 'VA *
        ValueDefinitions: (Name * Definition<'TA, 'VA>) list *
        InValue: Value<'TA, 'VA>
    | Destructure of
        Attributes: 'VA *
        Pattern: Pattern<'VA> *
        ValueToDestruct: Value<'TA, 'VA> *
        InValue: Value<'TA, 'VA>
    | IfThenElse of
        Attributes: 'VA *
        Condition: Value<'TA, 'VA> *
        ThenBranch: Value<'TA, 'VA> *
        ElseBranch: Value<'TA, 'VA>
    | PatternMatch of
        Attributes: 'VA *
        BranchOutOn: Value<'TA, 'VA> *
        Cases: (Pattern<'VA> * Value<'TA, 'VA> list)
    | UpdateRecord of
        Attributes: 'VA *
        ValueToUpdate: Value<'TA, 'VA> *
        FieldsToUpdate: Value<'TA, 'VA>
    | Unit of Attributes: 'VA

/// A value without any additional information.
and RawValue = Value<Unit, Unit>
/// A value with type information.
and TypedValue = Value<Unit, Type<Unit>>

and Pattern<'A> =
    | WildCardPattern of Attributes: 'A
    | AsPattern of Attributes: 'A * Pattern: Pattern<'A> * Name: Name
    | TuplePattern of Attributes: 'A * ElementPatterns: Pattern<'A> list
    | RecordPattern of Attributes: 'A * FieldNames: Name list
    | ConstructorPattern of Attributes: 'A * FQName * Pattern<'A> list
    | EmptyListPattern of Attributes: 'A
    | HeadTailPattern of Attributes: 'A * Pattern<'A> * Pattern<'A>
    | LiteralPattern of Attributes: 'A * Literal

/// Type that represents a value or function specification. The specification of what the value or function
/// is without the actual data or logic behind it.
and Specification<'TA> = {
    Inputs: List<Name * Type<'TA>>
    Output: Type<'TA>
}

/// Type that represents a value or function definition. A definition is the actual data or logic as opposed to a specification
/// which is just the specification of those. Value definitions can be typed or untyped. Exposed values have to be typed.
and Definition<'TA, 'VA> = {
    InputTypes: ParameterList<'TA, 'VA>
    OutputType: Type<'TA>
    Body: Value<'TA, 'VA>
}

let specification (inputs: List<Name * Type<'a>>) (output: Type<'a>) : Specification<'a> = {
    Inputs = inputs
    Output = output
}

/// Represents a function invocation. We use currying to represent function invocations with multiple arguments.
let inline apply
    (attributes: 'va)
    (func: Value<'ta, 'va>)
    (argument: Value<'ta, 'va>)
    : Value<'ta, 'va> =
    Apply(attributes, func, argument)

let inline constructor (attributes: 'va) (fqName: FQName) : Value<'ta, 'va> =
    Constructor(attributes, fqName)

/// Turns a definition into a specification by removing implementation details.
let definitionToSpecification (def: Definition<'TA, 'VA>) : Specification<'TA> = {
    Inputs =
        def.InputTypes
        |> ParameterList.map (fun (name, _, tpe) -> name, tpe)
    Output = def.OutputType
}

/// Turn a value definition into a value by wrapping the body value as needed based on the number of arguments the definition has.
let rec definitionToValue def =
    match def.InputTypes with
    | [] -> def.Body
    | (firstArgName, va, _) :: restOfArgs ->
        Lambda(
            va,
            AsPattern(va, WildCardPattern(va), firstArgName),
            (definitionToValue { def with InputTypes = restOfArgs })
        )
