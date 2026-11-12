// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.ReportTests

open System.Text
open System.Text.Json.Nodes
open Expecto
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.FakeWorker

let private caseIds = [ "classification/unknown-namespace"; "extism/echo"; "extism/error-set" ]

let private scripts = [
    "env-open-socket", [ refused "extension.abi_unsupported" "env.open_socket"; exitWith 3 ]
    "extism-echo", [ started []; echo 1; read 1; exitWith 0 ]
    // extism/error-set expects a failure fault; this host answers, so the case fails.
    "extism-error", [ started []; echo 1; exitWith 0 ]
]

let private subset = lazy (suiteOf caseIds)

/// One run of three cases against the fake, with a known deviation on the failing one.
let private report =
    lazy
        (let fake = host scripts

         try
             runSuite
                 { options fake [ Profile.ExtismKernelV1 ] with
                     Deviations = [
                         {
                             CaseId = "extism/error-set"
                             Tracking = "https://example.org/issues/1"
                             Note = "The fake answers instead of failing."
                         }
                     ]
                 }
                 (subset.Force())
         finally
             fake.Sandbox.Dispose())

let private bytes = lazy (HostReport.toUtf8 (report.Force()))

let private noExpectations: HostReport.Expectations = { Offering = None; Platform = None }

let private check (edited: byte array) =
    HostReport.check (subset.Force()) moduleFile noExpectations edited

let private edit (change: JsonObject -> unit) =
    let node = JsonNode.Parse(Encoding.UTF8.GetString(bytes.Force())).AsObject()
    change node

    Encoding.UTF8.GetBytes(
        node.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true))
    )

let private caseNode (node: JsonObject) (index: int) = node["cases"].[index].AsObject()

let rec private names (node: JsonNode | null) : string list =
    match node with
    | :? JsonObject as value ->
        value
        |> Seq.collect (fun pair ->
            pair.Key
            :: names pair.Value
        )
        |> List.ofSeq
    | :? JsonArray as items ->
        items
        |> Seq.collect names
        |> List.ofSeq
    | _ -> []

let private problems (result: Result<WasmHostReport, string list>) =
    match result with
    | Ok _ -> failtest "Expected the check to refuse the report"
    | Error problems -> problems

[<Tests>]
let tests =
    testList "WASM host report and report check (task 1.5)" [
        // Spec "Report identity".
        test "The report identifies the runner, the corpus, the contract, the host and each module" {
            let node = JsonNode.Parse(Encoding.UTF8.GetString(bytes.Force())).AsObject()
            let suite = subset.Force()

            Expect.equal
                (node["suite"].["corpus"].["sha256"].GetValue<string>())
                suite.Pin.Sha256
                "corpus digest"

            Expect.equal
                (node["suite"].["contract"].["version"].GetValue<string>())
                suite.Kit.ContractVersion
                "contract version"

            Expect.equal
                (node["runner"].["name"].GetValue<string>())
                "morphir-dotnet mck wasm-host"
                "runner"

            Expect.isNotEmpty (node["runner"].["version"].GetValue<string>()) "runner version"
            let host = node["host"].AsObject()

            Expect.equal
                (host["sha256"].GetValue<string>())
                (HostRunner.fileSha256 dotnet)
                "host digest"

            Expect.equal (host["executable"].GetValue<string>()) dotnet "host path"
            Expect.equal (host["buildIdentity"].GetValue<string>()) Identity "build identity"

            for name in [ "offering"; "platform"; "engine" ] do
                Expect.isNotNull host[name] $"host {name}"

            for index in 0..2 do
                let moduleNode = (caseNode node index).["module"]

                Expect.equal
                    (moduleNode["observedSha256"].GetValue<string>())
                    (moduleNode["sha256"].GetValue<string>())
                    "module digest"
        }

        test "The report starts with the pinned schema and records the normative verdict" {
            let node = JsonNode.Parse(Encoding.UTF8.GetString(bytes.Force()))

            Expect.equal
                (node.AsObject()
                 |> Seq.head
                 |> fun pair -> pair.Key)
                "$schema"
                "schema is the first member"

            Expect.equal
                (node["$schema"].GetValue<string>())
                "https://morphir.finos.org/schemas/mep-wasm-host-kit/0.1.0-draft.1/report.schema.json"
                "pinned schema"

            Expect.isFalse
                (node["verdict"].["qualified"].GetValue<bool>())
                "failed case and missing guest evidence"

            Expect.isNull node["qualificationNotice"] "obsolete notice removed"
        }

        test "The same observations give the same bytes" {
            Expect.equal (HostReport.toUtf8 (report.Force())) (bytes.Force()) "written twice"

            match HostReport.parse (subset.Force()) (bytes.Force()) with
            | Ok parsed ->
                Expect.equal
                    (HostReport.toUtf8 parsed.Report)
                    (bytes.Force())
                    "read and written again"
            | Error problems -> failtest (String.concat "\n" problems)
        }

        test "deadline margin uses the schema's positive numeric wire shape" {
            use document =
                System.Text.Json.JsonDocument.Parse(System.ReadOnlyMemory(bytes.Force()))

            let margin =
                document.RootElement.GetProperty("suite").GetProperty("deadlineMarginSeconds")

            Expect.equal
                margin.ValueKind
                System.Text.Json.JsonValueKind.Number
                "a JSON number, not decimal text"

            Expect.equal
                (margin.GetDecimal())
                (subset.Force()).Kit.DeadlineMarginSeconds
                "exact decimal value"

            match HostReport.parse (subset.Force()) (bytes.Force()) with
            | Ok _ -> ()
            | Error errors -> failtest (String.concat "\n" errors)

            let legacyText =
                edit (fun node -> node["suite"].["deadlineMarginSeconds"] <- JsonValue.Create("1"))

            match HostReport.parse (subset.Force()) legacyText with
            | Error errors ->
                Expect.exists
                    errors
                    (fun error -> error.Contains "positive JSON number")
                    "text is refused at the reader boundary"
            | Ok _ -> failtest "A textual deadline margin is not the advertised schema"
        }

        for citationExists in [ false; true ] do
            testCase
                $"public report check refuses unchecked citation even with matching digests exists={citationExists}"
                (fun () ->
                    let fake = host []
                    use _ = fake.Sandbox
                    let citationPath = fake.Sandbox.PathOf "guest-report.json"

                    if citationExists then
                        System.IO.File.WriteAllText(
                            citationPath,
                            "{\"verdict\":{\"qualified\":true,\"reasons\":[]}}"
                        )

                    let citation: GuestKitCitation = {
                        Profile = Profile.ExtismKernelV1
                        Report = citationPath
                        ReportSha256 =
                            if citationExists then
                                HostRunner.fileSha256 citationPath
                            else
                                String.replicate 64 "2"
                        HostSha256 = (report.Force()).Host.Sha256
                        GuestModuleSha256 = String.replicate 64 "4"
                    }

                    let withCitation =
                        { report.Force() with
                            GuestKitCitations = [ citation ]
                        }

                    Expect.exists
                        (problems (check (HostReport.toUtf8 withCitation)))
                        (fun error ->
                            error.Contains "generic guest kit report checker is unavailable"
                        )
                        "matching file and caller digests cannot replace the missing checker"
                )

        // Spec "Known deviations are recorded, not hidden".
        test "A known deviation explains a failure and does not turn it into a pass" {
            let failing =
                (report.Force()).Cases
                |> List.find (fun case -> case.Case.Case.CaseId = "extism/error-set")

            Expect.equal failing.Outcome.WireValue "failed" "still failed"
            Expect.isSome failing.Deviation "the deviation is attached"
            Expect.equal (HostRunner.exitCode (report.Force())) 1 "the run does not pass"
        }

        test "report-check passes the runner's own report and recomputes its outcomes" {
            match check (bytes.Force()) with
            | Ok checkedReport ->
                Expect.equal
                    (checkedReport.Cases
                     |> List.map (fun case -> case.Outcome.WireValue))
                    [ "passed"; "passed"; "failed" ]
                    "the outcomes from the observations"
            | Error problems -> failtest (String.concat "\n" problems)
        }

        test "report-check refuses a report with a missing case" {
            let edited = edit (fun node -> node["cases"].AsArray().RemoveAt 1)

            Expect.exists
                (problems (check edited))
                (fun problem -> problem.Contains "has no result for the case")
                "named"
        }

        test "report-check refuses a changed module digest" {
            let edited =
                edit (fun node ->
                    (caseNode node 0).["module"].["sha256"] <- String.replicate 64 "0"
                )

            Expect.exists
                (problems (check edited))
                (fun problem -> problem.Contains "module digest")
                "named"
        }

        test "report-check refuses a case from another kit" {
            let edited = edit (fun node -> (caseNode node 2).["id"] <- "wasm-host-9999")

            Expect.exists
                (problems (check edited))
                (fun problem -> problem.Contains "is not a case of the kit")
                "named"
        }

        test "report-check refuses a report of another corpus" {
            let edited =
                edit (fun node -> node["suite"].["corpus"].["sha256"] <- String.replicate 64 "1")

            Expect.exists
                (problems (check edited))
                (fun problem -> problem.Contains "of the corpus")
                "named"
        }

        // Spec "Report identity": Edited verdict.
        test "report-check recomputes outcomes, so an outcome edited to passed is refused" {
            let edited = edit (fun node -> (caseNode node 2).["outcome"] <- "passed")

            Expect.exists
                (problems (check edited))
                (fun problem -> problem.Contains "its observations give failed")
                "named"
        }

        test "report-check retains its stricter refusal of an unrecorded extra member" {
            let edited = edit (fun node -> node["qualified"] <- true)

            Expect.exists
                (problems (check edited))
                (fun problem -> problem.Contains "is not what the runner writes")
                "named"
        }

        // Spec "Report identity"; corpus convention identity at 48d10706f.
        test "report-check refuses a report without the host's offering, platform or engine" {
            for name in [ "offering"; "platform"; "engine" ] do
                for missing: JsonNode in
                    [
                        null
                        System.Text.Json.Nodes.JsonValue.Create "" :> JsonNode
                        System.Text.Json.Nodes.JsonValue.Create " " :> JsonNode
                    ] do
                    let edited = edit (fun node -> node["host"].[name] <- missing)

                    Expect.exists
                        (problems (check edited))
                        (fun problem ->
                            problem.Contains $"does not identify the host's {name}"
                            && problem.Contains "identity"
                        )
                        $"the {name} is named with the convention"
        }

        // Spec "Each alternative is its own scenario" and "A runaway guest is stopped in time": the report
        // records which bound ended the call.
        test
            "A passed case records the alternative it satisfied, and a failed one the failures of each" {
            let fake =
                host [
                    "extism-spin", [ started []; read 1; fault "call-deadline"; exitWith 1 ]
                    // classification/known-name-other-signature expects a refusal; this host serves the module.
                    "bytes-input-byte-i64", [ started []; read 1; exitWith 0 ]
                ]

            use _ = fake.Sandbox

            let alternatives =
                suiteOf [ "bounds/spin-extism"; "classification/known-name-other-signature" ]

            let report = runSuite (options fake [ Profile.ExtismKernelV1 ]) alternatives

            let node =
                JsonNode.Parse(Encoding.UTF8.GetString(HostReport.toUtf8 report)).AsObject()

            let caseOf (caseId: string) =
                node["cases"].AsArray()
                |> Seq.find (fun case -> case["caseId"].GetValue<string>() = caseId)

            let spin = caseOf "bounds/spin-extism"

            Expect.equal
                (spin["outcome"].GetValue<string>())
                "failed"
                "call-deadline at once is outside 30 to 32"

            let failing = caseOf "classification/known-name-other-signature"

            Expect.equal
                (failing["outcome"].GetValue<string>())
                "failed"
                "a served module is not the refusal"

            Expect.all
                (failing["failures"].AsArray()
                 |> List.ofSeq)
                (fun failure -> failure["alternative"].GetValue<int>() = 2)
                "an Extism-only host is judged on the second alternative only"

            Expect.equal
                (spin["failures"].AsArray()
                 |> Seq.map (fun failure -> failure["alternative"].GetValue<int>())
                 |> Seq.distinct
                 |> List.ofSeq)
                [ 1; 2 ]
                "both alternatives of a metering host, each with its failures"

            let passingFake =
                host [ "extism-spin", [ started []; read 1; fault "budget-exhausted"; exitWith 1 ] ]

            use _ = passingFake.Sandbox

            let passed =
                runSuite
                    (options passingFake [ Profile.ExtismKernelV1 ])
                    (suiteOf [ "bounds/spin-extism" ])

            let passedNode =
                JsonNode.Parse(Encoding.UTF8.GetString(HostReport.toUtf8 passed)).["cases"].[0]

            Expect.equal (passedNode["outcome"].GetValue<string>()) "passed" "passed"

            Expect.equal
                (passedNode["satisfied"].ToJsonString())
                """[{"run":1,"alternative":1,"when":{"bound":"instructionBudget","enforced":true}}]"""
                "the satisfied alternative and its condition"

            match
                HostReport.check
                    (suiteOf [ "bounds/spin-extism" ])
                    moduleFile
                    noExpectations
                    (HostReport.toUtf8 passed)
            with
            | Ok checkedReport ->
                Expect.equal
                    (checkedReport.Cases.Head.Outcome)
                    (CaseOutcome.Passed [
                        {
                            Run = 1
                            Alternative = 1
                            Condition = Condition.Bound(Bound.InstructionBudget, true)
                        }
                    ])
                    "report-check recomputes the same alternative"
            | Error problems -> failtest (String.concat "\n" problems)
        }

        // Spec "One executable per report": AOT report cited for the tool.
        test "report-check refuses a report cited for another offering or platform" {
            let cited: HostReport.Expectations = {
                Offering = Some "dotnet-tool"
                Platform = None
            }

            Expect.exists
                (problems (HostReport.check (subset.Force()) moduleFile cited (bytes.Force())))
                (fun problem -> problem.Contains "it is cited for dotnet-tool")
                "the offering"

            let platform: HostReport.Expectations = {
                Offering = None
                Platform = Some "linux-x64"
            }

            Expect.exists
                (problems (HostReport.check (subset.Force()) moduleFile platform (bytes.Force())))
                (fun problem -> problem.Contains "it is cited for linux-x64")
                "the platform"
        }

        // Spec "A host does not qualify on crafted cases alone": Guest evidence from another executable.
        test "another executable citation is a verdict reason rather than a checker problem" {
            let citation: GuestKitCitation = {
                Profile = Profile.ExtismKernelV1
                Report = "guest-run.json"
                ReportSha256 = String.replicate 64 "2"
                HostSha256 = String.replicate 64 "3"
                GuestModuleSha256 = String.replicate 64 "4"
            }

            let withCitation =
                { report.Force() with
                    GuestKitCitations = [ citation ]
                }

            match HostReport.checkContract (subset.Force()) (HostReport.toUtf8 withCitation) with
            | Error errors -> failtest (String.concat "\n" errors)
            | Ok verdict ->
                Expect.stringContains
                    (Json.compact verdict)
                    "guest_kit_host_mismatch"
                    "normative verdict reason in synthetic policy data"
        }

        // Spec "A host does not qualify on crafted cases alone": Refusals pass but compile fails.
        test "A report records the verdict and a per-profile citation list" {
            let all = names (JsonNode.Parse(Encoding.UTF8.GetString(bytes.Force())))
            Expect.contains all "qualified" "computed verdict"
            Expect.contains all "guestKitCitations" "the citation list is always written"
            Expect.isFalse (List.contains "guestKitCitation" all) "singular citation removed"
        }
    ]
