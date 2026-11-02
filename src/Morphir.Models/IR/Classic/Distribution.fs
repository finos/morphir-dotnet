namespace Morphir.IR.Classic

/// <summary>
/// Distribution module provides the distribution system for Morphir IR.
/// A Distribution represents a complete, self-contained package of Morphir code
/// with all its dependencies.
/// </summary>
module Distribution =

    open Morphir.IR
    open Package
    open System.Collections.Generic // For Map

    /// <summary>
    /// Distribution represents a complete, self-contained package of Morphir code.
    /// A Library holds a package definition. A Specs distribution (IR 3.1.0) holds only a package
    /// specification, so it has no definitions.
    /// </summary>
    type Distribution<'typeAttributes, 'valueAttributes> =
        | Library of PackageName * Map<PackageName, PackageSpecification<'typeAttributes>> * PackageDefinition<'typeAttributes, 'valueAttributes>
        | Specs of PackageName * Map<PackageName, PackageSpecification<'typeAttributes>> * PackageSpecification<'typeAttributes>

    /// <summary>
    /// Creates a Library distribution.
    /// </summary>
    let library<'typeAttributes, 'valueAttributes> (packageName: PackageName) (dependencies: Map<PackageName, PackageSpecification<'typeAttributes>>) (packageDefinition: PackageDefinition<'typeAttributes, 'valueAttributes>) : Distribution<'typeAttributes, 'valueAttributes> =
        Library(packageName, dependencies, packageDefinition)

    /// <summary>
    /// Gets the package name from a Distribution.
    /// </summary>
    let packageName<'typeAttributes, 'valueAttributes> (distribution: Distribution<'typeAttributes, 'valueAttributes>) : PackageName =
        match distribution with
        | Library(pkgName, _, _)
        | Specs(pkgName, _, _) -> pkgName

    /// <summary>
    /// Gets the dependencies from a Distribution.
    /// </summary>
    let dependencies<'typeAttributes, 'valueAttributes> (distribution: Distribution<'typeAttributes, 'valueAttributes>) : Map<PackageName, PackageSpecification<'typeAttributes>> =
        match distribution with
        | Library(_, deps, _)
        | Specs(_, deps, _) -> deps

    /// <summary>
    /// Gets the package definition from a Distribution. A Specs distribution has none.
    /// </summary>
    let packageDefinition<'typeAttributes, 'valueAttributes> (distribution: Distribution<'typeAttributes, 'valueAttributes>) : PackageDefinition<'typeAttributes, 'valueAttributes> option =
        match distribution with
        | Library(_, _, pkgDef) -> Some pkgDef
        | Specs _ -> None

