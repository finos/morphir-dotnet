// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open System
open System.Collections.Generic
open System.Globalization
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax

/// <summary>The syntax factory of Roslyn under a short name.</summary>
type internal SF = SyntaxFactory

/// <summary>Small helpers over <c>SyntaxFactory</c>. The backend builds every node with them; it never writes C# text.</summary>
module internal Cs =
    /// <summary>An identifier token; a keyword gets the escape <c>@</c>.</summary>
    let token (text: string) : SyntaxToken =
        let reserved = SyntaxFacts.GetKeywordKind text <> SyntaxKind.None
        let contextual = SyntaxFacts.GetContextualKeywordKind text <> SyntaxKind.None
        SF.Identifier(if reserved || contextual then "@" + text else text)

    let id (text: string) : IdentifierNameSyntax = SF.IdentifierName(token text)

    let private globalAlias: IdentifierNameSyntax =
        SF.IdentifierName(SF.Token SyntaxKind.GlobalKeyword)

    /// <summary><c>global::A.B.C</c>.</summary>
    let globalName (segments: string list) : NameSyntax =
        match segments with
        | [] -> invalidArg "segments" "A global name needs a segment."
        | first :: rest ->
            let start: NameSyntax = SF.AliasQualifiedName(globalAlias, id first)
            rest |> List.fold (fun (left: NameSyntax) segment -> SF.QualifiedName(left, id segment) :> NameSyntax) start

    /// <summary>A name with type arguments: <c>Name&lt;A, B&gt;</c>.</summary>
    let genericName (name: string) (arguments: TypeSyntax list) : SimpleNameSyntax =
        match arguments with
        | [] -> id name :> SimpleNameSyntax
        | _ ->
            SF.GenericName(token name, SF.TypeArgumentList(SF.SeparatedList<TypeSyntax>(arguments))) :> SimpleNameSyntax

    /// <summary><c>global::A.B.Name&lt;args&gt;</c>: the last segment takes the type arguments.</summary>
    let globalGeneric (segments: string list) (arguments: TypeSyntax list) : TypeSyntax =
        let leading = segments |> List.take (List.length segments - 1)
        let last = List.last segments

        match leading with
        | [] -> SF.AliasQualifiedName(globalAlias, genericName last arguments) :> TypeSyntax
        | _ -> SF.QualifiedName(globalName leading, genericName last arguments) :> TypeSyntax

    /// <summary>A dotted name without <c>global::</c>, for a namespace declaration.</summary>
    let dottedName (segments: string list) : NameSyntax =
        match segments with
        | [] -> invalidArg "segments" "A name needs a segment."
        | first :: rest ->
            rest |> List.fold (fun (left: NameSyntax) segment -> SF.QualifiedName(left, id segment) :> NameSyntax) (id first)

    let keywordType (kind: SyntaxKind) : TypeSyntax = SF.PredefinedType(SF.Token kind)

    let argument (e: ExpressionSyntax) : ArgumentSyntax = SF.Argument e

    let argList (arguments: ExpressionSyntax list) : ArgumentListSyntax =
        SF.ArgumentList(SF.SeparatedList<ArgumentSyntax>(arguments |> List.map argument))

    let invoke (target: ExpressionSyntax) (arguments: ExpressionSyntax list) : ExpressionSyntax =
        SF.InvocationExpression(target, argList arguments) :> ExpressionSyntax

    let access (target: ExpressionSyntax) (name: string) : ExpressionSyntax =
        SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, target, id name) :> ExpressionSyntax

    let accessGeneric (target: ExpressionSyntax) (name: SimpleNameSyntax) : ExpressionSyntax =
        SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, target, name) :> ExpressionSyntax

    let paren (e: ExpressionSyntax) : ExpressionSyntax = SF.ParenthesizedExpression e :> ExpressionSyntax

    let cast (t: TypeSyntax) (e: ExpressionSyntax) : ExpressionSyntax = SF.CastExpression(t, e) :> ExpressionSyntax

    let newObject (t: TypeSyntax) (arguments: ExpressionSyntax list) : ExpressionSyntax =
        SF.ObjectCreationExpression(t).WithArgumentList(argList arguments) :> ExpressionSyntax

    let lambda (parameter: string) (body: ExpressionSyntax) : ExpressionSyntax =
        SF.SimpleLambdaExpression(SF.Parameter(token parameter), body) :> ExpressionSyntax

    let lambdaBlock (parameter: string) (body: BlockSyntax) : ExpressionSyntax =
        SF.SimpleLambdaExpression(SF.Parameter(token parameter)).WithBlock(body) :> ExpressionSyntax

    let lambdaNoArgs (body: BlockSyntax) : ExpressionSyntax =
        SF.ParenthesizedLambdaExpression().WithBlock(body) :> ExpressionSyntax

    let block (statements: StatementSyntax list) : BlockSyntax = SF.Block(statements)

    let returns (e: ExpressionSyntax) : StatementSyntax = SF.ReturnStatement e :> StatementSyntax

    let local (t: TypeSyntax) (name: string) (value: ExpressionSyntax) : StatementSyntax =
        SF.LocalDeclarationStatement(
            SF.VariableDeclaration(
                t,
                SF.SingletonSeparatedList(SF.VariableDeclarator(token name).WithInitializer(SF.EqualsValueClause value))
            )
        )
        :> StatementSyntax

    let localVar (name: string) (value: ExpressionSyntax) : StatementSyntax =
        local (SF.IdentifierName "var") name value

    let discard (e: ExpressionSyntax) : StatementSyntax =
        SF.ExpressionStatement(SF.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SF.IdentifierName "_", e))
        :> StatementSyntax

    let throwNew (exceptionType: string list) (message: string) : ExpressionSyntax =
        SF.ThrowExpression(newObject (globalGeneric exceptionType []) [ SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal message) ])
        :> ExpressionSyntax

    let throwStatement (exceptionType: string list) (message: string) : StatementSyntax =
        SF.ThrowStatement(newObject (globalGeneric exceptionType []) [ SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal message) ])
        :> StatementSyntax

    let intLiteral (value: int) : ExpressionSyntax =
        SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal value) :> ExpressionSyntax

    let stringLiteral (value: string) : ExpressionSyntax =
        SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal value) :> ExpressionSyntax

    let boolLiteral (value: bool) : ExpressionSyntax =
        SF.LiteralExpression(if value then SyntaxKind.TrueLiteralExpression else SyntaxKind.FalseLiteralExpression) :> ExpressionSyntax

    let tuple (items: ExpressionSyntax list) : ExpressionSyntax =
        SF.TupleExpression(SF.SeparatedList<ArgumentSyntax>(items |> List.map argument)) :> ExpressionSyntax

    let funcType (input: TypeSyntax) (output: TypeSyntax) : TypeSyntax =
        globalGeneric [ "System"; "Func" ] [ input; output ]

    let modifiers (kinds: SyntaxKind list) : SyntaxTokenList = SF.TokenList(kinds |> List.map SF.Token)

    /// <summary>An <c>info</c> to keep the compiler quiet about an unused local.</summary>
    let unused (name: string) : StatementSyntax = discard (SF.IdentifierName(token name))
