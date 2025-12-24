namespace Morphir.IR.Classic.DSL

/// <summary>
/// Distributions module provides Computation Expression builders for creating Distribution-related IR constructs.
/// </summary>
module Distributions =

    open Morphir.IR.PackageName
    open Morphir.IR.Classic.Distribution
    open Morphir.IR.Classic.Package
    open System.Collections.Generic

    /// <summary>
    /// DistributionBuilder provides a Computation Expression for creating Distribution values.
    /// </summary>
    type DistributionBuilder<'typeAttributes, 'valueAttributes>
        (
            packageName: PackageName option,
            dependencies: Map<PackageName, PackageSpecification<'typeAttributes>>,
            packageDefinition: PackageDefinition<'typeAttributes, 'valueAttributes> option
        ) =
        new() = DistributionBuilder(None, Map.empty, None)

        member this.PackageName = packageName
        member this.Dependencies = dependencies
        member this.PackageDefinition = packageDefinition

        /// <summary>
        /// Sets the package name for the library distribution.
        /// </summary>
        member this.Library(pkgName: PackageName) =
            DistributionBuilder(Some pkgName, dependencies, packageDefinition)

        /// <summary>
        /// Sets the package name from a list of strings.
        /// </summary>
        member this.Library(strs: string list) =
            let pkgName =
                Morphir.IR.Path.fromList (strs |> List.map Morphir.IR.Name.fromString)
                |> Morphir.IR.PackageName.packageName
            DistributionBuilder(Some pkgName, dependencies, packageDefinition)

        /// <summary>
        /// Adds a dependency to the distribution.
        /// </summary>
        member this.Dependency(pkgName: PackageName, spec: PackageSpecification<'typeAttributes>) =
            DistributionBuilder(packageName, Map.add pkgName spec dependencies, packageDefinition)

        /// <summary>
        /// Sets the package definition for the distribution.
        /// </summary>
        member this.Package(pkgDef: PackageDefinition<'typeAttributes, 'valueAttributes>) =
            DistributionBuilder(packageName, dependencies, Some pkgDef)

        /// <summary>
        /// Yields the builder itself for chaining.
        /// </summary>
        member _.Yield(_: unit) = DistributionBuilder(packageName, dependencies, packageDefinition)

        /// <summary>
        /// Combines two builders, taking non-None values from the second.
        /// </summary>
        member _.Combine(builder1: DistributionBuilder<'typeAttributes, 'valueAttributes>, builder2: DistributionBuilder<'typeAttributes, 'valueAttributes>) =
            DistributionBuilder(
                Option.orElse builder1.PackageName builder2.PackageName,
                Map.fold (fun acc k v -> Map.add k v acc) builder1.Dependencies builder2.Dependencies,
                Option.orElse builder1.PackageDefinition builder2.PackageDefinition
            )

        /// <summary>
        /// Runs the builder to produce the final Distribution.
        /// </summary>
        member _.Run(builder: DistributionBuilder<'typeAttributes, 'valueAttributes>) =
            let pkgName =
                builder.PackageName
                |> Option.defaultValue Morphir.IR.PackageName.emptyPackageName
            let pkgDef =
                builder.PackageDefinition
                |> Option.defaultValue (
                    Morphir.IR.Classic.Package.packageDefinition Map.empty)
            library pkgName builder.Dependencies pkgDef

    /// <summary>
    /// DistributionBuilder for unit attributes.
    /// </summary>
    type DistributionBuilder() =
        inherit DistributionBuilder<unit, unit>()

    /// <summary>
    /// Global builder instance for use in Computation Expressions.
    /// </summary>
    let distribution = DistributionBuilder()

