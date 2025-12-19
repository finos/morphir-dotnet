namespace Morphir.Internal.CodeGeneration.Generators

open Myriad.Core

/// Generates active patterns from discriminated unions
///
/// Example input:
/// [<GenerateActivePatterns>]
/// type Result<'T, 'E> =
///     | Ok of 'T
///     | Error of 'E
///
/// Example output:
/// module Result =
///     let (|Ok|Error|) (result: Result<'T, 'E>) =
///         match result with
///         | Ok value -> Choice1Of2 value
///         | Error err -> Choice2Of2 err
[<MyriadGenerator("active-patterns")>]
type ActivePatternGenerator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".fs" }
        
        member _.Generate(_context: GeneratorContext) : Output =
            // TODO: Implement active pattern generation
            Output.Ast []
