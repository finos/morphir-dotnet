// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The morphirup command line of this slice, run in process: one result document on stdout, progress on stderr.
module Morphir.Toolchain.Tests.CliTests

open System
open System.IO
open System.Text
open Expecto
open Morphir.Toolchain
open Morphir.TestSupport
open Morphirup

type Run =
    { Exit: int
      Stdout: string
      StdoutBytes: byte array
      Stderr: string }

/// Runs morphirup in process in a working directory, on a given platform.
let runIn (directory: string) (platform: Platform) (args: string list) =
    use out = new MemoryStream()
    use error = new StringWriter()

    let exit =
        Cli.run
            { Out = out
              Error = error
              CurrentDirectory = directory
              Platform = Some platform }
            args

    let bytes = out.ToArray()

    { Exit = exit
      Stdout = Encoding.UTF8.GetString bytes
      StdoutBytes = bytes
      Stderr = error.ToString() }

let fixture (relative: string) =
    Path.Combine(Fixtures.unsigned, relative.Replace('/', Path.DirectorySeparatorChar))

let repositoryText = String.replicate 64 "b"

/// A sizes file for every artifact of the synthetic F# recipe.
let sizesFile (directory: string) =
    let recipe = BindingCaseTests.recipeAt "recipe/fsharp-synthetic.json"

    let targets =
        [ for host in recipe.Hosts do
              for _, binding in host.Platforms -> binding.Target.Text
          for provider in recipe.Providers do
              match provider.Delivery with
              | RecipeDelivery.Process platforms -> for _, binding in platforms -> binding.Target.Text
              | RecipeDelivery.Wasm(_, artifact) -> yield artifact.Target.Text
              | RecipeDelivery.DotnetTool package -> yield package.Target.Text ]
        |> List.distinct

    let file = Path.Combine(directory, "sizes.json")

    let json =
        targets
        |> List.map (fun target -> $"\"{target}\": {{ \"downloadBytes\": 41943040, \"installedBytes\": 104857600 }}")
        |> String.concat ",\n"

    File.WriteAllText(file, "{\n" + json + "\n}\n")
    file

/// A sandbox for one test: its own temporary tree, home and machine folders, deleted when the test disposes it.
let tempDirectory () = TestSandbox.Create "morphirup-"

let readResult (run: Run) =
    match ResultDocument.read run.StdoutBytes with
    | Ok result -> result
    | Error refusal ->
        failtestf "stdout is not a valid result (%s at %s): %s" (Code.readerName refusal.Code) (DocumentPath.render refusal.Path) run.Stdout

let planArgs (directory: string) request =
    [ "setup"
      "--plan"
      "--json"
      "--request"
      fixture request
      "--recipe"
      fixture "recipe/fsharp-synthetic.json"
      "--repository-id"
      repositoryText
      "--artifact-sizes"
      sizesFile directory ]

[<Tests>]
let tests =
    testList "morphirup" [
        testCase "--version prints only the version" (fun () ->
            use sandbox = tempDirectory ()
            let run = runIn sandbox.Work Platform.WinArm64 [ "--version" ]
            Expect.equal run.Exit 0 "exit code"
            Expect.isTrue (run.Stdout.EndsWith "\n" && not (run.Stdout.TrimEnd().Contains "\n")) "one line"
            Expect.isNonEmpty (run.Stdout.Trim()) "a version")

        testCase "setup --plan writes one planned result and nothing else to stdout" (fun () ->
            use directorySandbox = tempDirectory ()
            let directory = directorySandbox.Work
            let run = runIn directory Platform.WinArm64 (planArgs directory "request/approved.toml")
            Expect.equal run.Exit 0 $"exit code; stderr: {run.Stderr}"
            let result = readResult run
            Expect.equal (result.Command, result.Outcome) (Command.Setup, Outcome.Planned) "a planned setup"
            Expect.isEmpty result.Diagnostics "no diagnostics"
            Expect.equal (result.Plan |> Option.map _.Plan.Platform) (Some Platform.WinArm64) "the current platform"
            Expect.stringContains run.Stderr "morphirup: planning win-arm64" "progress on stderr"
            Expect.equal (Directory.GetFiles directory |> Array.map Path.GetFileName) [| "sizes.json" |] "plan-only writes no file")

        testCase "--expect-plan accepts the same plan and refuses another" (fun () ->
            use directorySandbox = tempDirectory ()
            let directory = directorySandbox.Work
            let first = runIn directory Platform.WinX64 (planArgs directory "request/approved.toml") |> readResult
            let digest = (Option.get first.Plan).Sha256.Text

            let same = runIn directory Platform.WinX64 (planArgs directory "request/approved.toml" @ [ "--expect-plan"; digest ])
            Expect.equal same.Exit 0 "the same plan"

            let other =
                runIn directory Platform.WinX64 (planArgs directory "request/approved.toml" @ [ "--expect-plan"; String.replicate 64 "0" ])

            Expect.equal other.Exit 1 "a different plan is refused"
            let result = readResult other
            Expect.equal result.Outcome Outcome.Failed "failed"

            match result.Diagnostics with
            | [ { Detail = DiagnosticDetail.UnsupportedContract(ReaderCode.InconsistentDocument, DocumentKind.Result, path) } ] ->
                Expect.equal (DocumentPath.render path) "planSha256" "the plan digest"
            | other -> failtestf "unexpected diagnostics %A" other)

        testCase "the selector options give the same plan as the approved request file" (fun () ->
            use directorySandbox = tempDirectory ()
            let directory = directorySandbox.Work
            let fromFile = runIn directory Platform.WinX64 (planArgs directory "request/approved.toml") |> readResult

            let fromOptions =
                runIn
                    directory
                    Platform.WinX64
                    [ "setup"
                      "--implementation"
                      "dotnet"
                      "--deployment"
                      "native-aot"
                      "--preset"
                      "fsharp"
                      "--channel"
                      "stable"
                      "--plan"
                      "--recipe"
                      fixture "recipe/fsharp-synthetic.json"
                      "--repository-id"
                      repositoryText
                      "--artifact-sizes"
                      sizesFile directory ]
                |> readResult

            Expect.equal (fromOptions.Plan |> Option.map _.Sha256) (fromFile.Plan |> Option.map _.Sha256) "the same plan")

        testCase "a refused request gives a failed result naming the request member" (fun () ->
            use directorySandbox = tempDirectory ()
            let directory = directorySandbox.Work
            let run = runIn directory Platform.WinX64 (planArgs directory "request/negative/draft-2.toml")
            Expect.equal run.Exit 1 "exit code"

            match (readResult run).Diagnostics with
            | [ { Detail = DiagnosticDetail.UnsupportedContract(ReaderCode.UnsupportedContractVersion, DocumentKind.Request, path) } ] ->
                Expect.equal (DocumentPath.render path) "schemaVersion" "the member"
            | other -> failtestf "unexpected diagnostics %A" other)

        testCase "a credential in a request is refused and its value is never written" (fun () ->
            use directorySandbox = tempDirectory ()
            let directory = directorySandbox.Work
            let run = runIn directory Platform.WinX64 (planArgs directory "request/negative/credential-member.toml")
            Expect.equal run.Exit 1 "exit code"
            Expect.isFalse (run.Stdout.Contains "fixture-not-a-token") "no secret on stdout"
            Expect.isFalse (run.Stderr.Contains "fixture-not-a-token") "no secret on stderr"
            Expect.stringContains run.Stdout "credential_in_document" "the refusal")

        testCase "an unresolvable request gives an unsupported-artifact result with alternatives" (fun () ->
            use directorySandbox = tempDirectory ()
            let directory = directorySandbox.Work
            let run = runIn directory Platform.LinuxMuslArm64 (planArgs directory "request/approved.toml")
            Expect.equal run.Exit 1 "exit code"

            match (readResult run).Diagnostics with
            | [ { Detail = DiagnosticDetail.UnsupportedArtifact(ArtifactProblem.Unsupported, Component.Host _, Platform.LinuxMuslArm64, Some alternatives) } ] ->
                Expect.isNonEmpty alternatives "alternatives"
            | other -> failtestf "unexpected diagnostics %A" other)

        testCase "setup without --plan or with --yes changes nothing and writes no result" (fun () ->
            use directorySandbox = tempDirectory ()
            let directory = directorySandbox.Work

            for args in
                [ planArgs directory "request/approved.toml" |> List.filter ((<>) "--plan")
                  planArgs directory "request/approved.toml" @ [ "--yes" ] ] do
                let run = runIn directory Platform.WinX64 args
                Expect.equal run.Exit 2 "a usage error"
                Expect.equal run.Stdout "" "no result"
                Expect.stringContains run.Stderr "this build plans only: acquisition, installation and activation are not implemented yet" "says why")

        testList "every invocation error exits 2 with its own message and no result" [
            for name, args, message in
                [ "missing sizes reach the planner's SizeUnknown",
                  (fun directory -> planArgs directory "request/approved.toml" |> List.take 9),
                  "the size of artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip is unknown; give it in --artifact-sizes"
                  "no --repository-id",
                  (fun directory -> planArgs directory "request/approved.toml" |> List.filter (fun a -> a <> "--repository-id" && a <> repositoryText)),
                  "--repository-id is required until authenticated acquisition exists"
                  "an unknown option", (fun _ -> [ "setup"; "--plan"; "--frobnicate" ]), "unknown option --frobnicate"
                  "a value that is another option",
                  (fun _ -> [ "setup"; "--plan"; "--request"; "--json" ]),
                  "--request needs a value"
                  "an option given twice", (fun _ -> [ "setup"; "--plan"; "--plan" ]), "--plan is given twice"
                  "a missing request file",
                  (fun directory ->
                      [ "setup"; "--plan"; "--request"; Path.Combine(directory, "absent.toml"); "--recipe"; "x"; "--repository-id"; repositoryText ]),
                  "absent.toml does not exist"
                  "a directory as the recipe",
                  (fun directory ->
                      [ "setup"; "--plan"; "--request"; fixture "request/approved.toml"; "--recipe"; directory; "--repository-id"; repositoryText ]),
                  "is a directory, not a file"
                  "a Windows device path",
                  (fun _ -> [ "doctor"; "--request"; @"\\.\pipe\morphirup-test"; "--lock"; fixture "lock/two-platforms-bound.json" ]),
                  "is not a regular file"
                  "an unknown command", (fun _ -> [ "install" ]), "install is not a command of this build"
                  "no command", (fun _ -> []), "no command given"
                  "doctor --recipe without --repository-id",
                  (fun _ ->
                      [ "doctor"
                        "--request"
                        fixture "request/approved.toml"
                        "--lock"
                        fixture "lock/two-platforms-bound.json"
                        "--recipe"
                        fixture "recipe/fsharp-synthetic.json" ]),
                  "--repository-id is required with --recipe" ] ->
                testCase name (fun () ->
                    use sandbox = tempDirectory ()
                    let run = runIn sandbox.Work Platform.WinX64 (args sandbox.Work)
                    Expect.equal run.Exit 2 "exit code"
                    Expect.equal run.Stdout "" "no result"
                    Expect.stringContains run.Stderr message "the message")
        ]

        testCase "a file over its provisional limit is refused before it is parsed" (fun () ->
            use sandbox = tempDirectory ()
            let big = Path.Combine(sandbox.Work, "toolchain.toml")
            File.WriteAllBytes(big, Array.create (int Cli.Limits.request + 1) (byte '#'))

            let run =
                runIn sandbox.Work Platform.WinX64 [ "doctor"; "--request"; big; "--lock"; fixture "lock/two-platforms-bound.json" ]

            Expect.equal run.Exit 2 "exit code"
            Expect.stringContains run.Stderr $"is larger than the provisional limit of {Cli.Limits.request} bytes" "the message")

        testCase "a non-portable recipe target is non_portable_path at the recipe, checked before any file is read" (fun () ->
            use sandbox = tempDirectory ()

            let run =
                runIn
                    sandbox.Work
                    Platform.WinX64
                    [ "setup"
                      "--plan"
                      "--request"
                      Path.Combine(sandbox.Work, "absent.toml")
                      "--recipe"
                      Path.Combine(sandbox.Work, "absent.json")
                      "--repository-id"
                      repositoryText
                      "--recipe-target"
                      "toolchains/recipes/con/1.0.0.json" ]

            Expect.equal run.Exit 1 $"a failed result; stderr: {run.Stderr}"
            Expect.isFalse (run.Stderr.Contains "reading") "nothing was read"

            match (readResult run).Diagnostics with
            | [ { Detail = DiagnosticDetail.UnsupportedContract(ReaderCode.NonPortablePath, DocumentKind.Recipe, path) } ] ->
                Expect.equal (DocumentPath.render path) "" "the empty path: the recipe target itself"
            | other -> failtestf "unexpected diagnostics %A" other)

        testCase "a derived recipe target that is not portable is non_portable_path too" (fun () ->
            use sandbox = tempDirectory ()
            let recipe = Path.Combine(sandbox.Work, "recipe.json")
            File.WriteAllText(recipe, File.ReadAllText(fixture "recipe/fsharp-synthetic.json").Replace("\"preset\": \"fsharp\"", "\"preset\": \"con\""))
            let request = Path.Combine(sandbox.Work, "toolchain.toml")
            File.WriteAllText(request, File.ReadAllText(fixture "request/approved.toml").Replace("preset = \"fsharp\"", "preset = \"con\""))

            let run =
                runIn sandbox.Work Platform.WinX64 [ "setup"; "--plan"; "--request"; request; "--recipe"; recipe; "--repository-id"; repositoryText ]

            Expect.equal run.Exit 1 $"a failed result; stderr: {run.Stderr}"

            match (readResult run).Diagnostics with
            | [ { Detail = DiagnosticDetail.UnsupportedContract(ReaderCode.NonPortablePath, DocumentKind.Recipe, _) } ] -> ()
            | other -> failtestf "unexpected diagnostics %A" other)

        testCase "a recipe provider of another repository is repository_mismatch before planning" (fun () ->
            use sandbox = tempDirectory ()
            let run = runIn sandbox.Work Platform.WinX64 (planArgs sandbox.Work "request/approved.toml" |> List.map (fun a -> if a = repositoryText then String.replicate 64 "c" else a))
            Expect.equal run.Exit 1 "exit code"

            match (readResult run).Diagnostics with
            | [ { Detail = DiagnosticDetail.TrustFailure(TrustCode.RepositoryMismatch, repository, None, None, None) } ] ->
                Expect.equal repository.Text (String.replicate 64 "c") "the repository the check ran against"
            | other -> failtestf "unexpected diagnostics %A" other)

        testCase "the --expect-plan mismatch diagnostic says it is provisional" (fun () ->
            use sandbox = tempDirectory ()
            let run = runIn sandbox.Work Platform.WinX64 (planArgs sandbox.Work "request/approved.toml" @ [ "--expect-plan"; String.replicate 64 "0" ])
            Expect.equal run.Exit 1 "exit code"

            match (readResult run).Diagnostics with
            | [ { Message = message } ] -> Expect.stringContains message.Text "Provisional: the contract has no code" "provisional"
            | other -> failtestf "unexpected diagnostics %A" other)
        testCase "the AOT probe's plan normalizes its decomposed character and has the recorded digest" (fun () ->
            // build/Build.MyriadJson.cs TestMorphirupAot runs this command under JIT and Native AOT with --expect-plan.
            let probe = Path.Combine(__SOURCE_DIRECTORY__, "AotProbe")
            let recipe = File.ReadAllBytes(Path.Combine(probe, "recipe.json"))
            let decomposed = Encoding.UTF8.GetBytes "cafe\u0301"
            Expect.isTrue ((ReadOnlySpan recipe).IndexOf(ReadOnlySpan decomposed) >= 0) "the recipe holds a decomposed é"

            let run =
                runIn
                    probe
                    Platform.WinX64
                    [ "setup"
                      "--plan"
                      "--platform"
                      "linux-x64"
                      "--request"
                      "toolchain.toml"
                      "--recipe"
                      "recipe.json"
                      "--repository-id"
                      repositoryText
                      "--artifact-sizes"
                      "artifact-sizes.json"
                      "--expect-plan"
                      File.ReadAllText(Path.Combine(probe, "plan-sha256.txt")).Trim() ]

            Expect.equal run.Exit 0 $"the recorded digest; stdout: {run.Stdout}"

            match (readResult run).Plan with
            | Some digested ->
                let canonical = ReadOnlySpan digested.Canonical
                Expect.isTrue (canonical.IndexOf(ReadOnlySpan(Encoding.UTF8.GetBytes "caf\u00E9")) >= 0) "NFC in the canonical bytes"
                Expect.isTrue (canonical.IndexOf(ReadOnlySpan decomposed) < 0) "no decomposed form in the canonical bytes"
            | None -> failtest "no plan")

        testList "doctor" [
            /// A sandbox whose work folder is a project with the request and the two-platform lock; the caller disposes it.
            let project (request: string) =
                let sandbox = tempDirectory ()
                let morphir = Directory.CreateDirectory(Path.Combine(sandbox.Work, ".morphir"))
                File.Copy(fixture request, Path.Combine(morphir.FullName, "toolchain.toml"))
                File.Copy(fixture "lock/two-platforms-bound.json", Path.Combine(morphir.FullName, "toolchain.lock.json"))
                sandbox

            testCase "a project whose lock binds its request and platform succeeds" (fun () ->
                use sandbox = project "request/approved.toml"
                let run = runIn sandbox.Work Platform.OsxArm64 [ "doctor"; "--json" ]
                Expect.equal run.Exit 0 $"exit code; stdout: {run.Stdout}"
                let result = readResult run
                Expect.equal (result.Command, result.Outcome, result.Diagnostics) (Command.Doctor, Outcome.Succeeded, []) "no diagnostics")

            testCase "an unlocked platform is platform_not_locked, naming no command this build refuses" (fun () ->
                use sandbox = project "request/approved.toml"
                let run = runIn sandbox.Work Platform.LinuxX64 [ "doctor" ]
                Expect.equal run.Exit 1 "exit code"

                match (readResult run).Diagnostics with
                | [ { Detail = DiagnosticDetail.LockBinding(BindingProblem.PlatformNotLocked Platform.LinuxX64, path)
                      Instruction = None
                      Message = message } ] ->
                    Expect.equal (DocumentPath.render path) "platforms" "the lock member"
                    Expect.stringContains message.Text "does not implement it yet" "says that extending the lock is not implemented"
                | other -> failtestf "unexpected diagnostics %A" other)

            testCase "a reformatted request is request_changed" (fun () ->
                use sandbox = project "request/reformatted-approved.toml"
                let run = runIn sandbox.Work Platform.WinX64 [ "doctor" ]
                Expect.equal run.Exit 1 "exit code"

                match (readResult run).Diagnostics with
                | [ { Detail = DiagnosticDetail.LockBinding(BindingProblem.RequestChanged None, path) } ] ->
                    Expect.equal (DocumentPath.render path) "requestSha256" "the lock member"
                | other -> failtestf "unexpected diagnostics %A" other)

            testCase "a lock checked against another recipe's bytes is lock_recipe_mismatch" (fun () ->
                use sandbox = project "request/approved.toml"

                let run =
                    runIn sandbox.Work Platform.WinX64 [ "doctor"; "--recipe"; fixture "recipe/fsharp-synthetic.json"; "--repository-id"; repositoryText ]

                Expect.equal run.Exit 1 "exit code"

                match (readResult run).Diagnostics with
                | [ { Detail = DiagnosticDetail.LockBinding(BindingProblem.LockRecipeMismatch, path) } ] ->
                    Expect.equal (DocumentPath.render path) "recipe.sha256" "the lock member"
                | other -> failtestf "unexpected diagnostics %A" other)
        ]
    ]
