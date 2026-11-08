// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open FSharp.Compiler.Symbols
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.Frontend.FSharp
open Morphir.IR

/// <summary>
/// Lowering (FSF-1): the single lowering of a planned package, generic over <c>IIrBuilder</c>, so every IR
/// family builds natively from the same code (MID-1). Task 3.5 lowers type definitions; task 3.6 adds values.
/// </summary>
[<RequireQualifiedAccess>]
module internal Lowering =

    let private fullName (entity: FSharpEntity) =
        match entity.TryFullName with
        | Some name -> name
        | None -> $"{entity.AccessPath}.{entity.CompiledName}"

    let private unsupportedReference (location: Location) (name: string) =
        Checked.fail [
            Diagnostic.error
                FSharpCodes.unsupportedReference
                $"The F# type '{name}' has no Morphir mapping. Use a type of this package or a mapped Morphir SDK type."
            |> Diagnostic.at location
        ]

    /// <summary>Lowers a checked F# type. Every node takes the location of the nearest declaration.</summary>
    let rec lowerType
        (builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>)
        (plan: FrontendPlan)
        (location: Location)
        (typ: FSharpType)
        : Checked<'Type> =
        let lower = lowerType builder plan location

        let arguments () =
            typ.GenericArguments
            |> List.ofSeq
            |> Checked.traverse lower

        // A type of this package, including an abbreviation, is a reference. This check comes first: FCS
        // answers IsTupleType and IsFunctionType through abbreviations, so 'Pair<int>' would look like a tuple.
        let packageType =
            if
                typ.IsAbbreviation
                || typ.HasTypeDefinition
            then
                try
                    plan.TryType typ.TypeDefinition
                with _ ->
                    None
            else
                None

        match packageType with
        | Some fqName ->
            arguments ()
            |> Checked.bind (fun arguments -> builder.TypeReference(location, fqName, arguments))
        | None when typ.IsAbbreviation -> lower typ.AbbreviatedType
        | None when typ.IsGenericParameter ->
            builder.TypeVariable(location, Name.fromString typ.GenericParameter.Name)
        | None when typ.IsFunctionType -> check {
            let! argument = lower typ.GenericArguments[0]
            and! result = lower typ.GenericArguments[1]
            return! builder.TypeFunction(location, argument, result)
          }
        | None when
            typ.IsTupleType
            && not typ.IsStructTupleType
            ->
            arguments ()
            |> Checked.bind (fun elements -> builder.TypeTuple(location, elements))
        | None when typ.HasTypeDefinition ->
            let definition = typ.TypeDefinition

            if fullName definition = "Microsoft.FSharp.Core.Unit" then
                builder.TypeUnit location
            else
                match SdkMapping.tryType (fullName definition) with
                | Some entry ->
                    arguments ()
                    |> Checked.bind (fun arguments ->
                        match SdkMapping.orderArguments entry arguments with
                        | Some ordered -> builder.TypeReference(location, entry.Target, ordered)
                        | None -> unsupportedReference location (fullName definition)
                    )
                | None -> unsupportedReference location (fullName definition)
        | None -> unsupportedReference location (typ.Format FSharpDisplayContext.Empty)

    let private locate (location: Location) (range: range) =
        Location.Create(location.Uri, CompilerEdge.rangeOf range)

    let private parameters (entity: FSharpEntity) =
        entity.GenericParameters
        |> Seq.map (fun parameter -> Name.fromString parameter.Name)
        |> List.ofSeq

    let private argumentName index (field: FSharpField) =
        if field.IsNameGenerated then
            Name [ "arg"; string (index + 1) ]
        else
            Name.fromString field.Name

    /// <summary>Lowers one planned type definition.</summary>
    let lowerTypeDefinition
        (builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>)
        (plan: FrontendPlan)
        (planned: PlannedType)
        : Checked<'TypeDefinition> =
        let entity = planned.Entity
        let at = planned.Location
        let (SynTypeDefn(typeRepr = representation)) = planned.Syntax

        match representation with
        | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.TypeAbbrev(rhsType = rhs)) ->
            lowerType builder plan (locate at rhs.Range) entity.AbbreviatedType
            |> Checked.bind (fun body -> builder.TypeAlias(at, parameters entity, body))

        | SynTypeDefnRepr.Simple(simpleRepr = SynTypeDefnSimpleRepr.Record(recordFields = fields)) ->
            let ranges =
                fields
                |> List.map (fun (SynField(range = range)) -> range)

            entity.FSharpFields
            |> List.ofSeq
            |> List.mapi (fun index field ->
                let location =
                    match List.tryItem index ranges with
                    | Some range -> locate at range
                    | None -> at

                let name =
                    plan.TryField(entity, field)
                    |> Option.defaultValue (Name.fromString field.Name)

                lowerType builder plan location field.FieldType
                |> Checked.map (fun fieldType -> name, fieldType)
            )
            |> Checked.sequence
            |> Checked.bind (fun fields -> builder.TypeRecord(at, fields))
            |> Checked.bind (fun body -> builder.TypeAlias(at, parameters entity, body))

        | SynTypeDefnRepr.Simple(
            simpleRepr = SynTypeDefnSimpleRepr.Union(accessibility = access; unionCases = cases)) ->
            let constructorAccess =
                match access with
                | Some(SynAccess.Private _)
                | Some(SynAccess.Internal _) -> Access.Private
                | _ -> Access.Public

            let ranges =
                cases
                |> List.map (fun (SynUnionCase(range = range)) -> range)

            entity.UnionCases
            |> List.ofSeq
            |> List.mapi (fun caseIndex case ->
                let location =
                    match List.tryItem caseIndex ranges with
                    | Some range -> locate at range
                    | None -> at

                let name =
                    match plan.TryConstructor case with
                    | Some fqName -> fqName.LocalName
                    | None -> Name.fromString case.Name

                case.Fields
                |> List.ofSeq
                |> List.mapi (fun index field ->
                    lowerType builder plan location field.FieldType
                    |> Checked.map (fun fieldType -> argumentName index field, fieldType)
                )
                |> Checked.sequence
                |> Checked.map (fun arguments ->
                    { Name = name; Arguments = arguments }: ConstructorPlan<'Type>
                )
            )
            |> Checked.sequence
            |> Checked.bind (fun constructors ->
                builder.CustomType(at, parameters entity, constructorAccess, constructors)
            )

        | _ ->
            Checked.fail [
                Diagnostic.error
                    (DiagnosticCode.Create "fsharp.unsupported_declaration")
                    $"The type '{entity.DisplayName}' is not a record, union or type abbreviation."
                |> Diagnostic.at at
            ]
