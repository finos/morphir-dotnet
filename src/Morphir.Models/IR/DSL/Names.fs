namespace Morphir.IR.DSL

/// <summary>
/// Names module provides Computation Expression builders for creating Name-related IR constructs.
/// </summary>
module Names =

    open Morphir.IR.Name
    open Morphir.IR.Path
    open Morphir.IR.FQName
    open Morphir.IR.QName
    open Morphir.IR.PackageName
    open Morphir.IR.ModulePath

    /// <summary>
    /// NameBuilder provides a Computation Expression for creating Name values.
    /// </summary>
    type NameBuilder() =
        /// <summary>
        /// Yields a Name from a single string.
        /// </summary>
        member _.Yield(str: string) = Morphir.IR.Name.fromString str

        /// <summary>
        /// Yields a Name from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) = Morphir.IR.Name.fromList strs

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
        member _.Zero() = Morphir.IR.Name.empty

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
            Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString str ]

        /// <summary>
        /// Yields a Path from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) =
            Morphir.IR.Path.fromList (strs |> List.map Morphir.IR.Name.fromString)

        /// <summary>
        /// Yields a Path from a list of Names.
        /// </summary>
        member _.Yield(names: Name list) = Morphir.IR.Path.fromList names

        /// <summary>
        /// Yields a Path directly.
        /// </summary>
        member _.Yield(path: Path) = path

        /// <summary>
        /// Combines multiple Paths (appends).
        /// </summary>
        member _.Combine(path1: Path, path2: Path) =
            let (Morphir.IR.Path.Path names1) = path1
            let (Morphir.IR.Path.Path names2) = path2
            Morphir.IR.Path.fromList (names1 @ names2)

        /// <summary>
        /// Supports for loops.
        /// </summary>
        member _.For(items: 'a seq, f: 'a -> Path) =
            items
            |> Seq.map f
            |> Seq.fold (fun acc p ->
                let (Morphir.IR.Path.Path names1) = acc
                let (Morphir.IR.Path.Path names2) = p
                Morphir.IR.Path.fromList (names1 @ names2)) Morphir.IR.Path.empty

        /// <summary>
        /// Zero case (empty Path).
        /// </summary>
        member _.Zero() = Morphir.IR.Path.empty

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
            Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString str ]
            |> Morphir.IR.PackageName.packageName

        /// <summary>
        /// Yields a PackageName from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) =
            Morphir.IR.Path.fromList (strs |> List.map Morphir.IR.Name.fromString)
            |> Morphir.IR.PackageName.packageName

        /// <summary>
        /// Yields a PackageName from a Path.
        /// </summary>
        member _.Yield(path: Path) = Morphir.IR.PackageName.packageName path

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
        member _.Zero() = Morphir.IR.PackageName.emptyPackageName

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
            Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString str ]
            |> Morphir.IR.ModulePath.modulePath

        /// <summary>
        /// Yields a ModulePath from a list of strings.
        /// </summary>
        member _.Yield(strs: string list) =
            Morphir.IR.Path.fromList (strs |> List.map Morphir.IR.Name.fromString)
            |> Morphir.IR.ModulePath.modulePath

        /// <summary>
        /// Yields a ModulePath from a Path.
        /// </summary>
        member _.Yield(path: Path) = Morphir.IR.ModulePath.modulePath path

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
        member _.Zero() = Morphir.IR.ModulePath.emptyModulePath

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
        member this.packagePath(pkg: PackageName) =
            FQNameBuilder(Some pkg, modulePath, localName)

        /// <summary>
        /// Sets the package path from a list of strings.
        /// </summary>
        member this.packagePath(strs: string list) =
            let pkg =
                Morphir.IR.Path.fromList (strs |> List.map Morphir.IR.Name.fromString)
                |> Morphir.IR.PackageName.packageName
            FQNameBuilder(Some pkg, modulePath, localName)

        /// <summary>
        /// Sets the module path.
        /// </summary>
        member this.Module'(modPath: Morphir.IR.ModulePath.ModulePath) =
            FQNameBuilder(packagePath, Some modPath, localName)

        /// <summary>
        /// Sets the module path from a list of strings.
        /// </summary>
        member this.Module'(strs: string list) =
            let modPath =
                Morphir.IR.Path.fromList (strs |> List.map Morphir.IR.Name.fromString)
                |> Morphir.IR.ModulePath.modulePath
            FQNameBuilder(packagePath, Some modPath, localName)

        /// <summary>
        /// Sets the local name.
        /// </summary>
        member this.localName(name: Name) =
            FQNameBuilder(packagePath, modulePath, Some name)

        /// <summary>
        /// Sets the local name from a string.
        /// </summary>
        member this.localName(str: string) =
            FQNameBuilder(packagePath, modulePath, Some(Morphir.IR.Name.fromString str))

        /// <summary>
        /// Sets the local name from a list of strings.
        /// </summary>
        member this.localName(strs: string list) =
            FQNameBuilder(packagePath, modulePath, Some(Morphir.IR.Name.fromList strs))

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
                |> Option.defaultValue Morphir.IR.PackageName.emptyPackageName
            let modPath =
                builder.ModulePath
                |> Option.defaultValue Morphir.IR.ModulePath.emptyModulePath
            let local =
                builder.LocalName
                |> Option.defaultValue (Morphir.IR.Name.fromString "")

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
        member this.Module'(modPath: Morphir.IR.ModulePath.ModulePath) =
            QNameBuilder(Some modPath, localName)

        /// <summary>
        /// Sets the module path from a list of strings.
        /// </summary>
        member this.Module'(strs: string list) =
            let modPath =
                Morphir.IR.Path.fromList (strs |> List.map Morphir.IR.Name.fromString)
                |> Morphir.IR.ModulePath.modulePath
            QNameBuilder(Some modPath, localName)

        /// <summary>
        /// Sets the local name.
        /// </summary>
        member this.localName(name: Name) =
            QNameBuilder(modulePath, Some name)

        /// <summary>
        /// Sets the local name from a string.
        /// </summary>
        member this.localName(str: string) =
            QNameBuilder(modulePath, Some(Morphir.IR.Name.fromString str))

        /// <summary>
        /// Sets the local name from a list of strings.
        /// </summary>
        member this.localName(strs: string list) =
            QNameBuilder(modulePath, Some(Morphir.IR.Name.fromList strs))

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
                |> Option.defaultValue Morphir.IR.ModulePath.emptyModulePath
            let local =
                builder.LocalName
                |> Option.defaultValue (Morphir.IR.Name.fromString "")

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

