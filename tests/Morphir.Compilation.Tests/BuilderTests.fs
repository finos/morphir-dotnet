module Morphir.Compilation.Tests.BuilderTests

open System.Numerics
open System.Text
open System.Text.Json
open Expecto
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR
open Morphir.IR.Model

let private at line character length =
    Location.Create(
        "Main.fs",
        Range.Create(
            Position.Create(line, character),
            Position.Create(
                line,
                character
                + length
            )
        )
    )

let private here = at 2 4 3
let private n (text: string) = Name.fromString text

let private fq (pkg: string) (m: string) (local: string) = {
    PackagePath = PackageName.packageName (Path.fromList [ n pkg ])
    ModulePath = ModulePath.modulePath (Path.fromList [ n m ])
    LocalName = n local
}

let private value (result: Checked<'T>) =
    match result with
    | Passed(value, []) -> value
    | other -> failtest $"Expected a passed result without diagnostics, got {other}"

let private codes (result: Checked<'T>) =
    result.Diagnostics
    |> Seq.map (fun d ->
        d.CodeOrNull
        |> Option.ofObj
        |> Option.map (fun c -> c.Value)
    )
    |> List.ofSeq

let private classic = ClassicBuilder() :> IIrBuilder<_, _, _, _, _, _, _>
let private v4 = V4Builder() :> IIrBuilder<_, _, _, _, _, _, _>

let private classicInt = Classic.Type.Reference((), fq "morphir" "basics" "int", [])

let private v4Source: V4.SourceLocation = {
    StartLine = 3
    StartColumn = 5
    EndLine = 3
    EndColumn = 8
}

let private v4Int =
    V4.Type.Reference(V4.TypeAttributes.empty, fq "morphir" "basics" "int", [])

let private v4Attributes =
    ({
        Source = Some v4Source
        InferredType = Some v4Int
        Extensions = None
    }
    : V4.ValueAttributes)

/// One small package, lowered once against any builder: Main.rate = let base = 60 in base.
type private SampleProgram(literal: LiteralValue) =
    interface IBuilderProgram with
        member _.Run(builder) = check {
            let! intType = builder.TypeReference(here, fq "morphir" "basics" "int", [])
            let! sixty = builder.Literal(here, intType, literal)
            let! inner = builder.ValueDefinition(here, [], intType, sixty)
            let! variable = builder.Variable(here, intType, n "base")
            let! body = builder.LetDefinition(here, intType, n "base", inner, variable)
            let! rate = builder.ValueDefinition(here, [], intType, body)

            let plan = {
                Path = ModulePath.modulePath (Path.fromList [ n "main" ])
                Access = Access.Public
                Documentation = None
                Location = here
            }

            let! m =
                builder.Module(
                    plan,
                    [],
                    [
                        {
                            Name = n "rate"
                            Access = Access.Public
                            Documentation = Some "The rate."
                            Definition = rate
                            Location = here
                        }
                    ]
                )

            return!
                builder.Package(
                    {
                        Package = PackageName.packageName (Path.fromList [ n "acme" ])
                        Documentation = None
                    },
                    [ m ]
                )
        }

[<Tests>]
let tests =
    testList "Native builders" [
        testList "ClassicBuilder" [
            test "values carry their inferred type and no source" {
                Expect.equal
                    (value (classic.Variable(here, classicInt, n "x")))
                    (Classic.Value.Variable(classicInt, n "x"))
                    "variable"

                Expect.equal
                    (value (classic.Literal(here, classicInt, LiteralValue.Integer(BigInteger 7))))
                    (Classic.Value.Literal(classicInt, Classic.WholeNumberLiteral 7L))
                    "literal"

                Expect.equal
                    (value (classic.PatternWildcard(here, classicInt)))
                    (Classic.WildcardPattern classicInt)
                    "pattern"
            }

            test "types have unit attributes" {
                Expect.equal
                    (value (classic.TypeVariable(here, n "a")))
                    (Classic.Type.Variable((), n "a"))
                    "variable"

                Expect.equal
                    (value (classic.TypeRecord(here, [ n "seats", classicInt ])))
                    (Classic.Type.Record((), [ { Name = n "seats"; Type = classicInt } ]))
                    "record"
            }

            test "an integer outside int64 is unrepresentable at its location" {
                let result =
                    classic.Literal(
                        here,
                        classicInt,
                        LiteralValue.Integer(BigInteger.Pow(BigInteger 2, 64))
                    )

                Expect.equal (codes result) [ Some "morphir.unrepresentable" ] "code"
                Expect.equal (Diagnostic.location result.Diagnostics[0]) (Some here) "location"
            }

            test "a character outside the BMP is unrepresentable" {
                let result = classic.Literal(here, classicInt, LiteralValue.Char(Rune 0x1F600))
                Expect.equal (codes result) [ Some "morphir.unrepresentable" ] "code"
            }

            test "duplicate record fields are refused" {
                Expect.equal
                    (codes (
                        classic.Record(
                            here,
                            classicInt,
                            [
                                n "a", Classic.Value.Unit classicInt
                                n "a", Classic.Value.Unit classicInt
                            ]
                        )
                    ))
                    [ Some "compile.duplicate_name" ]
                    "code"
            }
        ]

        testList "V4Builder" [
            test "values carry their inferred type and a 1-based source" {
                Expect.equal
                    (value (v4.Variable(here, v4Int, n "x")))
                    (V4.Value.Variable(v4Attributes, n "x"))
                    "variable"

                Expect.equal
                    (value (v4.PatternWildcard(here, v4Int)))
                    (V4.WildcardPattern v4Attributes)
                    "pattern"
            }

            test "types carry their source" {
                Expect.equal
                    (value (v4.TypeVariable(here, n "a")))
                    (V4.Type.Variable(
                        { V4.TypeAttributes.empty with
                            Source = Some v4Source
                        },
                        n "a"
                    ))
                    "variable"
            }

            test "v4 holds what classic refuses" {
                Expect.equal
                    (value (
                        v4.Literal(
                            here,
                            v4Int,
                            LiteralValue.Integer(BigInteger.Pow(BigInteger 2, 64))
                        )
                    ))
                    (V4.Value.Literal(
                        v4Attributes,
                        V4.IntegerLiteral(BigInteger.Pow(BigInteger 2, 64))
                    ))
                    "large integer"

                Expect.equal
                    (value (v4.Literal(here, v4Int, LiteralValue.Char(Rune 0x1F600))))
                    (V4.Value.Literal(v4Attributes, V4.CharLiteral "😀"))
                    "non-BMP character"
            }

            test "record field order is kept" {
                match
                    value (
                        v4.Record(
                            here,
                            v4Int,
                            [ n "b", V4.Value.Unit v4Attributes; n "a", V4.Value.Unit v4Attributes ]
                        )
                    )
                with
                | V4.Value.Record(_, fields) ->
                    Expect.equal (List.map fst fields) [ n "b"; n "a" ] "order"
                | other -> failtest $"{other}"
            }
        ]

        testList "One lowering, both families" [
            test "the same program builds a classic and a v4 model, each written by its own writer" {
                let program = SampleProgram(LiteralValue.Integer(BigInteger 60))

                let builders =
                    FamilyBuilderRegistry.Create [
                        ClassicFamilyBuilder() :> IFamilyBuilder
                        V4FamilyBuilder()
                    ]

                let writers = WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter ]

                match
                    builders.Resolve ModelFamily.Classic
                    |> Checked.bind (fun b -> b.Build program)
                with
                | Passed(FamilyModel.Classic _ as model, []) ->
                    let written =
                        writers.Resolve(Versioning.Classic 3)
                        |> Checked.bind (fun r ->
                            r.Writer.Write(r.Target, WriteOptions.Compact, model)
                        )

                    Expect.stringContains
                        (Encoding.UTF8.GetString(value written))
                        "\"LetDefinition\""
                        "classic JSON"
                | other -> failtest $"classic: {other}"

                match
                    builders.Resolve ModelFamily.V4
                    |> Checked.bind (fun b -> b.Build program)
                with
                | Passed(FamilyModel.V4(V4.Library _), []) -> ()
                | other -> failtest $"v4: {other}"
            }

            test "the classic family refuses a value that v4 holds" {
                let program = SampleProgram(LiteralValue.Integer(BigInteger.Pow(BigInteger 2, 70)))
                let classicResult = (ClassicFamilyBuilder() :> IFamilyBuilder).Build program
                let v4Result = (V4FamilyBuilder() :> IFamilyBuilder).Build program

                Expect.equal
                    (codes classicResult)
                    [ Some "morphir.unrepresentable" ]
                    "classic refuses"

                Expect.isTrue v4Result.IsPassed "v4 holds it"
            }
        ]
    ]

/// No code path converts a model from one family to another (ir-model-writing "Supplied input is not migrated").
/// The generate loader keeps each family: GenerationModelLoaderTests.JsonUsesSharedCodecsAndPreservesExactVersion
/// checks it for 1, 2, 3, 3.1.0, 4 and 4.0.0. This guard fails if a family conversion appears in production code.
[<Tests>]
let familyGuards =
    testList "No family conversion" [
        test "each family builder produces its own family" {
            for builder in [ ClassicFamilyBuilder() :> IFamilyBuilder; V4FamilyBuilder() ] do
                match builder.Build(SampleProgram(LiteralValue.Integer(BigInteger 1))) with
                | Passed(model, _) -> Expect.equal model.Family builder.Family "family"
                | other -> failtest $"{other}"
        }

        test "no production source defines a conversion between families" {
            let rec root (directory: System.IO.DirectoryInfo | null) =
                match directory with
                | null -> failtest "Repository root not found"
                | d when System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "Morphir.slnx")) ->
                    d.FullName
                | d -> root d.Parent

            let src =
                System.IO.Path.Combine(
                    root (System.IO.DirectoryInfo(System.AppContext.BaseDirectory)),
                    "src"
                )

            let pattern =
                System.Text.RegularExpressions.Regex(
                    @"(?i)\b(classic_?to_?v4|v4_?to_?classic|promoteToV4|demoteToClassic)\b"
                )

            let offenders = [
                for file in
                    System.IO.Directory.EnumerateFiles(
                        src,
                        "*.*",
                        System.IO.SearchOption.AllDirectories
                    ) do
                    if
                        (file.EndsWith ".fs"
                         || file.EndsWith ".cs")
                        && not (
                            file.Contains "/obj/"
                            || file.Contains "/bin/"
                        )
                    then
                        if pattern.IsMatch(System.IO.File.ReadAllText file) then
                            yield file
            ]

            Expect.isEmpty offenders "production code converts a model between families"
        }
    ]
