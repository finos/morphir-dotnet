// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

// ResultSyntax has a file of its own. F# initialises the module values of a file in one initialiser; in the file of
// CorpusJson that initialiser also runs the type initialiser of CorpusJson, so a thread that first used `result` and
// a thread that first used CorpusJson could wait for each other, and the CLR would let one of them read a CorpusJson
// value that is not yet set.

/// A small Result builder for the readers of this suite.
module internal ResultSyntax =
    type ResultBuilder() =
        member _.Bind(result: Result<'T, 'E>, next: 'T -> Result<'U, 'E>) = Result.bind next result
        member _.Return(value: 'T) : Result<'T, 'E> = Ok value
        member _.ReturnFrom(result: Result<'T, 'E>) = result
        member _.Zero() : Result<unit, 'E> = Ok()

    let result = ResultBuilder()

    /// The values of a list of results, or the first error.
    let all (results: Result<'T, 'E> list) : Result<'T list, 'E> =
        let folder (item: Result<'T, 'E>) (rest: Result<'T list, 'E>) =
            match item, rest with
            | Ok value, Ok values ->
                Ok(
                    value
                    :: values
                )
            | Error error, _ -> Error error
            | Ok _, Error error -> Error error

        List.foldBack folder results (Ok [])
