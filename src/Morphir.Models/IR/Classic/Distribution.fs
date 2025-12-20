namespace Morphir.IR.Classic

/// <summary>
/// Distribution module provides the distribution system for Morphir IR.
/// A Distribution represents a complete, self-contained package of Morphir code
/// with all its dependencies.
/// </summary>
module Distribution =

    open Morphir.IR.PackageName
    open Package
    open System.Collections.Generic // For Map

    /// <summary>
    /// Distribution represents a complete, self-contained package of Morphir code.
    /// Currently only Library distributions are supported.
    /// </summary>
    type Distribution<'typeAttributes, 'valueAttributes> =
        | Library of PackageName * Map<PackageName, PackageSpecification<'typeAttributes>> * PackageDefinition<'typeAttributes, 'valueAttributes>

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
        | Library(pkgName, _, _) -> pkgName

    /// <summary>
    /// Gets the dependencies from a Distribution.
    /// </summary>
    let dependencies<'typeAttributes, 'valueAttributes> (distribution: Distribution<'typeAttributes, 'valueAttributes>) : Map<PackageName, PackageSpecification<'typeAttributes>> =
        match distribution with
        | Library(_, deps, _) -> deps

    /// <summary>
    /// Gets the package definition from a Distribution.
    /// </summary>
    let packageDefinition<'typeAttributes, 'valueAttributes> (distribution: Distribution<'typeAttributes, 'valueAttributes>) : PackageDefinition<'typeAttributes, 'valueAttributes> =
        match distribution with
        | Library(_, _, pkgDef) -> pkgDef

