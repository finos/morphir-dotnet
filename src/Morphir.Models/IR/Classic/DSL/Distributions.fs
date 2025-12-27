namespace Morphir.IR.Classic.DSL

/// <summary>
/// Distributions module provides Computation Expression builders for creating Distribution-related IR constructs.
/// </summary>
module Distributions =

    open Morphir.IR
    open Morphir.IR.Classic.Distribution
    open Morphir.IR.Classic.Package

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
                Path.fromList (strs |> List.map Name.fromString)
                |> PackageName.packageName
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
                |> Option.defaultValue PackageName.emptyPackageName
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

    // ===== NEW CE-BASED DISTRIBUTION BUILDER =====

    /// <summary>
    /// Distribution state for CE pattern.
    /// </summary>
    type DistributionState = {
        PackageName: PackageName option
        Dependencies: Map<PackageName, PackageSpecification<unit>>
        Package: PackageDefinition<unit, unit> option
    }

    /// <summary>
    /// DistBuilder - CE-based distribution builder.
    /// </summary>
    type DistBuilder() =
        /// <summary>
        /// Yields unit to create initial empty state.
        /// </summary>
        member _.Yield((): unit) : DistributionState =
            { PackageName = None; Dependencies = Map.empty; Package = None }

        /// <summary>
        /// Zero creates empty state.
        /// </summary>
        member _.Zero() : DistributionState =
            { PackageName = None; Dependencies = Map.empty; Package = None }

        /// <summary>
        /// Delay delays computation.
        /// </summary>
        member _.Delay(f: unit -> DistributionState) = f

        /// <summary>
        /// Run produces final Distribution.
        /// Requires library name to be set.
        /// </summary>
        member _.Run(f: unit -> DistributionState) : Distribution<unit, unit> =
            let state = f()
            match state.PackageName with
            | None -> failwith "Distribution requires a library name. Use 'library \"com.example.myapp\"' to set the package name."
            | Some pkgName ->
                let pkgDef = state.Package |> Option.defaultValue (Morphir.IR.Classic.Package.packageDefinition Map.empty)
                library pkgName state.Dependencies pkgDef

        /// <summary>
        /// Combine merges two states (last wins for PackageName and Package).
        /// </summary>
        member _.Combine(state1: DistributionState, state2: DistributionState) : DistributionState =
            { PackageName = Option.orElse state1.PackageName state2.PackageName
              Dependencies = Map.fold (fun acc k v -> Map.add k v acc) state1.Dependencies state2.Dependencies
              Package = Option.orElse state1.Package state2.Package }

        /// <summary>
        /// CustomOperation: Sets the library package name.
        /// Usage: library "com.example.myapp"
        /// </summary>
        [<CustomOperation("library")>]
        member _.library(state: DistributionState, nameStr: string) : DistributionState =
            let pkgName = PackageName.packageNameFromString nameStr
            { state with PackageName = Some pkgName }

        /// <summary>
        /// CustomOperation: Sets the package definition.
        /// Usage: package myPackageDefinition
        /// </summary>
        [<CustomOperation("package")>]
        member _.package(state: DistributionState, pkgDef: PackageDefinition<unit, unit>) : DistributionState =
            { state with Package = Some pkgDef }

        /// <summary>
        /// CustomOperation: Adds a dependency to the distribution.
        /// Usage: dependency "com.acme.utils" utilsSpec
        /// </summary>
        [<CustomOperation("dependency")>]
        member _.dependency(state: DistributionState, nameStr: string, spec: PackageSpecification<unit>) : DistributionState =
            let pkgName = PackageName.packageNameFromString nameStr
            { state with Dependencies = Map.add pkgName spec state.Dependencies }

    /// <summary>
    /// Global dist builder instance.
    /// </summary>
    let dist = DistBuilder()

