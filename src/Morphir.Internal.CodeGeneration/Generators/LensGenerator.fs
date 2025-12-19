namespace Morphir.Internal.CodeGeneration.Generators

open Myriad.Core

/// Generates lenses for nested record updates
///
/// Example input:
/// [<GenerateLenses>]
/// type Config = { Port: int; Host: string }
///
/// Example output:
/// module Config.Lenses =
///     let port = {
///         Get = fun (c: Config) -> c.Port
///         Set = fun (value: int) (c: Config) -> { c with Port = value }
///     }
///     let host = {
///         Get = fun (c: Config) -> c.Host
///         Set = fun (value: string) (c: Config) -> { c with Host = value }
///     }
[<MyriadGenerator("lenses")>]
type LensGenerator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".fs" }
        
        member _.Generate(_context: GeneratorContext) : Output =
            // TODO: Implement lens generation
            Output.Ast []
