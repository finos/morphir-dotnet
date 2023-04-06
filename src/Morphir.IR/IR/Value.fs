module Morphir.IR.Value

open Morphir
open Morphir.IR.Type
open Morphir.IR.Literal
open Morphir.IR.Name
open Morphir.IR.FQName
open Morphir.SDK.Dict
open Morphir.SDK.List
open Morphir.SDK

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
        ValueDefinitions: Dict<Name, Definition<'TA, 'VA>> *
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
        Cases: (Pattern<'VA> * Value<'TA, 'VA>) list
    | UpdateRecord of
        Attributes: 'VA *
        ValueToUpdate: Value<'TA, 'VA> *
        FieldsToUpdate: Dict<Name, Value<'TA, 'VA>>
    | Unit of Attributes: 'VA

    member this.Attributes =
        match this with
        | Literal (attributes, _) -> attributes
        | Constructor (attributes, _) -> attributes
        | Tuple (attributes, _) -> attributes
        | List (attributes, _) -> attributes
        | Record (attributes, _) -> attributes
        | Variable (attributes, _) -> attributes
        | Reference (attributes, _) -> attributes
        | Field (attributes, _, _) -> attributes
        | FieldFunction (attributes, _) -> attributes
        | Apply (attributes, _, _) -> attributes
        | Lambda (attributes, _, _) -> attributes
        | LetDefinition (attributes, _, _, _) -> attributes
        | LetRecursion (attributes, _, _) -> attributes
        | Destructure (attributes, _, _, _) -> attributes
        | IfThenElse (attributes, _, _, _) -> attributes
        | PatternMatch (attributes, _, _) -> attributes
        | UpdateRecord (attributes, _, _) -> attributes
        | Unit attributes -> attributes

    interface Expression<'VA> with
        member this.Attributes = this.Attributes

/// A value without any additional information.
and RawValue = Value<Unit, Unit>
/// A value with type information.
and TypedValue = Value<Unit, Type<Unit>>

and Pattern<'A> =
    | WildcardPattern of Attributes: 'A
    | AsPattern of Attributes: 'A * Pattern: Pattern<'A> * Name: Name
    | TuplePattern of Attributes: 'A * ElementPatterns: Pattern<'A> list
    | ConstructorPattern of Attributes: 'A * FQName * Pattern<'A> list
    | EmptyListPattern of Attributes: 'A
    | HeadTailPattern of Attributes: 'A * Pattern<'A> * Pattern<'A>
    | LiteralPattern of Attributes: 'A * Literal
    | UnitPattern of Attributes: 'A

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

let definition
    (inputTypes: ParameterList<'TA, 'VA>)
    (outputType: Type<'TA>)
    (body: Value<'TA, 'VA>)
    : Definition<'TA, 'VA> =
    {
        InputTypes = inputTypes
        OutputType = outputType
        Body = body
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

let inline unit attributes = Unit attributes

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
            AsPattern(va, WildcardPattern(va), firstArgName),
            (definitionToValue { def with InputTypes = restOfArgs })
        )

let rec patternToString (pattern: Pattern<'a>) : string =
    match pattern with
    | WildcardPattern _ -> "_"
    | AsPattern (_, WildcardPattern (_), alias) -> Name.toCamelCase alias
    | AsPattern (_, subjectPattern, alias) ->
        $"{patternToString subjectPattern} as {Name.toCamelCase alias}"
    | TuplePattern (_, elements) ->
        let elementsString =
            elements
            |> List.map patternToString
            |> String.join ", "

        $"( {elementsString} )"
    | ConstructorPattern (attributes, fqName, argPatterns) ->
        let constructorString = FQName.toReferenceName fqName

        constructorString
        :: (argPatterns
            |> List.map patternToString)
        |> String.join ", "
    | EmptyListPattern _ -> "[]"
    | HeadTailPattern (_, headPattern, tailPattern) ->
        let headString = patternToString headPattern
        let tailString = patternToString tailPattern
        $"{headString} :: {tailString}"
    | LiteralPattern (_, lit) -> Literal.toString lit
    | UnitPattern _ -> "()"

let rec toString (value: Value<'ta, 'va>) : string = stringBuffer {
    match value with
    | Literal (_, lit) -> Literal.toString lit
    | Constructor (_, fQName) -> FQName.toReferenceName fQName
    | Tuple (_, elems) ->
        $"""( {elems
               |> List.map toString
               |> String.join ", "} )"""
    | List (_, items) ->
        $"""[ {items
               |> List.map toString
               |> String.join ", "} ]"""
    | Record (_, fields) ->
        let fieldStrings =
            fields
            |> List.map (fun (name, value) -> $"{Name.toCamelCase name} = {toString value}")
            |> String.join ", "

        $"{{ {fieldStrings} }}"
    | Variable (_, name) -> Name.toCamelCase name
    | Reference (_, fqName) -> FQName.toReferenceName fqName
    | Field (_, subject, fieldName) -> $"{toString subject}.{Name.toCamelCase fieldName}"
    | FieldFunction (_, fieldName) -> $".{Name.toCamelCase fieldName}"
    | Apply (_, func, arg) -> String.join " " [ toString func; toString arg ]
    | Lambda (_, pattern, body) -> $"""(\{patternToString pattern} -> {toString body})"""
    | LetDefinition (_, name, def, inValue) ->
        let args =
            def.InputTypes
            |> List.map (fun (name, _, _) -> Name.toCamelCase name)
            |> String.join " "

        $"let {Name.toCamelCase name} {args} = {toString def.Body} in {toString inValue}"
    | LetRecursion (_, defs, inValue) ->
        let args (def: Definition<'ta, 'va>) =
            def.InputTypes
            |> List.map (fun (name, _, _) -> Name.toCamelCase name)

        let defStrings =
            defs
            |> Dict.toList
            |> List.map (fun (name, def) ->
                let argsString =
                    args def
                    |> String.join " "

                $"{Name.toCamelCase name} {argsString} = {toString def.Body}"
            )

        $"""let {defStrings
                 |> String.join "; "} in {toString inValue}"""
    | Destructure (_, bindPattern, bindValue, inValue) ->
        $"""let {patternToString bindPattern} = {toString bindValue} in {toString inValue}"""
    | IfThenElse (_, condition, thenBranch, elseBranch) ->
        $"""if {toString condition} then {toString thenBranch} else {toString elseBranch}"""
    | PatternMatch (_, subject, cases) ->
        let casesString =
            cases
            |> List.map (fun (casePattern, caseBody) ->
                $"""{patternToString casePattern} -> {toString caseBody}"""
            )
            |> String.join "; "

        $"""case {toString subject} of {casesString}"""
    | UpdateRecord (_, subject, fields) ->
        let fieldsString =
            fields
            |> Dict.toList
            |> List.map (fun (fieldName, fieldValue) ->
                $"""{Name.toCamelCase fieldName} = {toString fieldValue}"""
            )
            |> String.join ", "

        $"""{{ {toString subject} | {fieldsString} }}"""
    | Unit _ -> "()"
}

let inline valueToString (value: Value<'ta, 'va>) = toString value
