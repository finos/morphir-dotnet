// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Morphir.Lang.CSharp
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

    /// <summary>
    /// The constraint clauses of the type parameters of a method (EQL-2): <c>INumber&lt;T&gt;</c> for the class number,
    /// <c>IComparable&lt;T&gt;</c> for the class comparable, and <c>notnull</c> for each other type parameter.
    /// </summary>
    let methodConstraintClauses (classes: Map<Name, Set<ValueClass>>) (names: Name list) : TypeParameterConstraintClauseSyntax list =
        names
        |> List.map (fun name ->
            let self = [ SF.IdentifierName(Cs.token (generic name)) :> TypeSyntax ]
            let found = Map.tryFind name classes |> Option.defaultValue Set.empty

            let constraint' : TypeParameterConstraintSyntax =
                if Set.contains Number found then
                    SF.TypeConstraint(Cs.globalGeneric [ "System"; "Numerics"; "INumber" ] self)
                elif Set.contains Comparable found then
                    SF.TypeConstraint(Cs.globalGeneric [ "System"; "IComparable" ] self)
                else
                    SF.TypeConstraint(SF.IdentifierName "notnull")

            SF.TypeParameterConstraintClause(Cs.id (generic name))
                .WithConstraints(SF.SingletonSeparatedList<TypeParameterConstraintSyntax>(constraint')))

    let private access (isPublic: bool) : SyntaxKind = if isPublic then SyntaxKind.PublicKeyword else SyntaxKind.InternalKeyword

    let private parameterList (parameters: (TypeSyntax * string option * AttributeListSyntax list) list) : ParameterListSyntax =
        SF.ParameterList(
            SF.SeparatedList<ParameterSyntax>(
                parameters
                |> List.map (fun (t, name, attributes) ->
                    match name with
                    | Some text -> SF.Parameter(Cs.token text).WithType(t).WithAttributeLists(SF.List<AttributeListSyntax>(attributes))
                    | None -> SF.Parameter(SF.List<AttributeListSyntax>(attributes), SF.TokenList(), t, Unchecked.defaultof<SyntaxToken>, null))
            )
        )

    /// <summary>The attribute <c>MorphirEquality</c> with a value, as the backend writes it back from the carrier (OPT-1).</summary>
    let equalityAttribute (value: FloatEquality) : AttributeListSyntax =
        let word =
            match value with
            | FloatEquality.Clr -> "Clr"
            | FloatEquality.Ieee -> "Ieee"

        SF.AttributeList(
            SF.SingletonSeparatedList(
                SF.Attribute(
                    Cs.globalName [ "Morphir"; "MorphirEquality" ],
                    SF.AttributeArgumentList(SF.SingletonSeparatedList(SF.AttributeArgument(Cs.access (Cs.globalName [ "Morphir"; "FloatEquality" ] :> ExpressionSyntax) word)))
                )
            )
        )

    /// <summary>A positional record, or a record with no members when there is no field. A base type makes it a case.</summary>
    let private record
        (attributes: AttributeListSyntax list)
        (modifiers: SyntaxKind list)
        (name: string)
        (typeParameters: Name list)
        (fields: (TypeSyntax * string * AttributeListSyntax list) list)
        (baseTypes: TypeSyntax list)
        (members: MemberDeclarationSyntax list)
        : MemberDeclarationSyntax =
        let declaration =
            SF.RecordDeclaration(SyntaxKind.RecordDeclaration, SF.Token SyntaxKind.RecordKeyword, Cs.token name)
                .WithAttributeLists(SF.List<AttributeListSyntax>(attributes))
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
            else declaration.WithParameterList(parameterList (fields |> List.map (fun (t, n, a) -> t, Some n, a)))

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
        (attributes: AttributeListSyntax list)
        (modifiers: SyntaxKind list)
        (name: string)
        (typeParameters: Name list)
        (cases: TypeSyntax list)
        (baseTypes: TypeSyntax list)
        (members: MemberDeclarationSyntax list)
        : MemberDeclarationSyntax =
        let hasBody = not (List.isEmpty members)

        SF.UnionDeclaration(
            SF.List<AttributeListSyntax>(attributes),
            Cs.modifiers modifiers,
            SF.Token SyntaxKind.UnionKeyword,
            Cs.token name,
            (if List.isEmpty typeParameters then null else typeParameterList typeParameters),
            parameterList (cases |> List.map (fun t -> t, None, [])),
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

    /// <summary>
    /// The members of IEEE equality of a record: NaN is not equal to NaN, as in Morphir. A field with plain equality
    /// (a member attribute, OPT-1) compares with the default equality comparer of its type, as plain C# does.
    /// </summary>
    let private recordEquality (self: TypeSyntax) (typed: (string * TypeSyntax * FloatEquality) list) : MemberDeclarationSyntax list =
        let other = "otherValue"
        let morphirValue = sdkType "IMorphirValue"
        let fields = typed |> List.map (fun (field, _, _) -> field)

        let comparisons =
            typed
            |> List.map (fun (field, t, equality) ->
                match equality with
                | FloatEquality.Ieee -> basicsEqual (Cs.id field) (Cs.access (Cs.id other) field)
                | FloatEquality.Clr ->
                    let comparer = Cs.access (Cs.globalGeneric [ "System"; "Collections"; "Generic"; "EqualityComparer" ] [ t ] :> ExpressionSyntax) "Default"
                    Cs.invoke (Cs.access comparer "Equals") [ Cs.id field; Cs.access (Cs.id other) field ])
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

    /// <summary>
    /// The fields of one owner (a record alias or a case) with their C# type and float equality, whether the owner gets the
    /// members of IEEE equality, and the attributes that the backend writes back from the carrier: one on the type when
    /// each field carries the same value, else one on each field that has a carrier.
    /// </summary>
    let private equalityOf (context: Context) (typeName: FQName) (owner: Name) (fields: (Name * TypeSyntax) list) =
        let typed = fields |> List.map (fun (field, t) -> title field, t, context.FieldEquality typeName owner field)

        let ieee =
            match typed with
            | [] -> context.TypeEquality typeName = FloatEquality.Ieee
            | _ -> typed |> List.exists (fun (_, _, equality) -> equality = FloatEquality.Ieee)

        let carried = fields |> List.map (fun (field, _) -> Map.containsKey (typeName, owner, field) context.Carriers.Fields)
        let values = typed |> List.map (fun (_, _, equality) -> equality) |> List.distinct

        let typeAttributes, fieldAttributes =
            match carried, values with
            | [], _ -> [], []
            | _, [ single ] when List.forall id carried -> [ equalityAttribute single ], List.map (fun _ -> []) typed
            | _ -> [], List.map2 (fun isCarried (_, _, equality) -> if isCarried then [ equalityAttribute equality ] else []) carried typed

        typed, ieee, typeAttributes, (if List.isEmpty fieldAttributes then List.map (fun _ -> []) typed else fieldAttributes)

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
                    let typedFields = fields |> List.map (fun (field, t) -> field, typeOf parameters t)
                    let typed, ieee, attributes, fieldAttributes = equalityOf context declaration.Name declaration.Name.LocalName typedFields
                    let self = Cs.genericName name (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax)) :> TypeSyntax

                    [ record
                          attributes
                          [ accessKind; SyntaxKind.SealedKeyword ]
                          name
                          parameters
                          (List.map2 (fun (field, t) a -> t, title field, a) typedFields fieldAttributes)
                          (if ieee then [ sdkType "IMorphirValue" ] else [])
                          (if ieee then recordEquality self typed else []) ]
                else
                    []
            | Alias body ->
                typeOf declaration.Parameters body |> ignore
                []
            | Cases(publicCases, cases) ->
                let parameters = declaration.Parameters
                let self = Cs.globalGeneric (ModuleLayouts.qualified context.Layouts declaration.Name) (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax))

                if TypeSyntaxes.isUnion declaration then
                    let unionSelf = Cs.genericName name (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax)) :> TypeSyntax

                    // A union compares its case value: it has IEEE equality when a case has it. It takes the attribute when
                    // each case carries the same value, because a case of a union has no place for an attribute.
                    let perCase = cases |> List.map (fun (caseName, fields) -> equalityOf context declaration.Name caseName [ for f, t in fields -> f, typeOf parameters t ])
                    let ieee = perCase |> List.exists (fun (_, caseIeee, _, _) -> caseIeee)

                    let attributes =
                        match perCase |> List.map (fun (_, _, typeAttributes, _) -> typeAttributes) |> List.distinctBy (List.map string) with
                        | [ single ] -> single
                        | _ -> []

                    [ union
                          attributes
                          [ accessKind ]
                          name
                          parameters
                          (cases |> List.map (fun (_, fields) -> typeOf parameters (snd (List.head fields))))
                          (if ieee then [ sdkType "IMorphirValue" ] else [])
                          (if ieee then unionEquality unionSelf else []) ]
                else
                    // Each case is a sibling type in the module: Checks.validateLayout refuses a case with the name of
                    // another type, a value or a module class of the container.
                    let caseAccess = access (declaration.Public && publicCases)

                    record [] [ accessKind; SyntaxKind.ClosedKeyword ] name parameters [] [] []
                    :: (cases
                        |> List.map (fun (caseName, fields) ->
                            let caseId = ModuleLayouts.identifier context.Layouts { declaration.Name with LocalName = caseName }
                            let caseSelf = Cs.genericName caseId (parameters |> List.map (fun p -> Cs.id (generic p) :> TypeSyntax)) :> TypeSyntax
                            let typedFields = fields |> List.map (fun (field, t) -> field, typeOf parameters t)
                            let typed, ieee, attributes, fieldAttributes = equalityOf context declaration.Name caseName typedFields

                            record
                                attributes
                                [ caseAccess; SyntaxKind.SealedKeyword ]
                                caseId
                                parameters
                                (List.map2 (fun (field, t) a -> t, title field, a) typedFields fieldAttributes)
                                (self :: (if ieee then [ sdkType "IMorphirValue" ] else []))
                                (if ieee then recordEquality caseSelf typed else [])))
        )
