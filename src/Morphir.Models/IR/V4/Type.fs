namespace Morphir.IR.V4

open Morphir.IR

/// <summary>The attributes of a type expression.</summary>
type TypeAttributes = {
    /// <summary>The source location.</summary>
    Source: SourceLocation option
    /// <summary>Type constraints, a document object.</summary>
    Constraints: DocumentValue option
    /// <summary>Custom extension data, a document object.</summary>
    Extensions: DocumentValue option
}

/// <summary>Functions for <see cref="TypeAttributes"/>.</summary>
[<RequireQualifiedAccess>]
module TypeAttributes =
    /// <summary>The attributes of a node that states none.</summary>
    let empty = {
        Source = None
        Constraints = None
        Extensions = None
    }

/// <summary>A v4 type expression. Record fields keep their order, which counts in equality.</summary>
[<RequireQualifiedAccess>]
type Type =
    /// <summary>A type variable.</summary>
    | Variable of attributes: TypeAttributes * name: Name
    /// <summary>A reference to a named type with its arguments.</summary>
    | Reference of attributes: TypeAttributes * fqName: FQName * arguments: Type list
    /// <summary>A tuple.</summary>
    | Tuple of attributes: TypeAttributes * elements: Type list
    /// <summary>A record.</summary>
    | Record of attributes: TypeAttributes * fields: (Name * Type) list
    /// <summary>A record extending a type variable.</summary>
    | ExtensibleRecord of attributes: TypeAttributes * variable: Name * fields: (Name * Type) list
    /// <summary>A function.</summary>
    | Function of attributes: TypeAttributes * parameterType: Type * returnType: Type
    /// <summary>The unit type.</summary>
    | Unit of attributes: TypeAttributes
