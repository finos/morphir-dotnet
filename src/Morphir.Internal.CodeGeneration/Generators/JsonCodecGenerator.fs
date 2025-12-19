namespace Morphir.Internal.CodeGeneration.Generators

open Myriad.Core

/// Generates JSON encoder and decoder functions for F# types
///
/// Example input:
/// [<GenerateJsonCodec>]
/// type User = { Id: int; Name: string }
///
/// Example output:
/// module User.JsonCodec =
///     open System.Text.Json
///
///     let encode (value: User) : JsonElement =
///         // Generated encoder without reflection
///         ...
///
///     let decode (json: JsonElement) : Result<User, string> =
///         // Generated decoder without reflection
///         ...
[<MyriadGenerator("json-codec")>]
type JsonCodecGenerator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".fs" }

        member _.Generate(_context: GeneratorContext) : Output =
            // TODO: Implement JSON codec generation
            // For now, return empty output
            Output.Ast []
