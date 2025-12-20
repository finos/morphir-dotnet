namespace Morphir.Internal.CodeGeneration.Tests.TestData

open Morphir.Internal.CodeGeneration

/// Test discriminated union for visitor pattern generation
[<GenerateVisitor>]
type TypeExpr =
    | TInt
    | TString
    | TBool
    | TTuple of TypeExpr list
    | TRecord of fields: Map<string, TypeExpr>
    | TFunc of input: TypeExpr * output: TypeExpr

/// Another test discriminated union
[<GenerateVisitor(VisitorName = "ExpressionVisitor")>]
type Expression =
    | Literal of int
    | Variable of string
    | Binary of left: Expression * op: string * right: Expression
    | Unary of op: string * operand: Expression

