// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open System
open System.Collections.Generic
open System.Numerics
open System.Text
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.Frontend.FSharp
open Morphir.IR

/// <summary>
/// A checked type, or a function type made of checked parts. FCS cannot create an <c>FSharpType</c>, so the
/// type of a constructor used as a function is kept as its parts.
/// </summary>
[<RequireQualifiedAccess>]
type internal LoweredType =
    | Checked of FSharpType
    | Arrow of argument: LoweredType * result: LoweredType

    /// <summary>The argument and result of a function type.</summary>
    member this.TryPeel() =
        match this with
        | Arrow(argument, result) -> Some(argument, result)
        | Checked typ ->
            let stripped = typ.StripAbbreviations()

            if stripped.IsFunctionType then
                Some(Checked stripped.GenericArguments[0], Checked stripped.GenericArguments[1])
            else
                None

    /// <summary>The checked type, when this is not made of parts.</summary>
    member this.TryChecked =
        match this with
        | Checked typ -> Some typ
        | Arrow _ -> None

/// <summary>
/// The lowering of value definitions, expressions and patterns (FSF-1, FSF-2), generic over the IR builder.
/// Structure and locations come from the original syntax. Every type comes from the compiler: the typed
/// expression at the same range, the instantiation of the enclosing call, the match evidence, or the checked
/// type that the enclosing declaration expects. Lowering never solves an F# type itself.
/// </summary>
[<Sealed>]
type internal ValueLowering<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>
    (
        builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>,
        plan: FrontendPlan,
        evidence: CheckedEvidence,
        typed: TypedIndex,
        document: CheckedDocument
    ) =

    let matches =
        let found = Dictionary<SynExpr, MatchEvidence>(HashIdentity.Reference)

        for evidenceOfMatch in evidence.Matches do
            if evidenceOfMatch.Document.Index = document.Index then
                found[evidenceOfMatch.Expression] <- evidenceOfMatch

        found

    let freshNames =
        let used = HashSet<Name>()

        document.Syntax
        |> ParsedInput.fold
            (fun () _ node ->
                match node with
                | SyntaxNode.SynPat(SynPat.Named(ident = SynIdent(ident, _))) ->
                    used.Add(Name.fromString ident.idText)
                    |> ignore
                | _ -> ()
            )
            ()

        let mutable next = 0

        fun () ->
            let rec attempt () =
                next <- next + 1
                let candidate = Name [ "subject"; string next ]
                if used.Contains candidate then attempt () else candidate

            attempt ()

    let locate (range: range) =
        Location.Create(document.Uri, CompilerEdge.rangeOf range)

    let fail (code: DiagnosticCode) (range: range) (message: string) : Checked<'T> =
        Checked.fail [
            Diagnostic.error code message
            |> Diagnostic.at (locate range)
        ]

    let unsupported range message =
        fail (DiagnosticCode.Create "fsharp.unsupported_expression") range message

    let noType range =
        unsupported
            range
            "The F# compiler gives no type for this expression, so it cannot be lowered exactly."

    let symbolsAt (range: range) =
        evidence.SymbolsAt(document.Index, range)
        |> List.map (fun symbolUse -> symbolUse.Symbol)

    let rec lowerTy (range: range) (ty: LoweredType) : Checked<'Type> =
        match ty with
        | LoweredType.Checked typ -> Lowering.lowerType builder plan (locate range) typ
        | LoweredType.Arrow(argument, result) -> check {
            let! argument = lowerTy range argument
            and! result = lowerTy range result
            return! builder.TypeFunction(locate range, argument, result)
          }

    let instantiate
        (generic: FSharpType)
        (parameters: FSharpGenericParameter seq)
        (arguments: FSharpType list)
        =
        let parameters = List.ofSeq parameters

        if
            parameters.Length = arguments.Length
            && not parameters.IsEmpty
        then
            Some(generic.Instantiate(List.zip parameters arguments))
        elif parameters.IsEmpty then
            Some generic
        else
            None

    /// <summary>The type of a union case used as a function: its instantiated fields, then the union.</summary>
    let constructorType (union: FSharpType) (case: FSharpUnionCase) =
        let stripped = union.StripAbbreviations()

        let substitution =
            if stripped.HasTypeDefinition then
                List.zip
                    (List.ofSeq stripped.TypeDefinition.GenericParameters)
                    (List.ofSeq stripped.GenericArguments)
            else
                []

        let fields =
            case.Fields
            |> Seq.map (fun field -> LoweredType.Checked(field.FieldType.Instantiate substitution))
            |> List.ofSeq

        List.foldBack
            (fun field result -> LoweredType.Arrow(field, result))
            fields
            (LoweredType.Checked union)

    /// <summary>The checked type at a range, or else the expected type.</summary>
    let typeOf (range: range) (expected: LoweredType option) =
        match typed.TypeAt(document.Index, range) with
        | Some typ -> Some(LoweredType.Checked typ)
        | None -> expected

    let literal (range: range) (constant: SynConst) : Checked<LiteralValue> =
        match constant with
        | SynConst.Bool value -> Checked.pass (LiteralValue.Bool value)
        | SynConst.Char value when not (Char.IsSurrogate value) ->
            Checked.pass (LiteralValue.Char(Rune value))
        | SynConst.String(text = text) -> Checked.pass (LiteralValue.String text)
        | SynConst.Int32 value -> Checked.pass (LiteralValue.Integer(BigInteger value))
        | SynConst.Int64 value -> Checked.pass (LiteralValue.Integer(BigInteger value))
        | SynConst.Double value -> Checked.pass (LiteralValue.Float value)
        | SynConst.Decimal value -> Checked.pass (LiteralValue.Decimal value)
        | _ -> unsupported range "This literal is not supported."

    let fieldName (field: FSharpField) range : Checked<Name> =
        match field.DeclaringEntity with
        | Some record ->
            match plan.TryField(record, field) with
            | Some name -> Checked.pass name
            | None ->
                fail
                    FSharpCodes.unsupportedReference
                    range
                    $"The record field '{field.FullName}' is not a field of this package."
        | None ->
            fail
                FSharpCodes.unsupportedReference
                range
                $"The field '{field.Name}' has no record type."

    /// <summary>The instantiated type of a field of a record value of a checked type.</summary>
    let fieldType (record: LoweredType) (field: FSharpField) =
        match record.TryChecked with
        | Some typ ->
            let stripped = typ.StripAbbreviations()

            if stripped.HasTypeDefinition then
                instantiate
                    field.FieldType
                    stripped.TypeDefinition.GenericParameters
                    (List.ofSeq stripped.GenericArguments)
                |> Option.map LoweredType.Checked
            else
                None
        | None -> None

    /// <summary>The Morphir constructor of a union case: a case of this package, or an SDK constructor.</summary>
    let constructorOf (case: FSharpUnionCase) =
        match plan.TryConstructor case with
        | Some fqName -> Some fqName
        | None ->
            match SdkMapping.tryCase (SdkMapping.caseKey case) with
            | Some {
                       Kind = SdkCaseKind.Constructor
                       Target = target
                   } -> Some target
            | _ -> None

    /// <summary>The operand shape of the first parameter of a function type.</summary>
    let operandOf (ty: LoweredType) =
        ty.TryPeel()
        |> Option.bind (fun (argument, _) -> argument.TryChecked)
        |> Option.map SdkMapping.operand

    member private this.Pattern(evidenceOfPattern: PatternEvidence) : Checked<'Pattern> =
        let range = evidenceOfPattern.Syntax.Range
        let at = locate range

        check {
            let! patternType = Lowering.lowerType builder plan at evidenceOfPattern.Type

            let children () =
                evidenceOfPattern.Children
                |> Checked.traverse this.Pattern

            match evidenceOfPattern.Shape with
            | PatternShape.Wildcard -> return! builder.PatternWildcard(at, patternType)
            | PatternShape.Unit -> return! builder.PatternUnit(at, patternType)
            | PatternShape.EmptyList -> return! builder.PatternEmptyList(at, patternType)
            | PatternShape.Literal constant ->
                let! value = literal range constant
                return! builder.PatternLiteral(at, patternType, value)
            | PatternShape.Variable value ->
                let! wildcard = builder.PatternWildcard(at, patternType)

                return!
                    builder.PatternAs(at, patternType, wildcard, Name.fromString value.DisplayName)
            | PatternShape.Tuple ->
                let! elements = children ()
                return! builder.PatternTuple(at, patternType, elements)
            | PatternShape.Cons ->
                match! children () with
                | [ head; tail ] -> return! builder.PatternHeadTail(at, patternType, head, tail)
                | _ -> return! unsupported range "A cons pattern needs a head and a tail."
            | PatternShape.Constructor case ->
                let! arguments = children ()

                match constructorOf case with
                | Some fqName ->
                    return! builder.PatternConstructor(at, patternType, fqName, arguments)
                | None ->
                    return!
                        fail
                            FSharpCodes.unsupportedReference
                            range
                            $"The union case '{SdkMapping.caseKey case}' is not a constructor of this package or of the Morphir SDK."
            | PatternShape.As ->
                match evidenceOfPattern.Children with
                | [ inner; { Shape = PatternShape.Variable alias } ] ->
                    let! innerPattern = this.Pattern inner

                    return!
                        builder.PatternAs(
                            at,
                            patternType,
                            innerPattern,
                            Name.fromString alias.DisplayName
                        )
                | _ -> return! unsupported range "The right side of 'as' must be a name."
        }

    /// <summary>Lowers a parameter or let pattern of an expected checked type.</summary>
    member private this.ParameterPattern
        (
            syntax: SynPat,
            expected: LoweredType
        ) : Checked<'Pattern> =
        match expected.TryChecked with
        | None -> unsupported syntax.Range "The F# compiler gives no type for this pattern."
        | Some typ ->
            match Evidence.patterns evidence document.Index typ syntax with
            | Ok found -> this.Pattern found
            | Error failure ->
                unsupported
                    syntax.Range
                    $"This pattern cannot be lowered exactly: {failure.Explanation}."

    /// <summary>
    /// Lowers a reference to a value or a union case. A refusal is reported at <c>failureRange</c>: the whole
    /// call when the reference is the function of an application.
    /// </summary>
    member private this.Reference
        (
            range: range,
            symbol: FSharpSymbol,
            ty: LoweredType,
            ?failureRange: range
        ) : Checked<'Value> =
        let at = locate range
        let failureRange = defaultArg failureRange range

        let refuse message =
            fail FSharpCodes.unsupportedReference failureRange message

        // The mapping is decided before the type is lowered, so an unmapped function is named even when its
        // type has no Morphir mapping either.
        let typed (build: 'Type -> Checked<'Value>) =
            lowerTy range ty
            |> Checked.bind build

        match symbol with
        | :? FSharpMemberOrFunctionOrValue as value ->
            match plan.TryValue value with
            | Some fqName -> typed (fun valueType -> builder.Reference(at, valueType, fqName))
            | None when not value.IsModuleValueOrMember ->
                typed (fun valueType ->
                    builder.Variable(at, valueType, Name.fromString value.DisplayName)
                )
            | None ->
                let key = SdkMapping.valueKey value
                let operand = operandOf ty

                match SdkMapping.tryValue key operand with
                | Some entry ->
                    typed (fun valueType -> builder.Reference(at, valueType, entry.Target))
                | None when SdkMapping.isMapped key ->
                    let operandText =
                        operand
                        |> Option.map (fun operand -> operand.Text)
                        |> Option.defaultValue "unknown"

                    refuse
                        $"The F# operator or function '{value.DisplayName}' ({value.FullName}) has no Morphir SDK mapping for '{operandText}' operands."
                | None -> refuse $"The F# value '{value.FullName}' has no Morphir mapping."
        | :? FSharpUnionCase as case ->
            match plan.TryConstructor case with
            | Some fqName -> typed (fun valueType -> builder.Constructor(at, valueType, fqName))
            | None ->
                match SdkMapping.tryCase (SdkMapping.caseKey case) with
                | Some {
                           Kind = SdkCaseKind.Constructor
                           Target = target
                       } -> typed (fun valueType -> builder.Constructor(at, valueType, target))
                | Some {
                           Kind = SdkCaseKind.Function
                           Target = target
                       } -> typed (fun valueType -> builder.Reference(at, valueType, target))
                | None ->
                    refuse $"The union case '{SdkMapping.caseKey case}' has no Morphir mapping."
        | other -> refuse $"The F# symbol '{other.FullName}' has no Morphir mapping."

    /// <summary>The value symbol named by an identifier, and its type when the compiler gives one.</summary>
    member private this.Head(range: range, spine: range, arity: int, expected: LoweredType option) =
        let symbol =
            symbolsAt range
            |> List.tryFind (fun symbol ->
                match symbol with
                | :? FSharpMemberOrFunctionOrValue
                | :? FSharpUnionCase -> true
                | _ -> false
            )

        // The compiler's types of the arguments and of the result give the function type of the head. This
        // also holds where FCS reports the result type for the function of an application.
        let arrowOf (arguments: FSharpExpr list) (result: FSharpType) =
            List.foldBack
                (fun (argument: FSharpExpr) state ->
                    LoweredType.Arrow(LoweredType.Checked argument.Type, state)
                )
                arguments
                (LoweredType.Checked result)

        // Another call can have the same range: the pipe in 'xs |> List.map f' or an operator's witness.
        let isHead (value: FSharpMemberOrFunctionOrValue) =
            match symbol with
            | Some(:? FSharpMemberOrFunctionOrValue as head) -> head.IsEffectivelySameAs value
            | _ -> false

        let fromCall =
            typed.At(document.Index, spine)
            |> List.tryPick (fun expression ->
                match expression with
                | NewUnionCase(union, case, _) -> Some(constructorType union case)
                | Call(_, value, _, _, arguments) when
                    isHead value
                    && arguments.Length = arity
                    && arity > 0
                    ->
                    Some(arrowOf arguments expression.Type)
                | Application(_, _, arguments) when
                    arguments.Length = arity
                    && arity > 0
                    ->
                    Some(arrowOf arguments expression.Type)
                | Call(_, value, classTypes, methodTypes, _) when isHead value ->
                    instantiate
                        value.FullType
                        value.GenericParameters
                        (classTypes
                         @ methodTypes)
                    |> Option.map LoweredType.Checked
                | Application(Value value, typeArguments, _) ->
                    instantiate value.FullType value.GenericParameters typeArguments
                    |> Option.map LoweredType.Checked
                | _ -> None
            )

        let fromSymbol =
            match symbol with
            | Some(:? FSharpMemberOrFunctionOrValue as value) when value.GenericParameters.Count = 0 ->
                Some(LoweredType.Checked value.FullType)
            | Some(:? FSharpUnionCase as case) ->
                match typed.TypeAt(document.Index, spine) with
                | Some union -> Some(constructorType union case)
                | None ->
                    match expected with
                    | Some(LoweredType.Checked union) when case.Fields.Count = 0 ->
                        Some(LoweredType.Checked union)
                    | _ -> None
            | _ -> None

        let ty =
            match typed.TypeAt(document.Index, range) with
            | Some typ -> Some(LoweredType.Checked typ)
            | None ->
                fromCall
                |> Option.orElse fromSymbol
                |> Option.orElse expected

        symbol, ty

    /// <summary>Lowers an identifier chain: a value, then any record fields.</summary>
    member private this.Identifiers
        (
            range: range,
            identifiers: Ident list,
            expected: LoweredType option
        ) =
        // The compiler records a value at its identifier, or a qualified value such as List.map at the whole
        // long identifier.
        let valueAt (ident: Ident) =
            let isValue range =
                symbolsAt range
                |> List.exists (fun symbol ->
                    match symbol with
                    | :? FSharpMemberOrFunctionOrValue
                    | :? FSharpUnionCase -> true
                    | _ -> false
                )

            [ ident.idRange; Range.unionRanges identifiers.Head.idRange ident.idRange ]
            |> List.tryFind isValue

        match
            identifiers
            |> List.indexed
            |> List.tryPick (fun (index, ident) ->
                valueAt ident
                |> Option.map (fun symbolRange -> index, symbolRange)
            )
        with
        | None -> unsupported range "This name does not refer to a value."
        | Some(index, symbolRange) ->
            let head = identifiers[index]
            let fields = List.skip (index + 1) identifiers
            let headRange = Range.unionRanges identifiers.Head.idRange head.idRange
            let headExpected = if fields.IsEmpty then expected else None

            match this.Head(symbolRange, range, 0, headExpected) with
            | Some symbol, Some ty ->
                let start =
                    this.Reference(headRange, symbol, ty)
                    |> Checked.map (fun value -> value, ty)

                this.Fields(start, headRange, fields)
            | Some _, None -> noType headRange
            | None, _ -> unsupported headRange "This name does not refer to a value."

    member private this.Fields
        (
            start: Checked<'Value * LoweredType>,
            startRange: range,
            fields: Ident list
        ) =
        fields
        |> List.fold
            (fun (state: Checked<'Value * LoweredType> * range) (ident: Ident) ->
                let current, currentRange = state
                let range = Range.unionRanges currentRange ident.idRange

                let next =
                    current
                    |> Checked.bind (fun (target, targetType) ->
                        // FCS records a field use at the field name or at the whole access.
                        let field =
                            symbolsAt ident.idRange
                            @ symbolsAt range
                            |> List.tryPick (fun symbol ->
                                match symbol with
                                | :? FSharpField as field -> Some field
                                | _ -> None
                            )

                        let member' =
                            symbolsAt ident.idRange
                            @ symbolsAt range
                            |> List.tryPick (fun symbol ->
                                match symbol with
                                | :? FSharpMemberOrFunctionOrValue as value when value.IsMember ->
                                    Some value
                                | _ -> None
                            )

                        match field, member' with
                        | None, Some value ->
                            fail
                                FSharpCodes.unsupportedReference
                                range
                                $"The .NET member '{value.FullName}' has no Morphir mapping."
                        | None, None ->
                            unsupported ident.idRange "Only record fields can follow a value."
                        | Some field, _ ->
                            match typeOf range (fieldType targetType field) with
                            | None -> noType range
                            | Some ty -> check {
                                let! name = fieldName field ident.idRange
                                let! fieldValueType = lowerTy range ty

                                let! value =
                                    builder.Field(locate range, fieldValueType, target, name)

                                return value, ty
                              }
                    )

                next, range
            )
            (start, startRange)
        |> fst
        |> Checked.map fst

    /// <summary>
    /// The checked type of an expression without an expected type: the typed expression at its range, or the
    /// checked type of a non-generic value that a name refers to.
    /// </summary>
    member private this.TypeOfValue(expression: SynExpr) : LoweredType option =
        match typeOf expression.Range None with
        | Some found -> Some found
        | None ->
            match expression with
            | SynExpr.Paren(expr = inner)
            | SynExpr.Typed(expr = inner) -> this.TypeOfValue inner
            | _ ->
                let named =
                    match expression with
                    | SynExpr.Ident ident -> Some ident
                    | SynExpr.LongIdent(longDotId = name) -> List.tryLast name.LongIdent
                    | _ -> None

                named
                |> Option.bind (fun ident ->
                    symbolsAt ident.idRange
                    |> List.tryPick (fun symbol ->
                        match symbol with
                        | :? FSharpMemberOrFunctionOrValue as value when
                            value.GenericParameters.Count = 0
                            ->
                            Some(LoweredType.Checked value.FullType)
                        | _ -> None
                    )
                )

    /// <summary>Lowers an expression, with the type the enclosing declaration expects when there is one.</summary>
    member this.Expression(expression: SynExpr, expected: LoweredType option) : Checked<'Value> =
        let range = expression.Range
        let at = locate range

        match expression with
        | SynExpr.Paren(expr = inner)
        | SynExpr.Typed(expr = inner) -> this.Expression(inner, expected)

        | SynExpr.Const(SynConst.Unit, _) ->
            match typeOf range expected with
            | Some ty ->
                lowerTy range ty
                |> Checked.bind (fun unitType -> builder.Unit(at, unitType))
            | None ->
                builder.TypeUnit at
                |> Checked.bind (fun unitType -> builder.Unit(at, unitType))

        | SynExpr.Const(constant, _) ->
            match typeOf range expected with
            | None -> noType range
            | Some ty -> check {
                let! literalType = lowerTy range ty
                and! value = literal range constant
                return! builder.Literal(at, literalType, value)
              }

        | SynExpr.TypeApp(expr = SynExpr.Ident ident) ->
            this.Identifiers(range, [ ident ], typeOf range expected)
        | SynExpr.TypeApp(expr = SynExpr.LongIdent(longDotId = name)) ->
            this.Identifiers(range, name.LongIdent, typeOf range expected)
        | SynExpr.Ident ident -> this.Identifiers(range, [ ident ], expected)
        | SynExpr.LongIdent(longDotId = name) -> this.Identifiers(range, name.LongIdent, expected)

        | SynExpr.DotGet(expr = target; longDotId = name) ->
            let start =
                this.Expression(target, None)
                |> Checked.bind (fun value ->
                    match this.TypeOfValue target with
                    | Some ty -> Checked.pass (value, ty)
                    | None -> noType target.Range
                )

            this.Fields(start, target.Range, name.LongIdent)

        | SynExpr.App _ -> this.Application(expression, expected)

        | SynExpr.Tuple(isStruct = false; exprs = elements) ->
            match
                typeOf range expected
                |> Option.bind (fun ty -> ty.TryChecked)
            with
            | None -> noType range
            | Some typ ->
                let parts =
                    let stripped = typ.StripAbbreviations()

                    if stripped.IsTupleType then
                        List.ofSeq stripped.GenericArguments
                    else
                        []

                if
                    parts.Length
                    <> elements.Length
                then
                    noType range
                else
                    check {
                        let! tupleType = lowerTy range (LoweredType.Checked typ)

                        and! values =
                            List.zip elements parts
                            |> Checked.traverse (fun (element, part) ->
                                this.Expression(element, Some(LoweredType.Checked part))
                            )

                        return! builder.Tuple(at, tupleType, values)
                    }

        | SynExpr.ArrayOrList(isArray = false; exprs = items) -> this.List(range, items, expected)
        | SynExpr.ArrayOrListComputed(isArray = false; expr = inner) ->
            let rec elements expression =
                match expression with
                | SynExpr.Sequential(expr1 = first; expr2 = rest) ->
                    first
                    :: elements rest
                | other -> [ other ]

            this.List(range, elements inner, expected)

        | SynExpr.Record(copyInfo = copy; recordFields = fields) ->
            match typeOf range expected with
            | None -> noType range
            | Some recordType ->
                let assignments =
                    fields
                    |> List.choose (fun (SynExprRecordField(fieldName = (name, _); expr = value)) ->
                        value
                        |> Option.map (fun value -> name, value)
                    )

                let lowered =
                    assignments
                    |> Checked.traverse (fun (name, value) ->
                        let ident = List.last name.LongIdent

                        let field =
                            symbolsAt ident.idRange
                            |> List.tryPick (fun symbol ->
                                match symbol with
                                | :? FSharpField as field -> Some field
                                | _ -> None
                            )

                        match field with
                        | None -> unsupported name.Range "This is not a record field."
                        | Some field -> check {
                            let! morphirName = fieldName field ident.idRange
                            and! fieldValue = this.Expression(value, fieldType recordType field)
                            return morphirName, fieldValue
                          }
                    )

                check {
                    let! recordValueType = lowerTy range recordType
                    let! assigned = lowered

                    match copy with
                    | Some(target, _) ->
                        let! targetValue = this.Expression(target, Some recordType)
                        return! builder.UpdateRecord(at, recordValueType, targetValue, assigned)
                    | None -> return! builder.Record(at, recordValueType, assigned)
                }

        | SynExpr.Lambda(parsedData = Some(parameters, body)) ->
            this.Lambda(range, parameters, body, expected)

        | SynExpr.LetOrUse letOrUse when
            not letOrUse.IsUse
            && not letOrUse.IsBang
            ->
            this.Let(range, letOrUse.IsRecursive, letOrUse.Bindings, letOrUse.Body, expected)

        | SynExpr.IfThenElse(ifExpr = condition; thenExpr = thenBranch; elseExpr = Some elseBranch) ->
            match typeOf range expected with
            | None -> noType range
            | Some ty -> check {
                let! resultType = lowerTy range ty
                and! conditionValue = this.Expression(condition, None)
                and! thenValue = this.Expression(thenBranch, Some ty)
                and! elseValue = this.Expression(elseBranch, Some ty)
                return! builder.IfThenElse(at, resultType, conditionValue, thenValue, elseValue)
              }

        | SynExpr.Match(expr = subject) ->
            match matches.TryGetValue expression with
            | true, found -> check {
                let! subjectValue =
                    this.Expression(subject, Some(LoweredType.Checked found.SubjectType))

                return! this.Match(range, found, subjectValue)
              }
            | _ -> unsupported range "This match has no checked evidence."

        | SynExpr.MatchLambda _ ->
            match matches.TryGetValue expression with
            | true, found ->
                let argument = freshNames ()
                let subjectType = LoweredType.Checked found.SubjectType

                check {
                    let! argumentType = lowerTy range subjectType

                    let! lambdaType =
                        lowerTy
                            range
                            (LoweredType.Arrow(subjectType, LoweredType.Checked found.ResultType))

                    let! wildcard = builder.PatternWildcard(at, argumentType)
                    let! parameter = builder.PatternAs(at, argumentType, wildcard, argument)
                    let! subjectValue = builder.Variable(at, argumentType, argument)
                    let! body = this.Match(range, found, subjectValue)
                    return! builder.Lambda(at, lambdaType, parameter, body)
                }
            | _ -> unsupported range "This match has no checked evidence."

        | _ -> unsupported range "This expression is outside the supported subset."

    member private this.List(range: range, items: SynExpr list, expected: LoweredType option) =
        match
            typeOf range expected
            |> Option.bind (fun ty -> ty.TryChecked)
        with
        | None -> noType range
        | Some typ ->
            let element =
                let stripped = typ.StripAbbreviations()

                if stripped.GenericArguments.Count = 1 then
                    Some(LoweredType.Checked stripped.GenericArguments[0])
                else
                    None

            check {
                let! listType = lowerTy range (LoweredType.Checked typ)

                and! values =
                    items
                    |> Checked.traverse (fun item -> this.Expression(item, element))

                return! builder.List(locate range, listType, values)
            }

    member private this.Match(range: range, found: MatchEvidence, subjectValue: 'Value) = check {
        let! resultType = Lowering.lowerType builder plan (locate range) found.ResultType

        let! cases =
            found.Clauses
            |> Checked.traverse (fun clause ->
                let (SynMatchClause(resultExpr = body)) = clause.Clause

                check {
                    let! pattern = this.Pattern clause.Pattern
                    and! value = this.Expression(body, Some(LoweredType.Checked clause.ResultType))
                    return pattern, value
                }
            )

        return! builder.PatternMatch(locate range, resultType, subjectValue, cases)
    }

    /// <summary>
    /// Lowers an application spine <c>f a b</c> as nested applications. The function type comes from the
    /// compiler's call or application at the spine's range, or from the function's checked type.
    /// </summary>
    member private this.Application(expression: SynExpr, expected: LoweredType option) =
        // 'a + b' is an infix application of the operator to 'a', applied to 'b'.
        let rec flatten expression arguments =
            match expression with
            | SynExpr.App(funcExpr = func; argExpr = argument) ->
                flatten
                    func
                    ((argument, expression.Range)
                     :: arguments)
            | head -> head, arguments

        let head, arguments = flatten expression []

        // An explicit type application (identity<int> 1) names the same head; the compiler's call or
        // application at the spine gives its instantiation.
        let head =
            match head with
            | SynExpr.TypeApp(expr = inner) -> inner
            | other -> other

        // The value symbol of a named head, and the range where the compiler records it: the identifier, or
        // the whole long identifier for a qualified name such as List.map.
        let named =
            let valueSymbol range =
                symbolsAt range
                |> List.tryFind (fun symbol ->
                    match symbol with
                    | :? FSharpMemberOrFunctionOrValue
                    | :? FSharpUnionCase -> true
                    | _ -> false
                )
                |> Option.map (fun symbol -> symbol, range)

            match head with
            | SynExpr.Ident ident -> valueSymbol ident.idRange
            | SynExpr.LongIdent(longDotId = name) when name.LongIdent.Length > 0 ->
                let last = List.last name.LongIdent

                valueSymbol last.idRange
                |> Option.orElse (valueSymbol name.Range)
            | _ -> None

        let structural =
            match named with
            | Some(:? FSharpMemberOrFunctionOrValue as value, _) ->
                SdkMapping.tryStructural (SdkMapping.valueKey value)
            | _ -> None

        match structural, arguments with
        | Some operator, [ left, _; right, _ ] ->
            match named with
            | Some(:? FSharpMemberOrFunctionOrValue as value, _) ->
                this.Structural(expression.Range, value, operator, left, right)
            | _ -> unsupported expression.Range "This operator has no checked symbol."
        | Some _, _ ->
            fail
                FSharpCodes.unsupportedReference
                expression.Range
                "A pipe or composition operator is lowered only with both of its operands."
        | None, _ -> this.Apply(expression, head, named, arguments)

    /// <summary>
    /// Lowers <c>x |&gt; f</c> and <c>f &lt;| x</c> as <c>f x</c>, <c>f &gt;&gt; g</c> and
    /// <c>g &lt;&lt; f</c> as <c>fun x -&gt; g (f x)</c>, with the types of the compiler's operator call, and
    /// <c>a &amp;&amp; b</c> and <c>a || b</c> as if-then-else, which keeps short-circuit evaluation.
    /// </summary>
    member private this.Structural
        (
            range: range,
            operatorValue: FSharpMemberOrFunctionOrValue,
            operator: StructuralOperator,
            left: SynExpr,
            right: SynExpr
        ) : Checked<'Value> =
        let at = locate range

        let operatorType =
            match operator with
            // The compiler gives no call for && and ||: they are already if-then-else. They are not generic.
            | StructuralOperator.BooleanAnd
            | StructuralOperator.BooleanOr -> Some operatorValue.FullType
            | _ ->
                typed.At(document.Index, range)
                |> List.tryPick (fun expression ->
                    match expression with
                    | Call(_, value, classTypes, methodTypes, _) when
                        value.IsEffectivelySameAs operatorValue
                        ->
                        instantiate
                            value.FullType
                            value.GenericParameters
                            (classTypes
                             @ methodTypes)
                    | _ -> None
                )

        let parts =
            operatorType
            |> Option.bind (fun full -> (LoweredType.Checked full).TryPeel())
            |> Option.bind (fun (leftType, rest) ->
                rest.TryPeel()
                |> Option.map (fun (rightType, resultType) -> leftType, rightType, resultType)
            )

        let apply (func: SynExpr) funcType (argument: SynExpr) argumentType resultType = check {
            let! resultValueType = lowerTy range resultType
            and! funcValue = this.Expression(func, Some funcType)
            and! argumentValue = this.Expression(argument, Some argumentType)
            return! builder.Apply(at, resultValueType, funcValue, argumentValue)
        }

        let compose
            (first: SynExpr)
            firstType
            (second: SynExpr)
            secondType
            (resultType: LoweredType)
            =
            match
                resultType.TryPeel(),
                firstType
                |> fun (ty: LoweredType) -> ty.TryPeel()
            with
            | Some(argumentType, finalType), Some(_, middleType) ->
                let argument = freshNames ()

                check {
                    let! functionType = lowerTy range resultType
                    and! argumentValueType = lowerTy range argumentType
                    and! middleValueType = lowerTy range middleType
                    and! finalValueType = lowerTy range finalType
                    and! firstValue = this.Expression(first, Some firstType)
                    and! secondValue = this.Expression(second, Some secondType)
                    let! wildcard = builder.PatternWildcard(at, argumentValueType)
                    let! parameter = builder.PatternAs(at, argumentValueType, wildcard, argument)
                    let! variable = builder.Variable(at, argumentValueType, argument)
                    let! inner = builder.Apply(at, middleValueType, firstValue, variable)
                    let! outer = builder.Apply(at, finalValueType, secondValue, inner)
                    return! builder.Lambda(at, functionType, parameter, outer)
                }
            | _ -> noType range

        // a && b is if a then b else false; a || b is if a then true else b.
        let condition (first: SynExpr) firstType (second: SynExpr) secondType resultType constant = check {
            let! resultValueType = lowerTy range resultType
            and! firstValue = this.Expression(first, Some firstType)
            and! secondValue = this.Expression(second, Some secondType)
            let! constantValue = builder.Literal(at, resultValueType, LiteralValue.Bool constant)

            let thenValue, elseValue =
                if constant then
                    constantValue, secondValue
                else
                    secondValue, constantValue

            return! builder.IfThenElse(at, resultValueType, firstValue, thenValue, elseValue)
        }

        match parts with
        | None -> noType range
        | Some(leftType, rightType, resultType) ->
            match operator with
            | StructuralOperator.PipeRight -> apply right rightType left leftType resultType
            | StructuralOperator.PipeLeft -> apply left leftType right rightType resultType
            | StructuralOperator.ComposeRight -> compose left leftType right rightType resultType
            | StructuralOperator.ComposeLeft -> compose right rightType left leftType resultType
            | StructuralOperator.BooleanAnd ->
                condition left leftType right rightType resultType false
            | StructuralOperator.BooleanOr ->
                condition left leftType right rightType resultType true

    /// <summary>
    /// Lowers an application spine <c>f a b</c> as nested applications. The function type comes from the
    /// compiler's call or application at the spine's range, or from the function's checked type.
    /// </summary>
    member private this.Apply
        (
            expression: SynExpr,
            head: SynExpr,
            named: (FSharpSymbol * range) option,
            arguments: (SynExpr * range) list
        ) =
        // A union case with several fields takes them as one tuple in F# syntax, but its Morphir constructor
        // takes them one by one. A case with one tuple-valued field keeps the tuple. 'x :: xs' is a case
        // with two fields applied to a tuple.
        let arguments =
            let caseFields =
                match named with
                | Some(:? FSharpUnionCase as case, _) -> Some case.Fields.Count
                | _ -> None

            let rec unparen expression =
                match expression with
                | SynExpr.Paren(expr = inner) -> unparen inner
                | other -> other

            match caseFields, arguments with
            | Some count, [ argument, range ] when count > 1 ->
                match unparen argument with
                | SynExpr.Tuple(isStruct = false; exprs = elements) when elements.Length = count ->
                    elements
                    |> List.map (fun element -> element, Range.unionRanges head.Range element.Range)
                | _ -> [ argument, range ]
            | _ -> arguments

        let headValue =
            match named with
            | Some(symbol, symbolRange) ->
                let referenceRange =
                    match head with
                    | SynExpr.LongIdent _ -> head.Range
                    | _ -> symbolRange

                match this.Head(symbolRange, expression.Range, arguments.Length, None) with
                | _, Some ty ->
                    this.Reference(referenceRange, symbol, ty, expression.Range)
                    |> Checked.map (fun value -> value, ty)
                | _, None -> noType referenceRange
            | None ->
                match head with
                | SynExpr.Ident ident ->
                    unsupported ident.idRange "This name does not refer to a value."
                | other ->
                    this.Expression(other, None)
                    |> Checked.bind (fun value ->
                        match this.TypeOfValue other with
                        | Some ty -> Checked.pass (value, ty)
                        | None -> noType other.Range
                    )

        arguments
        |> List.fold
            (fun (state: Checked<'Value * LoweredType>) (argument, range) ->
                state
                |> Checked.bind (fun (func, funcType) ->
                    match funcType.TryPeel() with
                    | None -> unsupported range "This value is not a function."
                    | Some(parameterType, resultType) -> check {
                        let! argumentValue = this.Expression(argument, Some parameterType)
                        let! resultValueType = lowerTy range resultType

                        let! applied =
                            builder.Apply(locate range, resultValueType, func, argumentValue)

                        return applied, resultType
                      }
                )
            )
            headValue
        |> Checked.map fst

    /// <summary>Lowers <c>fun p1 p2 -> body</c> as one lambda for each parameter pattern.</summary>
    member private this.Lambda
        (
            range: range,
            parameters: SynPat list,
            body: SynExpr,
            expected: LoweredType option
        ) =
        match typeOf range expected with
        | None -> noType range
        | Some lambdaType ->
            let rec build (ty: LoweredType) (parameters: SynPat list) : Checked<'Value> =
                match parameters with
                | [] -> this.Expression(body, Some ty)
                | parameter :: rest ->
                    match ty.TryPeel() with
                    | None -> unsupported range "This lambda has no checked function type."
                    | Some(parameterType, resultType) -> check {
                        let! functionType = lowerTy range ty
                        and! pattern = this.ParameterPattern(parameter, parameterType)
                        and! inner = build resultType rest
                        return! builder.Lambda(locate range, functionType, pattern, inner)
                      }

            build lambdaType parameters

    /// <summary>
    /// Lowers a value definition: leading parameters that are names become inputs; the other parameters
    /// become lambdas around the body, so every supported parameter pattern is kept.
    /// </summary>
    member this.Definition(binding: SynBinding, valueType: FSharpType) : Checked<'ValueDefinition> =
        let (SynBinding(headPat = head; expr = body; range = range)) = binding

        let parameters =
            let rec arguments pattern =
                match pattern with
                | SynPat.LongIdent(argPats = SynArgPats.Pats arguments) -> arguments
                | SynPat.Paren(pat = inner)
                | SynPat.Typed(pat = inner)
                | SynPat.Attrib(pat = inner) -> arguments inner
                | _ -> []

            arguments head

        let rec simpleName pattern =
            match pattern with
            | SynPat.Named(ident = SynIdent(ident, _)) -> Some ident
            | SynPat.Paren(pat = inner)
            | SynPat.Typed(pat = inner) -> simpleName inner
            | _ -> None

        let inputs =
            parameters
            |> List.takeWhile (fun parameter -> (simpleName parameter).IsSome)

        let patterns = List.skip inputs.Length parameters

        let rec peelInputs (ty: LoweredType) (remaining: SynPat list) acc =
            match remaining with
            | [] -> Checked.pass (List.rev acc, ty)
            | parameter :: rest ->
                match ty.TryPeel(), simpleName parameter with
                | Some(parameterType, resultType), Some ident ->
                    lowerTy parameter.Range parameterType
                    |> Checked.bind (fun inputType ->
                        peelInputs
                            resultType
                            rest
                            ((Name.fromString ident.idText, inputType)
                             :: acc)
                    )
                | _ -> unsupported parameter.Range "This parameter has no checked type."

        check {
            let! inputs, outputType = peelInputs (LoweredType.Checked valueType) inputs []
            let! output = lowerTy range outputType

            let! bodyValue =
                if patterns.IsEmpty then
                    this.Expression(body, Some outputType)
                else
                    this.Lambda(
                        Range.unionRanges patterns.Head.Range body.Range,
                        patterns,
                        body,
                        Some outputType
                    )

            return! builder.ValueDefinition(locate range, inputs, output, bodyValue)
        }

    member private this.Let
        (
            range: range,
            isRecursive: bool,
            bindings: SynBinding list,
            body: SynExpr,
            expected: LoweredType option
        ) : Checked<'Value> =
        let at = locate range

        let bound (binding: SynBinding) =
            let (SynBinding(headPat = head)) = binding

            let rec name pattern =
                match pattern with
                | SynPat.Named(ident = SynIdent(ident, _)) -> Some ident
                | SynPat.LongIdent(longDotId = SynLongIdent(id = [ ident ])) -> Some ident
                | SynPat.Paren(pat = inner)
                | SynPat.Typed(pat = inner) -> name inner
                | _ -> None

            match name head with
            | Some ident ->
                symbolsAt ident.idRange
                |> List.tryPick (fun symbol ->
                    match symbol with
                    | :? FSharpMemberOrFunctionOrValue as value -> Some(ident, value)
                    | _ -> None
                )
            | None -> None

        let definitions =
            bindings
            |> Checked.traverse (fun binding ->
                match bound binding with
                | Some(ident, value) ->
                    this.Definition(binding, value.FullType)
                    |> Checked.map (fun definition ->
                        Choice1Of2(Name.fromString ident.idText, definition)
                    )
                | None ->
                    let (SynBinding(headPat = pattern; expr = valueExpression)) = binding

                    match this.TypeOfValue valueExpression with
                    | None -> noType valueExpression.Range
                    | Some valueType -> check {
                        let! destructured = this.ParameterPattern(pattern, valueType)
                        and! value = this.Expression(valueExpression, Some valueType)
                        return Choice2Of2(destructured, value)
                      }
            )

        match typeOf range (typeOf body.Range expected) with
        | None -> noType range
        | Some ty -> check {
            let! resultType = lowerTy range ty
            let! lowered = definitions
            let! inner = this.Expression(body, Some ty)

            if isRecursive then
                let named =
                    lowered
                    |> List.choose (fun item ->
                        match item with
                        | Choice1Of2 definition -> Some definition
                        | Choice2Of2 _ -> None
                    )

                if
                    named.Length
                    <> lowered.Length
                then
                    return! unsupported range "A recursive let binds names only."
                else
                    return! builder.LetRecursion(at, resultType, named, inner)
            else
                return!
                    List.foldBack
                        (fun item (state: Checked<'Value>) ->
                            state
                            |> Checked.bind (fun inValue ->
                                match item with
                                | Choice1Of2(name, definition) ->
                                    builder.LetDefinition(
                                        at,
                                        resultType,
                                        name,
                                        definition,
                                        inValue
                                    )
                                | Choice2Of2(pattern, value) ->
                                    builder.Destructure(at, resultType, pattern, value, inValue)
                            )
                        )
                        lowered
                        (Checked.pass inner)
          }

/// <summary>The lowering of whole modules and the package.</summary>
[<RequireQualifiedAccess>]
module internal PackageLowerings =

    /// <summary>
    /// Refuses two different local names in one value definition that give the same Morphir name, such as
    /// <c>fooBar</c> and <c>foo_bar</c>: Morphir variables are names, so one could capture the other.
    /// Shadowing with the same F# name keeps F# scoping and is allowed.
    /// </summary>
    let localCollisions
        (evidence: CheckedEvidence)
        (document: CheckedDocument)
        (binding: range)
        : Checked<unit> =
        let locals =
            evidence.Original.Uses
            |> Array.filter (fun symbolUse ->
                symbolUse.IsFromDefinition
                && String.Equals(symbolUse.Range.FileName, document.Path, StringComparison.Ordinal)
                && Range.rangeContainsRange binding symbolUse.Range
            )
            |> Array.choose (fun symbolUse ->
                match symbolUse.Symbol with
                | :? FSharpMemberOrFunctionOrValue as value when not value.IsModuleValueOrMember ->
                    Some(value.DisplayName, symbolUse.Range)
                | _ -> None
            )
            |> Array.sortBy (fun (_, range) -> struct (range.StartLine, range.StartColumn))

        let first = Dictionary<Name, string * range>()

        let diagnostics = [
            for text, range in locals do
                let name = Name.fromString text

                match first.TryGetValue name with
                | true, (firstText, firstRange) when
                    firstText
                    <> text
                    ->
                    let words = String.Join(" ", Name.toList name)

                    Diagnostic.error
                        FSharpCodes.nameCollision
                        $"'{text}' and '{firstText}' have the same Morphir name ({words}) in one value definition. Rename one of them."
                    |> Diagnostic.at (Location.Create(document.Uri, CompilerEdge.rangeOf range))
                    |> Diagnostic.withRelated
                        {
                            Location =
                                Location.Create(document.Uri, CompilerEdge.rangeOf firstRange)
                            Message = $"'{firstText}' is declared here."
                        }
                | true, _ -> ()
                | _ -> first[name] <- (text, range)
        ]

        Checked.ofDiagnostics () diagnostics

    /// <summary>Lowers one planned module: its types, then its values.</summary>
    let lowerModule
        (builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>)
        (plan: FrontendPlan)
        (evidence: CheckedEvidence)
        (typed: TypedIndex)
        (planned: PlannedModule)
        : Checked<'Module> =
        let types =
            planned.Types
            |> Checked.traverse (fun plannedType ->
                Lowering.lowerTypeDefinition builder plan plannedType
                |> Checked.map (fun definition -> {
                    Name = plannedType.Name
                    Access = plannedType.Access
                    Documentation = plannedType.Documentation
                    Definition = definition
                    Location = plannedType.Location
                })
            )

        let values =
            planned.Values
            |> Checked.traverse (fun plannedValue ->
                let document =
                    evidence.Original.Documents
                    |> List.find (fun document -> document.Uri = plannedValue.Location.Uri)

                // The binding's own range covers only its head; the check needs the body too.
                localCollisions evidence document plannedValue.Syntax.RangeOfBindingWithRhs
                |> Checked.bind (fun () ->
                    ValueLowering(builder, plan, evidence, typed, document)
                        .Definition(plannedValue.Syntax, plannedValue.Symbol.FullType)
                )
                |> Checked.map (fun definition -> {
                    Name = plannedValue.Name
                    Access = plannedValue.Access
                    Documentation = plannedValue.Documentation
                    Definition = definition
                    Location = plannedValue.Location
                })
            )

        check {
            let! types = types
            and! values = values
            return! builder.Module(planned.Plan, types, values)
        }

    /// <summary>Lowers the whole planned package with one builder.</summary>
    let lowerPackage
        (builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>)
        (plan: FrontendPlan)
        (evidence: CheckedEvidence)
        : Checked<'Package> =
        let typed = TypedIndex evidence

        plan.Modules
        |> Checked.traverse (lowerModule builder plan evidence typed)
        |> Checked.bind (fun modules -> builder.Package(plan.Package, modules))

/// <summary>The lowering of a planned package as a builder program, for any registered IR family.</summary>
[<Sealed>]
type internal PackageLowering(plan: FrontendPlan, evidence: CheckedEvidence) =
    interface IBuilderProgram with
        member _.Run(builder) =
            PackageLowerings.lowerPackage builder plan evidence
