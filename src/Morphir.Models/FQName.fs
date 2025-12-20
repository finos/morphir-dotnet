namespace Morphir.IR

/// <summary>
/// FQName (Fully-Qualified Name) provides a globally unique identifier for any type or value.
/// It consists of a package path, module path, and local name.
/// </summary>
module FQName =

    open Name
    open Path

    /// <summary>
    /// PackageName represents the globally unique identifier for a package.
    /// </summary>
    type PackageName = PackageName of Path

    /// <summary>
    /// ModulePath represents the path to a module within a package.
    /// </summary>
    type ModulePath = ModulePath of Path

    /// <summary>
    /// FQName (Fully-Qualified Name) provides a globally unique identifier for any type or value.
    /// Structure: (package path, module path, local name)
    /// </summary>
    type FQName =
        { PackagePath: PackageName
          ModulePath: ModulePath
          LocalName: Name }

    /// <summary>
    /// Creates a PackageName from a Path.
    /// </summary>
    let packageName (path: Path) = PackageName path

    /// <summary>
    /// Creates a PackageName from a list of Names.
    /// </summary>
    let packageNameFromList (names: Name list) = Path.fromList names |> PackageName

    /// <summary>
    /// Creates a PackageName from a string.
    /// </summary>
    let packageNameFromString (input: string) = Path.fromString input |> PackageName

    /// <summary>
    /// Gets the Path from a PackageName.
    /// </summary>
    let packageNameToPath (PackageName path) = path

    /// <summary>
    /// Creates an empty PackageName.
    /// </summary>
    let emptyPackageName = PackageName Path.empty

    /// <summary>
    /// Creates a ModulePath from a Path.
    /// </summary>
    let modulePath (path: Path) = ModulePath path

    /// <summary>
    /// Creates a ModulePath from a list of Names.
    /// </summary>
    let modulePathFromList (names: Name list) = Path.fromList names |> ModulePath

    /// <summary>
    /// Creates a ModulePath from a string.
    /// </summary>
    let modulePathFromString (input: string) = Path.fromString input |> ModulePath

    /// <summary>
    /// Gets the Path from a ModulePath.
    /// </summary>
    let modulePathToPath (ModulePath path) = path

    /// <summary>
    /// Creates an empty ModulePath.
    /// </summary>
    let emptyModulePath = ModulePath Path.empty

    /// <summary>
    /// Creates an FQName from a package path, module path, and local name.
    /// </summary>
    let fqName (packagePath: PackageName) (modulePath: ModulePath) (localName: Name) =
        { PackagePath = packagePath
          ModulePath = modulePath
          LocalName = localName }

    /// <summary>
    /// Creates an FQName from Path values and a Name.
    /// </summary>
    let fqNameFromPaths (packagePath: Path) (modulePath: Path) (localName: Name) =
        { PackagePath = PackageName packagePath
          ModulePath = ModulePath modulePath
          LocalName = localName }

    /// <summary>
    /// Gets the package path from an FQName.
    /// </summary>
    let packagePath (fqName: FQName) = fqName.PackagePath

    /// <summary>
    /// Gets the module path from an FQName.
    /// </summary>
    let modulePathFromFQName (fqName: FQName) = fqName.ModulePath

    /// <summary>
    /// Gets the local name from an FQName.
    /// </summary>
    let localName (fqName: FQName) = fqName.LocalName

