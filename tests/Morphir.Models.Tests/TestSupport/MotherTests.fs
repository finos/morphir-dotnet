/// Checks the object mother and the generators of Morphir.Models.TestSupport: every named case is a model that the
/// existing writers encode, or the known refusal its name stands for, and generated models encode too.
module Morphir.Models.Tests.TestSupport.MotherTests

open System.Text
open Expecto
open FsCheck
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.Classic
open Morphir.Json.V4
open Morphir.Models.TestSupport

let private v4Json (distribution: Distribution) =
    match V4JsonWriter.writeDistribution false distribution with
    | Ok bytes -> Encoding.UTF8.GetString bytes
    | Error error -> failtestf "Expected a v4 write, got %s at %s: %s" error.Rule error.Location error.Message

let private v3Json distribution =
    match ClassicJsonWriter.writeTyped ClassicWireProfile.v3 false distribution with
    | Ok(bytes: byte array) -> Encoding.UTF8.GetString bytes
    | Error(error: ClassicWriteError) ->
        failtestf "Expected a v3 write, got %s at %s: %s" error.Rule error.Location error.Message

let private occurrences (text: string) (json: string) =
    let rec count (from: int) found =
        match json.IndexOf(text, from, System.StringComparison.Ordinal) with
        | -1 -> found
        | index -> count (index + text.Length) (found + 1)

    count 0 0

let private v4Cases = [
    "v4LibraryWithOneModule", DistributionMother.v4LibraryWithOneModule
    "v4LibraryWithDependency", DistributionMother.v4LibraryWithDependency
    "v4Specs", DistributionMother.v4Specs
    "v4Application", DistributionMother.v4Application
    "emptyModule", DistributionMother.emptyModule
    "noModules", DistributionMother.noModules
    "nameUsedOnce", DistributionMother.nameUsedOnce
    "nameUsedOften", DistributionMother.nameUsedOften
    "windowsReservedStems", DistributionMother.windowsReservedStems
]

let private v3Cases = [
    "v3Library", DistributionMother.v3Library
    "nonAsciiName", DistributionMother.nonAsciiName
]

let private modulesOf (distribution: Distribution) =
    match distribution with
    | Library(_, _, definition)
    | Application(_, _, definition, _) ->
        OrderedMap.toList definition.Modules
        |> List.map (fun (path, m) -> path, OrderedMap.keys m.Value.Types, OrderedMap.keys m.Value.Values)
    | Specs(_, _, specification) ->
        OrderedMap.toList specification.Modules
        |> List.map (fun (path, m) -> path, OrderedMap.keys m.Types, OrderedMap.keys m.Values)

let private config = {
    FsCheckConfig.defaultConfig with
        maxTest = 200
}

[<Tests>]
let tests =
    testList "Mothers" [
        testList "every mother case builds and encodes" [
            for name, distribution in v4Cases do
                test name { v4Json distribution |> ignore }
            for name, distribution in v3Cases do
                test name { v3Json distribution |> ignore }
        ]

        testList "a case that stands for a refusal is refused as expected" [
            test "v3Specs is refused by the classic writer until the 3.1.0 Specs writer exists" {
                match ClassicJsonWriter.writeTyped ClassicWireProfile.v3 false DistributionMother.v3Specs with
                | Ok _ -> failtest "The classic writer wrote a Specs distribution; v3Specs now belongs with the cases that encode."
                | Error error -> Expect.equal error.Rule "unsupported-distribution" "the known refusal"
            }

            test "collidingStems spells two different type names the same way, so the v4 writer refuses it" {
                let types =
                    modulesOf DistributionMother.collidingStems
                    |> List.collect (fun (_, types, _) -> types)

                Expect.hasLength types 2 "one module with two types"
                Expect.notEqual types[0] types[1] "two different names"

                Expect.equal
                    (CanonicalNames.nameToString types[0])
                    (CanonicalNames.nameToString types[1])
                    "one spelling, so one stem"

                match V4JsonWriter.writeDistribution false DistributionMother.collidingStems with
                | Ok _ -> failtest "A name with no canonical spelling was written."
                | Error error -> Expect.equal error.Rule "invalid_name" "the known refusal"
            }
        ]

        testList "each case has the shape its name says" [
            test "v4Specs is a Specs distribution and v4Application an Application with an entry point" {
                Expect.isTrue
                    (match DistributionMother.v4Specs with
                     | Specs _ -> true
                     | _ -> false)
                    "Specs"

                match DistributionMother.v4Application with
                | Application(_, _, _, entryPoints) -> Expect.isNonEmpty entryPoints "an entry point"
                | other -> failtestf "Expected an Application, got %A" other
            }

            test "v4LibraryWithDependency has one dependency, and v4LibraryWithOneModule one module" {
                match DistributionMother.v4LibraryWithDependency with
                | Library(_, dependencies, _) -> Expect.equal (OrderedMap.count dependencies) 1 "one dependency"
                | other -> failtestf "Expected a Library, got %A" other

                Expect.hasLength (modulesOf DistributionMother.v4LibraryWithOneModule) 1 "one module"
            }

            test "emptyModule has one module with no types or values, and noModules has no module" {
                match modulesOf DistributionMother.emptyModule with
                | [ _, [], [] ] -> ()
                | other -> failtestf "Expected one empty module, got %A" other

                Expect.isEmpty (modulesOf DistributionMother.noModules) "no module"
            }

            test "the name of nameUsedOnce is written once, and the name of nameUsedOften many times" {
                let text = CanonicalNames.nameToString DistributionMother.Names.repeated

                Expect.equal (occurrences text (v4Json DistributionMother.nameUsedOnce)) 1 "used once"
                Expect.isGreaterThanOrEqual (occurrences text (v4Json DistributionMother.nameUsedOften)) 10 "used often"
            }

            test "nonAsciiName has a type name with an é" {
                match DistributionMother.nonAsciiName with
                | Morphir.IR.Classic.Distribution.Library(_, _, definition) ->
                    let names =
                        definition.Modules
                        |> Map.toList
                        |> List.collect (fun (_, m) -> Map.keys m.Value.Types |> List.ofSeq)

                    Expect.contains names DistributionMother.Names.nonAscii "the type"

                    Expect.stringContains
                        (String.concat "" (Name.toList DistributionMother.Names.nonAscii))
                        "é"
                        "the name"
                | other -> failtestf "Expected a Library, got %A" other
            }

            test "windowsReservedStems has a module and types named for Windows device names" {
                let reserved = [ "con"; "aux"; "nul" ]

                match modulesOf DistributionMother.windowsReservedStems with
                | [ ModulePath(Path [ moduleName ]), types, _ ] ->
                    Expect.contains reserved (CanonicalNames.nameToString moduleName) "the module"

                    Expect.equal
                        (types |> List.map CanonicalNames.nameToString)
                        reserved
                        "the types"
                | other -> failtestf "Expected one module, got %A" other
            }
        ]

        testList "generated distributions encode" [
            testPropertyWithConfig config "v4"
            <| Prop.forAll (Arb.fromGen Generators.v4Distribution) (fun distribution -> v4Json distribution |> ignore)

            testPropertyWithConfig config "v3"
            <| Prop.forAll (Arb.fromGen Generators.v3Distribution) (fun distribution -> v3Json distribution |> ignore)
        ]
    ]
