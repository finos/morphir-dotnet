module Morphir.Extensions.Tests.AnalyzerTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.IR
open Morphir.IR.Model
open Morphir.Extensions

let private host = ReleaseVersion.Create "0.4.0"

let private formatVersion text =
    match Versioning.parse text with
    | Ok parsed -> parsed
    | Error error -> failwith $"bad format version {text}: {error}"

let private sourceVersion text =
    match SourceFormatVersion.parse text with
    | Ok parsed -> parsed
    | Error error -> failwith $"bad version {text}: {error}"

let private classicModel: VersionedModel = {
    SourceVersion = sourceVersion "3"
    Distribution =
        DistributionModel.Classic(
            Classic.Distribution.Library(PackageName.packageNameFromString "My.Pack", Map.empty, { Modules = Map.empty })
        )
}

let private v4Model: VersionedModel = {
    SourceVersion = sourceVersion "4.0.0"
    Distribution =
        DistributionModel.V4(
            V4.Distribution.Library(
                PackageName.packageNameFromString "My.Pack",
                V4.OrderedMap.empty,
                { Modules = V4.OrderedMap.empty }
            )
        )
}

let private diagnostic severity code message : Morphir.Mep.Diagnostic = {
    Severity = severity
    Code = Some code
    Message = message
    Location = None
    Related = [||]
    Data = None
}

/// A fake analyzer: classic versions 1 to 3 unless told otherwise, and a fixed result.
type private Fake(id: string, result: unit -> IReadOnlyList<Morphir.Mep.Diagnostic>, ?versions: string list) =
    let versions = defaultArg versions [ "1"; "2"; "3" ]
    member val Calls = 0 with get, set

    interface IModelAnalyzer with
        member _.Entry =
            ExtensionEntry.BuiltInValidator(
                ExtensionIdentity.Create(ExtensionId.Create id, ExtensionName.Create id, host, null),
                match ValidatorClaim.ofSeq (versions |> List.map formatVersion) with
                | Ok claim -> claim
                | Error error -> failwith (string error)
            )

        member this.AnalyzeAsync(_model, _ct) =
            this.Calls <- this.Calls + 1
            Task.FromResult(result ())

let private reporting id (messages: string list) =
    Fake(
        id,
        fun () ->
            [|
                for message in messages -> diagnostic Morphir.Mep.DiagnosticSeverity.Warning "rule" message
            |]
    )

let private run turnedOff (analyzers: Fake list) model =
    AnalyzerRunner
        .RunAsync(
            analyzers |> Seq.map (fun analyzer -> analyzer :> IModelAnalyzer),
            model,
            turnedOff |> Seq.map ExtensionId.Create,
            CancellationToken.None
        )
        .GetAwaiter()
        .GetResult()

let private outcomes (result: AnalysisResult) =
    result.Runs
    |> Seq.map (fun run -> run.Analyzer.Value, run.Outcome)
    |> List.ofSeq

let private reported (result: AnalysisResult) =
    result.Diagnostics
    |> Seq.map (fun item -> item.Extension.Value, item.Diagnostic.Message)
    |> List.ofSeq

[<Tests>]
let tests =
    testList "Analyzer runner" [
        test "diagnostics merge additively, by extension id and then in report order" {
            let result =
                run [] [ reporting "zeta.lint" [ "z1"; "z0" ]; reporting "alpha.lint" [ "a2"; "a1" ] ] classicModel

            Expect.equal
                (reported result)
                [ "alpha.lint", "a2"; "alpha.lint", "a1"; "zeta.lint", "z1"; "zeta.lint", "z0" ]
                "stable merged order"

            Expect.equal
                (outcomes result)
                [ "alpha.lint", AnalyzerOutcome.Ran; "zeta.lint", AnalyzerOutcome.Ran ]
                "both ran, in id order"

            Expect.isEmpty result.HostDiagnostics "an analyzer ran"
            Expect.isFalse result.HasErrors "warnings only"
            Expect.isFalse result.HasFailures "no failure"
        }

        test "the order does not depend on the registration order" {
            let forward = run [] [ reporting "a.one" [ "1" ]; reporting "b.two" [ "2" ] ] classicModel
            let backward = run [] [ reporting "b.two" [ "2" ]; reporting "a.one" [ "1" ] ] classicModel
            Expect.equal (reported forward) (reported backward) "same order"
        }

        test "a turned-off analyzer does not run and is reported as turned off" {
            let off = reporting "morphir.type-validator" [ "mismatch" ]
            let result = run [ "morphir.type-validator" ] [ off; reporting "other.lint" [ "x" ] ] classicModel
            Expect.equal off.Calls 0 "not called"

            Expect.equal
                (outcomes result)
                [
                    "morphir.type-validator", AnalyzerOutcome.NotRun AnalyzerSkipReason.TurnedOff
                    "other.lint", AnalyzerOutcome.Ran
                ]
                "outcomes"

            Expect.equal (reported result) [ "other.lint", "x" ] "no diagnostic from the turned-off analyzer"
            Expect.equal AnalyzerSkipReason.TurnedOff.WireValue "turned-off" "wire value"
        }

        test "a failing analyzer is a failed outcome and the others still report" {
            let failing = Fake("bad.lint", fun () -> raise (InvalidOperationException "boom"))
            let result = run [] [ failing; reporting "good.lint" [ "ok" ] ] classicModel

            match outcomes result with
            | [ ("bad.lint", AnalyzerOutcome.Failed message); ("good.lint", AnalyzerOutcome.Ran) ] ->
                Expect.stringContains message "boom" "the failure names the cause"
            | other -> failtest $"unexpected outcomes {other}"

            Expect.equal (reported result) [ "good.lint", "ok" ] "the working analyzer reports"
            Expect.isTrue result.HasFailures "a failure"
            Expect.equal (AnalyzerOutcome.Failed "x").WireValue "failed" "wire value"
        }

        test "an analyzer that returns null fails instead of reporting nothing" {
            let broken = Fake("null.lint", fun () -> Unchecked.defaultof<_>)
            let result = run [] [ broken ] classicModel

            match outcomes result with
            | [ ("null.lint", AnalyzerOutcome.Failed _) ] -> ()
            | other -> failtest $"unexpected outcomes {other}"
        }

        test "cancellation is not an analyzer failure" {
            use source = new CancellationTokenSource()
            source.Cancel()

            let cancelled =
                Fake("slow.lint", fun () -> raise (OperationCanceledException source.Token))

            Expect.throwsT<OperationCanceledException>
                (fun () ->
                    AnalyzerRunner
                        .RunAsync([ cancelled :> IModelAnalyzer ], classicModel, [], source.Token)
                        .GetAwaiter()
                        .GetResult()
                    |> ignore)
                "cancellation propagates"
        }

        test "error diagnostics are errors of the result" {
            let erroring =
                Fake(
                    "strict.lint",
                    fun () -> [| diagnostic Morphir.Mep.DiagnosticSeverity.Error "type-mismatch" "bad" |]
                )

            Expect.isTrue (run [] [ erroring ] classicModel).HasErrors "an error"
        }

        test "with no analyzer for the model's family, one info diagnostic names the family" {
            let classicOnly = reporting "morphir.type-validator" [ "x" ]
            let result = run [] [ classicOnly ] v4Model
            Expect.equal classicOnly.Calls 0 "a classic analyzer does not read V4"

            Expect.equal
                (outcomes result)
                [ "morphir.type-validator", AnalyzerOutcome.NotRun AnalyzerSkipReason.UnsupportedModel ]
                "not run"

            Expect.isEmpty result.Diagnostics "no analyzer diagnostic"

            match List.ofSeq result.HostDiagnostics with
            | [ info ] ->
                Expect.equal info.Severity Morphir.Mep.DiagnosticSeverity.Info "info"
                Expect.equal info.Code (Some "no-analyzer") "code"
                Expect.stringContains info.Message "V4" "names the family"
                Expect.stringContains info.Message "4.0.0" "names the version"
            | other -> failtest $"expected one info diagnostic, got {other}"

            Expect.isFalse result.HasErrors "info only"
            Expect.equal AnalyzerSkipReason.UnsupportedModel.WireValue "unsupported-model" "wire value"
        }

        test "with no analyzer at all, the info diagnostic is still reported" {
            let result = run [] [] classicModel
            Expect.isEmpty result.Runs "no runs"
            Expect.equal result.HostDiagnostics.Count 1 "one info diagnostic"
            Expect.stringContains result.HostDiagnostics[0].Message "Classic" "names the family"
        }

        test "an analyzer can claim a SemVer pre-release version" {
            let v4 = Fake("v4.lint", (fun () -> [||]), [ "4.0.0" ])
            let result = run [] [ v4 ] v4Model
            Expect.equal (outcomes result) [ "v4.lint", AnalyzerOutcome.Ran ] "ran"
            Expect.isEmpty result.HostDiagnostics "an analyzer ran"
        }

        test "an analyzer that returns a null diagnostic in its list fails" {
            let broken = Fake("null-item.lint", fun () -> [| Unchecked.defaultof<Morphir.Mep.Diagnostic> |])

            match outcomes (run [] [ broken ] classicModel) with
            | [ ("null-item.lint", AnalyzerOutcome.Failed detail) ] ->
                Expect.stringContains detail "null diagnostic" "detail"
            | other -> failtest $"unexpected outcomes {other}"
        }

        test "an analyzer with no readable entry is a failure and the others still run" {
            let throwing =
                { new IModelAnalyzer with
                    member _.Entry = raise (InvalidOperationException "no entry")
                    member _.AnalyzeAsync(_, _) = Task.FromResult<IReadOnlyList<Morphir.Mep.Diagnostic>>([||])
                }

            let result =
                AnalyzerRunner
                    .RunAsync([ throwing; reporting "good.lint" [ "ok" ] :> IModelAnalyzer ], classicModel, [], CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            Expect.equal (outcomes result) [ "good.lint", AnalyzerOutcome.Ran ] "the other analyzer ran"
            Expect.isTrue result.HasFailures "a failure"
            Expect.isTrue result.HasErrors "an error"

            match List.ofSeq result.HostDiagnostics with
            | [ error ] -> Expect.equal error.Code (Some "analyzer-entry") "code"
            | other -> failtest $"expected one host diagnostic, got {other}"
        }

        test "C# callers decide on an outcome with Match" {
            let describe (outcome: AnalyzerOutcome) =
                outcome.Match(
                    Func<string>(fun () -> "ran"),
                    Func<string, string>(fun detail -> $"failed {detail}"),
                    Func<AnalyzerSkipReason, string>(fun reason -> $"skipped {reason.WireValue}")
                )

            Expect.equal (describe AnalyzerOutcome.Ran) "ran" "ran"
            Expect.equal (describe (AnalyzerOutcome.Failed "x")) "failed x" "failed"
            Expect.equal (describe (AnalyzerOutcome.NotRun AnalyzerSkipReason.TurnedOff)) "skipped turned-off" "not run"
            Expect.equal (AnalyzerOutcome.NotRun AnalyzerSkipReason.TurnedOff).SkipReasonOrNull AnalyzerSkipReason.TurnedOff "reason"
            Expect.isNull AnalyzerOutcome.Ran.SkipReasonOrNull "no reason"
        }

        test "C# callers read MEP diagnostics without options" {
            let location: Morphir.Mep.SourceLocation = {
                Uri = "file:///a.json"
                Range = { Start = { Line = 0u; Character = 0u }; End = { Line = 0u; Character = 1u } }
            }

            let full = MepRecords.CreateDiagnostic(Morphir.Mep.DiagnosticSeverity.Error, "m", "c", location)
            Expect.equal (MepRecords.CodeOrNull full) "c" "code"
            Expect.equal (MepRecords.LocationOrNull full) location "location"
            Expect.isFalse (MepRecords.DataOrNull full).HasValue "no data"
            Expect.isTrue (MepRecords.IsError full.Severity) "error"

            let bare = MepRecords.CreateDiagnostic(Morphir.Mep.DiagnosticSeverity.Hint, "m", null, null)
            Expect.isNull (MepRecords.CodeOrNull bare) "no code"
            Expect.isNull (MepRecords.LocationOrNull bare) "no location"
            Expect.isFalse (MepRecords.IsError bare.Severity) "not an error"

            Expect.equal
                ([
                    Morphir.Mep.DiagnosticSeverity.Error
                    Morphir.Mep.DiagnosticSeverity.Warning
                    Morphir.Mep.DiagnosticSeverity.Info
                    Morphir.Mep.DiagnosticSeverity.Hint
                 ]
                 |> List.map MepRecords.WireValue)
                [ "error"; "warning"; "info"; "hint" ]
                "severity wire values"
        }

        test "outcomes have wire values for the report" {
            Expect.equal AnalyzerOutcome.Ran.WireValue "ran" "ran"
            Expect.equal (AnalyzerOutcome.NotRun AnalyzerSkipReason.TurnedOff).WireValue "not-run" "not run"
            Expect.isNull AnalyzerOutcome.Ran.ReasonOrNull "no reason"
            Expect.equal (AnalyzerOutcome.NotRun AnalyzerSkipReason.TurnedOff).ReasonOrNull "turned-off" "reason"
            Expect.equal (AnalyzerOutcome.Failed "boom").DetailOrNull "boom" "detail"
        }
    ]
