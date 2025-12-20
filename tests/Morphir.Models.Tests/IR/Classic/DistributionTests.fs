module Morphir.IR.Classic.Tests.DistributionTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Testing.Assertions
open System.Collections.Generic

[<Tests>]
let tests =
    testList "Distribution" [
        testList "library" [
            testCase "Creates Library distribution with empty dependencies"
            <| fun _ ->
                let packageName = PackageName.packageNameFromList [ Name.fromList [ "my"; "package" ] ]
                let dependencies = Map.empty
                let packageDef = Package.packageDefinition Map.empty
                let distribution = Distribution.library packageName dependencies packageDef

                match distribution with
                | Distribution.Library(pkgName, deps, _) ->
                    pkgName |> Expect.equal packageName
                    deps.Count |> Expect.equal 0

            testCase "Creates Library distribution with dependencies"
            <| fun _ ->
                let packageName = PackageName.packageNameFromList [ Name.fromList [ "my"; "package" ] ]
                let depPackageName = PackageName.packageNameFromList [ Name.fromList [ "dependency"; "package" ] ]
                let depSpec = Package.packageSpecification Map.empty
                let dependencies = Map.empty |> Map.add depPackageName depSpec
                let packageDef = Package.packageDefinition Map.empty
                let distribution = Distribution.library packageName dependencies packageDef

                match distribution with
                | Distribution.Library(pkgName, deps, _) ->
                    pkgName |> Expect.equal packageName
                    deps.Count |> Expect.equal 1
                    deps.ContainsKey(depPackageName) |> Expect.isTrue
        ]

        testList "packageName" [
            testCase "Gets package name from Library distribution"
            <| fun _ ->
                let packageName = PackageName.packageNameFromList [ Name.fromList [ "test"; "package" ] ]
                let distribution = Distribution.library packageName Map.empty (Package.packageDefinition Map.empty)
                let retrievedName = Distribution.packageName distribution

                retrievedName |> Expect.equal packageName
        ]

        testList "dependencies" [
            testCase "Gets dependencies from Library distribution"
            <| fun _ ->
                let packageName = PackageName.packageNameFromList [ Name.fromList [ "my"; "package" ] ]
                let dep1Name = PackageName.packageNameFromList [ Name.fromList [ "dep1" ] ]
                let dep2Name = PackageName.packageNameFromList [ Name.fromList [ "dep2" ] ]
                let dependencies =
                    Map.empty
                    |> Map.add dep1Name (Package.packageSpecification Map.empty)
                    |> Map.add dep2Name (Package.packageSpecification Map.empty)
                let distribution = Distribution.library packageName dependencies (Package.packageDefinition Map.empty)
                let retrievedDeps = Distribution.dependencies distribution

                retrievedDeps.Count |> Expect.equal 2
                retrievedDeps.ContainsKey(dep1Name) |> Expect.isTrue
                retrievedDeps.ContainsKey(dep2Name) |> Expect.isTrue
        ]

        testList "packageDefinition" [
            testCase "Gets package definition from Library distribution"
            <| fun _ ->
                let packageName = PackageName.packageNameFromList [ Name.fromList [ "my"; "package" ] ]
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "myModule" ] ]
                let moduleDef = Module.moduleDefinition Map.empty Map.empty None
                let modules = Map.empty |> Map.add modulePath (AccessControlled.public' moduleDef)
                let packageDef = Package.packageDefinition modules
                let distribution = Distribution.library packageName Map.empty packageDef
                let retrievedDef = Distribution.packageDefinition distribution

                retrievedDef.Modules.Count |> Expect.equal 1
                retrievedDef.Modules.ContainsKey(modulePath) |> Expect.isTrue
        ]
    ]

