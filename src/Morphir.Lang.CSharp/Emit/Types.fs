// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open System.Threading
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Morphir.CodeGeneration.CSharp
open Morphir.IR
open TypeEmission

/// <summary>What the emitters share: the naming, the type catalog, the module layouts and the cancellation token.</summary>
type internal Context = {
    Naming: Naming
    Catalog: Map<FQName, Declaration>
    Layouts: Map<ModulePath, ModuleLayout>
    /// <summary>Whether a custom type or record alias has IEEE equality: NaN is not equal to NaN, and the members of Morphir equality are written.</summary>
    Ieee: FQName -> bool
    Ct: CancellationToken
}

/// <summary>Writes the C# type of a model type as a syntax node.</summary>
module internal TypeSyntaxes =
    let private sdkRoot name = [ "Morphir"; "SDK"; "CSharp"; name ]

    /// <summary>The generic parameter name: <c>T</c> and the name of the parameter.</summary>
    let parameter (name: Name) : string = generic name

    /// <summary>Whether a declaration is written as a C# union: each case wraps one reference with the name of the case.</summary>
    let isUnion (declaration: Declaration) : bool = declaration.Wrapper

    /// <summary>The name of the type that a case of a declaration is written as: the wrapped type for a union, the sealed record otherwise.</summary>
    let caseSegments (context: Context) (declaration: Declaration) (caseName: Name) : string list =
        ModuleLayouts.qualified context.Layouts { declaration.Name with LocalName = caseName }

    let rec render (context: Context) location (bound: Name list) (visiting: Set<FQName>) (shape: Shape) : TypeSyntax =
        context.Ct.ThrowIfCancellationRequested()
        let next = render context location bound visiting

        match shape with
        | Parameter name ->
            if not (List.contains name bound) then
                Admission.fail "unbound-type" location ("Unbound type parameter " + title name)

            Cs.id (parameter name) :> TypeSyntax
        | Unit -> Cs.globalGeneric (sdkRoot "Unit") []
        | Arrow(input, output) -> Cs.funcType (next input) (next output)
        | Product items when List.length items >= 2 ->
            SF.TupleType(SF.SeparatedList<TupleElementSyntax>(items |> List.map (fun item -> SF.TupleElement(next item)))) :> TypeSyntax
        | Product _ -> Admission.fail "tuple-arity" location "A C# tuple requires at least two elements."
        | Fields _ ->
            let owner, args = recordTarget context.Ct context.Catalog location shape
            Cs.globalGeneric (ModuleLayouts.qualified context.Layouts owner.Name) (List.map next args)
        | Named(name, args) ->
            match sdk name with
            | Some(modul, local) ->
                let target, arity =
                    match modul, local with
                    | "basics", "int" -> Cs.keywordType SyntaxKind.IntKeyword, 0
                    | "basics", "float" -> Cs.keywordType SyntaxKind.DoubleKeyword, 0
                    | "basics", "bool" -> Cs.keywordType SyntaxKind.BoolKeyword, 0
                    | "string", "string" -> Cs.keywordType SyntaxKind.StringKeyword, 0
                    | "char", "char" -> Cs.keywordType SyntaxKind.CharKeyword, 0
                    | "decimal", "decimal" -> Cs.keywordType SyntaxKind.DecimalKeyword, 0
                    | "basics", "order" -> Cs.globalGeneric (sdkRoot "Order") [], 0
                    | "list", "list" -> Cs.globalGeneric (sdkRoot "List") (List.map next args), 1
                    | "maybe", "maybe" -> Cs.globalGeneric (sdkRoot "Maybe") (List.map next args), 1
                    | "result", "result" -> Cs.globalGeneric (sdkRoot "Result") (List.map next args), 2
                    | "dict", "dict" -> Cs.globalGeneric (sdkRoot "Dict") (List.map next args), 2
                    | "set", "set" -> Cs.globalGeneric (sdkRoot "Set") (List.map next args), 1
                    | _ -> Admission.fail "unmapped-sdk-type" location ("No admitted SDK type: " + FQName.toString name)

                if List.length args <> arity then
                    Admission.fail "type-arity" location ("Incorrect type argument count for " + FQName.toString name)

                target
            | None ->
                match Map.tryFind name context.Catalog with
                | None -> Admission.fail "unresolved-type" location ("No implementation for " + FQName.toString name)
                | Some declaration ->
                    if List.length declaration.Parameters <> List.length args then
                        Admission.fail "type-arity" location ("Incorrect type argument count for " + FQName.toString name)

                    match declaration.Definition with
                    | Alias(Fields _ as body) ->
                        let concrete = substitute context.Ct (List.zip declaration.Parameters args |> Map.ofList) body
                        let owner, owned = recordTarget context.Ct context.Catalog location concrete
                        Cs.globalGeneric (ModuleLayouts.qualified context.Layouts owner.Name) (List.map next owned)
                    | Cases _ -> Cs.globalGeneric (ModuleLayouts.qualified context.Layouts name) (List.map next args)
                    | Alias body ->
                        if Set.contains name visiting then
                            Admission.fail "recursive-alias" location ("Cyclic alias " + FQName.toString name)

                        let body = substitute context.Ct (List.zip declaration.Parameters args |> Map.ofList) body
                        render context location bound (Set.add name visiting) body
