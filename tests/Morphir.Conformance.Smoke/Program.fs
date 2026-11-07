// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Smoke

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Morphir.Conformance

[<EntryPoint>]
let main args =
    if
        args.Length
        <> 1
    then
        Console.Error.WriteLine "Supply the conformance directory"
        2
    else
        let root = Path.GetFullPath args[0]

        let artifacts = [
            for file in Directory.GetFiles(root, "*", SearchOption.AllDirectories) do
                yield {
                    Path = Path.GetRelativePath(root, file).Replace('\u005c', '/')
                    Bytes = File.ReadAllBytes file
                }
        ]

        let options = {
            Suite = "frontend-fsharp"
            VocabularyVersion = "1.0"
            IrVersions = [ "1"; "2"; "3"; "4" ]
            InputKinds = [ "files"; "directory"; "project file" ]
        }

        match Corpus.read options artifacts with
        | Error issues ->
            for issue in issues do
                Console.Error.WriteLine(
                    issue.Code
                    + ": "
                    + issue.Message
                )

            1
        | Ok cases ->
            let identity = {
                Name = "aot-negative-control"
                Version = "1.0.0"
                Sha256 = String.replicate 64 "a"
            }

            let adapter =
                LibraryAdapter.create
                    identity
                    (fun _ _ ->
                        Task.FromResult(
                            Ok
                                {
                                    Success = true
                                    ModelJson = None
                                    Diagnostics = []
                                }
                        )
                    )

            let report =
                Runner.run cases adapter CancellationToken.None
                |> fun task -> task.GetAwaiter().GetResult()

            use output = Console.OpenStandardOutput()
            output.Write(Report.toUtf8 report)

            if
                not report.Qualified
                && not cases.Cases.IsEmpty
                && report.Results.Length = cases.Cases.Length
            then
                0
            else
                1
