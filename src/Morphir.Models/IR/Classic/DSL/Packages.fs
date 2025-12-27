namespace Morphir.IR.Classic.DSL

/// <summary>
/// Packages module provides Computation Expression builders for creating Package-related IR constructs.
/// </summary>
module Packages =

    open Morphir.IR
    open Morphir.IR.Classic.Package
    open Morphir.IR.Classic.Module
    open Morphir.IR.Classic.AccessControlled
    open System.Collections.Generic

    /// <summary>
    /// PackageSpecificationBuilder provides a Computation Expression for creating PackageSpecification values.
    /// </summary>
    type PackageSpecificationBuilder<'attributes>(modules: Map<ModulePath, ModuleSpecification<'attributes>>) =
        new() = PackageSpecificationBuilder(Map.empty)

        member this.Modules = modules

        /// <summary>
        /// Adds a module to the package specification.
        /// </summary>
        member this.Module(modulePath: ModulePath, moduleSpec: ModuleSpecification<'attributes>) =
            PackageSpecificationBuilder(Map.add modulePath moduleSpec modules)

        /// <summary>
        /// Yields the builder itself for chaining.
        /// </summary>
        member _.Yield(_: unit) = PackageSpecificationBuilder(modules)

        /// <summary>
        /// Combines two builders, merging their contents.
        /// </summary>
        member _.Combine(builder1: PackageSpecificationBuilder<'attributes>, builder2: PackageSpecificationBuilder<'attributes>) =
            PackageSpecificationBuilder(
                Map.fold (fun acc k v -> Map.add k v acc) builder1.Modules builder2.Modules
            )

        /// <summary>
        /// Runs the builder to produce the final PackageSpecification.
        /// </summary>
        member _.Run(builder: PackageSpecificationBuilder<'attributes>) =
            packageSpecification builder.Modules

    /// <summary>
    /// PackageDefinitionBuilder provides a Computation Expression for creating PackageDefinition values.
    /// </summary>
    type PackageDefinitionBuilder<'typeAttributes, 'valueAttributes>
        (modules: Map<ModulePath, AccessControlled<ModuleDefinition<'typeAttributes, 'valueAttributes>>>) =
        new() = PackageDefinitionBuilder(Map.empty)

        member this.Modules = modules

        /// <summary>
        /// Adds a public module to the package definition.
        /// </summary>
        member this.Module(modulePath: ModulePath, moduleDef: ModuleDefinition<'typeAttributes, 'valueAttributes>) =
            PackageDefinitionBuilder(Map.add modulePath (public' moduleDef) modules)

        /// <summary>
        /// Adds a private module to the package definition.
        /// </summary>
        member this.PrivateModule(modulePath: ModulePath, moduleDef: ModuleDefinition<'typeAttributes, 'valueAttributes>) =
            PackageDefinitionBuilder(Map.add modulePath (private' moduleDef) modules)

        /// <summary>
        /// Yields the builder itself for chaining.
        /// </summary>
        member _.Yield(_: unit) = PackageDefinitionBuilder(modules)

        /// <summary>
        /// Combines two builders, merging their contents.
        /// </summary>
        member _.Combine(builder1: PackageDefinitionBuilder<'typeAttributes, 'valueAttributes>, builder2: PackageDefinitionBuilder<'typeAttributes, 'valueAttributes>) =
            PackageDefinitionBuilder(
                Map.fold (fun acc k v -> Map.add k v acc) builder1.Modules builder2.Modules
            )

        /// <summary>
        /// Runs the builder to produce the final PackageDefinition.
        /// </summary>
        member _.Run(builder: PackageDefinitionBuilder<'typeAttributes, 'valueAttributes>) =
            packageDefinition builder.Modules

    /// <summary>
    /// PackageSpecificationBuilder for unit attributes.
    /// </summary>
    type PackageSpecificationBuilder() =
        inherit PackageSpecificationBuilder<unit>()

    /// <summary>
    /// PackageDefinitionBuilder for unit attributes.
    /// </summary>
    type PackageDefinitionBuilder() =
        inherit PackageDefinitionBuilder<unit, unit>()

    /// <summary>
    /// Global builder instances for use in Computation Expressions.
    /// </summary>
    let packageSpec = PackageSpecificationBuilder()
    let packageDef = PackageDefinitionBuilder()

    /// <summary>
    /// Alias for packageDef for convenience (using package' to avoid keyword conflict).
    /// </summary>
    let package' = packageDef

    // ===== NEW CE-BASED PACKAGE BUILDER =====

    /// <summary>
    /// Package state for CE pattern.
    /// </summary>
    type PackageState = {
        Modules: Map<ModulePath, AccessControlled<ModuleDefinition<unit, unit>>>
    }

    /// <summary>
    /// PackageBuilder - CE-based package definition builder.
    /// </summary>
    type PackageBuilder() =
        /// <summary>
        /// Yields unit to create initial empty state.
        /// </summary>
        member _.Yield((): unit) : PackageState =
            { Modules = Map.empty }

        /// <summary>
        /// Zero creates empty state.
        /// </summary>
        member _.Zero() : PackageState =
            { Modules = Map.empty }

        /// <summary>
        /// Delay delays computation.
        /// </summary>
        member _.Delay(f: unit -> PackageState) = f

        /// <summary>
        /// Run produces final PackageDefinition.
        /// </summary>
        member _.Run(f: unit -> PackageState) : PackageDefinition<unit, unit> =
            let state = f()
            packageDefinition state.Modules

        /// <summary>
        /// Combine merges two states (last wins for module collisions).
        /// </summary>
        member _.Combine(state1: PackageState, state2: PackageState) : PackageState =
            { Modules = Map.fold (fun acc k v -> Map.add k v acc) state1.Modules state2.Modules }

        /// <summary>
        /// For enables iteration support in computation expressions.
        /// </summary>
        member this.For(sequence: seq<'a>, body: 'a -> PackageState) : PackageState =
            sequence
            |> Seq.fold (fun state item ->
                let newState = body item
                this.Combine(state, newState)
            ) { Modules = Map.empty }

        /// <summary>
        /// CustomOperation: Adds a public module definition (default).
        /// Usage: moduledef "com.example.Customer" customerModule
        /// </summary>
        [<CustomOperation("moduledef")>]
        member _.moduledef(state: PackageState, pathStr: string, moduleDef: ModuleDefinition<unit, unit>) : PackageState =
            let modulePath = ModulePath.modulePathFromString pathStr
            let accessControlled = public' moduleDef
            { state with Modules = Map.add modulePath accessControlled state.Modules }

        /// <summary>
        /// CustomOperation: Adds a public module definition explicitly.
        /// Usage: publicModule "com.example.Order" orderModule
        /// </summary>
        [<CustomOperation("publicModule")>]
        member _.publicModule(state: PackageState, pathStr: string, moduleDef: ModuleDefinition<unit, unit>) : PackageState =
            let modulePath = ModulePath.modulePathFromString pathStr
            let accessControlled = public' moduleDef
            { state with Modules = Map.add modulePath accessControlled state.Modules }

        /// <summary>
        /// CustomOperation: Adds a private module definition explicitly.
        /// Usage: privateModule "com.internal.Utils" utilsModule
        /// </summary>
        [<CustomOperation("privateModule")>]
        member _.privateModule(state: PackageState, pathStr: string, moduleDef: ModuleDefinition<unit, unit>) : PackageState =
            let modulePath = ModulePath.modulePathFromString pathStr
            let accessControlled = private' moduleDef
            { state with Modules = Map.add modulePath accessControlled state.Modules }

        /// <summary>
        /// CustomOperation: Extends package with modules from another package definition.
        /// Usage: extend basePackage
        /// </summary>
        [<CustomOperation("extend")>]
        member _.extend(state: PackageState, pkgDef: PackageDefinition<unit, unit>) : PackageState =
            // Add all modules from the package definition to current state
            // Error on collision (module path already exists)
            let newModules =
                pkgDef.Modules
                |> Map.fold (fun acc path accessControlledModule ->
                    if Map.containsKey path state.Modules then
                        let pathStr = path |> ModulePath.modulePathToPath |> Path.toCanonicalString
                        failwithf "Module path collision: '%s' already exists in package. Cannot extend with duplicate module paths." pathStr
                    else
                        Map.add path accessControlledModule acc
                ) state.Modules
            { state with Modules = newModules }

    /// <summary>
    /// Global pkg builder instance.
    /// </summary>
    let pkg = PackageBuilder()

