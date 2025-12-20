namespace Morphir.Internal.CodeGeneration.Tests.TestData

open Morphir.Internal.CodeGeneration

/// Test discriminated union for active pattern generation
[<GenerateActivePatterns>]
type Result<'T, 'E> =
    | Ok of 'T
    | Error of 'E

/// Another test discriminated union
[<GenerateActivePatterns>]
type Option<'T> =
    | Some of 'T
    | None

