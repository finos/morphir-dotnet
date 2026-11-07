// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Spikes.OriginalReplayTests

open Expecto
open Morphir.Frontend.FSharp.Spikes.CheckedSource
open Morphir.Frontend.FSharp.Spikes.SyntaxMatchEvidence

// Sources are the unchanged, independently written original join probes.
// Expected type and pattern lists below are written by hand.
[<Tests>]
let tests =
    testList "Original match probes through syntax-led reconstruction" [
        testCase
            "wildcard"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get x = match x with | Some n -> n | _ -> 0
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Microsoft.FSharp.Core.FSharpOption<System.Int32>" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "constructor:Some"; "wildcard" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "variable"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int) = match x with | n -> n + 1
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "System.Int32" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "variable" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "literal"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int) = match x with | 0 -> 1 | _ -> 2
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "System.Int32" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "literal"; "wildcard" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "tuple"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int * int) = match x with | a, b -> a + b
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "(System.Int32 * System.Int32)" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "tuple" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "constructor"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    type Status = Available | Rented of int
    let get status = match status with | Rented 0 -> 0 | Rented n -> n + 1 | Available -> 10
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Acme.Demo.Pricing.Status" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "constructor:Rented"; "constructor:Rented"; "constructor:Available" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "empty-list"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (xs: int list) = match xs with | [] -> 0 | head :: _ -> head
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Microsoft.FSharp.Collections.FSharpList<System.Int32>" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "empty-list"; "cons" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "cons"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (xs: int list) = match xs with | head :: tail -> List.length tail + head | [] -> 0
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Microsoft.FSharp.Collections.FSharpList<System.Int32>" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "cons"; "empty-list" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "as"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: int option) = match x with | Some n as item -> n + Option.get item | None -> 0
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Microsoft.FSharp.Core.FSharpOption<System.Int32>" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "as"; "constructor:None" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "unit"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get (x: unit) = match x with | () -> 1
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Microsoft.FSharp.Core.Unit" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "unit" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "match-lambda"
            (fun () ->
                let program =
                    check
                        """namespace Acme.Demo
module Pricing =
    let get = function | Some n -> n + 1 | None -> 0
"""

                let matches = reconstruct program

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Microsoft.FSharp.Core.FSharpOption<System.Int32>" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "constructor:Some"; "constructor:None" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
        testCase
            "nested"
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

                Expect.equal
                    (matches
                     |> List.map (fun node -> shape node.SubjectType))
                    [ "Microsoft.FSharp.Core.FSharpOption<System.Int32>"; "System.Int32" ]
                    "Checked subject types are preserved"

                Expect.equal
                    (matches
                     |> List.map (fun matched ->
                         matched.Patterns
                         |> List.map (fun node -> node.Kind)
                     ))
                    [ [ "constructor:Some"; "constructor:None" ]; [ "literal"; "wildcard" ] ]
                    "Source pattern and clause order are preserved"

                for matched in matches do
                    Expect.equal
                        (shape matched.ResultType)
                        "System.Int32"
                        "Checked result type is preserved"
            )
    ]
