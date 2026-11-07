// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Spikes.SyntaxMatchEvidence

open System.Collections
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

type PatternEvidence = {
    Kind: string
    Type: FSharpType
    Span: range
    Children: PatternEvidence list
}

type MatchEvidence = {
    Span: range
    SubjectType: FSharpType
    ResultType: FSharpType
    Patterns: PatternEvidence list
    BodySpans: range list
}

let rec shape (typ: FSharpType) =
    if typ.IsAbbreviation then
        shape typ.AbbreviatedType
    elif typ.IsGenericParameter then
        "'"
        + typ.GenericParameter.Name
    elif typ.IsFunctionType then
        "("
        + shape typ.GenericArguments[0]
        + " -> "
        + shape typ.GenericArguments[1]
        + ")"
    elif typ.IsTupleType then
        typ.GenericArguments
        |> Seq.map shape
        |> String.concat " * "
        |> sprintf "(%s)"
    else
        let definition = typ.TypeDefinition

        let name =
            definition.AccessPath
            + "."
            + definition.CompiledName.Split('`')[0]

        if typ.GenericArguments.Count = 0 then
            name
        else
            name
            + "<"
            + (typ.GenericArguments
               |> Seq.map shape
               |> String.concat ",")
            + ">"

let position (span: range) =
    span.StartLine, span.StartColumn, span.EndLine, span.EndColumn

// Reflection visits test input syntax only; production lowering must use syntax visitors.
let rec nodes (node: obj) = seq {
    if not (isNull node) then
        yield node
        let typ = node.GetType()

        if Microsoft.FSharp.Reflection.FSharpType.IsUnion typ then
            let _, fields = FSharpValue.GetUnionFields(node, typ)

            for field in fields do
                yield! nodes field
        elif Microsoft.FSharp.Reflection.FSharpType.IsRecord typ then
            for field in FSharpValue.GetRecordFields node do
                yield! nodes field
        elif not (node :? string) then
            match node with
            | :? IEnumerable as items ->
                for item in items do
                    yield! nodes item
            | _ -> ()
}

let private symbolAt (program: CheckedSource.Program) span =
    program.Symbols
    |> Array.filter (fun symbol -> position symbol.Range = position span)
    |> Array.choose (fun useOfSymbol ->
        match useOfSymbol.Symbol with
        | :? FSharpMemberOrFunctionOrValue as value -> Some value
        | _ -> None
    )
    |> Array.distinct
    |> function
        | [| symbol |] -> symbol
        | candidates ->
            failwithf
                "Missing or ambiguous FCS symbol at %A: %d candidates"
                (position span)
                candidates.Length

let private signatures (program: CheckedSource.Program) =
    let fingerprint (typ: FSharpType) =
        let names = Dictionary<FSharpGenericParameter, int>()

        let rec describe (typ: FSharpType) =
            let typ = typ.StripAbbreviations()

            if typ.IsGenericParameter then
                let parameter = typ.GenericParameter

                if not (names.ContainsKey parameter) then
                    names.Add(parameter, names.Count)

                "'"
                + string names[parameter]
            elif typ.IsFunctionType then
                "("
                + describe typ.GenericArguments[0]
                + " -> "
                + describe typ.GenericArguments[1]
                + ")"
            elif typ.IsTupleType then
                typ.GenericArguments
                |> Seq.map describe
                |> String.concat " * "
                |> sprintf "(%s)"
            else
                let definition = typ.TypeDefinition

                definition.AccessPath
                + "."
                + definition.CompiledName
                + "<"
                + (typ.GenericArguments
                   |> Seq.map describe
                   |> String.concat ",")
                + ">"

        describe typ

    let rec bindings declarations = seq {
        for declaration in declarations do
            match declaration with
            | FSharpImplementationFileDeclaration.Entity(_, children) -> yield! bindings children
            | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(value, _, _) when
                not value.IsCompilerGenerated
                ->
                yield value.FullName, fingerprint value.FullType
            | _ -> ()
    }

    bindings program.Declarations
    |> Map.ofSeq

type private Site = {
    Expression: SynExpr
    Clauses: SynMatchClause list
    Inputs: string list
    Results: string list
}

type private Enrichment = {
    Program: CheckedSource.Program
    Sites: Site list
    MapSpan: range -> range
}

let private enrich checkAnalysis (program: CheckedSource.Program) =
    let lineStarts (source: string) = [|
        yield 0
        for index in
            0 .. source.Length
                 - 1 do
            if source[index] = '\n' then
                yield index + 1
    |]

    let originalLines = lineStarts program.Source

    let offset line column =
        originalLines[line - 1]
        + column

    let rec freshPrefix attempt =
        let prefix =
            "__morphir_evidence_"
            + string attempt
            + "_"

        if program.Source.Contains prefix then
            freshPrefix (
                attempt
                + 1
            )
        else
            prefix

    let prefix = freshPrefix 0

    let sites =
        program.Syntax
        |> box
        |> nodes
        |> Seq.choose (
            function
            | :? SynExpr as expression ->
                match expression with
                | SynExpr.Match(clauses = clauses)
                | SynExpr.MatchLambda(matchClauses = clauses) -> Some(expression, clauses)
                | _ -> None
            | _ -> None
        )
        |> Seq.sortBy (fun (expression, _) ->
            let span = expression.Range
            span.StartLine, span.StartColumn, -span.EndLine, -span.EndColumn
        )
        |> Seq.mapi (fun index (expression, clauses) ->
            let name clause suffix =
                prefix
                + string index
                + "_"
                + string clause
                + suffix

            {
                Expression = expression
                Clauses = clauses
                Inputs = List.mapi (fun clause _ -> name clause "_subject") clauses
                Results = List.mapi (fun clause _ -> name clause "_result") clauses
            }
        )
        |> Seq.toList

    let edits = ResizeArray<int * int * string>()
    let insert (at, text) = edits.Add(at, 0, text)

    for site in sites do
        for index, SynMatchClause.SynMatchClause(pat = pat; resultExpr = body) in
            List.indexed site.Clauses do
            let patRange, bodyRange = pat.Range, body.Range
            insert (offset patRange.StartLine patRange.StartColumn, "(")

            insert (
                offset patRange.EndLine patRange.EndColumn,
                " as "
                + site.Inputs[index]
                + ")"
            )

            let result = site.Results[index]

            insert (
                offset bodyRange.StartLine bodyRange.StartColumn,
                "(\n"
                + String.replicate bodyRange.StartColumn " "
                + "match (\n"
                + String.replicate bodyRange.StartColumn " "
            )

            edits.Add(
                offset bodyRange.EndLine bodyRange.EndColumn,
                -(offset bodyRange.StartLine bodyRange.StartColumn),
                ") with | "
                + result
                + " -> "
                + result
                + ")"
            )

    // Co-terminal nested bodies must close from the innermost body outward.
    let ordered =
        edits
        |> Seq.sortBy (fun (at, nesting, _) -> at, nesting)
        |> Seq.map (fun (at, _, text) -> at, text)
        |> Seq.toArray

    let output = System.Text.StringBuilder()
    let mutable cursor = 0

    for at, insertion in ordered do
        output.Append(
            program.Source,
            cursor,
            at
            - cursor
        )
        |> ignore

        output.Append insertion
        |> ignore

        cursor <- at

    output.Append(
        program.Source,
        cursor,
        program.Source.Length
        - cursor
    )
    |> ignore

    let source = output.ToString()
    let analyzed = checkAnalysis source

    if
        signatures program
        <> signatures analyzed
    then
        failwith "Type enrichment changed a checked binding signature"

    let analyzedLines = lineStarts source

    let toPosition at =
        let line =
            analyzedLines
            |> Array.findIndexBack (fun start ->
                start
                <= at
            )

        Position.mkPos
            (line + 1)
            (at
             - analyzedLines[line])

    let mapSpan (span: range) =
        let start = offset span.StartLine span.StartColumn
        let finish = offset span.EndLine span.EndColumn

        let startShift =
            ordered
            |> Array.sumBy (fun (at, text) ->
                if
                    at
                    <= start
                then
                    text.Length
                else
                    0
            )

        let endShift =
            ordered
            |> Array.sumBy (fun (at, text) -> if at < finish then text.Length else 0)

        Range.mkRange
            span.FileName
            (toPosition (
                start
                + startShift
            ))
            (toPosition (
                finish
                + endShift
            ))

    {
        Program = analyzed
        Sites = sites
        MapSpan = mapSpan
    }

let reconstructWith checkAnalysis (program: CheckedSource.Program) : MatchEvidence list =
    let enriched = enrich checkAnalysis program
    let program = enriched.Program

    let rec pattern (expected: FSharpType) =
        function
        | SynPat.As(lhsPat = inner; rhsPat = alias; range = span) -> {
            Kind = "as"
            Type = expected
            Span = span
            Children = [ pattern expected inner; pattern expected alias ]
          }
        | SynPat.ArrayOrList(isArray = false; elementPats = []; range = span) -> {
            Kind = "empty-list"
            Type = expected
            Span = span
            Children = []
          }
        | SynPat.ListCons(lhsPat = head; rhsPat = tail; range = span) ->
            let list = expected.StripAbbreviations()

            if
                list.TypeDefinition.CompiledName
                <> "FSharpList`1"
            then
                failwithf "Expected a checked list type, got %s" (shape list)

            {
                Kind = "cons"
                Type = expected
                Span = span
                Children = [ pattern list.GenericArguments[0] head; pattern expected tail ]
            }
        | SynPat.LongIdent(longDotId = name; argPats = SynArgPats.Pats args; range = span) ->
            let cases =
                program.Symbols
                |> Array.filter (fun useOfSymbol ->
                    position useOfSymbol.Range = position (enriched.MapSpan name.Range)
                )
                |> Array.choose (fun useOfSymbol ->
                    match useOfSymbol.Symbol with
                    | :? FSharpUnionCase as case -> Some case
                    | _ -> None
                )
                |> Array.distinct

            let case =
                match cases with
                | [| case |] -> case
                | _ -> failwith "Missing or ambiguous checked union case"

            let subject = expected.StripAbbreviations()
            let result = case.ReturnType.StripAbbreviations()

            if
                subject.TypeDefinition
                <> result.TypeDefinition
            then
                failwith "Constructor belongs to another checked type"

            let substitutions =
                List.zip
                    (Seq.toList result.TypeDefinition.GenericParameters)
                    (Seq.toList subject.GenericArguments)

            let fields =
                case.Fields
                |> Seq.map (fun field -> field.FieldType.Instantiate substitutions)
                |> Seq.toList

            let rec unparen =
                function
                | SynPat.Paren(pat = inner) -> unparen inner
                | pat -> pat

            let args =
                match args with
                | [ arg ] when fields.Length > 1 ->
                    match unparen arg with
                    | SynPat.Tuple(elementPats = children) -> children
                    | _ -> args
                | _ -> args

            if
                fields.Length
                <> args.Length
            then
                failwith "Source constructor arity differs from checked fields"

            {
                Kind =
                    "constructor:"
                    + case.Name
                Type = expected
                Span = span
                Children = List.map2 pattern fields args
            }
        | SynPat.Paren(pat = inner)
        | SynPat.Typed(pat = inner) -> pattern expected inner
        | SynPat.Tuple(elementPats = children; range = span) ->
            let tuple = expected.StripAbbreviations()

            if
                not tuple.IsTupleType
                || tuple.GenericArguments.Count
                   <> children.Length
            then
                failwith "The checked subject does not provide the tuple component types"

            {
                Kind = "tuple"
                Type = expected
                Span = span
                Children = List.map2 pattern (Seq.toList tuple.GenericArguments) children
            }
        | SynPat.Wild span -> {
            Kind = "wildcard"
            Type = expected
            Span = span
            Children = []
          }
        | SynPat.Const(SynConst.Unit, span) -> {
            Kind = "unit"
            Type = expected
            Span = span
            Children = []
          }
        | SynPat.Const(_, span) -> {
            Kind = "literal"
            Type = expected
            Span = span
            Children = []
          }
        | SynPat.Named(ident = SynIdent(ident, _); range = span) ->
            let actual = (symbolAt program (enriched.MapSpan ident.idRange)).FullType

            if not (actual.StripAbbreviations().Equals(expected.StripAbbreviations())) then
                failwith "The pattern's FCS type differs from the checked subject"

            {
                Kind = "variable"
                Type = actual
                Span = span
                Children = []
            }
        | _ -> failwith "Pattern not yet covered by the proof"

    let witness name =
        program.Symbols
        |> Array.filter (fun useOfSymbol -> useOfSymbol.IsFromDefinition)
        |> Array.choose (fun useOfSymbol ->
            match useOfSymbol.Symbol with
            | :? FSharpMemberOrFunctionOrValue as value when value.DisplayName = name ->
                Some value.FullType
            | _ -> None
        )
        |> function
            | [| typ |] -> typ
            | _ -> failwith "Missing or ambiguous generated type witness"

    let consistent names =
        match List.map witness names with
        | head :: tail when
            tail
            |> List.forall (fun typ -> head.StripAbbreviations().Equals(typ.StripAbbreviations()))
            ->
            head
        | _ -> failwith "Clause type witnesses disagree"

    enriched.Sites
    |> List.map (fun site ->
        let subject, result = consistent site.Inputs, consistent site.Results

        {
            Span = site.Expression.Range
            SubjectType = subject
            ResultType = result
            Patterns =
                site.Clauses
                |> List.map (fun (SynMatchClause.SynMatchClause(pat = pat)) -> pattern subject pat)
            BodySpans =
                site.Clauses
                |> List.map (fun (SynMatchClause.SynMatchClause(resultExpr = body)) -> body.Range)
        }
    )

let reconstruct program =
    reconstructWith CheckedSource.check program
