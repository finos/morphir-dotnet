// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.Program

open Expecto
open System
open System.Threading
open Morphir.Extensions.Installation

[<EntryPoint>]
let main args =
    match args with
    | [| "--cancel-state-call"; home |] ->
        use cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds 100.)
        let elapsed = Diagnostics.Stopwatch.StartNew()
        let pending = InstalledState.CheckCompatibilityAsync(home, cancellation.Token)

        if elapsed.Elapsed > TimeSpan.FromMilliseconds 500. then
            eprintfn "public invocation blocked for %s" (elapsed.Elapsed.ToString())
            4
        else
            printfn "returned asynchronously"

            pending
                .GetAwaiter()
                .GetResult()
                .Match(
                    Func<bool, int>(fun _ ->
                        eprintfn "expected cancellation"
                        5
                    ),
                    Func<InstallationFailure, int>(fun failure ->
                        if failure.Code = "extension.cancelled" then
                            printfn "cancelled and settled"
                            0
                        else
                            eprintfn "%s" failure.Message
                            6
                    )
                )
    | [| "--package-lease-only"; home; digest; mode |] ->
        try
            use root = SafeDirectory.Open(home, false)

            FileSafety.withDirectories
                root
                [ "store"; "extensions"; "packages"; "sha256" ]
                false
                (fun directory ->
                    use lease =
                        directory.NamedLease(
                            digest
                            + ".lease",
                            (mode = "exclusive"),
                            false
                        )

                    printfn "package lease acquired"
                    0
                )
        with
        | :? FileLeaseBusyException ->
            eprintfn "package lease busy"
            2
        | InstallationException failure ->
            eprintfn "%s" failure.Message
            1
    | [| "--mutation-lease-only"; home |] ->
        try
            use root = SafeDirectory.Open(home, false)
            use mutation = FileSafety.mutationLease root
            printfn "mutation lease acquired"
            0
        with
        | :? System.IO.IOException as error ->
            eprintfn "%s" error.Message
            1
        | InstallationException error ->
            eprintfn "%s" (error.ToString())
            1
    | [| "--sweep-only"; home |] ->
        RunCopies
            .SweepAsync(home, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Match(
                Func<RunCopySweep, int>(fun _ -> 0),
                Func<InstallationFailure, int>(fun error ->
                    eprintfn "%s" (error.ToString())
                    1
                )
            )
    | _ -> runTestsInAssemblyWithCLIArgs [] args
