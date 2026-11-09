module Morphir.Analyzers.Tests.MessageMappingTests

open System
open System.IO
open Expecto
open Morphir.IR.Pipeline
open Morphir.Analyzers

[<Tests>]
let tests =
    testList "Message mapping" [
        test "severities map to MEP severities; fatal is an error" {
            Expect.equal
                ([ Info; Warning; Error; Fatal ] |> List.map MessageMapping.severity)
                [
                    Morphir.Mep.DiagnosticSeverity.Info
                    Morphir.Mep.DiagnosticSeverity.Warning
                    Morphir.Mep.DiagnosticSeverity.Error
                    Morphir.Mep.DiagnosticSeverity.Error
                ]
                "severity"
        }

        test "a message with no path and position has no location and keeps the IR node name" {
            let diagnostic =
                MessageMapping.toDiagnostic
                    { VMessage.create Error "Value has type A, declared B." with
                        Location = Some "Domain.Rules.dailyRate"
                        RuleId = Some "type-mismatch"
                    }

            Expect.equal diagnostic.Severity Morphir.Mep.DiagnosticSeverity.Error "severity"
            Expect.equal diagnostic.Code (Some "type-mismatch") "rule id is the code"
            Expect.equal diagnostic.Message "Domain.Rules.dailyRate: Value has type A, declared B." "message"
            Expect.isNone diagnostic.Location "no location"
            Expect.isEmpty diagnostic.Related "no related information"
            Expect.isNone diagnostic.Data "no data"
        }

        test "a message with no rule id and no location keeps only its text" {
            let diagnostic = MessageMapping.toDiagnostic (VMessage.create Warning "Something.")
            Expect.isNone diagnostic.Code "no code"
            Expect.equal diagnostic.Message "Something." "message"
        }

        test "a path and a position map to a file URI and a zero-based range" {
            let path = Path.Combine(Path.GetTempPath(), "model dir", "a.json")

            let diagnostic =
                MessageMapping.toDiagnostic
                    { VMessage.create Error "Bad." with
                        Path = Some path
                        Location = Some "Rules.value"
                        Position =
                            Some {
                                Start = { Line = 3; Column = 5; Offset = None }
                                End = { Line = 3; Column = 9; Offset = None }
                            }
                    }

            match diagnostic.Location with
            | Some location ->
                Expect.equal location.Uri (Uri(Path.GetFullPath path).AbsoluteUri) "file URI"
                Expect.stringStarts location.Uri "file:" "file scheme"
                Expect.equal location.Range.Start { Line = 2u; Character = 4u } "start made zero-based"
                Expect.equal location.Range.End { Line = 2u; Character = 8u } "end made zero-based"
            | None -> failtest "expected a location"

            Expect.equal diagnostic.Message "Bad." "a located message has no node prefix"
        }

        test "a path without a position has no location" {
            let diagnostic =
                MessageMapping.toDiagnostic
                    { VMessage.create Error "Bad." with
                        Path = Some "a.json"
                    }

            Expect.isNone diagnostic.Location "no range, no location"
        }
    ]
