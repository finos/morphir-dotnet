// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Syntax
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Morphir.Compilation
open Morphir.Frontend.FSharp

/// <summary>Why the checked evidence for a match is not available (FSF-2).</summary>
[<RequireQualifiedAccess>]
type internal EvidenceFailure =
    /// <summary>The analysis copy did not type-check.</summary>
    | AnalysisRejected
    /// <summary>A generated type witness has no checked symbol.</summary>
    | MissingWitness
    /// <summary>A generated type witness has more than one checked symbol.</summary>
    | AmbiguousWitness
    /// <summary>The clause witnesses of one match have different types.</summary>
    | InconsistentWitness
    /// <summary>The analysis copy changed the checked type of an original binding or reference.</summary>
    | ChangedSignature of name: string
    /// <summary>The analysis copy changed what an original name refers to.</summary>
    | ChangedResolution of name: string
    /// <summary>A pattern has no checked symbol, or more than one.</summary>
    | MissingSymbol
    /// <summary>A pattern is outside the supported pattern set.</summary>
    | UnsupportedPattern
    /// <summary>A constructor pattern has a different number of fields than its checked case.</summary>
    | ConstructorArity of case: string
    /// <summary>The analysis copy would pass a limit of the request.</summary>
    | LimitExceeded

    /// <summary>The stable reason in the diagnostic data.</summary>
    member this.Reason =
        match this with
        | AnalysisRejected -> "analysis_rejected"
        | MissingWitness -> "missing_witness"
        | AmbiguousWitness -> "ambiguous_witness"
        | InconsistentWitness -> "inconsistent_witness"
        | ChangedSignature _ -> "changed_signature"
        | ChangedResolution _ -> "changed_resolution"
        | MissingSymbol -> "missing_symbol"
        | UnsupportedPattern -> "unsupported_pattern"
        | ConstructorArity _ -> "constructor_arity"
        | LimitExceeded -> "limit_exceeded"

    /// <summary>The explanation in the diagnostic message. It never names generated code or paths.</summary>
    member this.Explanation =
        match this with
        | AnalysisRejected -> "the analysis copy of this match did not type-check"
        | MissingWitness -> "the compiler gave no type for a clause of this match"
        | AmbiguousWitness -> "the compiler gave more than one type for a clause of this match"
        | InconsistentWitness -> "the clauses of this match have different checked types"
        | ChangedSignature name -> $"type analysis would change the checked type of '{name}'"
        | ChangedResolution name -> $"type analysis would change what '{name}' refers to"
        | MissingSymbol -> "a pattern of this match has no single checked symbol"
        | UnsupportedPattern -> "a pattern of this match is outside the supported pattern set"
        | ConstructorArity case ->
            $"a pattern for '{case}' has a different number of fields than the checked case"
        | LimitExceeded -> "type analysis of this match would pass the limits of the request"

/// <summary>The limits on the analysis copy of one request.</summary>
type internal EvidenceLimits =
    {
        /// <summary>The largest number of matches that one request may analyze.</summary>
        MaxMatches: int
        /// <summary>The largest number of UTF-16 code units that the analysis copy may add.</summary>
        MaxAddedCharacters: int
    }

    /// <summary>The default limits.</summary>
    static member Default = {
        MaxMatches = 10_000
        MaxAddedCharacters =
            16
            * 1024
            * 1024
    }

/// <summary>The shape of one supported pattern.</summary>
[<RequireQualifiedAccess>]
type internal PatternShape =
    | Wildcard
    | Variable of value: FSharpMemberOrFunctionOrValue
    | Literal of constant: SynConst
    | Unit
    | Tuple
    | Constructor of case: FSharpUnionCase
    | EmptyList
    | Cons
    | As

/// <summary>One original pattern with its checked type. The children are in source order.</summary>
type internal PatternEvidence = {
    Shape: PatternShape
    Type: FSharpType
    Syntax: SynPat
    Children: PatternEvidence list
}

/// <summary>One original clause: its pattern evidence and the checked type of its result.</summary>
type internal ClauseEvidence = {
    Clause: SynMatchClause
    Pattern: PatternEvidence
    ResultType: FSharpType
}

/// <summary>One original <c>match</c> or <c>function</c>, with checked types for its subject and result.</summary>
type internal MatchEvidence = {
    /// <summary>The position of the match in document order, then source order (outer before inner).</summary>
    Ordinal: int
    Document: CheckedDocument
    Expression: SynExpr
    IsLambda: bool
    SubjectType: FSharpType
    ResultType: FSharpType
    Clauses: ClauseEvidence list
}

/// <summary>One text insertion of the analysis copy, at an original UTF-16 offset.</summary>
type internal Insertion = { At: int; Text: string }

/// <summary>
/// Maps original positions of one document into its analysis copy, and back. Offsets count UTF-16 code units.
/// An insertion at the start of a range goes before the range; an insertion at its end goes after it.
/// </summary>
[<Sealed>]
type internal InsertionMap(original: LineIndex, analysis: LineIndex, insertions: Insertion array) =
    let added =
        insertions
        |> Array.scan
            (fun total insertion ->
                total
                + insertion.Text.Length
            )
            0

    let firstAfter offset strict =
        let mutable low = 0
        let mutable high = insertions.Length

        while low < high do
            let middle =
                (low + high)
                / 2

            if
                (if strict then
                     insertions[middle].At > offset
                 else
                     insertions[middle].At
                     >= offset)
            then
                high <- middle
            else
                low <- middle + 1

        low

    /// <summary>The map of a document that the analysis copy does not change.</summary>
    static member Identity(lines: LineIndex) = InsertionMap(lines, lines, [||])

    /// <summary>The insertions, in text order.</summary>
    member _.Insertions = insertions

    /// <summary>The analysis offset of an original start offset.</summary>
    member _.StartOffset(offset: int) =
        offset
        + added[firstAfter offset true]

    /// <summary>The analysis offset of an original end offset.</summary>
    member _.EndOffset(offset: int) =
        offset
        + added[firstAfter offset false]

    /// <summary>The analysis range of an original range, in the analysis document.</summary>
    member this.ToAnalysis(path: string, range: range) =
        let start = this.StartOffset(original.Offset(range.StartLine, range.StartColumn))
        let finish = this.EndOffset(original.Offset(range.EndLine, range.EndColumn))
        let struct (startLine, startColumn) = analysis.Position start
        let struct (endLine, endColumn) = analysis.Position finish
        Range.mkRange path (Position.mkPos startLine startColumn) (Position.mkPos endLine endColumn)

    /// <summary>The original offset of an analysis offset. Inserted text maps to its insertion point.</summary>
    member _.ToOriginal(offset: int) =
        let rec walk index =
            if index = insertions.Length then
                offset
                - added[index]
            else
                let start =
                    insertions[index].At
                    + added[index]

                if offset < start then
                    offset
                    - added[index]
                elif
                    offset < start
                             + insertions[index].Text.Length
                then
                    insertions[index].At
                else
                    walk (index + 1)

        walk 0

/// <summary>
/// The checked evidence of a program. <c>Project</c> is the one checked project that lowering reads types and
/// symbols from: the analysis copy when the program has matches, or else the original check. Lowering always
/// takes structure and locations from the original syntax.
/// </summary>
type internal CheckedEvidence =
    {
        Original: CheckedProject
        Project: CheckedProject
        Matches: MatchEvidence list
        Maps: InsertionMap list
        Uses: IReadOnlyDictionary<struct (string * int * int * int * int), FSharpSymbolUse list>
    }

    /// <summary>
    /// The checked symbol uses at an original range of an original document, read from <c>Project</c>.
    /// </summary>
    member this.SymbolsAt(document: int, range: range) =
        let target = this.Project.Documents[document]
        let mapped = this.Maps[document].ToAnalysis(target.Path, range)

        match
            this.Uses.TryGetValue(
                struct (target.Path,
                        mapped.StartLine,
                        mapped.StartColumn,
                        mapped.EndLine,
                        mapped.EndColumn)
            )
        with
        | true, uses -> uses
        | _ -> []

/// <summary>
/// Builds checked match evidence from original syntax and an analysis-only copy (FSF-2). The copy adds a fresh
/// <c>as</c> witness to each clause pattern and a result witness to each clause body. It is checked with the
/// same options and document order, and is never evaluated or emitted.
/// </summary>
[<RequireQualifiedAccess>]
module internal Evidence =

    type private Site = {
        Ordinal: int
        Document: CheckedDocument
        Expression: SynExpr
        IsLambda: bool
        Clauses: SynMatchClause list
        Subjects: string list
        Results: string list
    }

    type private Edit = {
        At: int
        Closing: bool
        Nesting: int
        Sequence: int
        Text: string
    }

    let private key (range: range) =
        struct (range.FileName, range.StartLine, range.StartColumn, range.EndLine, range.EndColumn)

    let private index (project: CheckedProject) =
        let uses =
            Dictionary<struct (string * int * int * int * int), FSharpSymbolUse list>()

        for symbolUse in project.Uses do
            let at = key symbolUse.Range

            match uses.TryGetValue at with
            | true, existing ->
                uses[at] <-
                    existing
                    @ [ symbolUse ]
            | _ -> uses[at] <- [ symbolUse ]

        uses

    let private matchesOf (document: CheckedDocument) =
        let seen = HashSet<SynExpr>(HashIdentity.Reference)

        document.Syntax
        |> ParsedInput.fold
            (fun found _ node ->
                match node with
                | SyntaxNode.SynExpr(SynExpr.Match(clauses = clauses) as expression) when
                    seen.Add expression
                    ->
                    (expression, false, clauses)
                    :: found
                | SyntaxNode.SynExpr(SynExpr.MatchLambda(matchClauses = clauses) as expression) when
                    seen.Add expression
                    ->
                    (expression, true, clauses)
                    :: found
                | _ -> found
            )
            []
        |> List.sortBy (fun (expression, _, _) ->
            let range = expression.Range
            struct (range.StartLine, range.StartColumn, -range.EndLine, -range.EndColumn)
        )

    let private freshPrefix (documents: FSharpDocument list) =
        let rec attempt number =
            let prefix = $"__morphir_evidence_{number}_"

            if
                documents
                |> List.exists (fun document ->
                    document.Text.Contains(prefix, StringComparison.Ordinal)
                )
            then
                attempt (number + 1)
            else
                prefix

        attempt 0

    let private refusal
        (document: CheckedDocument)
        (expression: SynExpr)
        (failure: EvidenceFailure)
        =
        use data = JsonDocument.Parse $"{{\"reason\":\"{failure.Reason}\"}}"

        Diagnostic.error
            FSharpCodes.unsupportedMatchShape
            $"This match is not supported: {failure.Explanation}. The F# frontend does not guess types or change the structure of a match."
        |> Diagnostic.at (Location.Create(document.Uri, CompilerEdge.rangeOf expression.Range))
        |> Diagnostic.withData (data.RootElement.Clone())

    let private siteRefusal (site: Site) failure =
        refusal site.Document site.Expression failure

    let rec private describe
        (names: Dictionary<FSharpGenericParameter, int>)
        (inConstraint: bool)
        (typ: FSharpType)
        : string =
        let typ = typ.StripAbbreviations()

        let all (types: FSharpType seq) =
            types
            |> Seq.map (describe names inConstraint)
            |> String.concat ","

        if typ.IsGenericParameter then
            let parameter = typ.GenericParameter

            match names.TryGetValue parameter with
            | true, number -> $"'{number}"
            | _ ->
                names.Add(parameter, names.Count)

                let number =
                    names.Count
                    - 1

                // Constraints are expanded at the first level only. A constraint can name its own parameter.

                let constraints =
                    (if inConstraint then Seq.empty else parameter.Constraints)
                    |> Seq.map (fun constraintValue ->
                        if constraintValue.IsEqualityConstraint then
                            "equality"
                        elif constraintValue.IsComparisonConstraint then
                            "comparison"
                        elif constraintValue.IsCoercesToConstraint then
                            $"coerces({describe names true constraintValue.CoercesToTarget})"
                        elif constraintValue.IsMemberConstraint then
                            let data = constraintValue.MemberConstraintData

                            let arguments =
                                data.MemberArgumentTypes
                                |> Seq.map (describe names true)
                                |> String.concat ","

                            $"member({data.MemberName}:{arguments}->{describe names true data.MemberReturnType})"
                        elif constraintValue.IsSupportsNullConstraint then
                            "null"
                        elif constraintValue.IsReferenceTypeConstraint then
                            "class"
                        elif constraintValue.IsNonNullableValueTypeConstraint then
                            "struct"
                        elif constraintValue.IsRequiresDefaultConstructorConstraint then
                            "new"
                        elif constraintValue.IsUnmanagedConstraint then
                            "unmanaged"
                        elif constraintValue.IsDefaultsToConstraint then
                            $"default({describe names true constraintValue.DefaultsToConstraintData.DefaultsToTarget})"
                        else
                            "other"
                    )
                    |> Seq.sort
                    |> String.concat ";"

                $"'{number}[{constraints}]"
        elif typ.IsFunctionType then
            $"({describe names inConstraint typ.GenericArguments[0]} -> {describe names inConstraint typ.GenericArguments[1]})"
        elif typ.IsTupleType then
            let kind = if typ.IsStructTupleType then "struct" else ""
            $"{kind}({all typ.GenericArguments})"
        elif typ.HasTypeDefinition then
            let definition = typ.TypeDefinition

            let name =
                match definition.TryFullName with
                | Some name -> name
                | None -> $"{definition.AccessPath}.{definition.CompiledName}"

            $"{name}<{all typ.GenericArguments}>"
        else
            $"{typ.Format(FSharpDisplayContext.Empty)}<{all typ.GenericArguments}>"

    /// <summary>
    /// A text that is equal for two types exactly when they are the same type, with generic parameters
    /// numbered by first use and their constraints included. It compares types across two checks.
    /// </summary>
    let fingerprint (typ: FSharpType) =
        describe (Dictionary<FSharpGenericParameter, int>()) false typ

    let private symbolKey (symbolUse: FSharpSymbolUse) =
        let symbol = symbolUse.Symbol

        let name =
            try
                symbol.FullName
            with _ ->
                symbol.DisplayName

        let signature =
            match symbol with
            | :? FSharpMemberOrFunctionOrValue as value -> fingerprint value.FullType
            | :? FSharpUnionCase as case -> fingerprint case.ReturnType
            | :? FSharpField as field -> fingerprint field.FieldType
            | _ -> ""

        struct (symbol.GetType().Name, name, signature, symbolUse.IsFromDefinition)

    let private sites (documents: CheckedDocument list) (prefix: string) =
        documents
        |> List.collect (fun document ->
            matchesOf document
            |> List.map (fun (expression, isLambda, clauses) ->
                document, expression, isLambda, clauses
            )
        )
        |> List.mapi (fun ordinal (document, expression, isLambda, clauses) ->
            let name clause suffix = $"{prefix}{ordinal}_{clause}_{suffix}"

            {
                Ordinal = ordinal
                Document = document
                Expression = expression
                IsLambda = isLambda
                Clauses = clauses
                Subjects = List.mapi (fun clause _ -> name clause "subject") clauses
                Results = List.mapi (fun clause _ -> name clause "result") clauses
            }
        )

    let private edits (site: Site) =
        let lines = site.Document.Lines

        let offset (position: pos) =
            lines.Offset(position.Line, position.Column)

        site.Clauses
        |> List.indexed
        |> List.collect (fun (clause, SynMatchClause(pat = pattern; resultExpr = body)) ->
            let patternStart = offset pattern.Range.Start
            let patternEnd = offset pattern.Range.End
            let bodyStart = offset body.Range.Start
            let bodyEnd = offset body.Range.End
            let indent = String(' ', body.Range.StartColumn)
            let result = site.Results[clause]

            [
                {
                    At = patternStart
                    Closing = false
                    Nesting = -patternEnd
                    Sequence = 0
                    Text = "("
                }
                {
                    At = patternEnd
                    Closing = true
                    Nesting = -patternStart
                    Sequence = 0
                    Text = $" as {site.Subjects[clause]})"
                }
                {
                    At = bodyStart
                    Closing = false
                    Nesting = -bodyEnd
                    Sequence = 0
                    Text = $"(\n{indent}match (\n{indent}"
                }
                {
                    At = bodyEnd
                    Closing = true
                    Nesting = -bodyStart
                    Sequence = 0
                    Text = $") with | {result} -> {result})"
                }
            ]
        )

    let private ordered (edits: Edit list) =
        edits
        |> List.mapi (fun sequence edit -> { edit with Sequence = sequence })
        // Closings before openings at one offset; co-terminal bodies close from the innermost outward.
        |> List.sortBy (fun edit ->
            struct (edit.At, (if edit.Closing then 0 else 1), edit.Nesting, edit.Sequence)
        )
        |> List.map (fun edit -> { At = edit.At; Text = edit.Text }: Insertion)
        |> Array.ofList

    let private apply (text: string) (insertions: Insertion array) =
        let output =
            StringBuilder(
                text.Length
                + (insertions
                   |> Array.sumBy (fun insertion -> insertion.Text.Length))
            )

        let mutable cursor = 0

        for insertion in insertions do
            output
                .Append(
                    text,
                    cursor,
                    insertion.At
                    - cursor
                )
                .Append(insertion.Text)
            |> ignore

            cursor <- insertion.At

        output
            .Append(
                text,
                cursor,
                text.Length
                - cursor
            )
            .ToString()

    let private within (site: Site) (offset: int) =
        let lines = site.Document.Lines
        let range = site.Expression.Range

        lines.Offset(range.StartLine, range.StartColumn)
        <= offset
        && offset
           <= lines.Offset(range.EndLine, range.EndColumn)

    /// <summary>The innermost site that holds an original offset of a document, or the first site.</summary>
    let private siteAt (all: Site list) (document: int) (offset: int) =
        all
        |> List.filter (fun site ->
            site.Document.Index = document
            && within site offset
        )
        |> List.tryLast
        |> Option.orElse (
            all
            |> List.tryFind (fun site -> site.Document.Index = document)
        )
        |> Option.orElse (List.tryHead all)

    let private refuseAll (failures: (Site * EvidenceFailure) list) =
        failures
        |> List.distinctBy (fun (site, _) -> site.Ordinal)
        |> List.sortBy (fun (site, _) -> site.Ordinal)
        |> List.map (fun (site, failure) -> siteRefusal site failure)
        |> Checked.fail

    let private unparen pattern =
        let rec strip =
            function
            | SynPat.Paren(pat = inner) -> strip inner
            | pattern -> pattern

        strip pattern

    let private same (left: FSharpType) (right: FSharpType) =
        left.StripAbbreviations().Equals(right.StripAbbreviations())

    let private isList (typ: FSharpType) =
        let typ = typ.StripAbbreviations()

        typ.HasTypeDefinition
        && typ.TypeDefinition.TryFullName = Some "Microsoft.FSharp.Collections.FSharpList`1"

    /// <summary>
    /// The pattern evidence of an original pattern for an expected checked type: shapes, checked symbols and
    /// component types from the compiler. Lowering also uses it for function and let parameter patterns.
    /// </summary>
    let patterns (evidence: CheckedEvidence) (document: int) =
        let caseAt (range: range) =
            evidence.SymbolsAt(document, range)
            |> List.choose (fun symbolUse ->
                match symbolUse.Symbol with
                | :? FSharpUnionCase as case -> Some case
                | _ -> None
            )
            |> List.distinct

        let longCase (name: SynLongIdent) =
            let last =
                match List.tryLast name.LongIdent with
                | Some ident -> ident.idRange
                | None -> name.Range

            match caseAt name.Range with
            | [] -> caseAt last
            | found -> found

        let rec pattern
            (expected: FSharpType)
            (syntax: SynPat)
            : Result<PatternEvidence, EvidenceFailure> =
            let leaf shape =
                Ok
                    {
                        Shape = shape
                        Type = expected
                        Syntax = syntax
                        Children = []
                    }

            let constructor (case: FSharpUnionCase) (arguments: SynPat list) =
                let subject = expected.StripAbbreviations()
                let result = case.ReturnType.StripAbbreviations()

                if
                    not subject.HasTypeDefinition
                    || not result.HasTypeDefinition
                    || subject.TypeDefinition
                       <> result.TypeDefinition
                    || subject.GenericArguments.Count
                       <> result.TypeDefinition.GenericParameters.Count
                then
                    Error EvidenceFailure.MissingSymbol
                else
                    let substitutions =
                        List.zip
                            (List.ofSeq result.TypeDefinition.GenericParameters)
                            (List.ofSeq subject.GenericArguments)

                    let fields =
                        case.Fields
                        |> Seq.map (fun field -> field.FieldType.Instantiate substitutions)
                        |> List.ofSeq

                    let arguments =
                        match arguments with
                        | [ argument ] when fields.Length > 1 ->
                            match unparen argument with
                            | SynPat.Tuple(isStruct = false; elementPats = elements) -> elements
                            | _ -> arguments
                        | _ -> arguments

                    if
                        fields.Length
                        <> arguments.Length
                    then
                        Error(EvidenceFailure.ConstructorArity case.Name)
                    else
                        List.map2 pattern fields arguments
                        |> List.fold
                            (fun state next ->
                                match state, next with
                                | Ok children, Ok child ->
                                    Ok(
                                        child
                                        :: children
                                    )
                                | Error failure, _
                                | _, Error failure -> Error failure
                            )
                            (Ok [])
                        |> Result.map (fun children -> {
                            Shape = PatternShape.Constructor case
                            Type = expected
                            Syntax = syntax
                            Children = List.rev children
                        })

            let children shape (parts: (FSharpType * SynPat) list) =
                parts
                |> List.map (fun (typ, part) -> pattern typ part)
                |> List.fold
                    (fun state next ->
                        match state, next with
                        | Ok children, Ok child ->
                            Ok(
                                child
                                :: children
                            )
                        | Error failure, _
                        | _, Error failure -> Error failure
                    )
                    (Ok [])
                |> Result.map (fun children -> {
                    Shape = shape
                    Type = expected
                    Syntax = syntax
                    Children = List.rev children
                })

            match syntax with
            | SynPat.Paren(pat = inner)
            | SynPat.Typed(pat = inner) -> pattern expected inner
            | SynPat.Wild _ -> leaf PatternShape.Wildcard
            | SynPat.Const(SynConst.Unit, _) -> leaf PatternShape.Unit
            | SynPat.Const(constant, _) -> leaf (PatternShape.Literal constant)
            | SynPat.Named(ident = SynIdent(ident, _)) ->
                match caseAt ident.idRange with
                | [ case ] -> constructor case []
                | _ :: _ -> Error EvidenceFailure.MissingSymbol
                | [] ->
                    let values =
                        evidence.SymbolsAt(document, ident.idRange)
                        |> List.choose (fun symbolUse ->
                            match symbolUse.Symbol with
                            | :? FSharpMemberOrFunctionOrValue as value when
                                symbolUse.IsFromDefinition
                                ->
                                Some value
                            | _ -> None
                        )
                        |> List.distinct

                    match values with
                    | [ value ] when same value.FullType expected ->
                        leaf (PatternShape.Variable value)
                    | [ _ ] -> Error EvidenceFailure.InconsistentWitness
                    | _ -> Error EvidenceFailure.MissingSymbol
            | SynPat.LongIdent(longDotId = name; argPats = SynArgPats.Pats arguments) ->
                match longCase name with
                | [ case ] -> constructor case arguments
                | _ -> Error EvidenceFailure.MissingSymbol
            | SynPat.Tuple(isStruct = false; elementPats = elements) ->
                let tuple = expected.StripAbbreviations()

                if
                    not tuple.IsTupleType
                    || tuple.IsStructTupleType
                    || tuple.GenericArguments.Count
                       <> elements.Length
                then
                    Error EvidenceFailure.MissingSymbol
                else
                    children
                        PatternShape.Tuple
                        (List.zip (List.ofSeq tuple.GenericArguments) elements)
            | SynPat.ArrayOrList(isArray = false; elementPats = []) when isList expected ->
                leaf PatternShape.EmptyList
            | SynPat.ListCons(lhsPat = head; rhsPat = tail) when isList expected ->
                let element = expected.StripAbbreviations().GenericArguments[0]
                children PatternShape.Cons [ element, head; expected, tail ]
            | SynPat.As(lhsPat = inner; rhsPat = alias) ->
                children PatternShape.As [ expected, inner; expected, alias ]
            | _ -> Error EvidenceFailure.UnsupportedPattern

        pattern

    let private reconstruct
        (evidence: CheckedEvidence)
        (all: Site list)
        (witnesses: IReadOnlyDictionary<string, FSharpType list>)
        =
        let witness name =
            match witnesses.TryGetValue name with
            | true, [ typ ] -> Ok typ
            | true, _ :: _ -> Error EvidenceFailure.AmbiguousWitness
            | _ -> Error EvidenceFailure.MissingWitness

        let consistent names =
            names
            |> List.map witness
            |> List.fold
                (fun state next ->
                    match state, next with
                    | Ok found, Ok typ ->
                        Ok(
                            typ
                            :: found
                        )
                    | Error failure, _
                    | _, Error failure -> Error failure
                )
                (Ok [])
            |> Result.bind (fun types ->
                match List.rev types with
                | first :: rest when
                    rest
                    |> List.forall (same first)
                    ->
                    Ok(first, List.rev types)
                | [] -> Error EvidenceFailure.MissingWitness
                | _ -> Error EvidenceFailure.InconsistentWitness
            )

        let one (site: Site) =
            consistent site.Subjects
            |> Result.bind (fun (subject, _) ->
                consistent site.Results
                |> Result.bind (fun (result, results) ->
                    let pattern = patterns evidence site.Document.Index

                    List.zip site.Clauses results
                    |> List.map (fun ((SynMatchClause(pat = syntax) as clause), clauseResult) ->
                        pattern subject syntax
                        |> Result.map (fun evidence -> {
                            Clause = clause
                            Pattern = evidence
                            ResultType = clauseResult
                        })
                    )
                    |> List.fold
                        (fun state next ->
                            match state, next with
                            | Ok found, Ok clause ->
                                Ok(
                                    clause
                                    :: found
                                )
                            | Error failure, _
                            | _, Error failure -> Error failure
                        )
                        (Ok [])
                    |> Result.map (fun clauses -> {
                        Ordinal = site.Ordinal
                        Document = site.Document
                        Expression = site.Expression
                        IsLambda = site.IsLambda
                        SubjectType = subject
                        ResultType = result
                        Clauses = List.rev clauses
                    })
                )
            )

        let results =
            all
            |> List.map (fun site -> site, one site)

        match
            results
            |> List.choose (fun (site, result) ->
                match result with
                | Error failure -> Some(site, failure)
                | Ok _ -> None
            )
        with
        | [] ->
            Checked.pass
                { evidence with
                    Matches =
                        results
                        |> List.choose (fun (_, result) ->
                            match result with
                            | Ok found -> Some found
                            | Error _ -> None
                        )
                }
        | failures -> refuseAll failures

    /// <summary>
    /// Checks that the analysis copy keeps every original symbol use: the same symbols, with the same checked
    /// types, generalization and constraints, at the mapped ranges.
    /// </summary>
    let private preserved
        (original: CheckedProject)
        (analysis:
            IReadOnlyDictionary<struct (string * int * int * int * int), FSharpSymbolUse list>)
        (maps: InsertionMap list)
        (target: CheckedProject)
        =
        let originalUses = index original

        originalUses
        |> Seq.choose (fun pair ->
            let struct (path, _, _, _, _) = pair.Key

            match original.TryDocument path with
            | None -> None
            | Some document ->
                let mapped =
                    maps[document.Index]
                        .ToAnalysis(target.Documents[document.Index].Path, pair.Value.Head.Range)

                let found =
                    match analysis.TryGetValue(key mapped) with
                    | true, uses -> uses
                    | _ -> []

                let expected =
                    pair.Value
                    |> List.map symbolKey
                    |> List.distinct
                    |> List.sort

                let actual =
                    found
                    |> List.map symbolKey
                    |> List.distinct
                    |> List.sort

                if expected = actual then
                    None
                else
                    let struct (_, name, _, _) = List.head expected

                    let names keys =
                        keys
                        |> List.map (fun struct (kind, name, _, definition) ->
                            struct (kind, name, definition)
                        )

                    let sameNames = names expected = names actual

                    let failure =
                        if sameNames then
                            EvidenceFailure.ChangedSignature name
                        else
                            EvidenceFailure.ChangedResolution name

                    Some(
                        document.Index,
                        document.Lines.Offset(
                            pair.Value.Head.Range.StartLine,
                            pair.Value.Head.Range.StartColumn
                        ),
                        failure
                    )
        )
        |> List.ofSeq

    /// <summary>
    /// Builds the checked evidence for an original check. A program with no match uses the original check
    /// directly. Otherwise the analysis copy is checked, verified and read. Any missing, ambiguous or changed
    /// evidence fails with <c>fsharp.unsupported_match_shape</c> at the original match.
    /// </summary>
    /// <remarks>
    /// <c>rewrite</c> receives the fresh witness prefix and the analysis texts, and returns the texts to check.
    /// Production passes them through unchanged; tests change them to prove each refusal.
    /// </remarks>
    let collectWith
        (rewrite: string -> string list -> string list)
        (limits: EvidenceLimits)
        (session: CompilerEdge.Session)
        (original: CheckedProject)
        (cancellationToken: CancellationToken)
        : Task<Checked<CheckedEvidence>> =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            let prefix = freshPrefix session.Documents
            let all = sites original.Documents prefix

            if List.isEmpty all then
                return
                    Checked.pass
                        {
                            Original = original
                            Project = original
                            Matches = []
                            Maps =
                                original.Documents
                                |> List.map (fun document -> InsertionMap.Identity document.Lines)
                            Uses = index original
                        }
            elif all.Length > limits.MaxMatches then
                return refuseAll [ all[limits.MaxMatches], EvidenceFailure.LimitExceeded ]
            else
                let perDocument =
                    original.Documents
                    |> List.map (fun document ->
                        document,
                        all
                        |> List.filter (fun site -> site.Document.Index = document.Index)
                        |> List.collect edits
                        |> ordered
                    )

                let added =
                    perDocument
                    |> List.sumBy (fun (_, insertions) ->
                        insertions
                        |> Array.sumBy (fun insertion -> int64 insertion.Text.Length)
                    )

                if added > int64 limits.MaxAddedCharacters then
                    return refuseAll [ all.Head, EvidenceFailure.LimitExceeded ]
                else
                    let texts =
                        perDocument
                        |> List.map (fun (document, insertions) -> apply document.Text insertions)
                        |> rewrite prefix

                    let! analysis = session.Check(CheckKind.Analysis, original.References, texts, cancellationToken)

                    let maps =
                        List.map2
                            (fun (document: CheckedDocument, insertions) (copy: CheckedDocument) ->
                                InsertionMap(document.Lines, copy.Lines, insertions)
                            )
                            perDocument
                            analysis.Documents

                    let errors =
                        analysis.Results.Diagnostics
                        |> Array.filter (fun diagnostic ->
                            diagnostic.Severity = FSharpDiagnosticSeverity.Error
                        )
                        |> List.ofArray

                    if not errors.IsEmpty then
                        return
                            errors
                            |> List.choose (fun error ->
                                match analysis.TryDocument error.FileName with
                                | Some copy ->
                                    let offset =
                                        maps[copy.Index]
                                            .ToOriginal(
                                                copy.Lines.Offset(
                                                    error.StartLine,
                                                    error.StartColumn
                                                )
                                            )

                                    siteAt all copy.Index offset
                                | None -> List.tryHead all
                                |> Option.map (fun site -> site, EvidenceFailure.AnalysisRejected)
                            )
                            |> refuseAll
                    else
                        let uses = index analysis

                        match preserved original uses maps analysis with
                        | [] ->
                            let witnesses =
                                Dictionary<string, FSharpType list>(StringComparer.Ordinal)

                            for symbolUse in analysis.Uses do
                                match symbolUse.Symbol with
                                | :? FSharpMemberOrFunctionOrValue as value when
                                    symbolUse.IsFromDefinition
                                    && value.DisplayName.StartsWith(
                                        prefix,
                                        StringComparison.Ordinal
                                    )
                                    ->
                                    match witnesses.TryGetValue value.DisplayName with
                                    | true, types ->
                                        witnesses[value.DisplayName] <-
                                            types
                                            @ [ value.FullType ]
                                    | _ -> witnesses[value.DisplayName] <- [ value.FullType ]
                                | _ -> ()

                            return
                                reconstruct
                                    {
                                        Original = original
                                        Project = analysis
                                        Matches = []
                                        Maps = maps
                                        Uses = uses
                                    }
                                    all
                                    witnesses
                        | changes ->
                            return
                                changes
                                |> List.choose (fun (document, offset, failure) ->
                                    siteAt all document offset
                                    |> Option.map (fun site -> site, failure)
                                )
                                |> refuseAll
        }

    /// <summary>Builds the checked evidence for an original check, with the analysis copy unchanged.</summary>
    let collect limits session original cancellationToken =
        collectWith (fun _ texts -> texts) limits session original cancellationToken

/// <summary>The analysis stages of the F# frontend that use the compiler: FcsCheck, then TypeEvidence.</summary>
[<RequireQualifiedAccess>]
module internal FSharpAnalysis =

    /// <summary>
    /// Checks the ordered documents and builds their match evidence. F# errors stop before evidence.
    /// F# warnings and information stay with the result, also when evidence fails.
    /// </summary>
    let analyzeWithCatalog
        (catalog: AdmissionRule list)
        (rewrite: string -> string list -> string list)
        (limits: EvidenceLimits)
        (documents: FSharpDocument list)
        (cancellationToken: CancellationToken)
        : Task<Checked<CheckedEvidence>> =
        task {
            use session = new CompilerEdge.Session(documents)
            let! original = CompilerEdge.checkOriginal session cancellationToken

            match Checked.tryValue original with
            | None -> return Checked.fail (Checked.diagnostics original)
            | Some project ->
                // Admission reports every refused construct before evidence runs (FSF-1).
                let admitted =
                    original
                    |> Checked.bind (fun _ -> Admission.admitWith catalog project)

                let! combined =
                    match Checked.tryValue admitted with
                    | None -> Task.FromResult(Checked.fail (Checked.diagnostics admitted))
                    | Some() -> task {
                        let! evidence =
                            Evidence.collectWith rewrite limits session project cancellationToken

                        return
                            admitted
                            |> Checked.bind (fun _ -> evidence)
                      }

                // Diagnostics of both stages are in document, then position order (not production order).
                let ordered = CompilerEdge.order documents (Checked.diagnostics combined)

                match Checked.tryValue combined with
                | Some value -> return Checked.ofDiagnostics value ordered
                | None -> return Checked.fail ordered
        }

    /// <summary>Checks the ordered documents against the whole admission catalog (see <c>analyzeWithCatalog</c>).</summary>
    let analyzeWith rewrite limits documents cancellationToken =
        analyzeWithCatalog Admission.rules rewrite limits documents cancellationToken

    /// <summary>Checks the ordered documents and builds their match evidence (see <c>analyzeWithCatalog</c>).</summary>
    let analyze limits documents cancellationToken =
        analyzeWith (fun _ texts -> texts) limits documents cancellationToken
