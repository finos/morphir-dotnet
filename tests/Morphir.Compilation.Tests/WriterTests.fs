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

let private v4Library (package: string) : Morphir.IR.V4.Distribution =
    let definition: Morphir.IR.V4.PackageDefinition = {
        Modules = Morphir.IR.V4.OrderedMap.empty
    }

    Morphir.IR.V4.Distribution.Library(
        PackageName.packageName (Path.fromList [ Name.fromList [ package ] ]),
        Morphir.IR.V4.OrderedMap.empty,
        definition
    )

/// A test-only writer for the 4.1.0-draft.1 pre-release, written with the v4 JSON writer.
[<Sealed>]
type private DraftV4Writer() =
    static member Identity = ComponentId.Create "test.v4-draft"

    interface IIrWriter with
        member _.Identity = DraftV4Writer.Identity
        member _.Family = ModelFamily.V4

        member _.Versions =
            [| version "4.1.0-draft.1" |] :> System.Collections.Generic.IReadOnlyList<_>

        member _.Write(target, options, model) =
            match model with
            | FamilyModel.V4 distribution ->
                match
                    Morphir.Json.V4.V4JsonWriter.writeDocument
                        options.Indented
                        target.Version
                        distribution
                with
                | Ok bytes -> Checked.pass bytes
                | Error error ->
                    Checked.fail [ Diagnostic.error WriterCodes.Unrepresentable error.Message ]
            | FamilyModel.Classic _ ->
                Checked.fail [
                    Diagnostic.error
                        WriterCodes.FamilyMismatch
                        "The draft writer received a classic model."
                ]

let private v4Registry =
    WriterRegistry.Create [
        ClassicIrWriter() :> IIrWriter
        V4IrWriter() :> IIrWriter
        DraftV4Writer() :> IIrWriter
    ]

let private resolvedIdentity requested =
    match v4Registry.Resolve(version requested) with
    | Passed(resolved, []) -> Some resolved.Writer.Identity
    | _ -> None

let private writeV4 requested model =
    v4Registry.Resolve(version requested)
    |> Checked.bind (fun resolved ->
        resolved.Writer.Write(resolved.Target, WriteOptions.Compact, FamilyModel.V4 model)
    )

[<Tests>]
let v4Tests =
    testList "V4IrWriter" [
        for requested in [ "4"; "4.0.0" ] do
            test $"version {requested} selects the v4 writer and writes formatVersion 4" {
                Expect.equal
                    (resolvedIdentity requested)
                    (Some V4IrWriter.Identity)
                    "selected writer"

                let model = v4Library "acme"

                match writeV4 requested model with
                | Passed(bytes, []) ->
                    Expect.stringStarts
                        (Encoding.UTF8.GetString bytes)
                        "{\"formatVersion\":4,"
                        "spelling"

                    use document = JsonDocument.Parse(System.ReadOnlyMemory bytes)

                    match Morphir.Json.V4.V4Json.readDistribution document.RootElement with
                    | Ok read -> Expect.equal read.Distribution model "reads back"
                    | Error error -> failtest error.Message
                | other -> failtest $"{other}"
            }

        test "a writer declared for 4.1.0-draft.1 is selected for that exact version" {
            Expect.equal
                (resolvedIdentity "4.1.0-draft.1")
                (Some DraftV4Writer.Identity)
                "selected writer"

            match writeV4 "4.1.0-draft.1" (v4Library "acme") with
            | Passed(bytes, []) ->
                Expect.stringStarts
                    (Encoding.UTF8.GetString bytes)
                    "{\"formatVersion\":\"4.1.0-draft.1\","
                    "spelling"
            | other -> failtest $"{other}"
        }

        for requested in [ "4.1.0"; "4.1.0-draft.2" ] do
            test $"no writer is selected for {requested}" {
                Expect.equal
                    (codes (v4Registry.Resolve(version requested)))
                    [ Some "compile.unsupported_ir_version" ]
                    "code"
            }

        test "version 4 does not select the draft writer" {
            Expect.notEqual (resolvedIdentity "4") (Some DraftV4Writer.Identity) "selected writer"
        }

        test "a classic model is a family mismatch" {
            let writer = V4IrWriter() :> IIrWriter

            let result =
                writer.Write(
                    {
                        Family = ModelFamily.V4
                        Version = version "4"
                    },
                    WriteOptions.Compact,
                    sample (BoolLiteral true)
                )

            Expect.equal (codes result) [ Some "compile.ir_family_mismatch" ] "code"
        }

        test "a name with no canonical spelling is unrepresentable" {
            Expect.equal
                (codes (writeV4 "4" (v4Library "Acme")))
                [ Some "morphir.unrepresentable" ]
                "code"
        }
    ]
