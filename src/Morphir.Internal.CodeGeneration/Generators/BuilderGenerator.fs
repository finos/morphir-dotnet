namespace Morphir.Internal.CodeGeneration.Generators

open Myriad.Core

/// Generates type-safe builder pattern for F# types
///
/// Example input:
/// [<GenerateBuilder>]
/// type User = { Id: int; Name: string; Email: string }
///
/// Example output:
/// type UserBuilder() =
///     member val Id = 0 with get, set
///     member val Name = "" with get, set
///     member val Email = "" with get, set
///     
///     member this.Build() = {
///         Id = this.Id
///         Name = this.Name
///         Email = this.Email
///     }
[<MyriadGenerator("builder")>]
type BuilderGenerator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".fs" }
        
        member _.Generate(_context: GeneratorContext) : Output =
            // TODO: Implement builder generation
            Output.Ast []
