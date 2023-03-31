module Morphir.IR.Tests.ExpressionHelpers

open System
open System.Linq
open System.Linq.Expressions
open System.Threading

module Lambda =
    open Microsoft.FSharp.Quotations
    open Microsoft.FSharp.Linq.RuntimeHelpers.LeafExpressionConverter

    let rec translateExpr (linq: Expression) =
        match linq with
        | :? MethodCallExpression as methodCall ->
            match methodCall.Arguments.FirstOrDefault() with
            | :? LambdaExpression as lambda ->
                let args, body = translateExpr lambda.Body

                lambda.Parameters[0]
                :: args,
                body
            // | :? MemberExpression as me -> ...
            | _ -> [], linq
        | _ -> [], linq

    let inline toLinq<'a> expr =
        let args, body =
            expr
            |> QuotationToExpression
            |> translateExpr

        Expression.Lambda<'a>(
            body,
            args
            |> Array.ofList
        )

    let inline ofArity1 (expr: Expr<'a -> 'b>) = toLinq<Func<'a, 'b>> expr
    let inline ofArity2 (expr: Expr<'a -> 'b -> 'c>) = toLinq<Func<'a, 'b, 'c>> expr
    let inline ofAction (expr: Expr<'a -> unit>) = toLinq<Action<'a>> expr
