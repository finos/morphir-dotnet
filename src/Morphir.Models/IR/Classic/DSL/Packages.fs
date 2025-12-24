namespace Morphir.IR.Classic.DSL

/// <summary>
/// Packages module provides Computation Expression builders for creating Package-related IR constructs.
/// </summary>
module Packages =

    open Morphir.IR.ModulePath
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

