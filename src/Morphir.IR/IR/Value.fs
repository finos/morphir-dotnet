module Morphir.IR.Value

open Morphir.IR.Type
open Morphir.IR.Name
open Morphir.IR.FQName

/// Type that represents a value/expression.
type Value<'A> =
    /// A literal represents a fixed value in the IR. We only allow values of basic types: bool, char, string, int, float.
    | Literal of Attributes: 'A * Value: Literal
    | Constructor of Attributes: 'A * FullyQualifiedName: FQName
    | Tuple of Attributes: 'A * Elements: Value<'A> list
    | List of Attributes: 'A * Items: Value<'A> list
    | Record of Attributes: 'A * Fields: (Name * Value<'A>) list
    | Variable of Attributes: 'A * Name: Name
    | Reference of Attributes: 'A * FullyQualifiedName: FQName
    | Field of Attributes: 'A * SubjectValue: Value<'A> * FieldName: Name
    | FieldFunction of Attributes: 'A * FieldName: Name
    | Apply of Attributes: 'A * Function: Value<'A> * Argument: Value<'A>
    | Lambda of Attributes: 'A * ArgumentPattern: Pattern<'A> * Body: Value<'A>
    | LetDefinition of
        Attributes: 'A *
        ValueName: Name *
        ValueDefinition: Definition<'A> *
        InValue: Value<'A>
    | LetRecursion of
        Attributes: 'A *
        ValueDefinitions: (Name * Definition<'A>) list *
        InValue: Value<'A>
    | Destructure of
        Attributes: 'A *
        Pattern: Pattern<'A> *
        ValueToDestruct: Value<'A> *
        InValue: Value<'A>
    | IfThenElse of
        Attributes: 'A *
        Condition: Value<'A> *
        ThenBranch: Value<'A> *
        ElseBranch: Value<'A>
    | PatternMatch of
        Attributes: 'A *
        BranchOutOn: Value<'A> *
        Cases: (Pattern<'A> * Value<'A> list)
    | UpdateRecord of Attributes: 'A * ValueToUpdate: Value<'A> * FieldsToUpdate: Value<'A>
    | Unit of Attributes: 'A

/// Type that represents a literal value.
and Literal =
    | BoolLiteral of bool
    | CharLiteral of char
    | StringLiteral of string
    | IntLiteral of int
    | FloatLiteral of float

and Pattern<'A> =
    | WildCardPattern of Attributes: 'A
    | AsPattern of Attributes: 'A * Pattern: Pattern<'A> * Name: Name
    | TuplePattern of Attributes: 'A * ElementPatterns: Pattern<'A> list
    | RecordPattern of Attributes: 'A * FieldNames: Name list
    | ConstructorPattern of Attributes: 'A * FQName * Pattern<'A> list
    | EmptyListPattern of Attributes: 'A
    | HeadTailPattern of Attributes: 'A * Pattern<'A> * Pattern<'A>
    | LiteralPattern of Attributes: 'A * Literal

and Specification<'A> = {
    Inputs: Name * Type<'A> list
    Output: Type<'A>
}

and Definition<'A> =
    | TypedDefinition of ValueType: Type<'A> * ArgumentNames: Name list * Body: Value<'A>
    | UntypedDefinition of ArgumentNames: Name list * Body: Value<'A>


let getDefinitionBody =
    function
    | TypedDefinition (_, _, body) -> body
    | UntypedDefinition (_, body) -> body

let (|DefinitionBody|) = getDefinitionBody
