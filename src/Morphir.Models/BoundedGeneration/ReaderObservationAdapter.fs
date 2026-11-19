// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BoundedGeneration

open System
open System.IO
open Microsoft.Extensions.FileProviders
open Morphir.IR.Trees

/// Internal C# bridge for the development adapter. No product command or public API is added.
[<AbstractClass; Sealed>]
type internal ReaderObservationAdapter =
    static member Observe(provider: IFileProvider, revision: Func<string>, profile: string,
                          selection: string, pageSize: int, documentLimit: int64) =
        let encoding =
            match profile with
            | "plainJson" | "plainYaml" -> BoundedTreeEncoding.Plain
            | "contextualJson" | "contextualYaml" -> BoundedTreeEncoding.ContextualDraft1
            | _ -> invalidArg "profile" "Unknown bounded reader profile."
        let options = { MaxFiles = 64; MaxFileBytes = documentLimit }
        DocumentTreeStore.openSnapshotWithEncoding options encoding (fun () -> revision.Invoke()) provider
        |> Result.bind (ReaderObservation.observe selection pageSize documentLimit)
        |> function
            | Ok facts -> facts
            | Error errors -> raise (InvalidDataException(String.Join("; ", errors |> List.map _.Code)))
