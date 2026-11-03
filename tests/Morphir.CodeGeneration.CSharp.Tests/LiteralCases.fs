module Morphir.CodeGeneration.CSharp.Tests.LiteralCases

open System
open System.Numerics
open System.Text.Json
open Morphir.IR
open Morphir.IR.Model

let n = Name.fromString
let pkg = PackageName.packageNameFromString "example"
let path = ModulePath.modulePathFromString "class"
let a = Unchecked.defaultof<JsonElement>
let sdk modul local = TypeCases.sdk modul local

let classicSingle name output body =
    let definition: Morphir.IR.Classic.ValueDefinition<JsonElement, JsonElement> = {
        InputTypes = []
        OutputType = output
        Body = body
    }

    let modul: Morphir.IR.Classic.Module.ModuleDefinition<JsonElement, JsonElement> = {
        Types = Map.empty
        Values =
            Map.ofList [
                n name,
                Morphir.IR.Classic.AccessControlled.public' (
                    Morphir.IR.Classic.Documented.WithoutDocumentation definition
                )
            ]
        Doc = None
    }

    {
        SourceVersion = TypeCases.version "3"
        Distribution =
            DistributionModel.Classic(
                Morphir.IR.Classic.Distribution.Library(
                    pkg,
                    Map.empty,
                    {
                        Modules =
                            Map.ofList[path, Morphir.IR.Classic.AccessControlled.public' modul]
                    }
                )
            )
    }

let v4Single name output body =
    let definition: Morphir.IR.V4.ValueDefinition = {
        InputTypes = []
        Body = Morphir.IR.V4.ExpressionBody(output, body)
    }

    let modul: Morphir.IR.V4.ModuleDefinition = {
        Types = Morphir.IR.V4.OrderedMap.empty
        Values =
            TypeCases.ordered [
                n name,
                {
                    Access = Morphir.IR.V4.Public
                    Value = { Doc = None; Value = definition }
                }
            ]
        Doc = None
    }

    {
        SourceVersion = TypeCases.version "4"
        Distribution =
            DistributionModel.V4(
                Morphir.IR.V4.Library(
                    pkg,
                    Morphir.IR.V4.OrderedMap.empty,
                    {
                        Modules =
                            TypeCases.ordered[path,
                                              {
                                                  Access = Morphir.IR.V4.Public
                                                  Value = modul
                                              }]
                    }
                )
            )
    }

let text = "\u0000\"\\\r\n\t\u2028\U0001F600"

let classicLiteral name modul typ literal =
    classicSingle
        name
        (Morphir.IR.Classic.Type.Reference(a, sdk modul typ, []))
        (Morphir.IR.Classic.Value.Literal(a, literal))

let v4Literal name modul typ literal =
    v4Single
        name
        (Morphir.IR.V4.Type.Reference(Morphir.IR.V4.TypeAttributes.empty, sdk modul typ, []))
        (Morphir.IR.V4.Value.Literal(Morphir.IR.V4.ValueAttributes.empty, literal))

let admitted = [
    "classic-string",
    classicLiteral "value" "string" "string" (Morphir.IR.Classic.StringLiteral text),
    "return Example.ClassModule.Value() == \"\\u0000\\\"\\\\\\r\\n\\t\\u2028\\U0001F600\" ? 1 : 0;"
    "v4-string",
    v4Literal "value" "string" "string" (Morphir.IR.V4.StringLiteral text),
    "return Example.ClassModule.Value() == \"\\u0000\\\"\\\\\\r\\n\\t\\u2028\\U0001F600\" ? 1 : 0;"
    "classic-char",
    classicLiteral "value" "char" "char" (Morphir.IR.Classic.CharLiteral '\''),
    "return Example.ClassModule.Value() == '\\'' ? 1 : 0;"
    "v4-char",
    v4Literal "value" "char" "char" (Morphir.IR.V4.CharLiteral "'"),
    "return Example.ClassModule.Value() == '\\'' ? 1 : 0;"
    "classic-bool",
    classicLiteral "value" "basics" "bool" (Morphir.IR.Classic.BoolLiteral true),
    "return Example.ClassModule.Value() ? 1 : 0;"
    "v4-bool",
    v4Literal "value" "basics" "bool" (Morphir.IR.V4.BoolLiteral true),
    "return Example.ClassModule.Value() ? 1 : 0;"
    "classic-decimal",
    classicLiteral "value" "decimal" "decimal" (Morphir.IR.Classic.DecimalLiteral 1.2300M),
    "return Example.ClassModule.Value() == 1.23m ? 1 : 0;"
    "v4-decimal",
    v4Literal
        "value"
        "decimal"
        "decimal"
        (Morphir.IR.V4.DecimalLiteral(
            Morphir.IR.V4.DecimalLexeme.create "1.2300"
            |> Result.defaultWith failwith
        )),
    "return Example.ClassModule.Value() == 1.23m ? 1 : 0;"
    "classic-negative-zero",
    classicLiteral "value" "basics" "float" (Morphir.IR.Classic.FloatLiteral -0.0),
    "return System.BitConverter.DoubleToInt64Bits(Example.ClassModule.Value()) == long.MinValue ? 1 : 0;"
    "v4-negative-zero",
    v4Literal
        "value"
        "basics"
        "float"
        (Morphir.IR.V4.FloatLiteral(
            Morphir.IR.V4.FloatValue.create -0.0 None
            |> Result.defaultWith failwith
        )),
    "return System.BitConverter.DoubleToInt64Bits(Example.ClassModule.Value()) == long.MinValue ? 1 : 0;"
    "classic-nan",
    classicLiteral "value" "basics" "float" (Morphir.IR.Classic.FloatLiteral Double.NaN),
    "return double.IsNaN(Example.ClassModule.Value()) ? 1 : 0;"
]

let refused = [
    "classic-large-int",
    "representation-loss",
    classicLiteral "value" "basics" "int" (Morphir.IR.Classic.WholeNumberLiteral Int64.MaxValue)
    "v4-large-int",
    "representation-loss",
    v4Literal "value" "basics" "int" (Morphir.IR.V4.IntegerLiteral(BigInteger.Pow(10I, 40)))
    "classic-surrogate-char",
    "representation-loss",
    classicLiteral "value" "char" "char" (Morphir.IR.Classic.CharLiteral '\uD800')
    "v4-supplementary-char",
    "representation-loss",
    v4Literal "value" "char" "char" (Morphir.IR.V4.CharLiteral "\U0001F600")
    "classic-invalid-string",
    "representation-loss",
    classicLiteral
        "value"
        "string"
        "string"
        (Morphir.IR.Classic.StringLiteral(System.String(char 0xD800, 1)))
    "v4-invalid-string",
    "representation-loss",
    v4Literal "value" "string" "string" (Morphir.IR.V4.StringLiteral(System.String(char 0xD800, 1)))
    "v4-inexact-decimal",
    "representation-loss",
    v4Literal
        "value"
        "decimal"
        "decimal"
        (Morphir.IR.V4.DecimalLiteral(
            Morphir.IR.V4.DecimalLexeme.create "0.00000000000000000000000000001"
            |> Result.defaultWith failwith
        ))
    "classic-type-mismatch",
    "type-mismatch",
    classicLiteral "value" "basics" "int" (Morphir.IR.Classic.BoolLiteral true)
]
