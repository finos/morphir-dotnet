namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Package
open Morphir.IR.Classic.Module
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.DSL.Modules
open Morphir.IR.Classic.DSL.Packages
open Morphir.IR.Classic.DSL.Distributions

module DistributionsTests =

    // Helper: Create a simple test package
    let createTestPackage (modules: (string * string) list) =
        let mutable result = pkg { () }
        for (path, docText) in modules do
            result <- pkg {
                extend result
                moduledef path (mod' { doc docText })
            }
        result

    // Helper: Create a simple package specification
    let createTestPackageSpec() =
        packageSpecification Map.empty

    [<Tests>]
    let tests =
        testList "DSL Distributions" [
            testList "Basic Distribution Creation" [
                testCase "Creates distribution with library name and empty package" <| fun _ ->
                    let result = dist {
                        library "com.example.myapp"
                    }

                    match result with
                    | Library (pkgName, deps, pkgDef) ->
                        let expectedPkgName = PackageName.packageNameFromString "com.example.myapp"
                        Expect.equal pkgName expectedPkgName "Should have correct package name"
                        Expect.equal (Map.count deps) 0 "Should have no dependencies"
                        Expect.equal (Map.count pkgDef.Modules) 0 "Should have empty package definition"

                testCase "Creates distribution with library and package" <| fun _ ->
                    let testPackage = createTestPackage [
                        ("com.example.Customer", "Customer module")
                        ("com.example.Order", "Order module")
                    ]

                    let result = dist {
                        library "com.example.myapp"
                        package testPackage
                    }

                    match result with
                    | Library (pkgName, deps, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 2 "Should have 2 modules in package"
                        Expect.equal (Map.count deps) 0 "Should have no dependencies"

                testCase "Creates distribution with single dependency" <| fun _ ->
                    let utilsSpec = createTestPackageSpec()

                    let result = dist {
                        library "com.example.myapp"
                        dependency "com.acme.utils" utilsSpec
                    }

                    match result with
                    | Library (pkgName, deps, pkgDef) ->
                        Expect.equal (Map.count deps) 1 "Should have 1 dependency"
                        let expectedDepName = PackageName.packageNameFromString "com.acme.utils"
                        Expect.isTrue (Map.containsKey expectedDepName deps) "Should contain utils dependency"

                testCase "Creates distribution with multiple dependencies" <| fun _ ->
                    let utilsSpec = createTestPackageSpec()
                    let loggingSpec = createTestPackageSpec()
                    let coreSpec = createTestPackageSpec()

                    let result = dist {
                        library "com.example.myapp"
                        dependency "com.acme.utils" utilsSpec
                        dependency "com.acme.logging" loggingSpec
                        dependency "com.acme.core" coreSpec
                    }

                    match result with
                    | Library (pkgName, deps, pkgDef) ->
                        Expect.equal (Map.count deps) 3 "Should have 3 dependencies"

                        let utilsName = PackageName.packageNameFromString "com.acme.utils"
                        let loggingName = PackageName.packageNameFromString "com.acme.logging"
                        let coreName = PackageName.packageNameFromString "com.acme.core"

                        Expect.isTrue (Map.containsKey utilsName deps) "Should contain utils dependency"
                        Expect.isTrue (Map.containsKey loggingName deps) "Should contain logging dependency"
                        Expect.isTrue (Map.containsKey coreName deps) "Should contain core dependency"

                testCase "Creates complete distribution with library, package, and dependencies" <| fun _ ->
                    let testPackage = createTestPackage [
                        ("com.example.Customer", "Customer module")
                        ("com.example.Order", "Order module")
                        ("com.example.Product", "Product module")
                    ]
                    let utilsSpec = createTestPackageSpec()
                    let loggingSpec = createTestPackageSpec()

                    let result = dist {
                        library "com.example.ecommerce"
                        package testPackage
                        dependency "com.acme.utils" utilsSpec
                        dependency "com.acme.logging" loggingSpec
                    }

                    match result with
                    | Library (pkgName, deps, pkgDef) ->
                        let expectedPkgName = PackageName.packageNameFromString "com.example.ecommerce"
                        Expect.equal pkgName expectedPkgName "Should have correct package name"
                        Expect.equal (Map.count pkgDef.Modules) 3 "Should have 3 modules in package"
                        Expect.equal (Map.count deps) 2 "Should have 2 dependencies"
            ]

            testList "Library Name Validation" [
                testCase "Fails when library name is not provided" <| fun _ ->
                    Expect.throws
                        (fun () ->
                            dist {
                                package (createTestPackage [])
                            } |> ignore
                        )
                        "Should throw when library name is missing"

                testCase "Accepts single-segment library name" <| fun _ ->
                    let result = dist {
                        library "MyApp"
                    }

                    match result with
                    | Library (pkgName, _, _) ->
                        let expectedPkgName = PackageName.packageNameFromString "MyApp"
                        Expect.equal pkgName expectedPkgName "Should handle single-segment names"

                testCase "Accepts deep hierarchical library name" <| fun _ ->
                    let result = dist {
                        library "com.example.division.department.project.myapp"
                    }

                    match result with
                    | Library (pkgName, _, _) ->
                        let expectedPkgName = PackageName.packageNameFromString "com.example.division.department.project.myapp"
                        Expect.equal pkgName expectedPkgName "Should handle deep hierarchical names"
            ]

            testList "String Path Handling" [
                testCase "Converts dot-notation string to PackageName for library" <| fun _ ->
                    let result = dist {
                        library "com.example.domain.customer"
                    }

                    match result with
                    | Library (pkgName, _, _) ->
                        let expectedPkgName = PackageName.packageNameFromString "com.example.domain.customer"
                        Expect.equal pkgName expectedPkgName "Should convert library name correctly"

                testCase "Converts dot-notation string to PackageName for dependencies" <| fun _ ->
                    let utilsSpec = createTestPackageSpec()

                    let result = dist {
                        library "com.example.myapp"
                        dependency "com.acme.utils.core.v2" utilsSpec
                    }

                    match result with
                    | Library (_, deps, _) ->
                        let expectedDepName = PackageName.packageNameFromString "com.acme.utils.core.v2"
                        Expect.isTrue (Map.containsKey expectedDepName deps) "Should convert dependency name correctly"
            ]

            testList "Distribution Composition" [
                testCase "Last package wins when multiple packages provided" <| fun _ ->
                    let package1 = createTestPackage [("com.example.Module1", "First version")]
                    let package2 = createTestPackage [("com.example.Module2", "Second version")]

                    let result = dist {
                        library "com.example.myapp"
                        package package1
                        package package2
                    }

                    match result with
                    | Library (_, _, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 1 "Should use last package"
                        let module2Path = ModulePath.modulePathFromString "com.example.Module2"
                        Expect.isTrue (Map.containsKey module2Path pkgDef.Modules) "Should contain Module2 from second package"

                testCase "Dependencies are accumulated" <| fun _ ->
                    let spec1 = createTestPackageSpec()
                    let spec2 = createTestPackageSpec()
                    let spec3 = createTestPackageSpec()

                    let result = dist {
                        library "com.example.myapp"
                        dependency "com.acme.utils" spec1
                        dependency "com.acme.logging" spec2
                        dependency "com.acme.core" spec3
                    }

                    match result with
                    | Library (_, deps, _) ->
                        Expect.equal (Map.count deps) 3 "Should accumulate all dependencies"

                testCase "Last dependency spec wins on name collision" <| fun _ ->
                    let spec1 = createTestPackageSpec()
                    let spec2 = createTestPackageSpec()

                    let result = dist {
                        library "com.example.myapp"
                        dependency "com.acme.utils" spec1
                        dependency "com.acme.utils" spec2
                    }

                    match result with
                    | Library (_, deps, _) ->
                        Expect.equal (Map.count deps) 1 "Should have 1 dependency (last wins)"
            ]

            testList "Empty Distribution" [
                testCase "Empty package is valid when library name provided" <| fun _ ->
                    let result = dist {
                        library "com.example.empty"
                    }

                    match result with
                    | Library (pkgName, deps, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 0 "Empty package should have no modules"
                        Expect.equal (Map.count deps) 0 "Should have no dependencies"

                testCase "No dependencies is valid" <| fun _ ->
                    let testPackage = createTestPackage [("com.example.Standalone", "Standalone module")]

                    let result = dist {
                        library "com.example.standalone"
                        package testPackage
                    }

                    match result with
                    | Library (_, deps, _) ->
                        Expect.equal (Map.count deps) 0 "Should have no dependencies"
            ]
        ]
