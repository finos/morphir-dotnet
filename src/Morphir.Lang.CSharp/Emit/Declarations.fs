// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Morphir.CodeGeneration.CSharp
open Morphir.IR
open TypeEmission

/// <summary>Writes the type declarations of a module: records, closed records and unions.</summary>
module internal Declarations =
    let typeParameterList (names: Name list) : TypeParameterListSyntax =
        SF.TypeParameterList(SF.SeparatedList<TypeParameterSyntax>(names |> List.map (fun name -> SF.TypeParameter(Cs.token (generic name)))))

    let constraintClauses (names: Name list) : TypeParameterConstraintClauseSyntax list =
        names
        |> List.map (fun name ->
            SF.TypeParameterConstraintClause(Cs.id (generic name))
                .WithConstraints(SF.SingletonSeparatedList<TypeParameterConstraintSyntax>(SF.TypeConstraint(SF.IdentifierName "notnull"))))

    let private access (isPublic: bool) : SyntaxKind = if isPublic then SyntaxKind.PublicKeyword else SyntaxKind.InternalKeyword

    let private parameterList (parameters: (TypeSyntax * string option) list) : ParameterListSyntax =
        SF.ParameterList(
            SF.SeparatedList<ParameterSyntax>(
                parameters
                |> List.map (fun (t, name) ->
                    match name with
                    | Some text -> SF.Parameter(Cs.token text).WithType(t)
                    | None -> SF.Parameter(SF.List<AttributeListSyntax>(), SF.TokenList(), t, Unchecked.defaultof<SyntaxToken>, null))
            )
        )

    /// <summary>A positional record, or a record with no members when there is no field. A base type makes it a case.</summary>
    let private record
        (modifiers: SyntaxKind list)
        (name: string)
        (typeParameters: Name list)
        (fields: (TypeSyntax * string) list)
        (baseType: TypeSyntax option)
        : MemberDeclarationSyntax =
        let declaration =
            SF.RecordDeclaration(SyntaxKind.RecordDeclaration, SF.Token SyntaxKind.RecordKeyword, Cs.token name)
                .WithModifiers(Cs.modifiers modifiers)
                .WithOpenBraceToken(Unchecked.defaultof<SyntaxToken>)
                .WithCloseBraceToken(Unchecked.defaultof<SyntaxToken>)
                .WithSemicolonToken(SF.Token SyntaxKind.SemicolonToken)

        let declaration =
            if List.isEmpty typeParameters then declaration
            else
                declaration
                    .WithTypeParameterList(typeParameterList typeParameters)
                    .WithConstraintClauses(SF.List<TypeParameterConstraintClauseSyntax>(constraintClauses typeParameters))

        let declaration =
            if List.isEmpty fields then declaration
            else declaration.WithParameterList(parameterList (fields |> List.map (fun (t, n) -> t, Some n)))

        match baseType with
        | Some b -> declaration.WithBaseList(SF.BaseList(SF.SingletonSeparatedList<BaseTypeSyntax>(SF.SimpleBaseType b))) :> MemberDeclarationSyntax
        | None -> declaration :> MemberDeclarationSyntax

    let private union
        (modifiers: SyntaxKind list)
        (name: string)
        (typeParameters: Name list)
        (cases: TypeSyntax list)
        : MemberDeclarationSyntax =
        SF.UnionDeclaration(
            SF.List<AttributeListSyntax>(),
            Cs.modifiers modifiers,
            SF.Token SyntaxKind.UnionKeyword,
            Cs.token name,
            (if List.isEmpty typeParameters then null else typeParameterList typeParameters),
            parameterList (cases |> List.map (fun t -> t, None)),
            null,
            SF.List<TypeParameterConstraintClauseSyntax>(constraintClauses typeParameters),
            Unchecked.defaultof<SyntaxToken>,
            SF.List<MemberDeclarationSyntax>(),
            Unchecked.defaultof<SyntaxToken>,
            SF.Token SyntaxKind.SemicolonToken
        )
        :> MemberDeclarationSyntax

    /// <summary>The parameters that the declaration binds: for a record alias, the free parameters of its body.</summary>
    let private effectiveParameters (context: Context) (declaration: Declaration) =
        match declaration.Definition with
        | Alias(Fields _ as body) -> recordParameters context.Ct context.Catalog declaration body
        | _ -> declaration.Parameters

    /// <summary>The syntax of every declaration of one module, in the order of the catalog.</summary>
    let ofModule (context: Context) (path: ModulePath) : MemberDeclarationSyntax list =
        let own =
            context.Catalog
            |> Map.toList
            |> List.map snd
            |> List.filter (fun declaration -> declaration.Name.ModulePath = path)

        // The identifiers of the types that the module writes, to find a case that has the name of another type.
        let emittedNames =
            own
            |> List.collect (fun declaration ->
                match declaration.Definition with
                | Alias(Fields _ as body) ->
                    let owner, _ = recordTarget context.Ct context.Catalog declaration.Location body
                    if owner.Name = declaration.Name then [ title declaration.Name.LocalName, declaration ] else []
                | Cases _ -> [ title declaration.Name.LocalName, declaration ]
                | Alias _ -> [])
            |> Map.ofList

        own
        |> List.collect (fun declaration ->
            context.Ct.ThrowIfCancellationRequested()
            let location = declaration.Location
            let name = title declaration.Name.LocalName
            let accessKind = access declaration.Public

            let typeOf parameters shape =
                TypeSyntaxes.render context location parameters Set.empty shape

            match declaration.Definition with
            | Alias(Fields fields as body) ->
                let owner, _ = recordTarget context.Ct context.Catalog location body

                if owner.Name = declaration.Name then
                    let parameters = effectiveParameters context declaration
                    [ record [ accessKind; SyntaxKind.SealedKeyword ] name parameters (fields |> List.map (fun (field, t) -> typeOf parameters t, title field)) None ]
                else
                    []
            | Alias body ->
                typeOf declaration.Parameters body |> ignore
                []
            | Cases(publicCases, cases) ->
                let parameters = declaration.Parameters
                let self = Cs.globalGeneric (ModuleLayouts.qualified context.Layouts declaration.Name) (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax))

                if TypeSyntaxes.isUnion declaration then
                    [ union [ accessKind ] name parameters
                          (cases |> List.map (fun (_, fields) -> typeOf parameters (snd (List.head fields)))) ]
                else
                    for caseName, _ in cases do
                        match Map.tryFind (title caseName) emittedNames with
                        | Some other ->
                            // The case is a sibling type of the module: no other type of the module may have its name.
                            Admission.fail
                                "name-collision"
                                location
                                (title caseName + " is a case of " + name + " and the name of the type " + title other.Name.LocalName + " of the module.")
                        | _ -> ()

                    let caseAccess = access (declaration.Public && publicCases)

                    record [ accessKind; SyntaxKind.ClosedKeyword ] name parameters [] None
                    :: (cases
                        |> List.map (fun (caseName, fields) ->
                            record
                                [ caseAccess; SyntaxKind.SealedKeyword ]
                                (title caseName)
                                parameters
                                (fields |> List.map (fun (field, t) -> typeOf parameters t, title field))
                                (Some self)))
        )
