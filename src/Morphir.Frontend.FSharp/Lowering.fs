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
/// The Morphir SDK types that F# types map to. Task 3.7 folds this table into the SDK mapping table (FSF-4).
/// </summary>
[<RequireQualifiedAccess>]
module internal SdkTypes =

    /// <summary>The Morphir SDK package, <c>morphir/SDK</c>.</summary>
    let package = PackageName(Path [ Name [ "morphir" ]; Name [ "s"; "d"; "k" ] ])

    let private sdk (moduleName: string) (localName: string) = {
        PackagePath = package
        ModulePath = ModulePath(Path [ Name.fromString moduleName ])
        LocalName = Name.fromString localName
    }

    /// <summary>
    /// The SDK type and the order of its arguments for an F# type definition, by full name. F# <c>Result</c>
    /// is <c>Result&lt;'T, 'TError&gt;</c>; the SDK <c>Result</c> takes the error type first.
    /// </summary>
    let tryFind (fullName: string) : (FQName * ('a list -> 'a list)) option =
        match fullName with
        | "System.Int32" -> Some(sdk "basics" "int", id)
        | "System.Int64" -> Some(sdk "int" "int64", id)
        | "System.Boolean" -> Some(sdk "basics" "bool", id)
        | "System.Double" -> Some(sdk "basics" "float", id)
        | "System.Decimal" -> Some(sdk "decimal" "decimal", id)
        | "System.Char" -> Some(sdk "char" "char", id)
        | "System.String" -> Some(sdk "string" "string", id)
        | "Microsoft.FSharp.Core.FSharpOption`1" -> Some(sdk "maybe" "maybe", id)
        | "Microsoft.FSharp.Collections.FSharpList`1" -> Some(sdk "list" "list", id)
        | "Microsoft.FSharp.Core.FSharpResult`2" ->
            Some(
                sdk "result" "result",
                fun arguments ->
                    match arguments with
                    | [ success; error ] -> [ error; success ]
                    | other -> other
            )
        | _ -> None

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
                match SdkTypes.tryFind (fullName definition) with
                | Some(fqName, order) ->
                    arguments ()
                    |> Checked.bind (fun arguments ->
                        builder.TypeReference(location, fqName, order arguments)
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

    /// <summary>Lowers one planned module. Values come with task 3.6.</summary>
    let lowerModule
        (builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>)
        (plan: FrontendPlan)
        (planned: PlannedModule)
        : Checked<'Module> =
        planned.Types
        |> Checked.traverse (fun plannedType ->
            lowerTypeDefinition builder plan plannedType
            |> Checked.map (fun definition -> {
                Name = plannedType.Name
                Access = plannedType.Access
                Documentation = plannedType.Documentation
                Definition = definition
                Location = plannedType.Location
            })
        )
        |> Checked.bind (fun types -> builder.Module(planned.Plan, types, []))

    /// <summary>Lowers the whole planned package with one builder.</summary>
    let lowerPackage
        (builder:
            IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>)
        (plan: FrontendPlan)
        : Checked<'Package> =
        plan.Modules
        |> Checked.traverse (lowerModule builder plan)
        |> Checked.bind (fun modules -> builder.Package(plan.Package, modules))

/// <summary>The lowering of a planned package as a builder program, for any registered IR family.</summary>
[<Sealed>]
type internal PackageLowering(plan: FrontendPlan) =
    interface IBuilderProgram with
        member _.Run(builder) = Lowering.lowerPackage builder plan
