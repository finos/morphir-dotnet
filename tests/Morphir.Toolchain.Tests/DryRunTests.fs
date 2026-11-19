// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// <c>morphirup setup --dry-run</c>: the maintainer's rule that a command which changes state has a dry run. In this
/// plan-only build <c>setup</c> changes nothing, so <c>--dry-run</c> is <c>--plan</c> under another name: the same
/// result bytes, plan digest, refusals and exit codes, and no file created, changed or removed.
/// </summary>
module Morphir.Toolchain.Tests.DryRunTests

open System
open System.IO
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.CliTests

/// The arguments with <c>--plan</c> replaced by <c>--dry-run</c>.
let private dryRun (args: string list) = args |> List.map (fun arg -> if arg = "--plan" then "--dry-run" else arg)

/// Every directory and file under a root, with each file's length, digest and last write time: equal snapshots mean
/// nothing was created, changed or removed.
let private snapshot (root: string) =
    let directories =
        Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
        |> Array.map (fun path -> Path.GetRelativePath(root, path) + "/")

    let files =
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        |> Array.map (fun path ->
            let info = FileInfo path
            $"{Path.GetRelativePath(root, path)} {info.Length} {(Sha256.ofBytes (File.ReadAllBytes path)).Text} {info.LastWriteTimeUtc.Ticks}")

    Array.append directories files |> Array.sort |> List.ofArray

/// Asserts that the --plan and --dry-run forms of one command line give the same exit code and the same bytes on
/// stdout, and the same message on stderr.
let private sameAsPlan (directory: string) (platform: Platform) (args: string list) =
    let plan = runIn directory platform args
    let dry = runIn directory platform (dryRun args)
    Expect.equal dry.Exit plan.Exit "the same exit code"
    Expect.equal dry.StdoutBytes plan.StdoutBytes "the same result bytes"
    Expect.equal dry.Stderr plan.Stderr "the same messages"
    dry

[<Tests>]
let tests =
    testList "morphirup setup --dry-run" [
        testCase "a dry run is the plan: the same result bytes, outcome and plan digest" (fun () ->
            use sandbox = tempDirectory ()
            let args = planArgs sandbox.Work "request/approved.toml"
            let run = sameAsPlan sandbox.Work Platform.WinX64 args
            Expect.equal run.Exit 0 $"exit code; stderr: {run.Stderr}"

            let result = readResult run
            // Planned, never succeeded or applied: nothing was acquired, installed or activated.
            Expect.equal (result.Command, result.Outcome) (Command.Setup, Outcome.Planned) "a planned setup"

            let planned = runIn sandbox.Work Platform.WinX64 args |> readResult
            Expect.equal (result.Plan |> Option.map _.Sha256) (planned.Plan |> Option.map _.Sha256) "the same planSha256"
            Expect.isSome result.Plan "a plan")

        testCase "--dry-run and --plan together are the same request" (fun () ->
            use sandbox = tempDirectory ()
            let args = planArgs sandbox.Work "request/approved.toml"
            let plan = runIn sandbox.Work Platform.WinX64 args
            let both = runIn sandbox.Work Platform.WinX64 (args @ [ "--dry-run" ])
            Expect.equal both.Exit 0 "exit code"
            Expect.equal both.StdoutBytes plan.StdoutBytes "the same result bytes")

        testCase "--expect-plan works with a dry run as with a plan" (fun () ->
            use sandbox = tempDirectory ()
            let args = planArgs sandbox.Work "request/approved.toml"
            let digest = (Option.get (runIn sandbox.Work Platform.WinX64 args |> readResult).Plan).Sha256.Text
            Expect.equal (sameAsPlan sandbox.Work Platform.WinX64 (args @ [ "--expect-plan"; digest ])).Exit 0 "the same plan"
            let changed = sameAsPlan sandbox.Work Platform.WinX64 (args @ [ "--expect-plan"; String.replicate 64 "0" ])
            Expect.equal changed.Exit 1 "plan_changed"
            Expect.isNone (readResult changed).Plan "nothing planned")

        testList "a refusal is the plan's refusal, with the same result and exit code" [
            for name, request, platform in
                [ "a request of another draft", "request/negative/draft-2.toml", Platform.WinX64
                  "a credential in the request", "request/negative/credential-member.toml", Platform.WinX64
                  "an unresolvable request", "request/approved.toml", Platform.LinuxMuslArm64 ] do
              yield
                testCase name (fun () ->
                    use sandbox = tempDirectory ()
                    let run = sameAsPlan sandbox.Work platform (planArgs sandbox.Work request)
                    Expect.equal run.Exit 1 "failed"
                    Expect.equal (readResult run).Outcome Outcome.Failed "a failed result")

            yield
              testCase "a recipe target that is not portable" (fun () ->
                use sandbox = tempDirectory ()
                let run = sameAsPlan sandbox.Work Platform.WinX64 (planArgs sandbox.Work "request/approved.toml" @ [ "--recipe-target"; "a/../b.json" ])
                Expect.equal run.Exit 1 "failed")

            yield
              testCase "a recipe that cannot be read" (fun () ->
                use sandbox = tempDirectory ()

                let args =
                    planArgs sandbox.Work "request/approved.toml"
                    |> List.map (fun a -> if a.EndsWith "fsharp-synthetic.json" then Path.Combine(sandbox.Work, "absent.json") else a)

                let run = sameAsPlan sandbox.Work Platform.WinX64 args
                Expect.equal run.Exit 1 "document_unavailable")
        ]

        testList "an invocation error is the plan's, exit 2 with no result" [
            for name, args in
                [ "no --repository-id",
                  (fun directory -> planArgs directory "request/approved.toml" |> List.filter (fun a -> a <> "--repository-id" && a <> repositoryText))
                  "an unknown option", (fun _ -> [ "setup"; "--plan"; "--frobnicate" ])
                  "a value that is another option", (fun _ -> [ "setup"; "--plan"; "--request"; "--json" ])
                  "missing sizes", (fun directory -> planArgs directory "request/approved.toml" |> List.take 9) ] ->
                testCase name (fun () ->
                    use sandbox = tempDirectory ()
                    let run = sameAsPlan sandbox.Work Platform.WinX64 (args sandbox.Work)
                    Expect.equal run.Exit 2 "exit code"
                    Expect.equal run.Stdout "" "no result")
        ]

        testCase "--dry-run never permits applying: with --yes it is refused, exit 2, no result" (fun () ->
            use sandbox = tempDirectory ()

            for args in
                [ dryRun (planArgs sandbox.Work "request/approved.toml") @ [ "--yes" ]
                  planArgs sandbox.Work "request/approved.toml" @ [ "--dry-run"; "--yes" ] ] do
                let run = runIn sandbox.Work Platform.WinX64 args
                Expect.equal run.Exit 2 "a usage error"
                Expect.equal run.Stdout "" "no result"
                Expect.stringContains run.Stderr "this build plans only" "says why")

        testCase "--dry-run given twice is refused" (fun () ->
            use sandbox = tempDirectory ()
            let run = runIn sandbox.Work Platform.WinX64 [ "setup"; "--dry-run"; "--dry-run" ]
            Expect.equal run.Exit 2 "exit code"
            Expect.equal run.Stdout "" "no result"
            Expect.stringContains run.Stderr "--dry-run is given twice" "the message")

        testCase "help names --dry-run as the same request as --plan" (fun () ->
            use sandbox = tempDirectory ()
            let run = runIn sandbox.Work Platform.WinX64 [ "--help" ]
            Expect.equal run.Exit 0 "exit code"
            Expect.stringContains run.Stderr "morphirup setup (--plan | --dry-run)" "the usage line"
            Expect.stringContains run.Stderr "--dry-run               The same as --plan" "the option")

        testCase "valid and refused dry runs create, change and remove nothing in the project or the home" (fun () ->
            use sandbox = tempDirectory ()

            // Existing state that must be preserved: a project request and lock, and a manager home with a
            // generation receipt and a journal.
            let project = Path.Combine(sandbox.Work, "project")
            let morphir = Directory.CreateDirectory(Path.Combine(project, ".morphir")).FullName
            File.Copy(fixture "request/approved.toml", Path.Combine(morphir, "toolchain.toml"))
            File.Copy(fixture "lock/two-platforms-bound.json", Path.Combine(morphir, "toolchain.lock.json"))
            let generation = Directory.CreateDirectory(Path.Combine(sandbox.MorphirHome, "toolchains", "generations", "g1")).FullName
            File.WriteAllText(Path.Combine(generation, "receipt.json"), "{}\n")
            File.WriteAllText(Path.Combine(sandbox.MorphirHome, "journal.jsonl"), "")

            // A project with no state, which must stay without any.
            let fresh = Directory.CreateDirectory(Path.Combine(sandbox.Work, "fresh")).FullName
            let sizes = sizesFile sandbox.Work
            let common = [ "--recipe"; fixture "recipe/fsharp-synthetic.json"; "--repository-id"; repositoryText; "--artifact-sizes"; sizes ]
            let before = snapshot sandbox.Root

            let runs =
                [ "discovered", project, [ "setup"; "--dry-run"; "--json" ] @ common
                  "a project option", fresh, [ "setup"; "--dry-run"; "--project"; project ] @ common
                  "a request file", fresh, [ "setup"; "--dry-run"; "--request"; fixture "request/approved.toml" ] @ common
                  "selector options", fresh,
                  [ "setup"; "--dry-run"; "--implementation"; "dotnet"; "--deployment"; "native-aot"; "--preset"; "fsharp"; "--channel"; "stable" ] @ common
                  "a refused request", fresh, [ "setup"; "--dry-run"; "--request"; fixture "request/negative/draft-2.toml" ] @ common
                  "a changed plan", project, [ "setup"; "--dry-run"; "--expect-plan"; String.replicate 64 "0" ] @ common
                  "with --yes", project, [ "setup"; "--dry-run"; "--yes" ] @ common
                  "an unknown option", fresh, [ "setup"; "--dry-run"; "--frobnicate" ] ]

            for name, directory, args in runs do
                let run = runIn directory Platform.WinX64 args
                Expect.isTrue (run.Exit = 0 || run.Exit = 1 || run.Exit = 2) $"{name}: a normal exit code, not {run.Exit}"
                Expect.equal (snapshot sandbox.Root) before $"{name}: nothing created, changed or removed"

            Expect.equal (runIn project Platform.WinX64 ([ "setup"; "--dry-run" ] @ common)).Exit 0 "the discovered dry run plans"
            Expect.isFalse (Directory.Exists(Path.Combine(fresh, ".morphir"))) "no project state is created"
            Expect.isFalse (File.Exists(Path.Combine(sandbox.MorphirHome, "toolchains", "generations", "g1", "lock.json"))) "no generation file"
            Expect.isFalse (Directory.Exists(Path.Combine(sandbox.MorphirHome, "cache"))) "no cache"
            Expect.isFalse (Directory.Exists(Path.Combine(sandbox.MorphirHome, "bin"))) "no activation"
            Expect.equal (snapshot sandbox.Root) before "the tree is as it was"

            // Control: the snapshot sees a change, so an equal one means nothing changed.
            File.AppendAllText(Path.Combine(sandbox.MorphirHome, "journal.jsonl"), "x")
            Expect.notEqual (snapshot sandbox.Root) before "a changed file is seen")
    ]
    |> testLabel "PlatformSpecific"
