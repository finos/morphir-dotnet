namespace Morphir.IR

/// <summary>
/// PackageName represents the globally unique identifier for a package.
/// </summary>
type PackageName = PackageName of Path

/// <summary>
/// PackageName module provides functions for working with PackageName values.
/// PackageName represents the globally unique identifier for a package.
/// A package name is a Path that uniquely identifies a package across all Morphir systems.
/// </summary>
[<RequireQualifiedAccess>]
module PackageName =

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
