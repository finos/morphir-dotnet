namespace Morphir.IR

/// <summary>
/// QName (Qualified Name) provides a package-scoped identifier for types and values.
/// Structure: (module path, local name)
/// </summary>
type QName =
    { ModulePath: ModulePath
      LocalName: Name }

/// <summary>
/// QName module provides functions for working with QName values.
/// QName (Qualified Name) provides a package-scoped identifier for types and values.
/// It consists of a module path and local name (without package path).
/// </summary>
[<RequireQualifiedAccess>]
module QName =

    /// <summary>
    /// Creates a QName from a module path and local name.
    /// </summary>
    let qName (modulePath: ModulePath) (localName: Name) =
        { ModulePath = modulePath
          LocalName = localName }

    /// <summary>
    /// Creates a QName from a Path and a Name.
    /// </summary>
    let qNameFromPath (modulePath: Path) (localName: Name) =
        { ModulePath = ModulePath.modulePath modulePath
          LocalName = localName }

    /// <summary>
    /// Gets the module path from a QName.
    /// </summary>
    let modulePath (qName: QName) = qName.ModulePath

    /// <summary>
    /// Gets the local name from a QName.
    /// </summary>
    let localName (qName: QName) = qName.LocalName

    /// <summary>
    /// Converts a QName to its string representation.
    /// Formats as "ModulePath:LocalName" using title case for module path and camelCase for local name.
    /// </summary>
    let toString (qName: QName) : string =
        let moduleName =
            qName
            |> modulePath
            |> ModulePath.modulePathToPath
            |> Path.toString Name.toTitleCase "."

        let localName = qName |> localName |> Name.toCamelCase

        $"{moduleName}:{localName}"

    /// <summary>
    /// Converts a QName to a human-readable string representation.
    /// For QName, this is the same as toString since there's no package path to omit.
    /// </summary>
    let toHumanString (qName: QName) : string = toString qName

    /// <summary>
    /// Converts a QName to a debug string representation.
    /// Formats as "QName(ModulePath, LocalName)" showing all components explicitly.
    /// </summary>
    let toDebugString (qName: QName) : string =
        let moduleName =
            qName
            |> modulePath
            |> ModulePath.modulePathToPath
            |> Path.toString Name.toTitleCase "."

        let localName = qName |> localName |> Name.toCamelCase

        $"QName({moduleName}, {localName})"
