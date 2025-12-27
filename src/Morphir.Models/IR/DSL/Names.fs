namespace Morphir.IR.DSL

open System.Runtime.CompilerServices

/// <summary>
/// Names module provides Computation Expression builders for creating Name-related IR constructs.
/// </summary>
module Names =

    open Morphir.IR

    /// <summary>
    /// NameBuilder provides a Computation Expression for creating Name values.
    /// </summary>
    type NameBuilder() =
        /// <summary>
        /// Yields a Name from a single string.
        /// </summary>
        member _.Yield(str: string) = Name.fromString str

        /// <summary>
        /// Yields a Name from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) = Name.fromList strs

        /// <summary>
        /// Yields a Name directly.
        /// </summary>
        member _.Yield(name: Name) = name

        /// <summary>
        /// Combines multiple Names (takes the last one).
        /// </summary>
        member _.Combine(_: Name, name: Name) = name

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Name) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (empty Name).
        /// </summary>
        member _.Zero() = Name.empty

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Name) = f

        /// <summary>
        /// Runs the builder to produce the final Name.
        /// </summary>
        member _.Run(f: unit -> Name) = f()

    /// <summary>
    /// PathBuilder provides a Computation Expression for creating Path values.
    /// </summary>
    type PathBuilder() =
        /// <summary>
        /// Yields a Path from a single string (converted to Name).
        /// </summary>
        member _.Yield(str: string) =
            Path.fromList [ Name.fromString str ]

        /// <summary>
        /// Yields a Path from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) =
            Path.fromList (strs |> List.map Name.fromString)

        /// <summary>
        /// Yields a Path from a list of Names.
        /// </summary>
        member _.Yield(names: Name list) = Path.fromList names

        /// <summary>
        /// Yields a Path directly.
        /// </summary>
        member _.Yield(path: Path) = path

        /// <summary>
        /// Combines multiple Paths (appends).
        /// </summary>
        member _.Combine(path1: Path, path2: Path) =
            let (Path names1) = path1
            let (Path names2) = path2
            Path.fromList (names1 @ names2)

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Path) =
            items
            |> Seq.map f
            |> Seq.fold (fun acc p ->
                let (Path names1) = acc
                let (Path names2) = p
                Path.fromList (names1 @ names2)) Path.empty

        /// <summary>
        /// Zero case (empty Path).
        /// </summary>
        member _.Zero() = Path.empty

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> Path) = f

        /// <summary>
        /// Runs the builder to produce the final Path.
        /// </summary>
        member _.Run(f: unit -> Path) = f()

    /// <summary>
    /// PackageNameBuilder provides a Computation Expression for creating PackageName values.
    /// </summary>
    type PackageNameBuilder() =
        /// <summary>
        /// Yields a PackageName from a single string.
        /// </summary>
        member _.Yield(str: string) =
            Path.fromList [ Name.fromString str ]
            |> PackageName.packageName

        /// <summary>
        /// Yields a PackageName from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) =
            Path.fromList (strs |> List.map Name.fromString)
            |> PackageName.packageName

        /// <summary>
        /// Yields a PackageName from a Path.
        /// </summary>
        member _.Yield(path: Path) = PackageName.packageName path

        /// <summary>
        /// Yields a PackageName directly.
        /// </summary>
        member _.Yield(packageName: PackageName) = packageName

        /// <summary>
        /// Combines multiple PackageNames (takes the last one).
        /// </summary>
        member _.Combine(_: PackageName, packageName: PackageName) =
            packageName

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> PackageName) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (empty PackageName).
        /// </summary>
        member _.Zero() = PackageName.emptyPackageName

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> PackageName) = f

        /// <summary>
        /// Runs the builder to produce the final PackageName.
        /// </summary>
        member _.Run(f: unit -> PackageName) = f()

    /// <summary>
    /// ModulePathBuilder provides a Computation Expression for creating ModulePath values.
    /// </summary>
    type ModulePathBuilder() =
        /// <summary>
        /// Yields a ModulePath from a single string.
        /// </summary>
        member _.Yield(str: string) =
            Path.fromList [ Name.fromString str ]
            |> ModulePath.modulePath

        /// <summary>
        /// Yields a ModulePath from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) =
            Path.fromList (strs |> List.map Name.fromString)
            |> ModulePath.modulePath

        /// <summary>
        /// Yields a ModulePath from a Path.
        /// </summary>
        member _.Yield(path: Path) = ModulePath.modulePath path

        /// <summary>
        /// Yields a ModulePath directly.
        /// </summary>
        member _.Yield(modulePath: ModulePath) = modulePath

        /// <summary>
        /// Combines multiple ModulePaths (takes the last one).
        /// </summary>
        member _.Combine(_: ModulePath, modulePath: ModulePath) =
            modulePath

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> ModulePath) =
            items |> Seq.map f |> Seq.last

        /// <summary>
        /// Zero case (empty ModulePath).
        /// </summary>
        member _.Zero() = ModulePath.emptyModulePath

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> ModulePath) = f

        /// <summary>
        /// Runs the builder to produce the final ModulePath.
        /// </summary>
        member _.Run(f: unit -> ModulePath) = f()

    /// <summary>
    /// FQNameBuilder provides a Computation Expression for creating FQName values.
    /// </summary>
    type FQNameBuilder
        (
            packagePath: PackageName option,
            modulePath: ModulePath option,
            localName: Name option
        ) =
        new() = FQNameBuilder(None, None, None)

        member this.PackagePath = packagePath
        member this.ModulePath = modulePath
        member this.LocalName = localName

        /// <summary>
        /// Sets the package path.
        /// </summary>
        [<CustomOperation("packagePath")>]
        member this.packagePath(builder: FQNameBuilder, pkg: PackageName) =
            FQNameBuilder(Some pkg, builder.ModulePath, builder.LocalName)

        /// <summary>
        /// Sets the package path from a list of strings.
        /// </summary>
        [<CustomOperation("packagePath")>]
        member this.packagePath(builder: FQNameBuilder, strs: string list) =
            let pkg =
                Path.fromList (strs |> List.map Name.fromString)
                |> PackageName.packageName
            FQNameBuilder(Some pkg, builder.ModulePath, builder.LocalName)

        /// <summary>
        /// Sets the package path (alias for packagePath).
        /// </summary>
        [<CustomOperation("package")>]
        member this.package(builder: FQNameBuilder, pkg: PackageName) =
            FQNameBuilder(Some pkg, builder.ModulePath, builder.LocalName)

        /// <summary>
        /// Sets the package path from a list of strings (alias for packagePath).
        /// </summary>
        [<CustomOperation("package")>]
        member this.package(builder: FQNameBuilder, strs: string list) =
            let pkg =
                Path.fromList (strs |> List.map Name.fromString)
                |> PackageName.packageName
            FQNameBuilder(Some pkg, builder.ModulePath, builder.LocalName)

        /// <summary>
        /// Sets the module path.
        /// </summary>
        [<CustomOperation("modPath")>]
        member this.modPath(builder: FQNameBuilder, modPath: ModulePath) =
            FQNameBuilder(builder.PackagePath, Some modPath, builder.LocalName)

        /// <summary>
        /// Sets the module path from a list of strings.
        /// </summary>
        [<CustomOperation("modPath")>]
        member this.modPath(builder: FQNameBuilder, strs: string list) =
            let modPath =
                Path.fromList (strs |> List.map Name.fromString)
                |> ModulePath.modulePath
            FQNameBuilder(builder.PackagePath, Some modPath, builder.LocalName)

        /// <summary>
        /// Sets the module path (alias for modPath, using F# keyword-safe name).
        /// </summary>
        [<CustomOperation("module'")>]
        member this.module'(builder: FQNameBuilder, modPath: ModulePath) =
            FQNameBuilder(builder.PackagePath, Some modPath, builder.LocalName)

        /// <summary>
        /// Sets the module path from a list of strings (alias for modPath).
        /// </summary>
        [<CustomOperation("module'")>]
        member this.module'(builder: FQNameBuilder, strs: string list) =
            let modPath =
                Path.fromList (strs |> List.map Name.fromString)
                |> ModulePath.modulePath
            FQNameBuilder(builder.PackagePath, Some modPath, builder.LocalName)

        /// <summary>
        /// Sets the local name.
        /// </summary>
        [<CustomOperation("localName")>]
        member this.localName(builder: FQNameBuilder, name: Name) =
            FQNameBuilder(builder.PackagePath, builder.ModulePath, Some name)

        /// <summary>
        /// Sets the local name from a string.
        /// </summary>
        [<CustomOperation("localName")>]
        member this.localName(builder: FQNameBuilder, str: string) =
            FQNameBuilder(builder.PackagePath, builder.ModulePath, Some(Name.fromString str))

        /// <summary>
        /// Sets the local name from a list of strings.
        /// </summary>
        [<CustomOperation("localName")>]
        member this.localName(builder: FQNameBuilder, strs: string list) =
            FQNameBuilder(builder.PackagePath, builder.ModulePath, Some(Name.fromList strs))

        /// <summary>
        /// Sets the local name (alias for localName).
        /// </summary>
        [<CustomOperation("local")>]
        member this.local(builder: FQNameBuilder, name: Name) =
            FQNameBuilder(builder.PackagePath, builder.ModulePath, Some name)

        /// <summary>
        /// Sets the local name from a string (alias for localName).
        /// </summary>
        [<CustomOperation("local")>]
        member this.local(builder: FQNameBuilder, str: string) =
            FQNameBuilder(builder.PackagePath, builder.ModulePath, Some(Name.fromString str))

        /// <summary>
        /// Sets the local name from a list of strings (alias for localName).
        /// </summary>
        [<CustomOperation("local")>]
        member this.local(builder: FQNameBuilder, strs: string list) =
            FQNameBuilder(builder.PackagePath, builder.ModulePath, Some(Name.fromList strs))

        /// <summary>
        /// Yields the builder itself for chaining (for CE syntax support).
        /// </summary>
        member _.Yield(_: unit) = FQNameBuilder(packagePath, modulePath, localName)

        /// <summary>
        /// Zero case (empty FQName builder).
        /// </summary>
        member _.Zero() = FQNameBuilder(None, None, None)

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> FQNameBuilder) = f

        /// <summary>
        /// Combines two builders, taking non-None values from the second.
        /// </summary>
        member _.Combine(builder1: FQNameBuilder, builder2: FQNameBuilder) =
            FQNameBuilder(
                Option.orElse builder1.PackagePath builder2.PackagePath,
                Option.orElse builder1.ModulePath builder2.ModulePath,
                Option.orElse builder1.LocalName builder2.LocalName
            )

        /// <summary>
        /// Runs the builder to produce the final FQName.
        /// </summary>
        member _.Run(f: unit -> FQNameBuilder) =
            let builder = f()
            let pkg =
                builder.PackagePath
                |> Option.defaultValue PackageName.emptyPackageName
            let modPath =
                builder.ModulePath
                |> Option.defaultValue ModulePath.emptyModulePath
            let local =
                builder.LocalName
                |> Option.defaultValue (Name.fromString "")

            Morphir.IR.FQName.fqName pkg modPath local

    /// <summary>
    /// QNameBuilder provides a Computation Expression for creating QName values.
    /// </summary>
    type QNameBuilder(modulePath: ModulePath option, localName: Name option) =
        new() = QNameBuilder(None, None)

        member this.ModulePath = modulePath
        member this.LocalName = localName

        /// <summary>
        /// Sets the module path.
        /// </summary>
        [<CustomOperation("modPath")>]
        member this.modPath(builder: QNameBuilder, modPath: ModulePath) =
            QNameBuilder(Some modPath, builder.LocalName)

        /// <summary>
        /// Sets the module path from a list of strings.
        /// </summary>
        [<CustomOperation("modPath")>]
        member this.modPath(builder: QNameBuilder, strs: string list) =
            let modPath =
                Path.fromList (strs |> List.map Name.fromString)
                |> ModulePath.modulePath
            QNameBuilder(Some modPath, builder.LocalName)

        /// <summary>
        /// Sets the local name.
        /// </summary>
        [<CustomOperation("localName")>]
        member this.localName(builder: QNameBuilder, name: Name) =
            QNameBuilder(builder.ModulePath, Some name)

        /// <summary>
        /// Sets the local name from a string.
        /// </summary>
        [<CustomOperation("localName")>]
        member this.localName(builder: QNameBuilder, str: string) =
            QNameBuilder(builder.ModulePath, Some(Name.fromString str))

        /// <summary>
        /// Sets the local name from a list of strings.
        /// </summary>
        [<CustomOperation("localName")>]
        member this.localName(builder: QNameBuilder, strs: string list) =
            QNameBuilder(builder.ModulePath, Some(Name.fromList strs))

        /// <summary>
        /// Yields the builder itself for chaining (for CE syntax support).
        /// </summary>
        member _.Yield(_: unit) = QNameBuilder(modulePath, localName)

        /// <summary>
        /// Zero case (empty QName builder).
        /// </summary>
        member _.Zero() = QNameBuilder(None, None)

        /// <summary>
        /// Delays the computation (required for proper CE support).
        /// </summary>
        member _.Delay(f: unit -> QNameBuilder) = f

        /// <summary>
        /// Combines two builders, taking non-None values from the second.
        /// </summary>
        member _.Combine(builder1: QNameBuilder, builder2: QNameBuilder) =
            QNameBuilder(
                Option.orElse builder1.ModulePath builder2.ModulePath,
                Option.orElse builder1.LocalName builder2.LocalName
            )

        /// <summary>
        /// Runs the builder to produce the final QName.
        /// </summary>
        member _.Run(f: unit -> QNameBuilder) =
            let builder = f()
            let modPath =
                builder.ModulePath
                |> Option.defaultValue ModulePath.emptyModulePath
            let local =
                builder.LocalName
                |> Option.defaultValue (Name.fromString "")

            Morphir.IR.QName.qName modPath local

    /// <summary>
    /// Global builder instances for use in Computation Expressions.
    /// </summary>
    let name = NameBuilder()
    let path = PathBuilder()
    let packageName = PackageNameBuilder()
    let modulePath = ModulePathBuilder()
    let fqName = FQNameBuilder()
    let qName = QNameBuilder()

