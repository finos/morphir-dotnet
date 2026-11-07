// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Exercises Morphir.Compilation in a trimmed, Native AOT executable. Exit 0 means every check held.
module Morphir.Compilation.Smoke

open System
open System.Collections.Generic
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model

let private version text =
    match Versioning.parse text with
    | Ok version -> version
    | Error error -> failwith $"{text}: {error}"

let private writer family (versions: string list) =
    { new IIrWriter with
        member _.Identity = ComponentId.Create $"smoke-{versions.Head.Replace('.', '-')}"
        member _.Family = family

        member _.Versions =
            versions
            |> List.map version
            |> List.toArray
            :> IReadOnlyList<_>

        member _.Write(_, _, _) = Checked.pass [||]
    }

[<EntryPoint>]
let main _ =
    let warning = Diagnostic.warning (DiagnosticCode.Create "smoke.warning") "kept"
    let error = Diagnostic.error (DiagnosticCode.Create "smoke.error") "stops"

    let combined = check {
        let! a = Checked.ofDiagnostics 1 [ warning ]
        and! b = Checked.pass 2
        return a + b
    }

    let failed = check {
        do! Checked.note warning
        do! Checked.note error
        return 0
    }

    let registry =
        WriterRegistry.Create [ writer ModelFamily.Classic [ "3" ]; writer ModelFamily.V4 [ "4" ] ]

    let checks = [
        "and! keeps the warning",
        Checked.tryValue combined = Some 3
        && combined.Diagnostics.Count = 1
        "let! keeps the warning before the error",
        not failed.IsPassed
        && failed.Diagnostics.Count = 2
        "4.0.0 selects the v4 writer", (registry.Resolve(version "4.0.0")).IsPassed
        "a pre-release is refused", not (registry.Resolve(version "4.1.0-draft.1")).IsPassed
        "UTF-16 positions", Position.FromOffset("\"😀\"x", 4).Character = 4
        "diagnostic text", string error = "error smoke.error: stops"
    ]

    for name, held in checks do
        Console.Error.WriteLine $"""{(if held then "ok  " else "FAIL")} {name}"""

    if
        checks
        |> List.forall snd
    then
        0
    else
        1
