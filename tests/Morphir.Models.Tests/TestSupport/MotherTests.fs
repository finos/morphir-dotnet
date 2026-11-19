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

/// A classic Library is written at 3, and a Specs at 3.1.0, the version that introduced it.
let private v3Json (distribution: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>) =
    let profile =
        match distribution with
        | Morphir.IR.Classic.Distribution.Library _ -> ClassicWireProfile.v3
        | Morphir.IR.Classic.Distribution.Specs _ -> ClassicWireProfile.v3_1_0

    match ClassicJsonWriter.writeTyped profile false distribution with
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
    "v4SpecsWithModuleAnnotations", DistributionMother.v4SpecsWithModuleAnnotations
    "v4Application", DistributionMother.v4Application
    "acmeDemo", DistributionMother.acmeDemo
    "dependencyWithoutModules", DistributionMother.dependencyWithoutModules
    "emptyModule", DistributionMother.emptyModule
    "noModules", DistributionMother.noModules
    "nameUsedOnce", DistributionMother.nameUsedOnce
    "nameUsedOften", DistributionMother.nameUsedOften
    "longNameUsedOften", DistributionMother.longNameUsedOften
    "windowsReservedStems", DistributionMother.windowsReservedStems
    "privateAndDocumentedMembers", DistributionMother.privateAndDocumentedMembers
]

let private v3Cases = [
    "v3Library", DistributionMother.v3Library
    "v3Specs", DistributionMother.v3Specs
    "nonAsciiName", DistributionMother.nonAsciiName
    "v3PrivateAndDocumentedMembers", DistributionMother.v3PrivateAndDocumentedMembers
    "v3LongNameUsedOften", DistributionMother.v3LongNameUsedOften
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

/// What a model holds that a lossy conversion could drop, so a sample can show the generators produce it.
type private Features = {
    Private: bool
    Documented: bool
    TypeParameters: bool
    Specs: bool
    DependencyWithSeveralModules: bool
}

module private V4Features =
    let private typeDefinition (definition: TypeDefinition) =
        match definition with
        | TypeAliasDefinition(parameters, _)
        | IncompleteTypeDefinition(parameters, _, _) -> parameters <> [], false
        | CustomTypeDefinition(parameters, access, _) -> parameters <> [], access = Private

    let private typeSpecification (specification: TypeSpecification) =
        match specification with
        | TypeAliasSpecification(_, parameters, _)
        | OpaqueTypeSpecification(_, parameters)
        | CustomTypeSpecification(_, parameters, _)
        | DerivedTypeSpecification(_, parameters, _, _, _) -> parameters <> []

    let private definitionModule (m: AccessControlled<ModuleDefinition>) =
        let types = OrderedMap.toList m.Value.Types |> List.map snd
        let values = OrderedMap.toList m.Value.Values |> List.map snd
        let typeFacts = types |> List.map (fun t -> typeDefinition t.Value.Value)

        {|
            Private =
                m.Access = Private
                || types |> List.exists (fun t -> t.Access = Private)
                || values |> List.exists (fun v -> v.Access = Private)
                || typeFacts |> List.exists snd
            Documented =
                m.Value.Doc.IsSome
                || types |> List.exists (fun t -> t.Value.Doc.IsSome)
                || values |> List.exists (fun v -> v.Value.Doc.IsSome)
            TypeParameters = typeFacts |> List.exists fst
        |}

    let private specificationModule (m: ModuleSpecification) =
        let types = OrderedMap.toList m.Types |> List.map snd

        {|
            Private = false
            Documented =
                m.Doc.IsSome
                || types |> List.exists (fun t -> t.Doc.IsSome)
                || OrderedMap.toList m.Values |> List.exists (fun (_, v) -> v.Doc.IsSome)
            TypeParameters = types |> List.exists (fun t -> typeSpecification t.Value)
        |}

    let ofDistribution (distribution: Distribution) : Features =
        let modules, specs, dependencyModuleCounts =
            match distribution with
            | Library(_, dependencies, definition) ->
                (OrderedMap.toList definition.Modules |> List.map (snd >> definitionModule)),
                false,
                (OrderedMap.toList dependencies |> List.map (fun (_, d) -> OrderedMap.count d.Modules))
            | Specs(_, dependencies, specification) ->
                (OrderedMap.toList specification.Modules |> List.map (snd >> specificationModule)),
                true,
                (OrderedMap.toList dependencies |> List.map (fun (_, d) -> OrderedMap.count d.Modules))
            | Application(_, dependencies, definition, _) ->
                (OrderedMap.toList definition.Modules |> List.map (snd >> definitionModule)),
                false,
                (OrderedMap.toList dependencies |> List.map (fun (_, d) -> OrderedMap.count d.Modules))

        {
            Private = modules |> List.exists (fun m -> m.Private)
            Documented = modules |> List.exists (fun m -> m.Documented)
            TypeParameters = modules |> List.exists (fun m -> m.TypeParameters)
            Specs = specs
            DependencyWithSeveralModules = dependencyModuleCounts |> List.exists (fun count -> count > 1)
        }

module private V3Features =
    open Morphir.IR.Classic

    let private isPrivate (access: AccessControlled.Access) = access = AccessControlled.Private

    let private typeDefinition (definition: Type.TypeDefinition<unit>) =
        match definition with
        | Type.TypeAliasDefinition(parameters, _) -> parameters <> [], false
        | Type.CustomTypeDefinition(parameters, constructors) -> parameters <> [], isPrivate constructors.Access

    let private typeSpecification (specification: Type.TypeSpecification<unit>) =
        match specification with
        | Type.TypeAliasSpecification(parameters, _)
        | Type.OpaqueTypeSpecification parameters
        | Type.CustomTypeSpecification(parameters, _)
        | Type.DerivedTypeSpecification(parameters, _) -> parameters <> []

    let private definitionModule (m: AccessControlled.AccessControlled<Module.ModuleDefinition<unit, Type<unit>>>) =
        let types = Map.toList m.Value.Types |> List.map snd
        let values = Map.toList m.Value.Values |> List.map snd
        let typeFacts = types |> List.map (fun t -> typeDefinition (Documented.value t.Value))

        {|
            Private =
                isPrivate m.Access
                || types |> List.exists (fun t -> isPrivate t.Access)
                || values |> List.exists (fun v -> isPrivate v.Access)
                || typeFacts |> List.exists snd
            Documented =
                m.Value.Doc.IsSome
                || types |> List.exists (fun t -> (Documented.doc t.Value).IsSome)
                || values |> List.exists (fun v -> (Documented.doc v.Value).IsSome)
            TypeParameters = typeFacts |> List.exists fst
        |}

    let private specificationModule (m: Module.ModuleSpecification<unit>) =
        let types = Map.toList m.Types |> List.map snd

        {|
            Private = false
            Documented =
                m.Doc.IsSome
                || types |> List.exists (fun t -> (Documented.doc t).IsSome)
                || Map.toList m.Values |> List.exists (fun (_, v) -> (Documented.doc v).IsSome)
            TypeParameters = types |> List.exists (fun t -> typeSpecification (Documented.value t))
        |}

    let ofDistribution (distribution: Distribution.Distribution<unit, Type<unit>>) : Features =
        let modules, specs, dependencies =
            match distribution with
            | Morphir.IR.Classic.Distribution.Library(_, dependencies, definition) ->
                (Map.toList definition.Modules |> List.map (snd >> definitionModule)), false, dependencies
            | Morphir.IR.Classic.Distribution.Specs(_, dependencies, specification) ->
                (Map.toList specification.Modules |> List.map (snd >> specificationModule)), true, dependencies

        {
            Private = modules |> List.exists (fun m -> m.Private)
            Documented = modules |> List.exists (fun m -> m.Documented)
            TypeParameters = modules |> List.exists (fun m -> m.TypeParameters)
            Specs = specs
            DependencyWithSeveralModules =
                dependencies
                |> Map.exists (fun _ (d: Package.PackageSpecification<unit>) -> d.Modules.Count > 1)
        }

/// A fixed sample, so the check that the generators vary is deterministic.
let private sample (generator: Gen<'a>) =
    Gen.eval 30 (Random.mkStdGen 20261117L) (Gen.listOfLength 200 generator)

let private expectAllFeatures (features: Features list) =
    let seen what (has: Features -> bool) =
        Expect.isTrue (features |> List.exists has) $"some generated distribution has {what}"

    seen "a private member, module or constructor" (fun f -> f.Private)
    seen "documentation" (fun f -> f.Documented)
    seen "type parameters" (fun f -> f.TypeParameters)
    seen "the Specs kind" (fun f -> f.Specs)
    seen "a dependency with several modules" (fun f -> f.DependencyWithSeveralModules)

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

            test "privateAndDocumentedMembers has private, documented and generic members and a two-module dependency" {
                let expected = {
                    Private = true
                    Documented = true
                    TypeParameters = true
                    Specs = false
                    DependencyWithSeveralModules = true
                }

                Expect.equal (V4Features.ofDistribution DistributionMother.privateAndDocumentedMembers) expected "v4"
                Expect.equal (V3Features.ofDistribution DistributionMother.v3PrivateAndDocumentedMembers) expected "v3"
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

        testList "generated distributions vary what a conversion could lose" [
            test "v4" {
                sample Generators.v4Distribution
                |> List.map V4Features.ofDistribution
                |> expectAllFeatures
            }

            test "v3, with Specs" {
                sample Generators.v3AnyDistribution
                |> List.map V3Features.ofDistribution
                |> expectAllFeatures
            }
        ]

        testList "generated distributions encode" [
            testPropertyWithConfig config "v4"
            <| Prop.forAll (Arb.fromGen Generators.v4Distribution) (fun distribution -> v4Json distribution |> ignore)

            testPropertyWithConfig config "v3"
            <| Prop.forAll (Arb.fromGen Generators.v3AnyDistribution) (fun distribution -> v3Json distribution |> ignore)
        ]
    ]
