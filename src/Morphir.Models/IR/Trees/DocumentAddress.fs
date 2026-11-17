namespace Morphir.IR.Trees

open Morphir.IR

/// <summary>A document of one package: a module, or a type or value of a module, by its module path and name.</summary>
type ModuleDocumentAddress =
    /// <summary>The module document, which lists the module's types and values.</summary>
    | Module of ModulePath
    /// <summary>The document of a type of the module.</summary>
    | TypeDef of ModulePath * Name
    /// <summary>The document of a value of the module.</summary>
    | ValueDef of ModulePath * Name

    /// <summary>The module, type or value, for messages: for example <c>type billing#invoice-line</c>.</summary>
    override this.ToString() =
        let (ModulePath modulePath) =
            match this with
            | Module path
            | TypeDef(path, _)
            | ValueDef(path, _) -> path

        let path = CanonicalNames.pathToString modulePath

        match this with
        | Module _ -> $"module {path}"
        | TypeDef(_, name) -> $"type {path}#{CanonicalNames.nameToString name}"
        | ValueDef(_, name) -> $"value {path}#{CanonicalNames.nameToString name}"

/// <summary>
/// A document of a tree, addressed by what it is in the model. An address holds no file path, escaped stem,
/// extension or package version; <c>DocumentAddress.logicalPath</c> gives the
/// logical path the parent tree specification assigns it.
/// </summary>
type DocumentAddress =
    /// <summary>The manifest of the tree.</summary>
    | Manifest
    /// <summary>A document of the tree's own package, which the manifest names.</summary>
    | Own of ModuleDocumentAddress
    /// <summary>A document of a dependency, by its package.</summary>
    | Dependency of PackageName * ModuleDocumentAddress

    /// <summary>The document, for messages: for example <c>module basics of morphir/sdk</c>.</summary>
    override this.ToString() =
        match this with
        | Manifest -> "manifest"
        | Own address -> address.ToString()
        | Dependency(PackageName package, address) ->
            $"{address} of {CanonicalNames.pathToString package}"

/// <summary>Functions for <see cref="DocumentAddress"/>.</summary>
[<RequireQualifiedAccess>]
module DocumentAddress =
    /// The segment that ends a dependency's package path. It is bare while the v4 model carries no package version
    /// (decision 0015 of finos/morphir).
    let private versionSegment = "@"

    let private stems (Path names) =
        names
        |> List.map TreeStems.escapeName

    let private moduleSegments (address: ModuleDocumentAddress) =
        match address with
        | Module(ModulePath path) ->
            stems path
            @ [ "module" ]
        | TypeDef(ModulePath path, name) ->
            stems path
            @ [
                TreeStems.escapeName name
                + ".type"
            ]
        | ValueDef(ModulePath path, name) ->
            stems path
            @ [
                TreeStems.escapeName name
                + ".value"
            ]

    /// <summary>
    /// The logical path of a document in the tree of the package <paramref name="ownPackage"/>, as the parent v4
    /// tree specification assigns it: <c>manifest</c>, <c>pkg/&lt;package&gt;/&lt;module&gt;/module</c>,
    /// <c>pkg/…/&lt;stem&gt;.type</c>, <c>pkg/…/&lt;stem&gt;.value</c>, and the same below
    /// <c>deps/&lt;package&gt;/@/</c> for a dependency. Each segment is the escaped stem of a name, never cut for a
    /// path budget, and the path has no extension. <paramref name="ownPackage"/> is needed because an address of
    /// the own package does not hold the package: the manifest names it. C# calls this <c>LogicalPath</c>.
    /// </summary>
    [<CompiledName("LogicalPath")>]
    let logicalPath (ownPackage: PackageName) (address: DocumentAddress) : string =
        let (PackageName own) = ownPackage

        match address with
        | Manifest -> [ "manifest" ]
        | Own documentAddress ->
            "pkg"
            :: stems own
            @ moduleSegments documentAddress
        | Dependency(PackageName package, documentAddress) ->
            "deps"
            :: stems package
            @ versionSegment
              :: moduleSegments documentAddress
        |> String.concat "/"
