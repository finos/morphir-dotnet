// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open System
open System.Collections.Generic
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.SyntaxTrivia
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

/// <summary>
/// One document as the catalog sees it. The frontend and the analyzer each build it from their own checked
/// results, so the catalog depends only on the F# compiler service syntax and symbols (CAT-1).
/// </summary>
type internal AdmissionDocument = {
    Uri: string
    /// <summary>The file name in the compiler ranges of this document.</summary>
    Path: string
    Text: string
    Lines: LineIndex
    Syntax: ParsedInput
}

/// <summary>What admission reads while it checks one document.</summary>
type internal AdmissionContext = {
    Document: AdmissionDocument
    /// <summary>The original checked symbol uses at an exact range of this document.</summary>
    SymbolsAt: range -> FSharpSymbolUse list
}

/// <summary>
/// One refusal. <c>Report</c> is the range of the diagnostic. <c>Scope</c> is the construct that the refusal
/// covers: no other refusal is reported inside it, so one refused construct gives one diagnostic.
/// </summary>
type internal Finding = { Report: range; Scope: range }

/// <summary>How a rule finds its construct (FSF-3).</summary>
[<RequireQualifiedAccess>]
type internal Detector =
    /// <summary>A check of one syntax node and its ancestors (nearest first).</summary>
    | Node of (AdmissionContext -> SyntaxVisitorPath -> SyntaxNode -> Finding list)
    /// <summary>A check of the whole document: its kind or its directives.</summary>
    | Document of (AdmissionContext -> Finding list)

/// <summary>
/// One rule of the unsupported-construct catalog. The catalog is data: admission, the generated coverage
/// reference and the completeness tests read the same records.
/// </summary>
type internal AdmissionRule = {
    /// <summary>The stable diagnostic code, such as <c>fsharp.unsupported_mutable</c>.</summary>
    Code: string
    Construct: string
    Message: string
    Suggestion: string option
    /// <summary>The documents of a minimal example. The example type-checks and gives only this rule's code.</summary>
    Example: (string * string) list
    /// <summary>The source text at the range of the example's first diagnostic.</summary>
    Flagged: string
    /// <summary>When true, the rule applies only to a node that no specific rule refused.</summary>
    IsFallback: bool
    Detect: Detector
}

/// <summary>
/// The unsupported-construct catalog (FSF-3) and its detection. This file opens only the F# compiler service
/// and FSharp.Core, so the frontend and the editor analyzer can compile the same source (CAT-1).
/// </summary>
[<RequireQualifiedAccess>]
module internal AdmissionCatalog =

    // ----- Helpers -------------------------------------------------------------------------------------

    let private finding (range: range) = { Report = range; Scope = range }

    let private covers (outer: range) (inner: range) =
        String.Equals(outer.FileName, inner.FileName, StringComparison.Ordinal)
        && Position.posGeq inner.Start outer.Start
        && Position.posGeq outer.End inner.End

    let private one range = [ finding range ]

    let private attributeName (attribute: SynAttribute) =
        let name =
            match List.tryLast attribute.TypeName.LongIdent with
            | Some ident -> ident.idText
            | None -> ""

        if
            name.EndsWith("Attribute", StringComparison.Ordinal)
            && name.Length > "Attribute".Length
        then
            name.Substring(
                0,
                name.Length
                - "Attribute".Length
            )
        else
            name

    let private attributesOf (lists: SynAttributeList list) =
        lists
        |> List.collect (fun list -> list.Attributes)

    /// <summary>Attributes that have no effect on the meaning of the code that Morphir models.</summary>
    let private harmless =
        set [ "RequireQualifiedAccess"; "AutoOpen"; "CompilationRepresentation" ]

    /// <summary>Attributes that a specific rule refuses instead of the attribute rule.</summary>
    let private specific = set [ "Struct"; "Measure" ]

    let private hasAttribute name (lists: SynAttributeList list) =
        attributesOf lists
        |> List.exists (fun attribute -> attributeName attribute = name)

    let rec private elements expression =
        match expression with
        | SynExpr.Sequential(expr1 = first; expr2 = rest) ->
            first
            :: elements rest
        | expression -> [ expression ]

    let private isComprehensionElement expression =
        match expression with
        | SynExpr.ForEach _
        | SynExpr.For _
        | SynExpr.While _
        | SynExpr.YieldOrReturn _
        | SynExpr.YieldOrReturnFrom _
        | SynExpr.IndexRange _
        | SynExpr.LetOrUse _
        | SynExpr.SequentialOrImplicitYield _
        | SynExpr.IfThenElse(elseExpr = None) -> true
        | _ -> false

    let private isComprehension expression =
        elements expression
        |> List.exists isComprehensionElement

    /// <summary>True when a node is a Sequential that only separates the elements of a list literal.</summary>
    let private isListSeparator (path: SyntaxVisitorPath) =
        let rec walk path =
            match path with
            | SyntaxNode.SynExpr(SynExpr.Sequential _) :: rest -> walk rest
            | SyntaxNode.SynExpr(SynExpr.ArrayOrListComputed(isArray = false; expr = inner)) :: _ ->
                not (isComprehension inner)
            | _ -> false

        walk path

    let private isBindingHead (path: SyntaxVisitorPath) (pattern: SynPat) =
        match path with
        | SyntaxNode.SynBinding(SynBinding(headPat = head)) :: _ ->
            obj.ReferenceEquals(head, pattern)
        | _ -> false

    let private isActivePatternCase (context: AdmissionContext) (range: range) =
        context.SymbolsAt range
        |> List.exists (fun symbolUse ->
            match symbolUse.Symbol with
            | :? FSharpActivePatternCase -> true
            | _ -> false
        )

    let private isMeasure (context: AdmissionContext) (range: range) =
        context.SymbolsAt range
        |> List.exists (fun symbolUse ->
            match symbolUse.Symbol with
            | :? FSharpEntity as entity -> entity.IsMeasure
            | _ -> false
        )

    /// <summary>True when a type name resolves to an array type, also through an abbreviation such as 'array'.</summary>
    let private isArrayName (context: AdmissionContext) (name: SynLongIdent) =
        let ranges =
            name.Range
            :: (name.LongIdent
                |> List.tryLast
                |> Option.map (fun ident -> ident.idRange)
                |> Option.toList)

        ranges
        |> List.exists (fun range ->
            context.SymbolsAt range
            |> List.exists (fun symbolUse ->
                match symbolUse.Symbol with
                | :? FSharpEntity as entity ->
                    entity.IsArrayType
                    || (entity.IsFSharpAbbreviation
                        && (let target = entity.AbbreviatedType.StripAbbreviations()

                            target.HasTypeDefinition
                            && target.TypeDefinition.IsArrayType))
                | _ -> false
            )
        )

    let private supportedConstant constant =
        match constant with
        | SynConst.Unit
        | SynConst.Bool _
        | SynConst.Char _
        | SynConst.String _
        | SynConst.Int32 _
        | SynConst.Int64 _
        | SynConst.Double _
        | SynConst.Decimal _ -> true
        | _ -> false

    /// <summary>The range of a keyword in the text between two positions of a document.</summary>
    let private keywordBetween
        (document: AdmissionDocument)
        (keyword: string)
        (start: pos)
        (finish: pos)
        =
        let lines = document.Lines
        let from = lines.Offset(start.Line, start.Column)
        let until = lines.Offset(finish.Line, finish.Column)

        if
            until
            <= from
        then
            None
        else
            match
                document.Text.IndexOf(
                    keyword,
                    from,
                    until
                    - from,
                    StringComparison.Ordinal
                )
            with
            | -1 -> None
            | at ->
                let struct (startLine, startColumn) = lines.Position at

                let struct (endLine, endColumn) =
                    lines.Position(
                        at
                        + keyword.Length
                    )

                Some(
                    Range.mkRange
                        document.Path
                        (Position.mkPos startLine startColumn)
                        (Position.mkPos endLine endColumn)
                )

    let private expression detect =
        Detector.Node(fun context path node ->
            match node with
            | SyntaxNode.SynExpr expression -> detect context path expression
            | _ -> []
        )

    let private pattern detect =
        Detector.Node(fun context path node ->
            match node with
            | SyntaxNode.SynPat pattern -> detect context path pattern
            | _ -> []
        )

    let private typeNode detect =
        Detector.Node(fun context path node ->
            match node with
            | SyntaxNode.SynType typ -> detect context path typ
            | _ -> []
        )

    let private typeDefinition detect =
        Detector.Node(fun context path node ->
            match node with
            | SyntaxNode.SynTypeDefn definition -> detect context definition
            | _ -> []
        )

    let private binding detect =
        Detector.Node(fun context path node ->
            match node with
            | SyntaxNode.SynBinding binding -> detect context binding
            | _ -> []
        )

    let private declaration detect =
        Detector.Node(fun context path node ->
            match node with
            | SyntaxNode.SynModule declaration -> detect context declaration
            | _ -> []
        )

    let private objectModelKind
        (SynTypeDefn(typeRepr = representation; implicitConstructor = constructor))
        =
        match representation with
        | SynTypeDefnRepr.ObjectModel(kind = kind; members = members) ->
            match kind with
            | SynTypeDefnKind.Interface -> Some "interface"
            | SynTypeDefnKind.Struct -> Some "struct"
            | SynTypeDefnKind.Delegate _ -> Some "delegate"
            | SynTypeDefnKind.Augmentation _ -> Some "member"
            | SynTypeDefnKind.Unspecified when
                constructor.IsNone
                && not members.IsEmpty
                && members
                   |> List.forall (fun memberDefinition ->
                       match memberDefinition with
                       | SynMemberDefn.AbstractSlot _
                       | SynMemberDefn.Interface _
                       | SynMemberDefn.Inherit _ -> true
                       | _ -> false
                   )
                ->
                Some "interface"
            | _ -> Some "class"
        | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.General _)
        | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.LibraryOnlyILAssembly _) ->
            Some "class"
        | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.Enum _) -> Some "enum"
        | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.Exception _)
        | SynTypeDefnRepr.Exception _ -> Some "exception"
        | _ -> None

    let private typeKindRule kind =
        typeDefinition (fun _ definition ->
            if objectModelKind definition = Some kind then
                one definition.Range
            else
                []
        )

    let private typeAttributes (SynTypeDefn(typeInfo = SynComponentInfo(attributes = attributes))) =
        attributes

    let private module' (lines: string list) =
        let body =
            lines
            |> List.map (fun line -> if line.Length = 0 then line else $"    {line}")

        [
            "Example.fs",
            ([ "namespace Acme.Demo"; "module Example =" ]
             @ body
             |> String.concat "\n")
            + "\n"
        ]

    let private rule code construct message suggestion example flagged detect = {
        Code = code
        Construct = construct
        Message = message
        Suggestion = suggestion
        Example = example
        Flagged = flagged
        IsFallback = false
        Detect = detect
    }

    let private fallback code construct message example flagged detect =
        { rule code construct message None example flagged detect with
            IsFallback = true
        }

    // ----- The catalog ---------------------------------------------------------------------------------

    /// <summary>
    /// The unsupported-construct catalog. Specific rules come first. A fallback rule refuses any expression,
    /// pattern, type or declaration outside the supported subset that no specific rule refused, so lowering
    /// only sees admitted syntax.
    /// </summary>
    let rules: AdmissionRule list = [
        rule
            "fsharp.unsupported_mutable"
            "mutable binding or field"
            "Mutable bindings and fields are not supported; Morphir values are immutable."
            (Some "Use a new let binding, or a record update, instead of changing a value.")
            (module' [ "let mutable total = 0" ])
            "mutable"
            (Detector.Node(fun context _ node ->
                match node with
                | SyntaxNode.SynBinding(SynBinding(
                    isMutable = true; headPat = head; trivia = trivia)) ->
                    keywordBetween
                        context.Document
                        "mutable"
                        trivia.LeadingKeyword.Range.End
                        head.Range.Start
                    |> Option.map finding
                    |> Option.toList
                | SyntaxNode.SynTypeDefn(SynTypeDefn(
                    typeRepr = SynTypeDefnRepr.Simple(
                        simpleRepr = SynTypeDefnSimpleRepr.Record(recordFields = fields)))) ->
                    fields
                    |> List.choose (fun (SynField(trivia = trivia)) -> trivia.MutableKeyword)
                    |> List.map finding
                | _ -> []
            ))

        rule
            "fsharp.unsupported_assignment"
            "assignment"
            "Assignment with '<-' is not supported; Morphir values are immutable."
            (Some "Return a new value instead of changing an existing one.")
            (module' [ "let reset (builder: System.Text.StringBuilder) = builder.Length <- 0" ])
            "builder.Length <- 0"
            (expression (fun _ _ expression ->
                match expression with
                | SynExpr.Set _
                | SynExpr.LongIdentSet _
                | SynExpr.DotSet _
                | SynExpr.DotIndexedSet _
                | SynExpr.NamedIndexedPropertySet _
                | SynExpr.DotNamedIndexedPropertySet _ -> one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_loop"
            "loop"
            "Loops ('for', 'while') are not supported; Morphir has no statements."
            (Some "Use recursion or a List function such as List.map or List.fold.")
            (module' [ "let count () = for index in 1 .. 3 do ignore index" ])
            "for index in 1 .. 3 do ignore index"
            (expression (fun _ _ expression ->
                match expression with
                | SynExpr.For _
                | SynExpr.ForEach _
                | SynExpr.While _
                | SynExpr.WhileBang _ -> one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_class"
            "class"
            "Classes are not supported; Morphir types are records, unions and type abbreviations."
            (Some "Use a record for the data and module functions for the behavior.")
            (module' [ "type Counter() ="; "    member _.Value = 1" ])
            "Counter() =\n        member _.Value = 1"
            (typeKindRule "class")

        rule
            "fsharp.unsupported_interface"
            "interface"
            "Interfaces are not supported; Morphir has no subtyping."
            (Some "Use a record of functions, or a union of the cases.")
            (module' [ "type IShape ="; "    abstract Area: float" ])
            "IShape =\n        abstract Area: float"
            (typeKindRule "interface")

        rule
            "fsharp.unsupported_member"
            "member"
            "Members and type extensions are not supported; Morphir types have no methods."
            (Some "Define a module function that takes the value as a parameter.")
            (module' [ "type Car = { Seats: int } with"; "    member this.Double = this.Seats * 2" ])
            "member this.Double = this.Seats * 2"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynTypeDefn(SynTypeDefn(
                    typeRepr = SynTypeDefnRepr.Simple _; members = members) as definition) when
                    objectModelKind definition = None
                    ->
                    members
                    |> List.map (fun memberDefinition -> finding memberDefinition.Range)
                | SyntaxNode.SynTypeDefn definition when objectModelKind definition = Some "member" ->
                    one definition.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_object_expression"
            "object expression"
            "Object expressions are not supported; Morphir has no objects."
            (Some "Use a record of functions.")
            (module' [ "let text = { new System.Object() with member _.ToString() = \"x\" }" ])
            "{ new System.Object() with member _.ToString() = \"x\" }"
            (expression (fun _ _ expression ->
                match expression with
                | SynExpr.ObjExpr _ -> one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_sequence_expression"
            "sequence expression"
            "Sequence expressions ('seq { }') are not supported; Morphir has no lazy sequences."
            (Some "Use a list and the List functions.")
            (module' [ "let numbers = seq { yield 1 }" ])
            "{ yield 1 }"
            (expression (fun _ path expression ->
                match expression, path with
                | SynExpr.ComputationExpr _,
                  SyntaxNode.SynExpr(SynExpr.App(funcExpr = SynExpr.Ident ident)) :: _ when
                    ident.idText = "seq"
                    ->
                    one expression.Range
                | SynExpr.ComputationExpr(hasSeqBuilder = true), _ -> one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_computation_expression"
            "computation expression"
            "Computation expressions are not supported; Morphir has no effects or builders."
            (Some "Write the steps as plain functions, for example with Result.bind.")
            (module' [ "let work = async { return 1 }" ])
            "{ return 1 }"
            (expression (fun _ path expression ->
                match expression, path with
                | SynExpr.ComputationExpr _,
                  SyntaxNode.SynExpr(SynExpr.App(funcExpr = SynExpr.Ident ident)) :: _ when
                    ident.idText = "seq"
                      ->
                      []
                | SynExpr.ComputationExpr(hasSeqBuilder = false), _ -> one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_comprehension"
            "list comprehension or range"
            "List comprehensions and ranges ('[ for ... ]', '[ 1 .. 3 ]') are not supported."
            (Some "Use List.map, List.filter or an explicit list.")
            (module' [ "let squares = [ for x in [ 1; 2 ] -> x * x ]" ])
            "[ for x in [ 1; 2 ] -> x * x ]"
            (expression (fun _ _ expression ->
                match expression with
                | SynExpr.ArrayOrListComputed(isArray = false; expr = inner) when
                    isComprehension inner
                    ->
                    one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_array"
            "array"
            "Arrays are not supported; Morphir collections are immutable lists."
            (Some "Use a list.")
            (module' [ "let numbers = [| 1; 2 |]" ])
            "[| 1; 2 |]"
            (Detector.Node(fun context _ node ->
                match node with
                | SyntaxNode.SynType(SynType.App(typeName = SynType.LongIdent name) as typ) when
                    isArrayName context name
                    ->
                    one typ.Range
                | SyntaxNode.SynExpr(SynExpr.ArrayOrList(isArray = true) as expression)
                | SyntaxNode.SynExpr(SynExpr.ArrayOrListComputed(isArray = true) as expression) ->
                    one expression.Range
                | SyntaxNode.SynPat(SynPat.ArrayOrList(isArray = true) as pattern) ->
                    one pattern.Range
                | SyntaxNode.SynType(SynType.Array _ as typ) -> one typ.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_exception"
            "exception"
            "Exceptions ('exception', 'try ... with', 'try ... finally') are not supported; Morphir has no effects."
            (Some "Return a Result or an option.")
            (module' [ "exception Failed of string" ])
            "exception Failed of string"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynModule(SynModuleDecl.Exception(range = range)) -> one range
                | SyntaxNode.SynExpr(SynExpr.TryWith _ as expression)
                | SyntaxNode.SynExpr(SynExpr.TryFinally _ as expression) -> one expression.Range
                | SyntaxNode.SynTypeDefn definition when
                    objectModelKind definition = Some "exception"
                    ->
                    one definition.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_active_pattern"
            "active pattern"
            "Active patterns are not supported; Morphir patterns match only constructors and values."
            (Some "Use a union and a function that returns it.")
            (module' [ "let (|Even|Odd|) n = if n % 2 = 0 then Even else Odd" ])
            "let (|Even|Odd|) n = if n % 2 = 0 then Even else Odd"
            (Detector.Node(fun context path node ->
                match node with
                | SyntaxNode.SynBinding(SynBinding(headPat = head; trivia = trivia) as definition) ->
                    let rec isActive pattern =
                        match pattern with
                        | SynPat.LongIdent(longDotId = name) ->
                            name.LongIdent
                            |> List.exists (fun ident -> ident.idText.StartsWith('|'))
                        | SynPat.Named(ident = SynIdent(ident, _)) -> ident.idText.StartsWith('|')
                        | SynPat.Paren(pat = inner) -> isActive inner
                        | _ -> false

                    if isActive head then
                        [
                            {
                                Report =
                                    Range.unionRanges
                                        trivia.LeadingKeyword.Range
                                        definition.RangeOfBindingWithRhs
                                Scope =
                                    Range.unionRanges
                                        trivia.LeadingKeyword.Range
                                        definition.RangeOfBindingWithRhs
                            }
                        ]
                    else
                        []
                | SyntaxNode.SynPat(SynPat.LongIdent(longDotId = name) as pattern) when
                    not (isBindingHead path pattern)
                    && (isActivePatternCase context name.Range
                        || (
                            match List.tryLast name.LongIdent with
                            | Some ident -> isActivePatternCase context ident.idRange
                            | None -> false
                        ))
                    ->
                    one pattern.Range
                | SyntaxNode.SynPat(SynPat.Named(ident = SynIdent(ident, _)) as pattern) when
                    not (isBindingHead path pattern)
                    && isActivePatternCase context ident.idRange
                    ->
                    one pattern.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_unit_of_measure"
            "unit of measure"
            "Units of measure are not supported; Morphir numbers carry no unit."
            (Some "Use a single-case union to name the quantity.")
            (module' [ "[<Measure>] type kg" ])
            "[<Measure>] type kg"
            (Detector.Node(fun context _ node ->
                match node with
                | SyntaxNode.SynTypeDefn definition when
                    hasAttribute "Measure" (typeAttributes definition)
                    ->
                    let (SynTypeDefn(trivia = trivia)) = definition

                    let start =
                        match typeAttributes definition with
                        | first :: _ -> first.Range
                        | [] -> definition.Range

                    let whole =
                        Range.unionRanges
                            start
                            (Range.unionRanges trivia.LeadingKeyword.Range definition.Range)

                    [ { Report = whole; Scope = whole } ]
                | SyntaxNode.SynType(SynType.MeasurePower _ as typ) -> one typ.Range
                | SyntaxNode.SynType(SynType.LongIdent name as typ) when
                    isMeasure context name.Range
                    ->
                    one typ.Range
                | SyntaxNode.SynExpr(SynExpr.Const(SynConst.Measure _, range))
                | SyntaxNode.SynPat(SynPat.Const(SynConst.Measure _, range)) -> one range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_quotation"
            "quotation"
            "Quotations are not supported; Morphir models values, not code."
            None
            (module' [ "let expression = <@ 1 + 1 @>" ])
            "<@ 1 + 1 @>"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynExpr(SynExpr.Quote _ as expression) -> one expression.Range
                | SyntaxNode.SynPat(SynPat.QuoteExpr _ as pattern) -> one pattern.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_anonymous_record"
            "anonymous record"
            "Anonymous records are not supported; Morphir record types have names."
            (Some "Declare a record type.")
            (module' [ "let point = {| X = 1 |}" ])
            "{| X = 1 |}"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynExpr(SynExpr.AnonRecd _ as expression) -> one expression.Range
                | SyntaxNode.SynType(SynType.AnonRecd _ as typ) -> one typ.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_struct"
            "struct type or struct tuple"
            "Struct types and struct tuples are not supported; Morphir has no value-type layout."
            (Some "Remove [<Struct>] or 'struct'.")
            (module' [ "let pair = struct (1, 2)" ])
            "struct (1, 2)"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynTypeDefn definition when
                    hasAttribute "Struct" (typeAttributes definition)
                    || objectModelKind definition = Some "struct"
                    ->
                    one definition.Range
                | SyntaxNode.SynExpr(SynExpr.Tuple(isStruct = true) as expression) ->
                    one expression.Range
                | SyntaxNode.SynPat(SynPat.Tuple(isStruct = true) as pattern) -> one pattern.Range
                | SyntaxNode.SynType(SynType.Tuple(isStruct = true) as typ) -> one typ.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_inline"
            "inline function"
            "Inline functions are not supported; Morphir functions are not specialized at their call sites."
            (Some "Remove 'inline' and give the parameters concrete types.")
            (module' [ "let inline double (x: int) = x * 2" ])
            "inline"
            (binding (fun _ (SynBinding(trivia = trivia)) ->
                trivia.InlineKeyword
                |> Option.map finding
                |> Option.toList
            ))

        rule
            "fsharp.unsupported_attribute"
            "attribute with an effect"
            "This attribute changes the meaning of the code and is not supported."
            (Some
                "Remove the attribute. Only RequireQualifiedAccess, AutoOpen and CompilationRepresentation are accepted.")
            (module' [ "[<Literal>]"; "let Limit = 10" ])
            "Literal"
            (Detector.Node(fun _ _ node ->
                let refused lists =
                    attributesOf lists
                    |> List.filter (fun attribute ->
                        let name = attributeName attribute

                        not (harmless.Contains name)
                        && not (specific.Contains name)
                    )
                    |> List.map (fun attribute -> finding attribute.Range)

                match node with
                | SyntaxNode.SynBinding(SynBinding(attributes = attributes)) -> refused attributes
                | SyntaxNode.SynModule(SynModuleDecl.Attributes(attributes = attributes)) ->
                    refused attributes
                | SyntaxNode.SynModule(SynModuleDecl.NestedModule(
                    moduleInfo = SynComponentInfo(attributes = attributes))) -> refused attributes
                | SyntaxNode.SynModuleOrNamespace(SynModuleOrNamespace(attribs = attributes)) ->
                    refused attributes
                | SyntaxNode.SynPat(SynPat.Attrib(attributes = attributes)) -> refused attributes
                | SyntaxNode.SynTypeDefn(SynTypeDefn(
                    typeInfo = SynComponentInfo(attributes = attributes); typeRepr = representation)) ->
                    let inner =
                        match representation with
                        | SynTypeDefnRepr.Simple(
                            simpleRepr = SynTypeDefnSimpleRepr.Record(recordFields = fields)) ->
                            fields
                            |> List.collect (fun (SynField(attributes = attributes)) -> attributes)
                        | SynTypeDefnRepr.Simple(
                            simpleRepr = SynTypeDefnSimpleRepr.Union(unionCases = cases)) ->
                            cases
                            |> List.collect (fun
                                                 (SynUnionCase(
                                                     attributes = attributes; caseType = caseType)) ->
                                let fields =
                                    match caseType with
                                    | SynUnionCaseKind.Fields fields ->
                                        fields
                                        |> List.collect (fun (SynField(attributes = attributes)) ->
                                            attributes
                                        )
                                    | _ -> []

                                attributes
                                @ fields
                            )
                        | _ -> []

                    refused (
                        attributes
                        @ inner
                    )
                | _ -> []
            ))

        rule
            "fsharp.unsupported_type_test"
            "type test"
            "Type tests (':?') are not supported; Morphir has no runtime types."
            (Some "Use a union to tell the cases apart.")
            (module' [ "let isText (value: obj) = value :? string" ])
            "value :? string"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynExpr(SynExpr.TypeTest _ as expression) -> one expression.Range
                | SyntaxNode.SynPat(SynPat.IsInst _ as pattern) -> one pattern.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_cast"
            "cast"
            "Casts (':>', ':?>', 'upcast', 'downcast') are not supported; Morphir has no subtyping."
            None
            (module' [ "let boxed = (1 :> obj)" ])
            "1 :> obj"
            (expression (fun _ _ expression ->
                match expression with
                | SynExpr.Upcast _
                | SynExpr.Downcast _
                | SynExpr.InferredUpcast _
                | SynExpr.InferredDowncast _ -> one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_null"
            "null"
            "'null' is not supported; Morphir values are never null."
            (Some "Use an option.")
            (module' [ "let nothing: string = null" ])
            "null"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynExpr(SynExpr.Null range)
                | SyntaxNode.SynPat(SynPat.Null range) -> one range
                | SyntaxNode.SynType(SynType.WithNull _ as typ) -> one typ.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_use"
            "use binding"
            "'use' bindings are not supported; Morphir has no resources to dispose."
            (Some "Use 'let'.")
            (module' [
                "let size () ="
                "    use stream = new System.IO.MemoryStream()"
                "    stream.Length"
            ])
            "use stream = new System.IO.MemoryStream()\n        stream.Length"
            (expression (fun _ _ expression ->
                match expression with
                | SynExpr.LetOrUse letOrUse when
                    letOrUse.IsUse
                    && not letOrUse.IsBang
                    ->
                    one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_sequential"
            "sequential expression"
            "Sequential expressions ('a; b') are not supported; Morphir values have no side effects."
            (Some "Remove the expression whose value is discarded.")
            (module' [ "let both () ="; "    ignore 1"; "    2" ])
            "ignore 1\n        2"
            (expression (fun _ path expression ->
                match expression with
                | SynExpr.Sequential _ when not (isListSeparator path) -> one expression.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_literal"
            "literal of an unsupported type"
            "This literal type is not supported; Morphir numbers are Int, Float and Decimal."
            (Some "Use an int, int64, float or decimal literal.")
            (module' [ "let small = 1uy" ])
            "1uy"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynExpr(SynExpr.Const(constant, range))
                | SyntaxNode.SynPat(SynPat.Const(constant, range)) when
                    not (supportedConstant constant)
                    && (
                        match constant with
                        | SynConst.Measure _ -> false
                        | _ -> true
                    )
                    ->
                    one range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_enum"
            "enum"
            "Enums are not supported; Morphir custom types have no numeric values."
            (Some "Use a union without fields.")
            (module' [ "type Color ="; "    | Red = 0"; "    | Blue = 1" ])
            "Color =\n        | Red = 0\n        | Blue = 1"
            (typeKindRule "enum")

        rule
            "fsharp.unsupported_delegate"
            "delegate"
            "Delegates are not supported; Morphir has function types."
            (Some "Use a function type.")
            (module' [ "type Handler = delegate of int -> unit" ])
            "Handler = delegate of int -> unit"
            (typeKindRule "delegate")

        rule
            "fsharp.unsupported_pattern_guard"
            "pattern guard"
            "Pattern guards ('when') are not supported; a Morphir pattern has no condition."
            (Some "Match the case, then use 'if' in its body.")
            (module' [
                "let size n ="
                "    match n with"
                "    | n when n > 3 -> \"big\""
                "    | _ -> \"small\""
            ])
            "n > 3"
            (Detector.Node(fun _ _ node ->
                match node with
                | SyntaxNode.SynMatchClause(SynMatchClause(whenExpr = Some guard)) ->
                    one guard.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_or_pattern"
            "or pattern"
            "Or patterns ('A | B') are not supported; a Morphir case has one pattern."
            (Some "Write one case for each alternative.")
            (module' [
                "let low n ="
                "    match n with"
                "    | 1 | 2 -> true"
                "    | _ -> false"
            ])
            "1 | 2"
            (pattern (fun _ _ pattern ->
                match pattern with
                | SynPat.Or _ -> one pattern.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_record_pattern"
            "record pattern"
            "Record patterns are not supported."
            (Some "Bind the record to a variable and read its fields.")
            (module' [
                "type Car = { Seats: int }"
                "let seats car ="
                "    match car with"
                "    | { Seats = s } -> s"
            ])
            "{ Seats = s }"
            (pattern (fun _ _ pattern ->
                match pattern with
                | SynPat.Record _ -> one pattern.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_list_pattern"
            "list pattern with elements"
            "List patterns with elements ('[ a; b ]') are not supported."
            (Some "Use '::' and '[]' patterns.")
            (module' [ "let first xs ="; "    match xs with"; "    | [ a ] -> a"; "    | _ -> 0" ])
            "[ a ]"
            (pattern (fun _ _ pattern ->
                match pattern with
                | SynPat.ArrayOrList(isArray = false; elementPats = _ :: _) -> one pattern.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_named_field_pattern"
            "named field pattern"
            "Named field patterns ('Case(field = p)') are not supported."
            (Some "Match the fields by position.")
            (module' [
                "type Status = Rented of days: int"
                "let days status ="
                "    match status with"
                "    | Rented(days = d) -> d"
            ])
            "Rented(days = d)"
            (pattern (fun _ path pattern ->
                match pattern with
                | SynPat.LongIdent(argPats = SynArgPats.NamePatPairs _) when
                    not (isBindingHead path pattern)
                    ->
                    one pattern.Range
                | _ -> []
            ))

        rule
            "fsharp.unsupported_conditional_compilation"
            "conditional compilation"
            "Conditional compilation ('#if') is not supported, because this frontend does not choose defines."
            (Some "Remove the directive, or keep one branch.")
            [
                "Example.fs",
                "namespace Acme.Demo\nmodule Example =\n#if DEBUG\n    let mode = \"debug\"\n#else\n    let mode = \"release\"\n#endif\n"
            ]
            "#if DEBUG"
            (Detector.Document(fun context ->
                let directives =
                    match context.Document.Syntax with
                    | ParsedInput.ImplFile(ParsedImplFileInput(trivia = trivia)) ->
                        trivia.ConditionalDirectives
                    | ParsedInput.SigFile(ParsedSigFileInput(trivia = trivia)) ->
                        trivia.ConditionalDirectives

                directives
                |> List.choose (fun directive ->
                    match directive with
                    | ConditionalDirectiveTrivia.If(range = range) -> Some(finding range)
                    | _ -> None
                )
            ))

        rule
            "fsharp.unsupported_signature_file"
            "signature file"
            "Signature files ('.fsi') are not supported; access comes from F# visibility and the exposed modules."
            (Some "Remove the signature file and use 'private' or 'internal'.")
            [
                "Example.fsi", "namespace Acme.Demo\nmodule Example =\n    val total: int\n"
                "Example.fs", "namespace Acme.Demo\nmodule Example =\n    let total = 1\n"
            ]
            ""
            (Detector.Document(fun context ->
                match context.Document.Syntax with
                | ParsedInput.SigFile _ ->
                    let lines = context.Document.Lines
                    let struct (endLine, endColumn) = lines.Position lines.Length

                    [
                        {
                            Report =
                                Range.mkRange
                                    context.Document.Path
                                    (Position.mkPos 1 0)
                                    (Position.mkPos 1 0)
                            Scope =
                                Range.mkRange
                                    context.Document.Path
                                    (Position.mkPos 1 0)
                                    (Position.mkPos endLine endColumn)
                        }
                    ]
                | ParsedInput.ImplFile _ -> []
            ))

        // ----- Fallback rules: anything else outside the supported subset ------------------------------

        fallback
            "fsharp.unsupported_expression"
            "expression outside the supported subset"
            "This kind of expression is not supported by the F# frontend."
            (module' [ "let later = lazy 1" ])
            "lazy 1"
            (expression (fun _ path expression ->
                let supported =
                    match expression with
                    | SynExpr.Paren _
                    | SynExpr.Const _
                    | SynExpr.Typed _
                    | SynExpr.Tuple(isStruct = false)
                    | SynExpr.ArrayOrList(isArray = false)
                    | SynExpr.ArrayOrListComputed(isArray = false)
                    | SynExpr.Record(baseInfo = None)
                    | SynExpr.Lambda _
                    | SynExpr.MatchLambda _
                    | SynExpr.Match _
                    | SynExpr.App _
                    | SynExpr.TypeApp _
                    | SynExpr.IfThenElse(elseExpr = Some _)
                    | SynExpr.Ident _
                    | SynExpr.LongIdent(isOptional = false)
                    | SynExpr.DotGet _ -> true
                    | SynExpr.LetOrUse letOrUse -> not letOrUse.IsBang
                    | SynExpr.Sequential _ -> isListSeparator path
                    | _ -> false

                if supported then [] else one expression.Range
            ))

        fallback
            "fsharp.unsupported_pattern"
            "pattern outside the supported subset"
            "This kind of pattern is not supported by the F# frontend."
            (module' [ "let both pair ="; "    match pair with"; "    | (a, _) & (_, b) -> a + b" ])
            "(a, _) & (_, b)"
            (pattern (fun _ path pattern ->
                let supported =
                    match pattern with
                    | SynPat.Wild _
                    | SynPat.Named(isThisVal = false)
                    | SynPat.Const _
                    | SynPat.Typed _
                    | SynPat.Tuple(isStruct = false)
                    | SynPat.Paren _
                    | SynPat.ArrayOrList(isArray = false; elementPats = [])
                    | SynPat.ListCons _
                    | SynPat.As _ -> true
                    | SynPat.LongIdent(
                        argPats = SynArgPats.Pats _; typarDecls = None; extraId = None) -> true
                    | SynPat.LongIdent _ -> isBindingHead path pattern
                    | _ -> false

                if supported then [] else one pattern.Range
            ))

        fallback
            "fsharp.unsupported_type"
            "type outside the supported subset"
            "This kind of type is not supported by the F# frontend."
            (module' [ "let total (values: #seq<int>) = Seq.sum values" ])
            "#seq<int>"
            (typeNode (fun _ _ typ ->
                match typ with
                | SynType.LongIdent _
                | SynType.App _
                | SynType.LongIdentApp _
                | SynType.Tuple(isStruct = false)
                | SynType.Fun _
                | SynType.Var _
                | SynType.Anon _
                | SynType.Paren _ -> []
                | _ -> one typ.Range
            ))

        fallback
            "fsharp.unsupported_declaration"
            "declaration outside the supported subset"
            "This kind of declaration is not supported by the F# frontend."
            (module' [ "do ignore 1" ])
            "do ignore 1"
            (declaration (fun _ declaration ->
                match declaration with
                | SynModuleDecl.Let _
                | SynModuleDecl.Types _
                | SynModuleDecl.NestedModule _
                | SynModuleDecl.Open _
                | SynModuleDecl.NamespaceFragment _ -> []
                | _ -> one declaration.Range
            ))
    ]

    /// <summary>The findings of every rule in one document, before nested findings are removed.</summary>
    let findings
        (catalog: AdmissionRule list)
        (symbolsAt: range -> FSharpSymbolUse list)
        (document: AdmissionDocument)
        =
        let context = {
            Document = document
            SymbolsAt = symbolsAt
        }

        let specificRules =
            catalog
            |> List.filter (fun rule -> not rule.IsFallback)

        let fallbackRules =
            catalog
            |> List.filter (fun rule -> rule.IsFallback)

        let fromDocument =
            catalog
            |> List.collect (fun rule ->
                match rule.Detect with
                | Detector.Document detect ->
                    detect context
                    |> List.map (fun found -> rule, found)
                | Detector.Node _ -> []
            )

        let fromNodes =
            document.Syntax
            |> ParsedInput.fold
                (fun found path node ->
                    let apply rules =
                        rules
                        |> List.collect (fun rule ->
                            match rule.Detect with
                            | Detector.Node detect ->
                                detect context path node
                                |> List.map (fun hit -> rule, hit)
                            | Detector.Document _ -> []
                        )

                    match apply specificRules with
                    | [] ->
                        apply fallbackRules
                        @ found
                    | hits ->
                        hits
                        @ found
                )
                []
            |> List.rev

        fromDocument
        @ fromNodes
        |> List.distinctBy (fun (rule, found) -> rule.Code, found.Report, found.Scope)

    /// <summary>
    /// Removes each finding that lies inside the scope of another finding, so that one refused construct gives
    /// one diagnostic.
    /// </summary>
    let outermost (all: (AdmissionRule * Finding) list) =
        let indexed = List.indexed all

        indexed
        |> List.filter (fun (index, (_, found)) ->
            not (
                indexed
                |> List.exists (fun (otherIndex, (_, outer)) ->
                    otherIndex
                    <> index
                    && covers outer.Scope found.Report
                    && (
                    // A wider construct holds this one.
                    not (covers found.Scope outer.Scope)
                    // Two nodes with the same range: the outer node is visited first.
                    || (outer.Scope = found.Scope
                        && otherIndex < index))
                )
            )
        )
        |> List.map snd
