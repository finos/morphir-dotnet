namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.Module
open Morphir.IR.Classic.Package
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.DSL.Modules
open Morphir.IR.Classic.DSL.Packages

module PackagesTests =

    // Helper: Create a simple test module
    let createTestModule docText =
        mod' {
            doc docText
        }

    [<Tests>]
    let tests =
        testList "DSL Packages" [
            testList "Basic Package Definition Creation" [
                testCase "Creates empty package definition" <| fun _ ->
                    let result = pkg {
                        ()
                    }

                    Expect.equal result.Modules Map.empty "Empty package should have no modules"

                testCase "Adds public module definition using moduledef" <| fun _ ->
                    let customerModule = createTestModule "Customer module"
                    let result = pkg {
                        moduledef "com.example.Customer" customerModule
                    }

                    Expect.equal (Map.count result.Modules) 1 "Should have one module"
                    let modulePath = ModulePath.modulePathFromString "com.example.Customer"
                    Expect.isTrue (Map.containsKey modulePath result.Modules) "Should contain com.example.Customer"

                    let accessControlled = Map.find modulePath result.Modules
                    Expect.equal accessControlled.Access Public "Module should be public by default"

                testCase "Adds multiple modules using moduledef" <| fun _ ->
                    let customerModule = createTestModule "Customer module"
                    let orderModule = createTestModule "Order module"
                    let result = pkg {
                        moduledef "com.example.Customer" customerModule
                        moduledef "com.example.Order" orderModule
                    }

                    Expect.equal (Map.count result.Modules) 2 "Should have two modules"

                    let customerPath = ModulePath.modulePathFromString "com.example.Customer"
                    let orderPath = ModulePath.modulePathFromString "com.example.Order"

                    Expect.isTrue (Map.containsKey customerPath result.Modules) "Should contain Customer"
                    Expect.isTrue (Map.containsKey orderPath result.Modules) "Should contain Order"
            ]

            testList "Access Control" [
                testCase "Adds private module using privateModule" <| fun _ ->
                    let internalModule = createTestModule "Internal module"
                    let result = pkg {
                        privateModule "com.internal.Utils" internalModule
                    }

                    let modulePath = ModulePath.modulePathFromString "com.internal.Utils"
                    let accessControlled = Map.find modulePath result.Modules
                    Expect.equal accessControlled.Access Private "Module should be private"

                testCase "Adds public module using publicModule" <| fun _ ->
                    let apiModule = createTestModule "Public API module"
                    let result = pkg {
                        publicModule "com.example.PublicAPI" apiModule
                    }

                    let modulePath = ModulePath.modulePathFromString "com.example.PublicAPI"
                    let accessControlled = Map.find modulePath result.Modules
                    Expect.equal accessControlled.Access Public "Module should be public"

                testCase "Mixes public and private modules" <| fun _ ->
                    let customerModule = createTestModule "Customer module"
                    let orderModule = createTestModule "Order module"
                    let utilsModule = createTestModule "Utils module"

                    let result = pkg {
                        publicModule "com.example.Customer" customerModule
                        moduledef "com.example.Order" orderModule
                        privateModule "com.internal.Utils" utilsModule
                    }

                    Expect.equal (Map.count result.Modules) 3 "Should have three modules"

                    let customerPath = ModulePath.modulePathFromString "com.example.Customer"
                    let orderPath = ModulePath.modulePathFromString "com.example.Order"
                    let utilsPath = ModulePath.modulePathFromString "com.internal.Utils"

                    let customerAccess = (Map.find customerPath result.Modules).Access
                    let orderAccess = (Map.find orderPath result.Modules).Access
                    let utilsAccess = (Map.find utilsPath result.Modules).Access

                    Expect.equal customerAccess Public "Customer should be public"
                    Expect.equal orderAccess Public "Order should be public (moduledef defaults to public)"
                    Expect.equal utilsAccess Private "Utils should be private"
            ]

            testList "String Path Handling" [
                testCase "Converts dot-notation string to ModulePath" <| fun _ ->
                    let testModule = createTestModule "Test module"
                    let result = pkg {
                        moduledef "com.example.domain.Customer" testModule
                    }

                    let expectedPath = ModulePath.modulePathFromString "com.example.domain.Customer"
                    Expect.isTrue (Map.containsKey expectedPath result.Modules) "Should convert string path correctly"

                testCase "Handles single-segment paths" <| fun _ ->
                    let testModule = createTestModule "Simple module"
                    let result = pkg {
                        moduledef "SimpleModule" testModule
                    }

                    let expectedPath = ModulePath.modulePathFromString "SimpleModule"
                    Expect.isTrue (Map.containsKey expectedPath result.Modules) "Should handle single-segment paths"

                testCase "Handles deep hierarchical paths" <| fun _ ->
                    let testModule = createTestModule "Deep module"
                    let result = pkg {
                        moduledef "com.example.ecommerce.domain.customer.models" testModule
                    }

                    let expectedPath = ModulePath.modulePathFromString "com.example.ecommerce.domain.customer.models"
                    Expect.isTrue (Map.containsKey expectedPath result.Modules) "Should handle deep paths"
            ]

            testList "Package Composition" [
                testCase "Combines multiple package states" <| fun _ ->
                    let module1 = createTestModule "Module 1"
                    let module2 = createTestModule "Module 2"
                    let module3 = createTestModule "Module 3"

                    let result = pkg {
                        moduledef "com.example.Module1" module1
                        moduledef "com.example.Module2" module2
                        moduledef "com.example.Module3" module3
                    }

                    Expect.equal (Map.count result.Modules) 3 "Should combine all modules"

                testCase "Last module wins on path collision" <| fun _ ->
                    let firstModule = createTestModule "First version"
                    let secondModule = createTestModule "Second version"

                    let result = pkg {
                        moduledef "com.example.Customer" firstModule
                        moduledef "com.example.Customer" secondModule
                    }

                    Expect.equal (Map.count result.Modules) 1 "Should have one module (last wins)"

                    let modulePath = ModulePath.modulePathFromString "com.example.Customer"
                    let accessControlled = Map.find modulePath result.Modules
                    let moduleDoc = accessControlled.Value.Doc

                    Expect.equal moduleDoc (Some "Second version") "Should use second module (last wins)"
            ]

            testList "Multiple Module Addition" [
                testCase "Adds three modules declaratively" <| fun _ ->
                    let customerModule = createTestModule "Customer"
                    let orderModule = createTestModule "Order"
                    let productModule = createTestModule "Product"

                    let result = pkg {
                        moduledef "com.example.Customer" customerModule
                        moduledef "com.example.Order" orderModule
                        moduledef "com.example.Product" productModule
                    }

                    Expect.equal (Map.count result.Modules) 3 "Should create all three modules"

                testCase "Mixes different access levels" <| fun _ ->
                    let customerModule = createTestModule "Customer"
                    let orderModule = createTestModule "Order"
                    let utilsModule = createTestModule "Utils"

                    let result = pkg {
                        publicModule "com.example.Customer" customerModule
                        moduledef "com.example.Order" orderModule
                        privateModule "com.internal.Utils" utilsModule
                    }

                    Expect.equal (Map.count result.Modules) 3 "Should create all modules with correct access"

                    let utilsPath = ModulePath.modulePathFromString "com.internal.Utils"
                    let utilsAccess = (Map.find utilsPath result.Modules).Access
                    Expect.equal utilsAccess Private "Utils should be private"
            ]

            testList "Package Extension" [
                testCase "Extends package with modules from base package" <| fun _ ->
                    let coreModule = createTestModule "Core module"
                    let commonModule = createTestModule "Common module"

                    let basePackage = pkg {
                        moduledef "com.example.Core" coreModule
                        moduledef "com.example.Common" commonModule
                    }

                    let advancedModule = createTestModule "Advanced module"

                    let extendedPackage = pkg {
                        extend basePackage
                        moduledef "com.example.Advanced" advancedModule
                    }

                    Expect.equal (Map.count extendedPackage.Modules) 3 "Should have 3 modules (2 from base + 1 new)"

                    let corePath = ModulePath.modulePathFromString "com.example.Core"
                    let commonPath = ModulePath.modulePathFromString "com.example.Common"
                    let advancedPath = ModulePath.modulePathFromString "com.example.Advanced"

                    Expect.isTrue (Map.containsKey corePath extendedPackage.Modules) "Should contain Core from base"
                    Expect.isTrue (Map.containsKey commonPath extendedPackage.Modules) "Should contain Common from base"
                    Expect.isTrue (Map.containsKey advancedPath extendedPackage.Modules) "Should contain Advanced"

                testCase "Extends package preserves access control from base" <| fun _ ->
                    let pubModule = createTestModule "Public module"
                    let privModule = createTestModule "Private module"

                    let basePackage = pkg {
                        publicModule "com.example.Public" pubModule
                        privateModule "com.example.Private" privModule
                    }

                    let extendedPackage = pkg {
                        extend basePackage
                    }

                    let publicPath = ModulePath.modulePathFromString "com.example.Public"
                    let privatePath = ModulePath.modulePathFromString "com.example.Private"

                    let publicAccess = (Map.find publicPath extendedPackage.Modules).Access
                    let privateAccess = (Map.find privatePath extendedPackage.Modules).Access

                    Expect.equal publicAccess Public "Public module should remain public"
                    Expect.equal privateAccess Private "Private module should remain private"

                testCase "Extend fails on module path collision" <| fun _ ->
                    let module1 = createTestModule "Module 1"
                    let module2 = createTestModule "Module 2"

                    let basePackage = pkg {
                        moduledef "com.example.Shared" module1
                    }

                    let createConflictingPackage() =
                        pkg {
                            moduledef "com.example.Shared" module2
                            extend basePackage
                        }

                    Expect.throws
                        (fun () -> createConflictingPackage() |> ignore)
                        "Should throw on module path collision"

                testCase "Can extend empty package" <| fun _ ->
                    let emptyPackage = pkg { () }

                    let newModule = createTestModule "New module"

                    let result = pkg {
                        extend emptyPackage
                        moduledef "com.example.New" newModule
                    }

                    Expect.equal (Map.count result.Modules) 1 "Should have 1 module"

                testCase "Can extend multiple packages sequentially" <| fun _ ->
                    let module1 = createTestModule "Module 1"
                    let module2 = createTestModule "Module 2"
                    let module3 = createTestModule "Module 3"

                    let package1 = pkg { moduledef "com.example.Module1" module1 }
                    let package2 = pkg { moduledef "com.example.Module2" module2 }

                    let combined = pkg {
                        extend package1
                        extend package2
                        moduledef "com.example.Module3" module3
                    }

                    Expect.equal (Map.count combined.Modules) 3 "Should have all 3 modules"
            ]
        ]
