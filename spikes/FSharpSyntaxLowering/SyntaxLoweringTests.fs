// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Spikes.SyntaxLoweringTests

open Expecto
open FSharp.Compiler.Symbols
open Morphir.Frontend.FSharp.Spikes.CheckedSource
open Morphir.Frontend.FSharp.Spikes.SyntaxMatchEvidence

[<Tests>]
let tests =
    testList "Syntax-led match evidence" [
        testCase
            "A reduced variable match retains its source pattern and inferred types"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int) = match x with | n -> n + 1
"""

                let matches = reconstruct program
                Expect.hasLength matches 1 "A reduced-away match remains a match"
                let matched = List.exactlyOne matches

                Expect.equal
                    (shape matched.SubjectType)
                    "System.Int32"
                    "Subject type comes from FCS"

                Expect.equal (shape matched.ResultType) "System.Int32" "Result type comes from FCS"

                Expect.equal
                    (matched.Patterns
                     |> List.map (fun node -> node.Kind))
                    [ "variable" ]
                    "Source pattern is retained"
            )
        testCase
            "Literal and wildcard cases keep their order and contextual types"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int) = match x with | 0 -> 1 | _ -> 2
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (matched.Patterns
                     |> List.map (fun node -> node.Kind))
                    [ "literal"; "wildcard" ]
                    "Source case order survives FCS reduction"

                Expect.equal
                    (matched.Patterns
                     |> List.map (fun node -> shape node.Type))
                    [ "System.Int32"; "System.Int32" ]
                    "Every pattern has the checked subject type"
            )
        testCase
            "A tuple pattern obtains each component type from the checked tuple"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int * string) = match x with | a, b -> b.Length + a
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                let pattern =
                    matched.Patterns
                    |> List.exactlyOne

                Expect.equal pattern.Kind "tuple" "Tuple structure survives reduction"

                Expect.equal
                    (pattern.Children
                     |> List.map (fun node -> shape node.Type))
                    [ "System.Int32"; "System.String" ]
                    "FCS supplies component types in source order"
            )
        testCase
            "Union cases and their arguments keep source order"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    type Status = Available | Rented of int
    let get status = match status with | Rented 0 -> 0 | Rented n -> n + 1 | Available -> 10
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (matched.Patterns
                     |> List.map (fun node -> node.Kind))
                    [ "constructor:Rented"; "constructor:Rented"; "constructor:Available" ]
                    "Clauses are never reordered"

                Expect.equal
                    (matched.Patterns[0].Children
                     |> List.map (fun node -> node.Kind))
                    [ "literal" ]
                    "Literal subpattern remains first"

                Expect.equal
                    (matched.Patterns[1].Children
                     |> List.map (fun node -> shape node.Type))
                    [ "System.Int32" ]
                    "Constructor fields carry compiler types"
            )
        testCase
            "Empty-list and cons patterns retain the checked element and tail types"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (xs: int list) = match xs with | head :: tail -> List.length tail + head | [] -> 0
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (matched.Patterns
                     |> List.map (fun node -> node.Kind))
                    [ "cons"; "empty-list" ]
                    "List case order is preserved"

                Expect.equal
                    (matched.Patterns[0].Children
                     |> List.map (fun node -> shape node.Type))
                    [ "System.Int32"; "Microsoft.FSharp.Collections.FSharpList<System.Int32>" ]
                    "Head and tail use compiler types"
            )
        testCase
            "As patterns retain the constructor and alias with compiler types"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int option) = match x with | Some n as item -> n + Option.get item | None -> 0
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                let pattern = matched.Patterns[0]
                Expect.equal pattern.Kind "as" "The alias is a source pattern"

                Expect.equal
                    (pattern.Children
                     |> List.map (fun node -> node.Kind))
                    [ "constructor:Some"; "variable" ]
                    "Both sides of the as pattern remain"

                Expect.equal
                    (shape pattern.Children[1].Type)
                    "Microsoft.FSharp.Core.FSharpOption<System.Int32>"
                    "Alias type is the full subject type"
            )
        testCase
            "Match lambdas expose checked input and result witnesses"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get = function | Some n -> n + 1 | None -> 0
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (shape matched.SubjectType)
                    "Microsoft.FSharp.Core.FSharpOption<System.Int32>"
                    "Implicit argument type comes from FCS"

                Expect.equal
                    (shape matched.ResultType)
                    "System.Int32"
                    "Return type is the checked function result"

                Expect.equal
                    (matched.Patterns
                     |> List.map (fun node -> node.Kind))
                    [ "constructor:Some"; "constructor:None" ]
                    "Source clause order remains"
            )
        testCase
            "Nested matches inherit checked result types without target-range joins"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int option) =
        match x with
        | Some n ->
            match n with
            | 0 -> 1
            | _ -> n
        | None -> 0
"""

                let matches = reconstruct program
                Expect.hasLength matches 2 "Outer and inner matches both survive"

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.ResultType))
                    [ "System.Int32"; "System.Int32" ]
                    "Both result types come from checked clause witnesses"

                Expect.equal
                    (position matches[0].BodySpans[0])
                    (6, 12, 8, 20)
                    "Whole nested source body range is retained"

                Expect.equal
                    (matches[1].Patterns
                     |> List.map (fun node -> node.Kind))
                    [ "literal"; "wildcard" ]
                    "Nested clause order remains source order"
            )
        testCase
            "Shadowed pattern variables use the symbol at their own declaration"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (n: string) (x: int option) = match x with | Some n -> n | None -> 0
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (shape matched.Patterns[0].Children[0].Type)
                    "System.Int32"
                    "The outer string named n is not selected"
            )
        testCase
            "Generic case fields are specialized by FCS type instantiation"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    type Box<'a> = Empty | Box of 'a
    let get (x: Box<string>) = match x with | Box value -> value | Empty -> ""
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (shape matched.Patterns[0].Children[0].Type)
                    "System.String"
                    "FCS supplies the specialized field type"

                Expect.equal
                    (shape matched.ResultType)
                    "System.String"
                    "The enclosing result remains checked"
            )
        testCase
            "A computed match subject does not require a surviving typed subject node"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get x = match (Some x) with | Some n -> n + 1 | None -> 0
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (shape matched.SubjectType)
                    "Microsoft.FSharp.Core.FSharpOption<System.Int32>"
                    "The constructed subject has its checked type"
            )
        testCase
            "A fully reduced empty-list subject still has its inferred element type"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get () = match [] with | [] -> 0 | head :: _ -> head
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (shape matched.SubjectType)
                    "Microsoft.FSharp.Collections.FSharpList<System.Int32>"
                    "The compiler infers int from the branches, even when no subject node survives"
            )
        testCase
            "Enrichment preserves polymorphic function generalization"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let identity = function | value -> value
    let number = identity 42
    let word = identity "hello"
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.isTrue matched.SubjectType.IsGenericParameter "Identity remains generic"

                Expect.isTrue
                    (matched.SubjectType.Equals matched.ResultType)
                    "Input and output share the same checked parameter"
            )
        testCase
            "Enrichment preserves a polymorphic value returned by a unit match"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let identity = match () with | () -> fun value -> value
    let number = identity 42
    let word = identity "hello"
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.isTrue
                    matched.ResultType.IsFunctionType
                    "The checked value remains a function"
            )
        testCase
            "Unicode offsets and fresh names map back to the original patterns"
            (fun () ->
                let source =
                    """namespace Acme.Demo
module Pricing =
    let __morphir_evidence_0_user = "😀"
    let get (x: int option) = match x with | Some n -> n | None -> __morphir_evidence_0_user.Length
"""

                let matched =
                    reconstruct (check source)
                    |> List.exactlyOne

                Expect.equal
                    (matched.Patterns
                     |> List.map (fun node -> node.Kind))
                    [ "constructor:Some"; "constructor:None" ]
                    "Fresh witnesses never capture user names"

                let crlf = source.Replace("\n", "\r\n")

                let again =
                    reconstruct (check crlf)
                    |> List.exactlyOne

                Expect.equal
                    (again.Patterns
                     |> List.map (fun node -> position node.Span))
                    (matched.Patterns
                     |> List.map (fun node -> position node.Span))
                    "CRLF does not change source coordinates"
            )
        testCase
            "Multi-field constructors preserve separate source arguments"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    type Pair = Pair of int * string
    let get value = match value with | Pair (number, word) -> number + word.Length
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.equal
                    (matched.Patterns[0].Children
                     |> List.map (fun node -> shape node.Type))
                    [ "System.Int32"; "System.String" ]
                    "Constructor fields stay separate and ordered"
            )
        testCase
            "A genuine tuple-valued constructor field remains one tuple argument"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    type Pair = Pair of (int * string)
    let get value = match value with | Pair (number, word) -> number + word.Length
"""

                let matched =
                    reconstruct program
                    |> List.exactlyOne

                Expect.hasLength
                    matched.Patterns[0].Children
                    1
                    "A tuple field is one constructor argument"

                Expect.equal
                    matched.Patterns[0].Children[0].Kind
                    "tuple"
                    "Tuple pattern is retained"
            )
        testCase
            "Missing type witnesses are rejected"
            (fun () ->
                let program =
                    check
                        "namespace Acme.Demo\nmodule Pricing =\n    let get x = match x with | n -> n + 1\n"

                Expect.throwsC
                    (fun () ->
                        reconstructWith
                            (fun source -> { check source with Symbols = [||] })
                            program
                        |> ignore
                    )
                    (fun error ->
                        Expect.stringContains
                            error.Message
                            "Missing or ambiguous generated type witness"
                            "No type is guessed"
                    )
            )
        testCase
            "Ambiguous type witnesses are rejected"
            (fun () ->
                let program =
                    check
                        "namespace Acme.Demo\nmodule Pricing =\n    let get x = match x with | n -> n + 1\n"

                let analyze (source: string) =
                    let analyzed = check source

                    { analyzed with
                        Symbols = Array.append analyzed.Symbols analyzed.Symbols
                    }

                Expect.throwsC
                    (fun () ->
                        reconstructWith analyze program
                        |> ignore
                    )
                    (fun error ->
                        Expect.stringContains
                            error.Message
                            "Missing or ambiguous generated type witness"
                            "No candidate is selected by order"
                    )
            )
        testCase
            "Evidence with changed binding signatures is rejected"
            (fun () ->
                let program =
                    check
                        "namespace Acme.Demo\nmodule Pricing =\n    let get x = match x with | n -> n + 1\n"

                Expect.throwsC
                    (fun () ->
                        reconstructWith
                            (fun _ ->
                                check
                                    "namespace Acme.Demo\nmodule Pricing =\n    let get (x:string) = x\n"
                            )
                            program
                        |> ignore
                    )
                    (fun error ->
                        Expect.stringContains
                            error.Message
                            "changed a checked binding signature"
                            "The original program remains the authority"
                    )
            )
        testCase
            "Missing mapped source symbols are rejected"
            (fun () ->
                let program =
                    check
                        "namespace Acme.Demo\nmodule Pricing =\n    let get x = match x with | n -> n + 1\n"

                let analyze (source: string) =
                    let analyzed = check source

                    let symbols =
                        analyzed.Symbols
                        |> Array.filter (fun useOfSymbol ->
                            match useOfSymbol.Symbol with
                            | :? FSharpMemberOrFunctionOrValue as value when
                                value.DisplayName = "n"
                                ->
                                false
                            | _ -> true
                        )

                    { analyzed with Symbols = symbols }

                Expect.throwsC
                    (fun () ->
                        reconstructWith analyze program
                        |> ignore
                    )
                    (fun error ->
                        Expect.stringContains
                            error.Message
                            "Missing or ambiguous FCS symbol"
                            "Source bindings must resolve exactly"
                    )
            )
        testCase
            "A non-BMP character before a match keeps original UTF-16 columns"
            (fun () ->
                let line =
                    "    let get (x: int option) = let marker = \"😀\" in match x with | Some n -> n + marker.Length | None -> 0"

                let source =
                    "namespace Acme.Demo\nmodule Pricing =\n"
                    + line
                    + "\n"

                let matched =
                    reconstruct (check source)
                    |> List.exactlyOne

                Expect.equal
                    matched.Patterns[0].Children[0].Span.StartColumn
                    (line.IndexOf("Some n")
                     + 5)
                    "FCS and .NET both count UTF-16 code units"
            )
        testCase
            "Nested result witnesses close the inner clause before the outer clause"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int option) =
        match x with
        | Some n ->
            match n with
            | 0 -> 1
            | _ -> n
        | None -> 0
"""

                let analyze (source: string) =
                    Expect.isLessThan
                        (source.IndexOf("__morphir_evidence_0_1_1_result"))
                        (source.IndexOf("__morphir_evidence_0_0_0_result"))
                        "The inner witness closes before the enclosing result witness"

                    check source

                reconstructWith analyze program
                |> ignore
            )
    ]
