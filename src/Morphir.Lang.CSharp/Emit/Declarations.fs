// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Morphir.Lang.CSharp.Backend
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
        (baseTypes: TypeSyntax list)
        (members: MemberDeclarationSyntax list)
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

        let declaration =
            match baseTypes with
            | [] -> declaration
            | _ -> declaration.WithBaseList(SF.BaseList(SF.SeparatedList<BaseTypeSyntax>(baseTypes |> List.map (fun b -> SF.SimpleBaseType b :> BaseTypeSyntax))))

        match members with
        | [] -> declaration :> MemberDeclarationSyntax
        | _ ->
            declaration
                .WithSemicolonToken(Unchecked.defaultof<SyntaxToken>)
                .WithOpenBraceToken(SF.Token SyntaxKind.OpenBraceToken)
                .WithMembers(SF.List<MemberDeclarationSyntax>(members))
                .WithCloseBraceToken(SF.Token SyntaxKind.CloseBraceToken)
            :> MemberDeclarationSyntax

    let private union
        (modifiers: SyntaxKind list)
        (name: string)
        (typeParameters: Name list)
        (cases: TypeSyntax list)
        (baseTypes: TypeSyntax list)
        (members: MemberDeclarationSyntax list)
        : MemberDeclarationSyntax =
        let hasBody = not (List.isEmpty members)

        SF.UnionDeclaration(
            SF.List<AttributeListSyntax>(),
            Cs.modifiers modifiers,
            SF.Token SyntaxKind.UnionKeyword,
            Cs.token name,
            (if List.isEmpty typeParameters then null else typeParameterList typeParameters),
            parameterList (cases |> List.map (fun t -> t, None)),
            (match baseTypes with
             | [] -> null
             | _ -> SF.BaseList(SF.SeparatedList<BaseTypeSyntax>(baseTypes |> List.map (fun b -> SF.SimpleBaseType b :> BaseTypeSyntax)))),
            SF.List<TypeParameterConstraintClauseSyntax>(constraintClauses typeParameters),
            (if hasBody then SF.Token SyntaxKind.OpenBraceToken else Unchecked.defaultof<SyntaxToken>),
            SF.List<MemberDeclarationSyntax>(members),
            (if hasBody then SF.Token SyntaxKind.CloseBraceToken else Unchecked.defaultof<SyntaxToken>),
            (if hasBody then Unchecked.defaultof<SyntaxToken> else SF.Token SyntaxKind.SemicolonToken)
        )
        :> MemberDeclarationSyntax

    let private sdkType (name: string) : TypeSyntax = Cs.globalGeneric [ "Morphir"; "SDK"; "CSharp"; name ] []

    let private basicsEqual (left: ExpressionSyntax) (right: ExpressionSyntax) : ExpressionSyntax =
        Cs.invoke (Cs.access (sdkType "Basics" :> ExpressionSyntax) "Equal") [ left; right ]

    let private expressionMember (returnType: TypeSyntax) (name: string) (parameters: (TypeSyntax * string) list) (modifiers: SyntaxKind list) (body: ExpressionSyntax) : MemberDeclarationSyntax =
        SF.MethodDeclaration(returnType, Cs.token name)
            .WithModifiers(Cs.modifiers modifiers)
            .WithParameterList(SF.ParameterList(SF.SeparatedList<ParameterSyntax>(parameters |> List.map (fun (t, n) -> SF.Parameter(Cs.token n).WithType t))))
            .WithExpressionBody(SF.ArrowExpressionClause body)
            .WithSemicolonToken(SF.Token SyntaxKind.SemicolonToken)
        :> MemberDeclarationSyntax

    let private is (value: ExpressionSyntax) (t: TypeSyntax) (designation: string) : ExpressionSyntax =
        SF.IsPatternExpression(value, SF.DeclarationPattern(t, SF.SingleVariableDesignation(Cs.token designation))) :> ExpressionSyntax

    let private all (conditions: ExpressionSyntax list) : ExpressionSyntax =
        conditions |> List.reduce (fun left right -> SF.BinaryExpression(SyntaxKind.LogicalAndExpression, left, right) :> ExpressionSyntax)

    let private nullable (t: TypeSyntax) : TypeSyntax = SF.NullableType t :> TypeSyntax

    /// <summary>The members of IEEE equality of a record: NaN is not equal to NaN, as in Morphir.</summary>
    let private recordEquality (self: TypeSyntax) (fields: string list) : MemberDeclarationSyntax list =
        let other = "otherValue"
        let morphirValue = sdkType "IMorphirValue"

        let comparisons = fields |> List.map (fun field -> basicsEqual (Cs.id field) (Cs.access (Cs.id other) field))
        let equals = all (is (Cs.id "other") self other :: comparisons)

        let payload: ExpressionSyntax =
            match fields with
            | [] -> Cs.access (sdkType "Unit" :> ExpressionSyntax) "Value"
            | [ field ] -> Cs.id field
            | _ -> Cs.tuple (fields |> List.map (fun f -> Cs.id f :> ExpressionSyntax))

        [ expressionMember (Cs.keywordType SyntaxKind.BoolKeyword) "MorphirEquals" [ morphirValue, "other" ] [ SyntaxKind.PublicKeyword ] equals
          expressionMember (Cs.keywordType SyntaxKind.IntKeyword) "MorphirHashCode" [] [ SyntaxKind.PublicKeyword ]
              (Cs.invoke (Cs.access (sdkType "MorphirValue" :> ExpressionSyntax) "Hash") [ payload ])
          expressionMember (Cs.keywordType SyntaxKind.BoolKeyword) "Equals" [ nullable self, "other" ] [ SyntaxKind.PublicKeyword ]
              (SF.BinaryExpression(
                  SyntaxKind.LogicalAndExpression,
                  SF.IsPatternExpression(Cs.id "other", SF.UnaryPattern(SF.ConstantPattern(SF.LiteralExpression SyntaxKind.NullLiteralExpression))),
                  Cs.invoke (Cs.id "MorphirEquals") [ Cs.id "other" ]
              ) :> ExpressionSyntax)
          expressionMember (Cs.keywordType SyntaxKind.IntKeyword) "GetHashCode" [] [ SyntaxKind.PublicKeyword; SyntaxKind.OverrideKeyword ]
              (Cs.invoke (Cs.id "MorphirHashCode") []) ]

    /// <summary>The members of IEEE equality of a union: the case values compare with Morphir equality, and the operators are written.</summary>
    let private unionEquality (self: TypeSyntax) : MemberDeclarationSyntax list =
        let morphirValue = sdkType "IMorphirValue"
        let boolType = Cs.keywordType SyntaxKind.BoolKeyword

        let operator (kind: SyntaxKind) (negate: bool) : MemberDeclarationSyntax =
            let call = Cs.invoke (Cs.access (Cs.id "left") "Equals") [ Cs.id "right" ]
            let body = if negate then SF.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, call) :> ExpressionSyntax else call

            SF.OperatorDeclaration(boolType, SF.Token kind)
                .WithModifiers(Cs.modifiers [ SyntaxKind.PublicKeyword; SyntaxKind.StaticKeyword ])
                .WithParameterList(SF.ParameterList(SF.SeparatedList<ParameterSyntax>([ SF.Parameter(Cs.token "left").WithType self; SF.Parameter(Cs.token "right").WithType self ])))
                .WithExpressionBody(SF.ArrowExpressionClause body)
                .WithSemicolonToken(SF.Token SyntaxKind.SemicolonToken)
            :> MemberDeclarationSyntax

        [ expressionMember boolType "MorphirEquals" [ morphirValue, "other" ] [ SyntaxKind.PublicKeyword ]
              (SF.BinaryExpression(SyntaxKind.LogicalAndExpression, is (Cs.id "other") self "otherValue", basicsEqual (Cs.id "Value") (Cs.access (Cs.id "otherValue") "Value")))
          expressionMember (Cs.keywordType SyntaxKind.IntKeyword) "MorphirHashCode" [] [ SyntaxKind.PublicKeyword ]
              (Cs.invoke (Cs.access (sdkType "MorphirValue" :> ExpressionSyntax) "Hash") [ Cs.id "Value" ])
          expressionMember boolType "Equals" [ self, "other" ] [ SyntaxKind.PublicKeyword ] (Cs.invoke (Cs.id "MorphirEquals") [ Cs.cast morphirValue (Cs.id "other") ])
          expressionMember boolType "Equals" [ nullable (Cs.keywordType SyntaxKind.ObjectKeyword), "obj" ] [ SyntaxKind.PublicKeyword; SyntaxKind.OverrideKeyword ]
              (SF.IsPatternExpression(Cs.id "obj", SF.DeclarationPattern(self, SF.SingleVariableDesignation(Cs.token "other"))) |> fun test ->
                  SF.BinaryExpression(SyntaxKind.LogicalAndExpression, test, Cs.invoke (Cs.id "Equals") [ Cs.id "other" ]) :> ExpressionSyntax)
          expressionMember (Cs.keywordType SyntaxKind.IntKeyword) "GetHashCode" [] [ SyntaxKind.PublicKeyword; SyntaxKind.OverrideKeyword ] (Cs.invoke (Cs.id "MorphirHashCode") [])
          operator SyntaxKind.EqualsEqualsToken false
          operator SyntaxKind.ExclamationEqualsToken true ]

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
            let name = ModuleLayouts.identifier context.Layouts declaration.Name
            let accessKind = access declaration.Public

            let typeOf parameters shape =
                TypeSyntaxes.render context location parameters Set.empty shape

            match declaration.Definition with
            | Alias(Fields fields as body) ->
                let owner, _ = recordTarget context.Ct context.Catalog location body

                if owner.Name = declaration.Name then
                    let parameters = effectiveParameters context declaration
                    let ieee = context.Ieee declaration.Name
                    let fieldNames = fields |> List.map (fst >> title)
                    let self = Cs.genericName name (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax)) :> TypeSyntax

                    [ record
                          [ accessKind; SyntaxKind.SealedKeyword ]
                          name
                          parameters
                          (fields |> List.map (fun (field, t) -> typeOf parameters t, title field))
                          (if ieee then [ sdkType "IMorphirValue" ] else [])
                          (if ieee then recordEquality self fieldNames else []) ]
                else
                    []
            | Alias body ->
                typeOf declaration.Parameters body |> ignore
                []
            | Cases(publicCases, cases) ->
                let parameters = declaration.Parameters
                let self = Cs.globalGeneric (ModuleLayouts.qualified context.Layouts declaration.Name) (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax))

                let ieee = context.Ieee declaration.Name

                if TypeSyntaxes.isUnion declaration then
                    let unionSelf = Cs.genericName name (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax)) :> TypeSyntax

                    [ union
                          [ accessKind ]
                          name
                          parameters
                          (cases |> List.map (fun (_, fields) -> typeOf parameters (snd (List.head fields))))
                          (if ieee then [ sdkType "IMorphirValue" ] else [])
                          (if ieee then unionEquality unionSelf else []) ]
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

                    record [ accessKind; SyntaxKind.ClosedKeyword ] name parameters [] [] []
                    :: (cases
                        |> List.map (fun (caseName, fields) ->
                            let caseId = ModuleLayouts.identifier context.Layouts { declaration.Name with LocalName = caseName }
                            let caseSelf = Cs.genericName caseId (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax)) :> TypeSyntax
                            let fieldNames = fields |> List.map (fst >> title)

                            record
                                [ caseAccess; SyntaxKind.SealedKeyword ]
                                caseId
                                parameters
                                (fields |> List.map (fun (field, t) -> typeOf parameters t, title field))
                                (self :: (if ieee then [ sdkType "IMorphirValue" ] else []))
                                (if ieee then recordEquality caseSelf fieldNames else [])))
        )
