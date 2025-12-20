namespace Morphir.Internal.CodeGeneration.Tests.TestData

open Morphir.Internal.CodeGeneration

/// Test record type for JSON codec generation
[<GenerateJsonCodec(PropertyNamingPolicy = "camelCase")>]
type User = {
    Id: int
    Name: string
    Email: string
}

/// Test record with nested types
[<GenerateJsonCodec>]
type Address = {
    Street: string
    City: string
    ZipCode: string
}

/// Test record with optional fields
[<GenerateJsonCodec>]
type Profile = {
    User: User
    Address: Address option
    Age: int option
}

