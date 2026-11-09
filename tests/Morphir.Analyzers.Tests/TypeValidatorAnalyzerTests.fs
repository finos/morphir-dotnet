module Morphir.Analyzers.Tests.TypeValidatorAnalyzerTests

open System
open System.IO
open System.Text.Json
open System.Threading
open Expecto
open Morphir.IR
open Morphir.IR.Model
open Morphir.Extensions
open Morphir.Analyzers

let private host = ReleaseVersion.Create "0.4.0"

let private load (name: string) : VersionedModel =
    let path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MorphirIR", name)
    use document = JsonDocument.Parse(File.ReadAllText path)

    match Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement with
    | Ok decoded ->
        let version =
            match SourceFormatVersion.parse "3" with
            | Ok version -> version
            | Error error -> failwith (string error)

        {
            SourceVersion = version
            Distribution = DistributionModel.Classic decoded.Distribution
        }
    | Error error -> failtest $"fixture {name} did not decode: {error.Message}"

let private analyze (model: VersionedModel) =
    let analyzer = TypeValidatorAnalyzer(host) :> IModelAnalyzer

    analyzer.AnalyzeAsync(model.Distribution, CancellationToken.None).GetAwaiter().GetResult()
    |> List.ofSeq

[<Tests>]
let tests =
    testList "Type validator analyzer" [
        test "the entry is a built-in validator for classic versions 1, 2 and 3" {
            let entry = (TypeValidatorAnalyzer(host) :> IModelAnalyzer).Entry
            Expect.equal entry.Identity.Id.Value "morphir.type-validator" "id"
            Expect.equal entry.Identity.Version host "the host version"
            Expect.equal (List.ofSeq entry.Types) [ ExtensionType.Validator ] "validator type"
            Expect.equal entry.Delivery ExtensionDelivery.BuiltIn "built-in"
            Expect.equal entry.Hosts HostAvailability.AllHosts "both hosts"

            match entry.ValidatorClaimOrNull with
            | null -> failtest "no validator claim"
            | claim -> Expect.equal (List.ofSeq claim.IrVersionTexts) [ "1"; "2"; "3" ] "classic versions"

            Expect.equal TypeValidatorAnalyzer.Id entry.Identity.Id "the static id is the entry id"
        }

        test "a classic model with a type mismatch gives one type-mismatch error that names both types" {
            match analyze (load "daily-rate-mismatch-v3.json") with
            | [ diagnostic ] ->
                Expect.equal diagnostic.Severity Morphir.Mep.DiagnosticSeverity.Error "error"
                Expect.equal diagnostic.Code (Some "type-mismatch") "code"
                Expect.stringContains diagnostic.Message "String" "names the String type"
                Expect.stringContains diagnostic.Message "Int" "names the Int type"
                Expect.stringStarts diagnostic.Message "Inventory.dailyRate: " "names the value"
                Expect.isNone diagnostic.Location "an IR node has no source position"
            | other -> failtest $"expected one diagnostic, got {other}"
        }

        test "a clean classic model gives no diagnostic" {
            Expect.isEmpty (analyze (load "daily-rate-v3.json")) "clean"
        }

        test "the analyzer leaves the model unchanged" {
            let model = load "daily-rate-mismatch-v3.json"
            let before = model.Distribution
            analyze model |> ignore
            Expect.equal model.Distribution before "unchanged"
        }

        test "the built-in analyzers are the type validator only" {
            Expect.equal
                (BuiltInAnalyzers.Create host
                 |> Seq.map (fun analyzer -> analyzer.Entry.Identity.Id.Value)
                 |> List.ofSeq)
                [ "morphir.type-validator" ]
                "built-ins"
        }
    ]
