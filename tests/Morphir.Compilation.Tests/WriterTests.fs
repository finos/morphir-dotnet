module Morphir.Compilation.Tests.WriterTests

open System.Text
open System.Text.Json
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented
open Morphir.IR.Model
open Morphir.Json.Classic

let private version text =
    match Versioning.parse text with
    | Ok version -> version
    | Error error -> failwith $"{text}: {error}"

let private sample (literal: Literal) =
    let n (text: string) = Name.fromString text
    let unitType = Type.Unit()

    let definition: ValueDefinition<unit, Type<unit>> = {
        InputTypes = []
        OutputType = unitType
        Body = Value.Literal(unitType, literal)
    }

    let moduleDefinition: Module.ModuleDefinition<unit, Type<unit>> = {
        Types = Map.empty
        Values =
            Map.ofList [
                n "value",
                {
                    Access = Public
                    Value = WithoutDocumentation definition
                }
            ]
        Doc = None
    }

    FamilyModel.Classic(
        Library(
            PackageName.packageName (Path.fromList [ n "acme" ]),
            Map.empty,
            {
                Modules =
                    Map.ofList [
                        ModulePath.modulePath (Path.fromList [ n "main" ]),
                        {
                            Access = Public
                            Value = moduleDefinition
                        }
                    ]
            }
        )
    )

let private registry = WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter ]

let private write requested model =
    registry.Resolve(version requested)
    |> Checked.bind (fun resolved ->
        resolved.Writer.Write(resolved.Target, WriteOptions.Compact, model)
    )

let private codes (result: Checked<'T>) =
    result.Diagnostics
    |> Seq.map (fun d ->
        d.CodeOrNull
        |> Option.ofObj
        |> Option.map (fun c -> c.Value)
    )
    |> List.ofSeq

[<Tests>]
let tests =
    testList "ClassicIrWriter" [
        for requested, written in [ "1", 1; "2", 2; "3", 3; "3.0.0", 3 ] do
            test $"version {requested} writes formatVersion {written} that reads back" {
                match write requested (sample (WholeNumberLiteral 7L)) with
                | Passed(bytes, []) ->
                    Expect.stringStarts
                        (Encoding.UTF8.GetString bytes)
                        $"{{\"formatVersion\":{written},"
                        "spelling"

                    use document = JsonDocument.Parse(System.ReadOnlyMemory bytes)

                    match ClassicJson.readDistribution document.RootElement with
                    | Ok read -> Expect.equal read.Version written "reads back"
                    | Error error -> failtest error.Message
                | other -> failtest $"{other}"
            }

        test "4 is not a classic version" {
            Expect.equal
                (codes (write "4" (sample (BoolLiteral true))))
                [ Some "compile.unsupported_ir_version" ]
                "code"
        }

        test "a non-finite float is unrepresentable" {
            Expect.equal
                (codes (write "3" (sample (FloatLiteral infinity))))
                [ Some "morphir.unrepresentable" ]
                "code"
        }

        test "a v4 model is a family mismatch" {
            let writer = ClassicIrWriter() :> IIrWriter
            let v4 = FamilyModel.V4(Unchecked.defaultof<_>)

            let result =
                writer.Write(
                    {
                        Family = ModelFamily.Classic
                        Version = version "3"
                    },
                    WriteOptions.Compact,
                    v4
                )

            Expect.equal (codes result) [ Some "compile.ir_family_mismatch" ] "code"
        }
    ]
