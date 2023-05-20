module rec Morphir.IR.Distribution

open Morphir.IR.Package
open Morphir.IR
open Morphir.IR.Type
open Morphir.SDK.Dict
open Morphir.SDK

/// Type that represents a package distribution. Currently the only distribution type we provide is a `Library`.
type Distribution =
    | Library of
        packageName: PackageName *
        dependencies: Dict<PackageName, Package.Specification<unit>> *
        definition: Package.Definition<unit, Type<unit>>

    static member EmptyLibrary(name: PackageName) =
        Library(name, Dict.empty, Package.emptyDefinition<unit, Type<unit>>)


let emptyLibrary (name: PackageName) : Distribution =
    Library(name, Dict.empty, Package.emptyDefinition<unit, Type<unit>>)

let library
    (packageName: PackageName)
    (dependencies: Dict<PackageName, Package.Specification<unit>>)
    (definition: Package.Definition<unit, Type<unit>>)
    : Distribution =
    Library(packageName, dependencies, definition)

/// Get the package name of the distribution.
let lookupPackageName (distribution: Distribution) : PackageName =
    match distribution with
    | Library(packageName, _, _) -> packageName

/// Add a package specification as a dependency of this library.
let insertDependency
    (dependencyPackageName: PackageName)
    (dependencyPackageSpec: Package.Specification<unit>)
    (distribution: Distribution)
    : Distribution =
    match distribution with
    | Library(packageName, dependencies, definition) ->
        Library(
            packageName,
            insert dependencyPackageName dependencyPackageSpec dependencies,
            definition
        )

let changePackageName packageName distribution =
    match distribution with
    | Library(_, dependencies, definition) -> Library(packageName, dependencies, definition)

let updateDefinition definition distribution =
    match distribution with
    | Library(packageName, dependencies, _) -> Library(packageName, dependencies, definition)

let updateDependencies dependencies distribution =
    match distribution with
    | Library(packageName, _, definition) -> Library(packageName, dependencies, definition)
