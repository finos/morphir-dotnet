namespace Morphir.Internal.CodeGeneration.Tests.TestData

open Morphir.Internal.CodeGeneration

/// Test record for builder generation
[<GenerateBuilder(BuilderName = "QueryBuilder")>]
type Query = {
    Select: string list
    From: string
    Where: string option
    OrderBy: string option
}

/// Another test record for builder
[<GenerateBuilder>]
type Request = {
    Method: string
    Path: string
    Headers: Map<string, string>
    Body: string option
}

