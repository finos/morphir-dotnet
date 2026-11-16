// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Operations
open Morphir.IR

/// <summary>The operand types that select an SDK operation for an overloaded C# operator.</summary>
[<RequireQualifiedAccess>]
type internal Operand =
    | Int
    | Float
    | Decimal
    | String
    | Char
    | Bool
    | Other

/// <summary>
/// The SDK mapping of the C# frontend. C# types, operators and calls of <c>Morphir.SDK.CSharp</c> map by explicit table to
/// Morphir SDK names. Tests check each entry against the reviewed SDK inventory.
/// </summary>
module internal SdkMapping =

    /// <summary>The Morphir SDK package, <c>morphir/SDK</c>.</summary>
    let package = PackageName(Path [ Name [ "morphir" ]; Name [ "s"; "d"; "k" ] ])

    /// <summary>An SDK name from its module and local name, such as <c>sdk "Basics" "lessThan"</c>.</summary>
    let sdk (moduleName: string) (localName: string) : FQName = {
        PackagePath = package
        ModulePath = ModulePath(Path [ Name.fromString moduleName ])
        LocalName = Name.fromString localName
    }

    let operandOf (t: ITypeSymbol) =
        if isNull t then
            Operand.Other
        else
            match t.SpecialType with
            | SpecialType.System_Int32 -> Operand.Int
            | SpecialType.System_Double -> Operand.Float
            | SpecialType.System_Decimal -> Operand.Decimal
            | SpecialType.System_String -> Operand.String
            | SpecialType.System_Char -> Operand.Char
            | SpecialType.System_Boolean -> Operand.Bool
            | _ -> Operand.Other

    let private isSdkAssembly (symbol: ISymbol) =
        not (isNull symbol.ContainingAssembly)
        && symbol.ContainingAssembly.Name = "Morphir.SDK.CSharp"

    /// <summary>
    /// The SDK type of a C# type and the order of its type arguments: for each SDK type argument, the index of the C#
    /// type argument.
    /// </summary>
    let tryType (t: INamedTypeSymbol) : (FQName * int list) option =
        match t.SpecialType with
        | SpecialType.System_Int32 -> Some(sdk "Basics" "Int", [])
        | SpecialType.System_Double -> Some(sdk "Basics" "Float", [])
        | SpecialType.System_Boolean -> Some(sdk "Basics" "Bool", [])
        | SpecialType.System_String -> Some(sdk "String" "String", [])
        | SpecialType.System_Char -> Some(sdk "Char" "Char", [])
        | SpecialType.System_Decimal -> Some(sdk "Decimal" "Decimal", [])
        | _ ->
            if isSdkAssembly t && t.ContainingNamespace.ToDisplayString() = "Morphir.SDK.CSharp" then
                match t.Name with
                | "List" -> Some(sdk "List" "List", [ 0 ])
                | "Maybe" -> Some(sdk "Maybe" "Maybe", [ 0 ])
                | "Result" -> Some(sdk "Result" "Result", [ 0; 1 ])
                | "Dict" -> Some(sdk "Dict" "Dict", [ 0; 1 ])
                | "Set" -> Some(sdk "Set" "Set", [ 0 ])
                | "Order" -> Some(sdk "Basics" "Order", [])
                | _ -> None
            elif
                t.OriginalDefinition.ToDisplayString() = "System.Collections.Generic.IReadOnlyList<T>"
            then
                Some(sdk "List" "List", [ 0 ])
            else
                None

    /// <summary>The SDK value of an overloaded binary operator and its operand type, with the number of arguments.</summary>
    let tryBinary (kind: BinaryOperatorKind) (operand: Operand) : FQName option =
        let numbers = operand = Operand.Int || operand = Operand.Float
        let comparable = numbers || operand = Operand.String || operand = Operand.Char

        match kind with
        | BinaryOperatorKind.Add when numbers -> Some(sdk "Basics" "add")
        | BinaryOperatorKind.Add when operand = Operand.Decimal -> Some(sdk "Decimal" "add")
        | BinaryOperatorKind.Add when operand = Operand.String -> Some(sdk "String" "append")
        | BinaryOperatorKind.Subtract when numbers -> Some(sdk "Basics" "subtract")
        | BinaryOperatorKind.Subtract when operand = Operand.Decimal -> Some(sdk "Decimal" "sub")
        | BinaryOperatorKind.Multiply when numbers -> Some(sdk "Basics" "multiply")
        | BinaryOperatorKind.Multiply when operand = Operand.Decimal -> Some(sdk "Decimal" "mul")
        | BinaryOperatorKind.Divide when operand = Operand.Decimal -> Some(sdk "Decimal" "div")
        | BinaryOperatorKind.Divide when operand = Operand.Float -> Some(sdk "Basics" "divide")
        | BinaryOperatorKind.Divide when operand = Operand.Int -> Some(sdk "Basics" "integerDivide")
        | BinaryOperatorKind.Remainder when operand = Operand.Int -> Some(sdk "Basics" "remainderBy")
        | BinaryOperatorKind.LessThan when comparable -> Some(sdk "Basics" "lessThan")
        | BinaryOperatorKind.LessThanOrEqual when comparable -> Some(sdk "Basics" "lessThanOrEqual")
        | BinaryOperatorKind.GreaterThan when comparable -> Some(sdk "Basics" "greaterThan")
        | BinaryOperatorKind.GreaterThanOrEqual when comparable -> Some(sdk "Basics" "greaterThanOrEqual")
        | BinaryOperatorKind.Equals -> Some(sdk "Basics" "equal")
        | BinaryOperatorKind.NotEquals -> Some(sdk "Basics" "notEqual")
        | _ -> None

    /// <summary>The SDK value of a unary operator.</summary>
    let tryUnary (kind: UnaryOperatorKind) (operand: Operand) : FQName option =
        match kind with
        | UnaryOperatorKind.Minus when operand = Operand.Int || operand = Operand.Float -> Some(sdk "Basics" "negate")
        | UnaryOperatorKind.Not when operand = Operand.Bool -> Some(sdk "Basics" "not")
        | _ -> None

    /// <summary>The C# operator spelling, for messages.</summary>
    let operatorText (kind: BinaryOperatorKind) =
        match kind with
        | BinaryOperatorKind.Add -> "+"
        | BinaryOperatorKind.Subtract -> "-"
        | BinaryOperatorKind.Multiply -> "*"
        | BinaryOperatorKind.Divide -> "/"
        | BinaryOperatorKind.Remainder -> "%"
        | BinaryOperatorKind.LessThan -> "<"
        | BinaryOperatorKind.LessThanOrEqual -> "<="
        | BinaryOperatorKind.GreaterThan -> ">"
        | BinaryOperatorKind.GreaterThanOrEqual -> ">="
        | BinaryOperatorKind.Equals -> "=="
        | BinaryOperatorKind.NotEquals -> "!="
        | BinaryOperatorKind.ConditionalAnd -> "&&"
        | BinaryOperatorKind.ConditionalOr -> "||"
        | BinaryOperatorKind.And -> "&"
        | BinaryOperatorKind.Or -> "|"
        | _ -> "operator"

    /// <summary>
    /// The SDK value of a static method of <c>Morphir.SDK.CSharp.Basics</c> or <c>Morphir.SDK.CSharp.Decimal</c>, with its
    /// number of parameters. Only these two classes map now: their methods have the names of the Morphir SDK operations of
    /// the modules <c>Basics</c> and <c>Decimal</c>. The C# backend writes the operations of <c>Decimal</c> as calls of
    /// that class, for example <c>Decimal.Mul(a, b)</c>.
    /// </summary>
    let tryMethod (method': IMethodSymbol) : (FQName * int) option =
        if isSdkAssembly method' && method'.IsStatic then
            match method'.ContainingType.Name with
            | "Basics" -> Some(sdk "Basics" method'.Name, method'.Parameters.Length)
            | "Decimal" -> Some(sdk "Decimal" method'.Name, method'.Parameters.Length)
            | _ -> None
        else
            None

    /// <summary>
    /// True for <c>Morphir.SDK.CSharp.List&lt;T&gt;.FromEnumerable</c>: the C# backend writes a list value as this call
    /// on an array with its items.
    /// </summary>
    let isListFromEnumerable (method': IMethodSymbol) : bool =
        isSdkAssembly method'
        && method'.IsStatic
        && method'.Name = "FromEnumerable"
        && method'.ContainingType.Name = "List"
        && method'.ContainingType.ContainingNamespace.ToDisplayString() = "Morphir.SDK.CSharp"
