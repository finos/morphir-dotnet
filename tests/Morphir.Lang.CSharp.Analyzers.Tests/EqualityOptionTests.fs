// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The rule that a type with IEEE equality from an attribute is partial (csharp-handling-options).
module Morphir.Lang.CSharp.Analyzers.Tests.EqualityOptionTests

open Expecto
open Morphir.Lang.CSharp.Analyzers
open Morphir.Lang.CSharp.Analyzers.Tests.AnalyzerSupport

let private ids (source: string) =
    Expect.isEmpty (compileErrorsWithAttributes source) "the source is valid C#"
    diagnoseWithAttributes source |> List.map (fun (id, line, _) -> id, line)

[<Tests>]
let tests =
    testList "Equality option" [
        test "Type that is not partial: the analyzer asks for partial" {
            let source = "using Morphir;\n[MorphirEquality(FloatEquality.Ieee)]\npublic record Quote(double Value);"
            Expect.equal (ids source) [ Rules.NonPartialEquality.Id, 3 ] "one diagnostic at the type"

            let _, _, message = diagnoseWithAttributes source |> List.head
            Expect.stringContains message "partial" "the message asks for partial"
        }

        test "a partial type with IEEE equality is accepted" {
            Expect.isEmpty (ids "using Morphir;\n[MorphirEquality(FloatEquality.Ieee)]\npublic partial record Quote(double Value);") "no diagnostic"
        }

        test "plain equality needs no partial" {
            Expect.isEmpty (ids "using Morphir;\n[MorphirEquality(FloatEquality.Clr)]\npublic record Quote(double Value);") "no diagnostic"
        }

        test "IEEE equality on a member needs partial on its type" {
            let source = "using Morphir;\npublic record Quote([MorphirEquality(FloatEquality.Ieee)] double Bid, double Ask);"
            Expect.equal (ids source) [ Rules.NonPartialEquality.Id, 2 ] "one diagnostic at the type"
        }

        test "IEEE equality on the assembly needs partial on each record" {
            let source = "using Morphir;\n[assembly: MorphirEquality(FloatEquality.Ieee)]\npublic partial record Quote(double Bid);\npublic record Price(double Value);"
            Expect.equal (ids source) [ Rules.NonPartialEquality.Id, 4 ] "the record that is not partial"
        }

        test "a type attribute with plain equality wins over the assembly attribute" {
            let source = "using Morphir;\n[assembly: MorphirEquality(FloatEquality.Ieee)]\n[MorphirEquality(FloatEquality.Clr)]\npublic record Price(double Value);"
            Expect.isEmpty (ids source) "no diagnostic"
        }

        test "the rule is silent in a project that is not a model project" {
            let source = "using Morphir;\n[MorphirEquality(FloatEquality.Ieee)]\npublic record Quote(double Value);"
            Expect.isEmpty (diagnoseOutsideModelProjectWithAttributes source) "no diagnostic"
        }
    ]
