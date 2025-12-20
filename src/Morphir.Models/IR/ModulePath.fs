namespace Morphir.IR

/// <summary>
/// ModulePath represents the path to a module within a package.
/// A module path is a Path that uniquely identifies a module within a package.
/// </summary>
module ModulePath =

    open Name
    open Path

    /// <summary>
    /// ModulePath represents the path to a module within a package.
    /// </summary>
    type ModulePath = ModulePath of Path

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

