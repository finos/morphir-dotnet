namespace Morphir.IR

/// <summary>
/// FQName (Fully-Qualified Name) provides a globally unique identifier for any type or value.
/// Structure: (package path, module path, local name)
/// </summary>
type FQName =
    { PackagePath: PackageName
      ModulePath: ModulePath
      LocalName: Name }

/// <summary>
/// FQName module provides functions for working with FQName values.
/// FQName (Fully-Qualified Name) provides a globally unique identifier for any type or value.
/// It consists of a package path, module path, and local name.
/// </summary>
[<RequireQualifiedAccess>]
module FQName =

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
        { PackagePath = PackageName.packageName packagePath
          ModulePath = ModulePath.modulePath modulePath
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

    /// <summary>
    /// Converts an FQName to its string representation.
    /// Formats as "Package.Module.Name" using title case for all parts.
    /// </summary>
    let toString (fqName: FQName) : string =
        let packageName =
            fqName
            |> packagePath
            |> PackageName.packageNameToPath
            |> Path.toString Name.toTitleCase "."

        let moduleName =
            fqName
            |> modulePathFromFQName
            |> ModulePath.modulePathToPath
            |> Path.toString Name.toTitleCase "."

        let localName = fqName |> localName |> Name.toTitleCase

        [ packageName; moduleName; localName ]
        |> List.filter (fun part -> part <> "")
        |> String.concat "."

    /// <summary>
    /// Converts an FQName to a human-readable string representation.
    /// Omits the package path for better readability when it's not empty.
    /// Formats as "Module.Name" (without package) or "Package.Module.Name" if package is empty.
    /// </summary>
    let toHumanString (fqName: FQName) : string =
        let packageName =
            fqName
            |> packagePath
            |> PackageName.packageNameToPath
            |> Path.toString Name.toTitleCase "."

        let moduleName =
            fqName
            |> modulePathFromFQName
            |> ModulePath.modulePathToPath
            |> Path.toString Name.toTitleCase "."

        let localName = fqName |> localName |> Name.toTitleCase

        // If package is empty, include it; otherwise omit for readability
        match packageName with
        | "" -> [ moduleName; localName ] |> List.filter (fun part -> part <> "") |> String.concat "."
        | _ -> [ moduleName; localName ] |> List.filter (fun part -> part <> "") |> String.concat "."

    /// <summary>
    /// Converts an FQName to a debug string representation.
    /// Formats as "FQName(Package, Module, Name)" showing all components explicitly.
    /// </summary>
    let toDebugString (fqName: FQName) : string =
        let packageName =
            fqName
            |> packagePath
            |> PackageName.packageNameToPath
            |> Path.toString Name.toTitleCase "."

        let moduleName =
            fqName
            |> modulePathFromFQName
            |> ModulePath.modulePathToPath
            |> Path.toString Name.toTitleCase "."

        let localName = fqName |> localName |> Name.toTitleCase

        $"FQName({packageName}, {moduleName}, {localName})"
