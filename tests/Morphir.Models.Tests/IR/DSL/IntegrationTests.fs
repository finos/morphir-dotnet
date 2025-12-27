namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.IR.Classic.Module
open Morphir.IR.Classic.Package
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.DSL.Modules
open Morphir.IR.Classic.DSL.Packages
open Morphir.IR.Classic.DSL.Distributions

/// <summary>
/// Integration tests that combine multiple DSLs to build complete, realistic Morphir IR structures.
/// These tests verify that all DSLs work together correctly and demonstrate real-world usage patterns.
/// </summary>
module IntegrationTests =

    // Helper to create a simple module
    let createSimpleModule docText =
        mod' {
            doc docText
        }

    [<Tests>]
    let tests =
        testList "DSL Integration Tests" [
            testList "Module + Package Integration" [
                testCase "Builds package with multiple modules" <| fun _ ->
                    let domainModule = createSimpleModule "Core domain types"
                    let servicesModule = createSimpleModule "Business services"
                    let apiModule = createSimpleModule "Public API"

                    let package' = pkg {
                        moduledef "com.example.Domain" domainModule
                        moduledef "com.example.Services" servicesModule
                        moduledef "com.example.Api" apiModule
                    }

                    Expect.equal (Map.count package'.Modules) 3 "Should have 3 modules"

                testCase "Mixes public and private modules in package" <| fun _ ->
                    let pubModule = createSimpleModule "Public API"
                    let internalModule = createSimpleModule "Internal utilities"

                    let package' = pkg {
                        publicModule "com.example.Api" pubModule
                        privateModule "com.example.Internal" internalModule
                    }

                    let apiPath = ModulePath.modulePathFromString "com.example.Api"
                    let internalPath = ModulePath.modulePathFromString "com.example.Internal"

                    let apiAccess = (Map.find apiPath package'.Modules).Access
                    let internalAccess = (Map.find internalPath package'.Modules).Access

                    Expect.equal apiAccess Public "API module should be public"
                    Expect.equal internalAccess Private "Internal module should be private"
            ]

            testList "Package + Distribution Integration" [
                testCase "Builds distribution with package" <| fun _ ->
                    let module1 = createSimpleModule "Module 1"
                    let module2 = createSimpleModule "Module 2"

                    let package' = pkg {
                        moduledef "app.Core" module1
                        moduledef "app.Utils" module2
                    }

                    let distribution = dist {
                        library "com.example.app"
                        package package'
                    }

                    match distribution with
                    | Library (pkgName, deps, pkgDef) ->
                        let expectedName = PackageName.packageNameFromString "com.example.app"
                        Expect.equal pkgName expectedName "Should have correct package name"
                        Expect.equal (Map.count pkgDef.Modules) 2 "Should have 2 modules"
                        Expect.equal (Map.count deps) 0 "Should have no dependencies"

                testCase "Builds distribution with dependencies" <| fun _ ->
                    let appModule = createSimpleModule "Main application"
                    let appPackage = pkg {
                        moduledef "app.Main" appModule
                    }

                    // Create a simple dependency spec
                    let utilsSpec = packageSpecification Map.empty

                    let distribution = dist {
                        library "com.example.app"
                        package appPackage
                        dependency "com.utils.core" utilsSpec
                    }

                    match distribution with
                    | Library (_, deps, _) ->
                        Expect.equal (Map.count deps) 1 "Should have 1 dependency"
                        let utilsName = PackageName.packageNameFromString "com.utils.core"
                        Expect.isTrue (Map.containsKey utilsName deps) "Should contain utils dependency"
            ]

            testList "Package Extension Integration" [
                testCase "Extends base package and creates distribution" <| fun _ ->
                    // Create base package with common modules
                    let commonModule = createSimpleModule "Common types"
                    let basePackage = pkg {
                        moduledef "framework.Common" commonModule
                    }

                    // Extend with domain-specific modules
                    let domainModule = createSimpleModule "Domain logic"
                    let extendedPackage = pkg {
                        extend basePackage
                        moduledef "app.Domain" domainModule
                    }

                    // Create distribution from extended package
                    let distribution = dist {
                        library "com.example.app"
                        package extendedPackage
                    }

                    match distribution with
                    | Library (_, _, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 2 "Should have 2 modules (base + extended)"

                        let commonPath = ModulePath.modulePathFromString "framework.Common"
                        let domainPath = ModulePath.modulePathFromString "app.Domain"

                        Expect.isTrue (Map.containsKey commonPath pkgDef.Modules) "Should contain Common module"
                        Expect.isTrue (Map.containsKey domainPath pkgDef.Modules) "Should contain Domain module"

                testCase "Composes multiple packages into distribution" <| fun _ ->
                    // Create core package
                    let coreModule = createSimpleModule "Core functionality"
                    let corePackage = pkg {
                        moduledef "app.Core" coreModule
                    }

                    // Create models package
                    let modelsModule = createSimpleModule "Data models"
                    let modelsPackage = pkg {
                        moduledef "app.Models" modelsModule
                    }

                    // Compose both packages
                    let composedPackage = pkg {
                        extend corePackage
                        extend modelsPackage
                    }

                    let distribution = dist {
                        library "app"
                        package composedPackage
                    }

                    match distribution with
                    | Library (_, _, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 2 "Should have 2 modules from composition"
            ]

            testList "Multi-layer Application" [
                testCase "Builds complete 3-tier application structure" <| fun _ ->
                    // Data layer
                    let repositoryModule = createSimpleModule "Data access layer"
                    let entityModule = createSimpleModule "Database entities"

                    // Business layer
                    let servicesModule = createSimpleModule "Business services"
                    let domainModule = createSimpleModule "Domain models"

                    // API layer
                    let controllersModule = createSimpleModule "API controllers"
                    let dtoModule = createSimpleModule "Data transfer objects"

                    // Build complete package
                    let appPackage = pkg {
                        // Data layer
                        privateModule "app.data.Repository" repositoryModule
                        privateModule "app.data.Entity" entityModule

                        // Business layer
                        privateModule "app.business.Services" servicesModule
                        publicModule "app.business.Domain" domainModule

                        // API layer
                        publicModule "app.api.Controllers" controllersModule
                        publicModule "app.api.Dto" dtoModule
                    }

                    let distribution = dist {
                        library "com.example.app"
                        package appPackage
                    }

                    // Verify structure
                    match distribution with
                    | Library (_, _, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 6 "Should have 6 modules across all layers"

                        // Verify public/private access control
                        let controllersPath = ModulePath.modulePathFromString "app.api.Controllers"
                        let repositoryPath = ModulePath.modulePathFromString "app.data.Repository"

                        let controllersAccess = (Map.find controllersPath pkgDef.Modules).Access
                        let repositoryAccess = (Map.find repositoryPath pkgDef.Modules).Access

                        Expect.equal controllersAccess Public "Controllers should be public"
                        Expect.equal repositoryAccess Private "Repository should be private"
            ]

            testList "Library with Dependencies" [
                testCase "Application depends on multiple external libraries" <| fun _ ->
                    // Create application package
                    let mainModule = createSimpleModule "Main application logic"
                    let appPackage = pkg {
                        moduledef "app.Main" mainModule
                    }

                    // Create dependency specifications
                    let loggingSpec = packageSpecification Map.empty
                    let configSpec = packageSpecification Map.empty
                    let coreSpec = packageSpecification Map.empty

                    // Create distribution with multiple dependencies
                    let distribution = dist {
                        library "com.example.app"
                        package appPackage
                        dependency "com.logging.core" loggingSpec
                        dependency "com.config.api" configSpec
                        dependency "com.framework.core" coreSpec
                    }

                    match distribution with
                    | Library (_, deps, _) ->
                        Expect.equal (Map.count deps) 3 "Should have 3 dependencies"

                        let loggingName = PackageName.packageNameFromString "com.logging.core"
                        let configName = PackageName.packageNameFromString "com.config.api"
                        let coreName = PackageName.packageNameFromString "com.framework.core"

                        Expect.isTrue (Map.containsKey loggingName deps) "Should depend on logging"
                        Expect.isTrue (Map.containsKey configName deps) "Should depend on config"
                        Expect.isTrue (Map.containsKey coreName deps) "Should depend on core framework"
            ]

            testList "Hierarchical Package Organization" [
                testCase "Organizes modules in deep hierarchical structure" <| fun _ ->
                    // Create modules at different hierarchy levels
                    let rootModule = createSimpleModule "Root level module"
                    let level1Module = createSimpleModule "Level 1 module"
                    let level2Module = createSimpleModule "Level 2 module"
                    let level3Module = createSimpleModule "Level 3 module"

                    let package' = pkg {
                        moduledef "app" rootModule
                        moduledef "app.domain" level1Module
                        moduledef "app.domain.customer" level2Module
                        moduledef "app.domain.customer.repository" level3Module
                    }

                    let distribution = dist {
                        library "app"
                        package package'
                    }

                    match distribution with
                    | Library (_, _, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 4 "Should have 4 modules at different levels"

                        // Verify all paths exist
                        let paths = [
                            "app"
                            "app.domain"
                            "app.domain.customer"
                            "app.domain.customer.repository"
                        ]

                        for pathStr in paths do
                            let path = ModulePath.modulePathFromString pathStr
                            Expect.isTrue (Map.containsKey path pkgDef.Modules) $"Should contain {pathStr}"
            ]

            testList "Real-world Scenarios" [
                testCase "Microservice package structure" <| fun _ ->
                    // Common layer (shared across services)
                    let commonTypesModule = createSimpleModule "Shared types"
                    let commonPackage = pkg {
                        publicModule "common.Types" commonTypesModule
                    }

                    // Service-specific package
                    let serviceModule = createSimpleModule "Service implementation"
                    let handlerModule = createSimpleModule "Request handlers"

                    let servicePackage = pkg {
                        extend commonPackage
                        publicModule "service.Api" serviceModule
                        privateModule "service.Handlers" handlerModule
                    }

                    let distribution = dist {
                        library "com.example.userservice"
                        package servicePackage
                    }

                    match distribution with
                    | Library (pkgName, _, pkgDef) ->
                        let expectedName = PackageName.packageNameFromString "com.example.userservice"
                        Expect.equal pkgName expectedName "Should have correct service name"
                        Expect.equal (Map.count pkgDef.Modules) 3 "Should have 3 modules (1 common + 2 service)"

                testCase "Modular monolith structure" <| fun _ ->
                    // Create bounded context packages
                    let orderContext = pkg {
                        publicModule "orders.Domain" (createSimpleModule "Order domain")
                        privateModule "orders.Impl" (createSimpleModule "Order implementation")
                    }

                    let customerContext = pkg {
                        publicModule "customers.Domain" (createSimpleModule "Customer domain")
                        privateModule "customers.Impl" (createSimpleModule "Customer implementation")
                    }

                    let inventoryContext = pkg {
                        publicModule "inventory.Domain" (createSimpleModule "Inventory domain")
                        privateModule "inventory.Impl" (createSimpleModule "Inventory implementation")
                    }

                    // Compose into monolith
                    let monolithPackage = pkg {
                        extend orderContext
                        extend customerContext
                        extend inventoryContext
                    }

                    let distribution = dist {
                        library "com.example.ecommerce"
                        package monolithPackage
                    }

                    match distribution with
                    | Library (_, _, pkgDef) ->
                        Expect.equal (Map.count pkgDef.Modules) 6 "Should have 6 modules (3 contexts * 2 modules each)"
            ]
        ]
