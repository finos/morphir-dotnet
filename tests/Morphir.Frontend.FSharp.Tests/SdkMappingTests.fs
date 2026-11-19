// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.SdkMappingTests

open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open FSharp.Compiler.Symbols
open Morphir.CodeGeneration.FSharp
open Morphir.Frontend.FSharp.Analysis
open Morphir.Frontend.FSharp.Tests.AnalysisSupport
open Morphir.IR

/// One operation of the reviewed SDK inventory: its Morphir name and its parameter count.
type private InventoryOperation = { Target: FQName; Arity: int }

let private sdkName (text: string) =
    // "Morphir.SDK.Basics.lessThan" is module Basics, local name lessThan.
    match text.Split '.' with
    | [| "Morphir"; "SDK"; moduleName; localName |] -> SdkMapping.sdk moduleName localName
    | _ -> failtestf "Not an SDK operation name: %s" text

let private inventory =
    let path =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "MorphirSDK", "inventory.json")

    use document = JsonDocument.Parse(File.ReadAllText path)

    document.RootElement.GetProperty("operations").EnumerateArray()
    |> Seq.map (fun operation -> {
        Target = sdkName (operation.GetProperty("operation").GetString())
        Arity = operation.GetProperty("parameters").GetArrayLength()
    })
    |> List.ofSeq

/// The SDK types that the inventory signatures use, in the inventory's type vocabulary.
let private inventoryTypes =
    let vocabulary =
        Map.ofList [
            "Int", SdkMapping.sdk "Basics" "Int"
            "Float", SdkMapping.sdk "Basics" "Float"
            "Bool", SdkMapping.sdk "Basics" "Bool"
            "Order", SdkMapping.sdk "Basics" "Order"
            "Decimal", SdkMapping.sdk "Decimal" "Decimal"
            "String", SdkMapping.sdk "String" "String"
            "Char", SdkMapping.sdk "Char" "Char"
            "List", SdkMapping.sdk "List" "List"
            "Maybe", SdkMapping.sdk "Maybe" "Maybe"
            "Result", SdkMapping.sdk "Result" "Result"
            "Dict", SdkMapping.sdk "Dict" "Dict"
            "Set", SdkMapping.sdk "Set" "Set"
        ]

    let path =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "MorphirSDK", "inventory.json")

    use document = JsonDocument.Parse(File.ReadAllText path)

    document.RootElement.GetProperty("operations").EnumerateArray()
    |> Seq.collect (fun operation ->
        let parameters =
            operation.GetProperty("parameters").EnumerateArray()
            |> Seq.map (fun parameter -> parameter.GetProperty("type").GetString())

        Seq.append parameters [ operation.GetProperty("result").GetString() ]
    )
    |> Seq.collect (fun signature -> Regex.Matches(signature, "[A-Z][A-Za-z0-9]*"))
    |> Seq.choose (fun token -> Map.tryFind token.Value vocabulary)
    |> Set.ofSeq

let private findOperation target =
    inventory
    |> List.tryFind (fun operation -> operation.Target = target)

let private name (target: FQName) = FQName.toString target

let private expectTier (target: FQName) (arity: int option) (tier: SdkTier) =
    match tier, findOperation target, arity with
    | SdkTier.Contract, Some operation, Some arity ->
        Expect.equal operation.Arity arity $"{name target} has the inventory's parameter count"
    | SdkTier.Contract, Some _, None -> ()
    | SdkTier.Contract, None, _ ->
        failtestf
            "%s is marked as contract, but the SDK inventory has no such operation"
            (name target)
    | SdkTier.Outside reason, found, _ ->
        Expect.isNone found $"{name target} is in the SDK inventory, so it must be a contract entry"
        Expect.isNotEmpty reason $"{name target} names why it is outside the SDK contract"

/// An operand type that an entry accepts, to resolve it again from a compiled F# symbol.
let private representative (rule: OperandRule) =
    match rule with
    | OperandRule.Any -> None
    | OperandRule.OneOf(first :: _) -> Some first
    | OperandRule.OneOf [] -> None
    | OperandRule.Comparable -> Some SdkOperand.Int

/// FSharp.Core and the runtime assemblies that a probe document is checked against.
let private probe (lines: string list) =
    let text =
        [ "namespace Acme.Probe"; "module Probe =" ]
        @ (lines
           |> List.map (fun line -> $"    {line}"))
        |> String.concat "\n"

    passed (
        analyze [
            document
                "Probe.fs"
                (text
                 + "\n")
        ]
    )

/// The symbols used on a line of a probe document. The first probe line is line 3.
let private usesOn (evidence: CheckedEvidence) (line: int) =
    evidence.Project.Uses
    |> Array.filter (fun symbolUse ->
        symbolUse.Range.StartLine = line
        && not symbolUse.IsFromDefinition
    )
    |> Array.map (fun symbolUse -> symbolUse.Symbol)
    |> List.ofArray

let rec private entities (entity: FSharpEntity) = seq {
    yield entity

    for nested in entity.NestedEntities do
        yield! entities nested
}

/// Every entity of FSharp.Core, by full name.
let private fsharpCore =
    lazy
        (let evidence = probe [ "let probe = 1" ]

         let core =
             evidence.Project.Results.ProjectContext.GetReferencedAssemblies()
             |> List.find (fun assembly -> assembly.SimpleName = "FSharp.Core")

         core.Contents.Entities
         |> Seq.collect entities
         |> Seq.choose (fun entity ->
             entity.TryFullName
             |> Option.map (fun fullName -> fullName, entity)
         )
         |> Map.ofSeq)

let private splitKey (key: string) =
    let index = key.LastIndexOf '.'
    key.Substring(0, index), key.Substring(index + 1)

[<Tests>]
let tests =
    testList "F# SDK mapping table" [
        test
            "every value entry is an inventory operation of the same arity, or names why it is outside" {
            for entry in SdkMapping.values do
                expectTier entry.Target (Some entry.Arity) entry.Tier
        }
        |> testLabel "PlatformSpecific"

        test
            "every union case entry is an inventory operation of the same arity, or names why it is outside" {
            for entry in SdkMapping.cases do
                expectTier entry.Target (Some entry.Arity) entry.Tier
        }
        |> testLabel "PlatformSpecific"

        test "every type entry is used by the inventory signatures, or names why it is outside" {
            for entry in SdkMapping.types do
                match entry.Tier with
                | SdkTier.Contract ->
                    Expect.isTrue
                        (inventoryTypes.Contains entry.Target)
                        $"{name entry.Target} is marked as contract, but no inventory signature uses it"
                | SdkTier.Outside reason ->
                    Expect.isFalse
                        (inventoryTypes.Contains entry.Target)
                        $"{name entry.Target} is used by the inventory, so it must be a contract entry"

                    Expect.isNotEmpty
                        reason
                        $"{name entry.Target} names why it is outside the SDK contract"
        }
        |> testLabel "PlatformSpecific"

        test "no F# symbol and operand type selects two value entries" {
            let operands = [
                None
                Some SdkOperand.Int
                Some SdkOperand.Float
                Some SdkOperand.Decimal
                Some SdkOperand.String
                Some SdkOperand.Char
                Some SdkOperand.Bool
                Some(SdkOperand.List SdkOperand.Int)
                Some(SdkOperand.Other "int64")
            ]

            for key in
                SdkMapping.values
                |> List.map (fun entry -> entry.Symbol)
                |> List.distinct do
                for operand in operands do
                    let selected =
                        SdkMapping.values
                        |> List.filter (fun entry ->
                            entry.Symbol = key
                            && entry.Operands.Accepts operand
                        )

                    Expect.isLessThanOrEqual
                        selected.Length
                        1
                        $"{key} with {operand} selects one entry"
        }

        test "every entry names an FSharp.Core symbol of the entry's arity" {
            let core = fsharpCore.Force()

            for entry in SdkMapping.values do
                let entityName, compiledName = splitKey entry.Symbol

                match Map.tryFind entityName core with
                | None -> failtestf "FSharp.Core has no entity %s" entityName
                | Some entity ->
                    let found =
                        entity.MembersFunctionsAndValues
                        |> Seq.tryFind (fun value -> value.CompiledName = compiledName)

                    match found with
                    | None -> failtestf "FSharp.Core has no value %s" entry.Symbol
                    | Some value ->
                        Expect.equal
                            value.CurriedParameterGroups.Count
                            entry.Arity
                            $"{entry.Symbol} has the entry's arity"

            for entry in SdkMapping.cases do
                let entityName, caseName = splitKey entry.Case

                match Map.tryFind entityName core with
                | None -> failtestf "FSharp.Core has no union %s" entityName
                | Some entity ->
                    match
                        entity.UnionCases
                        |> Seq.tryFind (fun case -> case.Name = caseName)
                    with
                    | None -> failtestf "FSharp.Core has no union case %s" entry.Case
                    | Some case ->
                        Expect.equal
                            case.Fields.Count
                            entry.Arity
                            $"{entry.Case} has the entry's arity"

            for key, _ in SdkMapping.structural do
                let entityName, compiledName = splitKey key

                Expect.isTrue
                    (core[entityName].MembersFunctionsAndValues
                     |> Seq.exists (fun value -> value.CompiledName = compiledName))
                    $"FSharp.Core has the structural operator {key}"
        }
        |> testLabel "PlatformSpecific"

        test "every value entry's F# type uses only types that lower to Morphir" {
            let core = fsharpCore.Force()

            let rec mapped (typ: FSharpType) =
                let typ = typ.StripAbbreviations()

                let ownType =
                    typ.IsGenericParameter
                    || typ.IsFunctionType
                    || typ.IsTupleType
                    || (typ.HasTypeDefinition
                        && (typ.TypeDefinition.TryFullName = Some "Microsoft.FSharp.Core.Unit"
                            || (SdkMapping.tryType typ.TypeDefinition.FullName).IsSome))

                typ.IsGenericParameter
                || ownType
                   && (typ.GenericArguments
                       |> Seq.forall mapped)

            for entry in SdkMapping.values do
                let entityName, compiledName = splitKey entry.Symbol

                let value =
                    core[entityName].MembersFunctionsAndValues
                    |> Seq.find (fun value -> value.CompiledName = compiledName)

                Expect.isTrue
                    (mapped value.FullType)
                    $"{entry.Symbol}: {value.FullType.Format FSharpDisplayContext.Empty} lowers to Morphir"
        }
        |> testLabel "PlatformSpecific"

        test
            "the F# generator writes every value and case entry as F# that maps back to the same SDK name" {
            let spelling target =
                match TargetSyntax.sdkValue (Emit.sdkName target) with
                | Some text -> text
                | None -> failtestf "The F# generator has no spelling for %s" (name target)

            let probes = [
                for entry in SdkMapping.values do
                    name entry.Target,
                    spelling entry.Target,
                    entry.Target,
                    representative entry.Operands
                for entry in SdkMapping.cases do
                    let text =
                        match entry.Kind with
                        | SdkCaseKind.Constructor ->
                            match TargetSyntax.sdkConstructor (Emit.sdkName entry.Target) with
                            | Some(text, arity) ->
                                Expect.equal
                                    arity
                                    entry.Arity
                                    $"{name entry.Target} constructor arity"

                                text
                            | None ->
                                failtestf
                                    "The F# generator has no constructor for %s"
                                    (name entry.Target)
                        | SdkCaseKind.Function -> spelling entry.Target

                    name entry.Target, text, entry.Target, None
            ]

            let evidence =
                probe (
                    probes
                    |> List.mapi (fun index (_, text, _, _) -> $"let probe{index} () = {text}")
                )

            probes
            |> List.iteri (fun index (label, text, target, operand) ->
                let resolved =
                    usesOn evidence (index + 3)
                    |> List.choose (fun symbol ->
                        match symbol with
                        | :? FSharpMemberOrFunctionOrValue as value ->
                            SdkMapping.tryValue (SdkMapping.valueKey value) operand
                            |> Option.map (fun entry -> entry.Target)
                        | :? FSharpUnionCase as case ->
                            SdkMapping.tryCase (SdkMapping.caseKey case)
                            |> Option.map (fun entry -> entry.Target)
                        | _ -> None
                    )

                Expect.contains resolved target $"{label}: the generator's '{text}' maps back"
            )
        }
        |> testLabel "PlatformSpecific"

        test
            "the F# generator writes every type entry as an F# type that maps back to the same SDK type" {
            let probes = [
                for entry in SdkMapping.types do
                    let arguments = List.replicate entry.ArgumentOrder.Length "int"

                    match TargetSyntax.sdkType (Emit.sdkName entry.Target) arguments with
                    | Some text -> entry, text
                    | None -> failtestf "The F# generator has no type for %s" (name entry.Target)
            ]

            let evidence =
                probe (
                    probes
                    |> List.mapi (fun index (_, text) -> $"type Probe{index} = {text}")
                )

            probes
            |> List.iteri (fun index (entry, text) ->
                let abbreviation =
                    evidence.Project.Uses
                    |> Array.tryPick (fun symbolUse ->
                        match symbolUse.Symbol with
                        | :? FSharpEntity as entity when
                            symbolUse.IsFromDefinition
                            && entity.DisplayName = $"Probe{index}"
                            ->
                            Some entity
                        | _ -> None
                    )

                match abbreviation with
                | None -> failtestf "The probe for %s was not checked" (name entry.Target)
                | Some entity ->
                    let definition = entity.AbbreviatedType.StripAbbreviations().TypeDefinition

                    match SdkMapping.tryType definition.FullName with
                    | Some found ->
                        Expect.equal
                            found.Target
                            entry.Target
                            $"the generator's '{text}' maps back"
                    | None -> failtestf "The generator's '%s' maps to no SDK type" text
            )
        }
        |> testLabel "PlatformSpecific"

        test "operand shapes follow the SDK comparable profile" {
            Expect.isTrue
                (SdkOperand.List(SdkOperand.Tuple [ SdkOperand.Int; SdkOperand.String ]))
                    .IsComparable
                "lists of tuples"

            Expect.isFalse SdkOperand.Decimal.IsComparable "Decimal compares with Decimal.compare"
            Expect.isFalse SdkOperand.Bool.IsComparable "Bool is not comparable"

            Expect.isFalse
                (SdkOperand.Other "'a").IsComparable
                "a generic operand is not known to be comparable"
        }
    ]
