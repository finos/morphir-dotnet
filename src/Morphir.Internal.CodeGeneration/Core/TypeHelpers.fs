namespace Morphir.Internal.CodeGeneration.Core

/// Type analysis utilities for code generation
module TypeHelpers =
    
    /// Represents a field in a record type
    type RecordField = {
        Name: string
        TypeName: string
    }
    
    /// Represents a case in a discriminated union
    type UnionCase = {
        Name: string
        FieldCount: int
    }
    
    /// Check if a type name represents a primitive type
    let isPrimitiveType (typeName: string) : bool =
        match typeName with
        | "int" | "string" | "bool" | "float" | "decimal" | "unit" -> true
        | _ -> false
