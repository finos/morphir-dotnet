namespace Morphir.IR

/// <summary>
/// FQName (Fully-Qualified Name) provides a globally unique identifier for any type or value.
/// It consists of a package path, module path, and local name.
/// </summary>
module FQName =

    open Name
    open Path
    open PackageName
    open ModulePath

    /// <summary>
    /// FQName (Fully-Qualified Name) provides a globally unique identifier for any type or value.
    /// Structure: (package path, module path, local name)
    /// </summary>
    type FQName =
        { PackagePath: PackageName.PackageName
          ModulePath: ModulePath.ModulePath
          LocalName: Name }

    /// <summary>
    /// Creates an FQName from a package path, module path, and local name.
    /// </summary>
    let fqName (packagePath: PackageName.PackageName) (modulePath: ModulePath.ModulePath) (localName: Name) =
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

