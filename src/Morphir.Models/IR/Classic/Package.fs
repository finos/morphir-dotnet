namespace Morphir.IR.Classic

/// <summary>
/// Package module provides the package system for Morphir IR, including
/// Package Specifications and Package Definitions.
/// Packages are collections of modules that are versioned and distributed together.
/// </summary>
module Package =

    open Morphir.IR.PackageName
    open Morphir.IR.ModulePath
    open Module
    open AccessControlled
    open System.Collections.Generic // For Map

    /// <summary>
    /// PackageSpecification provides the public interface of a package.
    /// Contains only publicly exposed modules with type signatures.
    /// </summary>
    type PackageSpecification<'attributes> =
        { Modules: Map<ModulePath, ModuleSpecification<'attributes>> }

    /// <summary>
    /// PackageDefinition provides the complete implementation of a package.
    /// Contains all modules (public and private) with full implementations.
    /// </summary>
    type PackageDefinition<'typeAttributes, 'valueAttributes> =
        { Modules: Map<ModulePath, AccessControlled<ModuleDefinition<'typeAttributes, 'valueAttributes>>> }

    /// <summary>
    /// Creates a PackageSpecification.
    /// </summary>
    let packageSpecification<'attributes> (modules: Map<ModulePath, ModuleSpecification<'attributes>>) : PackageSpecification<'attributes> =
        { Modules = modules }

    /// <summary>
    /// Creates a PackageDefinition.
    /// </summary>
    let packageDefinition<'typeAttributes, 'valueAttributes> (modules: Map<ModulePath, AccessControlled<ModuleDefinition<'typeAttributes, 'valueAttributes>>>) : PackageDefinition<'typeAttributes, 'valueAttributes> =
        { Modules = modules }

