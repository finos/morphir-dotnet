namespace Morphir.IR.V4

open Morphir.IR

/// <summary>The attributes of a value expression or pattern.</summary>
type ValueAttributes = {
    /// <summary>The source location.</summary>
    Source: SourceLocation option
    /// <summary>The type inferred during type checking.</summary>
    InferredType: Type option
    /// <summary>Custom extension data, a document object.</summary>
    Extensions: DocumentValue option
}

/// <summary>Functions for <see cref="ValueAttributes"/>.</summary>
[<RequireQualifiedAccess>]
module ValueAttributes =
    /// <summary>The attributes of a node that states none.</summary>
    let empty = {
        Source = None
        InferredType = None
        Extensions = None
    }

/// <summary>A pattern. A literal pattern never holds a <see cref="DocumentLiteral"/>.</summary>
type Pattern =
    /// <summary>Matches anything.</summary>
    | WildcardPattern of attributes: ValueAttributes
    /// <summary>Binds the matched value to a name.</summary>
    | AsPattern of attributes: ValueAttributes * pattern: Pattern * name: Name
    /// <summary>Matches a tuple.</summary>
    | TuplePattern of attributes: ValueAttributes * patterns: Pattern list
    /// <summary>Matches a constructor.</summary>
    | ConstructorPattern of attributes: ValueAttributes * fqName: FQName * patterns: Pattern list
    /// <summary>Matches an empty list.</summary>
    | EmptyListPattern of attributes: ValueAttributes
    /// <summary>Matches a list's head and tail.</summary>
    | HeadTailPattern of attributes: ValueAttributes * head: Pattern * tail: Pattern
    /// <summary>Matches a literal.</summary>
    | LiteralPattern of attributes: ValueAttributes * literal: Literal
    /// <summary>Matches unit.</summary>
    | UnitPattern of attributes: ValueAttributes

/// <summary>Why a hole exists.</summary>
type HoleReason =
    /// <summary>A reference to something that does not exist or was deleted.</summary>
    | UnresolvedReference of target: FQName
    /// <summary>A reference deleted by a refactoring.</summary>
    | DeletedDuringRefactor of txId: string
    /// <summary>A type mismatch, described as text.</summary>
    | TypeMismatch of expected: string * found: string

/// <summary>Why a type or value definition is incomplete.</summary>
[<RequireQualifiedAccess>]
type Incompleteness =
    /// <summary>A hole, and the partial type if one is known.</summary>
    | Hole of reason: HoleReason * partialBody: Type option
    /// <summary>Deliberately unfinished.</summary>
    | Draft

/// <summary>What kind of operation a native body performs.</summary>
type NativeHint =
    /// <summary>Basic arithmetic or logic.</summary>
    | Arithmetic
    /// <summary>A comparison.</summary>
    | Comparison
    /// <summary>A string operation.</summary>
    | StringOp
    /// <summary>A collection operation.</summary>
    | CollectionOp
    /// <summary>An operation specific to one platform.</summary>
    | PlatformSpecific of platform: string

/// <summary>Information about a native operation.</summary>
type NativeInfo = {
    /// <summary>The category.</summary>
    Hint: NativeHint
    /// <summary>A readable description.</summary>
    Description: string option
}

/// <summary>One per-target binding of an external definition.</summary>
type ExternalBinding = {
    /// <summary>The target platform.</summary>
    TargetPlatform: string
    /// <summary>The function's name on that platform.</summary>
    ExternalName: string
}

/// <summary>
/// A v4 value expression. Record fields and a definition's inputs keep their order, which counts in equality.
/// </summary>
[<RequireQualifiedAccess>]
type Value =
    /// <summary>A literal.</summary>
    | Literal of attributes: ValueAttributes * literal: Literal
    /// <summary>A reference to a constructor.</summary>
    | Constructor of attributes: ValueAttributes * fqName: FQName
    /// <summary>A tuple.</summary>
    | Tuple of attributes: ValueAttributes * elements: Value list
    /// <summary>A list.</summary>
    | List of attributes: ValueAttributes * items: Value list
    /// <summary>A record.</summary>
    | Record of attributes: ValueAttributes * fields: (Name * Value) list
    /// <summary>A variable in scope.</summary>
    | Variable of attributes: ValueAttributes * name: Name
    /// <summary>A reference to a defined value.</summary>
    | Reference of attributes: ValueAttributes * fqName: FQName
    /// <summary>A field access.</summary>
    | Field of attributes: ValueAttributes * target: Value * name: Name
    /// <summary>A function that extracts a field.</summary>
    | FieldFunction of attributes: ValueAttributes * name: Name
    /// <summary>A function application.</summary>
    | Apply of attributes: ValueAttributes * func: Value * argument: Value
    /// <summary>An anonymous function.</summary>
    | Lambda of attributes: ValueAttributes * pattern: Pattern * body: Value
    /// <summary>A let binding.</summary>
    | LetDefinition of
        attributes: ValueAttributes *
        name: Name *
        definition: Documented<ValueDefinition> *
        inValue: Value
    /// <summary>Mutually recursive let bindings.</summary>
    | LetRecursion of
        attributes: ValueAttributes *
        definitions: OrderedMap<Name, Documented<ValueDefinition>> *
        inValue: Value
    /// <summary>Pattern-based destructuring.</summary>
    | Destructure of attributes: ValueAttributes * pattern: Pattern * value: Value * inValue: Value
    /// <summary>A conditional.</summary>
    | IfThenElse of
        attributes: ValueAttributes *
        condition: Value *
        thenBranch: Value *
        elseBranch: Value
    /// <summary>A pattern match.</summary>
    | PatternMatch of attributes: ValueAttributes * value: Value * cases: (Pattern * Value) list
    /// <summary>A record update.</summary>
    | UpdateRecord of attributes: ValueAttributes * target: Value * fields: (Name * Value) list
    /// <summary>The unit value.</summary>
    | Unit of attributes: ValueAttributes
    /// <summary>A missing expression.</summary>
    | Hole of attributes: ValueAttributes * reason: HoleReason * expectedType: Type option

/// <summary>A value definition. Only an incomplete body may lack an output type.</summary>
and ValueDefinition = {
    /// <summary>The input parameters in order.</summary>
    InputTypes: (Name * Type) list
    /// <summary>The body and its output type.</summary>
    Body: ValueBody
}

/// <summary>What a value definition is made of.</summary>
and ValueBody =
    /// <summary>An IR expression.</summary>
    | ExpressionBody of outputType: Type * body: Value
    /// <summary>A platform operation with no IR expression.</summary>
    | NativeBody of outputType: Type * info: NativeInfo
    /// <summary>A function bound per target platform.</summary>
    | ExternalBody of outputType: Type * info: ExternalInfo
    /// <summary>A definition that cannot be fully resolved.</summary>
    | IncompleteBody of
        outputType: Type option *
        incompleteness: Incompleteness *
        partialBody: Value option

/// <summary>The bindings of an external definition.</summary>
and ExternalInfo = {
    /// <summary>At least one binding, with each target platform once.</summary>
    Externals: ExternalBinding list
    /// <summary>The expression for platforms with no binding.</summary>
    Fallback: Value option
}
