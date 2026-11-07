// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.MatchJoinSpikeTests

open System
open System.Collections
open System.IO
open Microsoft.FSharp.Reflection
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Symbols.FSharpExprPatterns
open FSharp.Compiler.Text
open Morphir.TestSupport
open Expecto

let private checker = FSharpChecker.Create(keepAssemblyContents = true)

// Reflection is confined to this spike's test visitor, never a production/AOT path.
let rec private syntaxExpressions (node: obj) = seq {
    if not (isNull node) then
        match node with
        | :? SynExpr as expression -> yield expression
        | _ -> ()

        let typ = node.GetType()

        if FSharpType.IsUnion typ then
            let _, fields = FSharpValue.GetUnionFields(node, typ)

            for field in fields do
                yield! syntaxExpressions field
        elif FSharpType.IsRecord typ then
            for field in FSharpValue.GetRecordFields node do
                yield! syntaxExpressions field
        elif not (node :? string) then
            match node with
            | :? IEnumerable as values ->
                for value in values do
                    yield! syntaxExpressions value
            | _ -> ()
}

let rec private typedExpressions (expression: FSharpExpr) = seq {
    yield expression

    for child in expression.ImmediateSubExpressions do
        yield! typedExpressions child
}

let rec private declarationExpressions declarations = seq {
    for declaration in declarations do
        match declaration with
        | FSharpImplementationFileDeclaration.Entity(_, children) ->
            yield! declarationExpressions children
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(_, _, body)
        | FSharpImplementationFileDeclaration.InitAction body -> yield! typedExpressions body
}

let private position (span: range) =
    span.StartLine, span.StartColumn, span.EndLine, span.EndColumn

let private verifyJoin (source: string) =
    use sandbox = TestSandbox.Create("morphir-fsharp-match-spike-")
    let file = Path.Combine(sandbox.Work, "Pricing.fs")
    File.WriteAllText(file, source)
    let platform = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") :?> string

    let references =
        platform.Split(Path.PathSeparator)
        |> Array.map (fun path ->
            "-r:"
            + path
        )

    let options =
        checker.GetProjectOptionsFromCommandLineArgs(
            Path.Combine(sandbox.Work, "Spike.fsproj"),
            Array.concat [
                [| "--target:library"; "--noframework"; "--simpleresolution"; "--optimize-" |]
                references
                [| file |]
            ]
        )

    let parsing, _ = checker.GetParsingOptionsFromProjectOptions options

    let parsed =
        checker.ParseFile(file, SourceText.ofString source, parsing)
        |> Async.RunSynchronously

    let project =
        checker.ParseAndCheckProject options
        |> Async.RunSynchronously

    let errors =
        project.Diagnostics
        |> Array.filter (fun diagnostic -> diagnostic.Severity = FSharpDiagnosticSeverity.Error)

    Expect.isEmpty errors (sprintf "The spike source must type-check: %A" errors)

    let matches =
        parsed.ParseTree
        |> box
        |> syntaxExpressions
        |> Seq.choose (
            function
            | SynExpr.Match(clauses = clauses; range = span)
            | SynExpr.MatchLambda(matchClauses = clauses; range = span) -> Some(span, clauses)
            | _ -> None
        )
        |> Seq.toList

    Expect.isNonEmpty matches "The spike must exercise a source match"

    let decisionTrees =
        project.AssemblyContents.ImplementationFiles
        |> Seq.collect (fun file -> declarationExpressions file.Declarations)
        |> Seq.choose (
            function
            | DecisionTree(_, targets) as expression -> Some(expression.Range, targets)
            | _ -> None
        )
        |> Seq.toList

    for span, clauses in matches do
        printfn
            "source=%A clauses=%A trees=%A"
            (position span)
            (clauses
             |> List.map (fun (SynMatchClause.SynMatchClause(resultExpr = body)) ->
                 position body.Range
             ))
            (decisionTrees
             |> List.map (fun (r, ts) ->
                 position r,
                 ts
                 |> List.map (fun (_, body) -> position body.Range)
             ))

        let joined =
            decisionTrees
            |> List.filter (fun (typedSpan, _) -> position typedSpan = position span)

        Expect.hasLength
            joined
            1
            $"A source match at {position span} must have exactly one typed decision tree"

        let _, targets = List.exactlyOne joined

        let sourceBodies =
            clauses
            |> List.map (fun (SynMatchClause.SynMatchClause(resultExpr = body)) ->
                position body.Range
            )

        let targetBodies =
            targets
            |> List.map (fun (_, body) -> position body.Range)

        Expect.equal
            targetBodies
            sourceBodies
            "Typed targets must retain source clause order and body ranges"

[<Tests>]
let tests =
    testList "FCS match-range join spike" [
        testCase
            "wildcard"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get x = match x with | Some n -> n | _ -> 0
"""
            )
        testCase
            "variable"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get (x: int) = match x with | n -> n + 1
"""
            )
        testCase
            "literal"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get (x: int) = match x with | 0 -> 1 | _ -> 2
"""
            )
        testCase
            "tuple"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get (x: int * int) = match x with | a, b -> a + b
"""
            )
        testCase
            "constructor"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    type Status = Available | Rented of int
    let get status = match status with | Rented 0 -> 0 | Rented n -> n + 1 | Available -> 10
"""
            )
        testCase
            "empty-list"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get (xs: int list) = match xs with | [] -> 0 | head :: _ -> head
"""
            )
        testCase
            "cons"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get (xs: int list) = match xs with | head :: tail -> List.length tail + head | [] -> 0
"""
            )
        testCase
            "as"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get (x: int option) = match x with | Some n as item -> n + Option.get item | None -> 0
"""
            )
        testCase
            "unit"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get (x: unit) = match x with | () -> 1
"""
            )
        testCase
            "match-lambda"
            (fun () ->
                verifyJoin
                    """namespace Acme.Demo
module Pricing =
    let get = function | Some n -> n + 1 | None -> 0
"""
            )
        testCase
            "nested"
            (fun () ->
                verifyJoin
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
            )
    ]
