// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open FSharp.Compiler.Symbols
open Morphir.IR

/// <summary>
/// Whether a mapping entry is part of the reviewed .NET SDK contract
/// (<c>tests/Fixtures/MorphirSDK/inventory.json</c>). An entry outside the contract names why it is mapped.
/// </summary>
[<RequireQualifiedAccess>]
type internal SdkTier =
    | Contract
    | Outside of reason: string

/// <summary>The shape of an operand type. An overloaded F# operator selects its SDK operation by it.</summary>
[<RequireQualifiedAccess>]
type internal SdkOperand =
    | Int
    | Float
    | Decimal
    | String
    | Char
    | Bool
    | List of element: SdkOperand
    | Tuple of elements: SdkOperand list
    | Other of name: string

    /// <summary>
    /// True for the comparable types of the SDK profile: Int, Float, Char, String, and lists and tuples of them.
    /// </summary>
    member this.IsComparable =
        match this with
        | Int
        | Float
        | Char
        | String -> true
        | List element -> element.IsComparable
        | Tuple elements ->
            elements
            |> List.forall (fun element -> element.IsComparable)
        | Decimal
        | Bool
        | Other _ -> false

    /// <summary>The F# spelling of the operand type, for messages.</summary>
    member this.Text =
        match this with
        | Int -> "int"
        | Float -> "float"
        | Decimal -> "decimal"
        | String -> "string"
        | Char -> "char"
        | Bool -> "bool"
        | List element -> $"{element.Text} list"
        | Tuple elements ->
            elements
            |> List.map (fun element -> element.Text)
            |> String.concat " * "
        | Other name -> name

/// <summary>The operand types for which an entry applies.</summary>
[<RequireQualifiedAccess>]
type internal OperandRule =
    | Any
    | OneOf of SdkOperand list
    | Comparable

    /// <summary>True when the rule accepts the operand type.</summary>
    member this.Accepts(operand: SdkOperand option) =
        match this, operand with
        | Any, _ -> true
        | OneOf accepted, Some operand -> List.contains operand accepted
        | Comparable, Some operand -> operand.IsComparable
        | _, None -> false

/// <summary>One F# function or operator that maps to one Morphir SDK value.</summary>
type internal SdkValue = {
    /// <summary>The F# symbol: the full name of its declaring entity and its compiled name.</summary>
    Symbol: string
    /// <summary>The operand types for which this entry applies.</summary>
    Operands: OperandRule
    /// <summary>The Morphir SDK value.</summary>
    Target: FQName
    /// <summary>The number of curried parameters, on both sides.</summary>
    Arity: int
    Tier: SdkTier
}

/// <summary>How a mapped F# union case is lowered.</summary>
[<RequireQualifiedAccess>]
type internal SdkCaseKind =
    /// <summary>The case is an SDK constructor, in values and in patterns.</summary>
    | Constructor
    /// <summary>The case is an SDK function in values; its patterns have their own pattern shape.</summary>
    | Function

/// <summary>One F# union case that maps to a Morphir SDK constructor or function.</summary>
type internal SdkCase = {
    /// <summary>The full name of the union type and the case name.</summary>
    Case: string
    Target: FQName
    Kind: SdkCaseKind
    Arity: int
    Tier: SdkTier
}

/// <summary>One F# type definition that maps to a Morphir SDK type.</summary>
type internal SdkType = {
    /// <summary>The full name of the F# type definition.</summary>
    Type: string
    Target: FQName
    /// <summary>For each SDK type argument, the index of the F# type argument.</summary>
    ArgumentOrder: int list
    Tier: SdkTier
}

/// <summary>An F# operator that is lowered by its structure, not to an SDK value.</summary>
[<RequireQualifiedAccess>]
type internal StructuralOperator =
    /// <summary><c>x |&gt; f</c> is <c>f x</c>.</summary>
    | PipeRight
    /// <summary><c>f &lt;| x</c> is <c>f x</c>.</summary>
    | PipeLeft
    /// <summary><c>f &gt;&gt; g</c> is <c>fun x -&gt; g (f x)</c>.</summary>
    | ComposeRight
    /// <summary><c>g &lt;&lt; f</c> is <c>fun x -&gt; g (f x)</c>.</summary>
    | ComposeLeft
    /// <summary><c>a &amp;&amp; b</c> is <c>if a then b else false</c>, so <c>b</c> is evaluated only when needed.</summary>
    | BooleanAnd
    /// <summary><c>a || b</c> is <c>if a then true else b</c>, so <c>b</c> is evaluated only when needed.</summary>
    | BooleanOr

/// <summary>
/// The SDK mapping table (FSF-4). F# symbols map by full compiled name to Morphir SDK names. Overloaded
/// operators select an entry by the compiler's operand type. Pipes, composition, <c>&amp;&amp;</c> and <c>||</c> are
/// lowered by structure.
/// Tests check each entry against the SDK inventory, and check that the F# generator maps it back.
/// </summary>
[<RequireQualifiedAccess>]
module internal SdkMapping =

    /// <summary>The Morphir SDK package, <c>morphir/SDK</c>.</summary>
    let package = PackageName(Path [ Name [ "morphir" ]; Name [ "s"; "d"; "k" ] ])

    /// <summary>An SDK name from its module and local name, such as <c>sdk "Basics" "lessThan"</c>.</summary>
    let sdk (moduleName: string) (localName: string) = {
        PackagePath = package
        ModulePath = ModulePath(Path [ Name.fromString moduleName ])
        LocalName = Name.fromString localName
    }

    let private outsideBoolean =
        SdkTier.Outside
            "Morphir SDK Basics.not; the reviewed .NET SDK contract has no Boolean operations yet."

    let private outsideString =
        SdkTier.Outside
            "Morphir SDK String function with the same UTF-16 meaning; the reviewed .NET SDK contract has no String operations yet."

    let private operators = "Microsoft.FSharp.Core.Operators."

    let private intrinsic =
        "Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators."

    let private listModule = "Microsoft.FSharp.Collections.ListModule."
    let private optionModule = "Microsoft.FSharp.Core.OptionModule."
    let private resultModule = "Microsoft.FSharp.Core.ResultModule."
    let private stringModule = "Microsoft.FSharp.Core.StringModule."

    let private value symbol operands target arity tier = {
        Symbol = symbol
        Operands = operands
        Target = target
        Arity = arity
        Tier = tier
    }

    let private numbers = OperandRule.OneOf [ SdkOperand.Int; SdkOperand.Float ]
    let private decimals = OperandRule.OneOf [ SdkOperand.Decimal ]

    /// <summary>The mapped F# functions and operators.</summary>
    let values: SdkValue list = [
        value
            (operators
             + "op_Addition")
            numbers
            (sdk "Basics" "add")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Addition")
            decimals
            (sdk "Decimal" "add")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Addition")
            (OperandRule.OneOf [ SdkOperand.String ])
            (sdk "String" "append")
            2
            outsideString
        value
            (operators
             + "op_Subtraction")
            numbers
            (sdk "Basics" "subtract")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Subtraction")
            decimals
            (sdk "Decimal" "sub")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Multiply")
            numbers
            (sdk "Basics" "multiply")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Multiply")
            decimals
            (sdk "Decimal" "mul")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Division")
            (OperandRule.OneOf [ SdkOperand.Float ])
            (sdk "Basics" "divide")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Division")
            (OperandRule.OneOf [ SdkOperand.Int ])
            (sdk "Basics" "integerDivide")
            2
            SdkTier.Contract
        value
            (operators
             + "op_UnaryNegation")
            numbers
            (sdk "Basics" "negate")
            1
            SdkTier.Contract
        value
            (operators
             + "Checked.op_UnaryNegation")
            numbers
            (sdk "Basics" "negate")
            1
            SdkTier.Contract
        value
            (operators
             + "Abs")
            numbers
            (sdk "Basics" "abs")
            1
            SdkTier.Contract
        value
            (operators
             + "op_LessThan")
            OperandRule.Comparable
            (sdk "Basics" "lessThan")
            2
            SdkTier.Contract
        value
            (operators
             + "op_GreaterThan")
            OperandRule.Comparable
            (sdk "Basics" "greaterThan")
            2
            SdkTier.Contract
        value
            (operators
             + "op_LessThanOrEqual")
            OperandRule.Comparable
            (sdk "Basics" "lessThanOrEqual")
            2
            SdkTier.Contract
        value
            (operators
             + "op_GreaterThanOrEqual")
            OperandRule.Comparable
            (sdk "Basics" "greaterThanOrEqual")
            2
            SdkTier.Contract
        value
            (operators
             + "Min")
            OperandRule.Comparable
            (sdk "Basics" "min")
            2
            SdkTier.Contract
        value
            (operators
             + "Max")
            OperandRule.Comparable
            (sdk "Basics" "max")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Equality")
            OperandRule.Any
            (sdk "Basics" "equal")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Inequality")
            OperandRule.Any
            (sdk "Basics" "notEqual")
            2
            SdkTier.Contract
        value
            (operators
             + "Not")
            OperandRule.Any
            (sdk "Basics" "not")
            1
            outsideBoolean
        value
            (operators
             + "Identity")
            OperandRule.Any
            (sdk "Basics" "identity")
            1
            (SdkTier.Outside
                "Morphir SDK Basics.identity; the reviewed .NET SDK contract does not list it yet.")
        value
            (listModule
             + "Singleton")
            OperandRule.Any
            (sdk "List" "singleton")
            1
            SdkTier.Contract
        value
            (listModule
             + "Map")
            OperandRule.Any
            (sdk "List" "map")
            2
            SdkTier.Contract
        value
            (listModule
             + "Append")
            OperandRule.Any
            (sdk "List" "append")
            2
            SdkTier.Contract
        value
            (operators
             + "op_Append")
            OperandRule.Any
            (sdk "List" "append")
            2
            SdkTier.Contract
        value
            (listModule
             + "Length")
            OperandRule.Any
            (sdk "List" "length")
            1
            SdkTier.Contract
        value
            (listModule
             + "TryHead")
            OperandRule.Any
            (sdk "List" "head")
            1
            SdkTier.Contract
        value
            (optionModule
             + "Map")
            OperandRule.Any
            (sdk "Maybe" "map")
            2
            SdkTier.Contract
        value
            (optionModule
             + "Bind")
            OperandRule.Any
            (sdk "Maybe" "andThen")
            2
            SdkTier.Contract
        value
            (resultModule
             + "Map")
            OperandRule.Any
            (sdk "Result" "map")
            2
            SdkTier.Contract
        value
            (resultModule
             + "Bind")
            OperandRule.Any
            (sdk "Result" "andThen")
            2
            SdkTier.Contract
        value
            (stringModule
             + "Length")
            OperandRule.Any
            (sdk "String" "length")
            1
            outsideString
    ]

    let private case name target kind arity = {
        Case = name
        Target = target
        Kind = kind
        Arity = arity
        Tier = SdkTier.Contract
    }

    /// <summary>The mapped F# union cases.</summary>
    let cases: SdkCase list = [
        case
            "Microsoft.FSharp.Core.FSharpOption`1.Some"
            (sdk "Maybe" "Just")
            SdkCaseKind.Constructor
            1
        case
            "Microsoft.FSharp.Core.FSharpOption`1.None"
            (sdk "Maybe" "Nothing")
            SdkCaseKind.Constructor
            0
        case "Microsoft.FSharp.Core.FSharpResult`2.Ok" (sdk "Result" "Ok") SdkCaseKind.Constructor 1
        case
            "Microsoft.FSharp.Core.FSharpResult`2.Error"
            (sdk "Result" "Err")
            SdkCaseKind.Constructor
            1
        case
            "Microsoft.FSharp.Collections.FSharpList`1.op_ColonColon"
            (sdk "List" "cons")
            SdkCaseKind.Function
            2
    ]

    let private sdkType name target order tier = {
        Type = name
        Target = target
        ArgumentOrder = order
        Tier = tier
    }

    /// <summary>The mapped F# type definitions. F# <c>Result</c> puts the error type last; the SDK puts it first.</summary>
    let types: SdkType list = [
        sdkType "System.Int32" (sdk "Basics" "Int") [] SdkTier.Contract
        sdkType
            "System.Int64"
            (sdk "Int" "Int64")
            []
            (SdkTier.Outside
                "Morphir SDK Int.Int64; the reviewed .NET SDK contract has no operation on it yet.")
        sdkType "System.Boolean" (sdk "Basics" "Bool") [] SdkTier.Contract
        sdkType "System.Double" (sdk "Basics" "Float") [] SdkTier.Contract
        sdkType "System.Decimal" (sdk "Decimal" "Decimal") [] SdkTier.Contract
        sdkType
            "System.Char"
            (sdk "Char" "Char")
            []
            (SdkTier.Outside
                "Morphir SDK Char.Char; the dotnet-sdk-v1 text profile defines it, but no reviewed operation uses it yet.")
        sdkType "System.String" (sdk "String" "String") [] SdkTier.Contract
        sdkType "Microsoft.FSharp.Core.FSharpOption`1" (sdk "Maybe" "Maybe") [ 0 ] SdkTier.Contract
        sdkType
            "Microsoft.FSharp.Collections.FSharpList`1"
            (sdk "List" "List")
            [ 0 ]
            SdkTier.Contract
        sdkType
            "Microsoft.FSharp.Core.FSharpResult`2"
            (sdk "Result" "Result")
            [ 1; 0 ]
            SdkTier.Contract
    ]

    /// <summary>The F# operators that are lowered by their structure.</summary>
    let structural: (string * StructuralOperator) list = [
        operators
        + "op_PipeRight",
        StructuralOperator.PipeRight
        operators
        + "op_PipeLeft",
        StructuralOperator.PipeLeft
        operators
        + "op_ComposeRight",
        StructuralOperator.ComposeRight
        operators
        + "op_ComposeLeft",
        StructuralOperator.ComposeLeft
        intrinsic
        + "op_BooleanAnd",
        StructuralOperator.BooleanAnd
        intrinsic
        + "op_BooleanOr",
        StructuralOperator.BooleanOr
    ]

    let private entityName (entity: FSharpEntity) =
        match entity.TryFullName with
        | Some name -> name
        | None -> $"{entity.AccessPath}.{entity.CompiledName}"

    /// <summary>The table key of an F# function or operator: its declaring entity and compiled name.</summary>
    let valueKey (value: FSharpMemberOrFunctionOrValue) =
        match value.DeclaringEntity with
        | Some entity -> $"{entityName entity}.{value.CompiledName}"
        | None -> value.CompiledName

    /// <summary>The table key of an F# union case: its union type and its name.</summary>
    let caseKey (case: FSharpUnionCase) =
        $"{entityName case.DeclaringEntity}.{case.Name}"

    /// <summary>The operand shape of a checked type.</summary>
    let rec operand (original: FSharpType) : SdkOperand =
        let typ = original.StripAbbreviations()

        let text () =
            original.Format(FSharpDisplayContext.Empty.WithShortTypeNames true)

        if
            typ.IsTupleType
            && not typ.IsStructTupleType
        then
            SdkOperand.Tuple(
                typ.GenericArguments
                |> Seq.map operand
                |> List.ofSeq
            )
        elif typ.HasTypeDefinition then
            match entityName typ.TypeDefinition with
            | "System.Int32" -> SdkOperand.Int
            | "System.Double" -> SdkOperand.Float
            | "System.Decimal" -> SdkOperand.Decimal
            | "System.String" -> SdkOperand.String
            | "System.Char" -> SdkOperand.Char
            | "System.Boolean" -> SdkOperand.Bool
            | "Microsoft.FSharp.Collections.FSharpList`1" when typ.GenericArguments.Count = 1 ->
                SdkOperand.List(operand typ.GenericArguments[0])
            | _ -> SdkOperand.Other(text ())
        else
            SdkOperand.Other(text ())

    /// <summary>True when the table has an entry for this key, for any operand type.</summary>
    let isMapped (key: string) =
        values
        |> List.exists (fun entry -> entry.Symbol = key)

    /// <summary>The entry for an F# function or operator key and the operand type of its first parameter.</summary>
    let tryValue (key: string) (operandType: SdkOperand option) : SdkValue option =
        values
        |> List.tryFind (fun entry ->
            entry.Symbol = key
            && entry.Operands.Accepts operandType
        )

    /// <summary>The entry for an F# union case key.</summary>
    let tryCase (key: string) : SdkCase option =
        cases
        |> List.tryFind (fun entry -> entry.Case = key)

    /// <summary>The structural operator for an F# function or operator key.</summary>
    let tryStructural (key: string) : StructuralOperator option =
        structural
        |> List.tryPick (fun (symbol, operator) -> if symbol = key then Some operator else None)

    /// <summary>The entry for the full name of an F# type definition.</summary>
    let tryType (fullName: string) : SdkType option =
        types
        |> List.tryFind (fun entry -> entry.Type = fullName)

    /// <summary>The SDK type arguments, in SDK order, or None when the F# argument count differs.</summary>
    let orderArguments (entry: SdkType) (arguments: 'a list) : 'a list option =
        match entry.ArgumentOrder with
        | [] when arguments.IsEmpty -> Some []
        | order when order.Length = arguments.Length ->
            Some(
                order
                |> List.map (fun index -> arguments[index])
            )
        | _ -> None
