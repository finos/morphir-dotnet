namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.IO
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>
/// Where the files of a document tree go: the directory and stem of each document, the stems that a path budget
/// cuts, and the documents whose files would collide on a file system that ignores case.
/// </summary>
module internal TreeLayout =
    let private error code file message = StoreErrors.error code file message

    /// The extension of the JSON profile.
    [<Literal>]
    let Extension = ".json"

    [<Literal>]
    let ManifestFile = "manifest.json"

    let stems (Path names) =
        names
        |> List.map TreeStems.escapeName

    /// The module that holds an address of a type or value, or the address itself.
    let moduleOf (address: DocumentAddress) =
        match address with
        | Own(TypeDef(path, _))
        | Own(ValueDef(path, _)) -> Own(Module path)
        | Dependency(package, TypeDef(path, _))
        | Dependency(package, ValueDef(path, _)) -> Dependency(package, Module path)
        | Manifest
        | Own(Module _)
        | Dependency(_, Module _) -> address

    /// The name and the logical suffix of a type or value address.
    let memberOf (address: DocumentAddress) =
        match address with
        | Own(TypeDef(_, name))
        | Dependency(_, TypeDef(_, name)) -> Some(name, ".type")
        | Own(ValueDef(_, name))
        | Dependency(_, ValueDef(_, name)) -> Some(name, ".value")
        | Manifest
        | Own(Module _)
        | Dependency(_, Module _) -> None

    /// The directory of an address's file, with a trailing slash, or "" for the manifest.
    let directoryOf (own: PackageName) (address: DocumentAddress) =
        let modulePath (inner: ModuleDocumentAddress) =
            match inner with
            | Module(ModulePath path)
            | TypeDef(ModulePath path, _)
            | ValueDef(ModulePath path, _) -> stems path

        let segments =
            match address with
            | Manifest -> []
            | Own inner ->
                let (PackageName package) = own

                "pkg"
                :: stems package
                @ modulePath inner
            | Dependency(PackageName package, inner) ->
                "deps"
                :: stems package
                @ TreeStems.BareVersionSegment
                  :: modulePath inner

        segments
        |> List.map (fun segment ->
            segment
            + "/"
        )
        |> String.concat ""

    let byOrdinal (paths: ('a * string) list) =
        paths
        |> List.sortWith (fun (_, first) (_, second) -> String.CompareOrdinal(first, second))

    // Words of names, for messages that name two names with one spelling.
    let private words (Name parts) =
        "["
        + (parts
           |> List.map (fun part -> $"'{part}'")
           |> String.concat ", ")
        + "]"

    let private moduleWords (ModulePath(Path names)) =
        names
        |> List.map words
        |> String.concat "/"

    let describe (address: DocumentAddress) =
        let inner, package =
            match address with
            | Manifest -> None, None
            | Own inner -> Some inner, None
            | Dependency(PackageName package, inner) -> Some inner, Some package

        let ofPackage =
            match package with
            | Some(Path names) ->
                " of the dependency "
                + (names
                   |> List.map words
                   |> String.concat "/")
            | None -> ""

        match inner with
        | None -> "the manifest"
        | Some(Module path) -> $"the module {moduleWords path}{ofPackage}"
        | Some(TypeDef(path, name)) ->
            $"the type {words name} of the module {moduleWords path}{ofPackage}"
        | Some(ValueDef(path, name)) ->
            $"the value {words name} of the module {moduleWords path}{ofPackage}"

    /// Documents whose files would have one path on a file system that ignores case.
    let collisions (paths: (DocumentAddress * string) list) =
        paths
        |> List.groupBy (fun (_, path) -> path.ToUpperInvariant())
        |> List.filter (fun (_, group) -> group.Length > 1)
        |> List.map (fun (_, group) ->
            let path = snd group.Head

            let named =
                group
                |> List.map (
                    fst
                    >> describe
                )
                |> String.concat " and "

            error
                "tree.stem_collision"
                (Some path)
                $"The names of {named} have one file, {path}, on a file system that ignores case; each document needs a file of its own."
        )

    /// The physical path of each document, with the stems that the budget cut by module, or every error.
    let layout
        (budget: int)
        (own: PackageName)
        (whole: (DocumentAddress * string) list)
        : Result<Map<DocumentAddress, string> * Map<DocumentAddress, Map<Name, string>>, StoreError list> =
        let overBudget address path =
            error
                "invalid_distribution_shape"
                (Some path)
                $"The path of {describe address} has {String.length path} characters, and the path budget is {budget}; only a type or value stem can be cut."

        let tooLongModules =
            whole
            |> List.filter (fun (address, path) ->
                Option.isNone (memberOf address)
                && path.Length > budget
            )

        let blocked =
            tooLongModules
            |> List.map fst
            |> Set.ofList

        // A module's fileNames maps a name to one stem, and a reader finds the type file and the value file of a name
        // by it, so the members of a module that share a name share one stem: it is cut when the path of any of their
        // files is over the budget, so that the longest of their files fits.
        let sharedStems =
            whole
            |> List.choose (fun (address, path) ->
                memberOf address
                |> Option.map (fun (name, suffix) -> (moduleOf address, name), (path, suffix))
            )
            |> List.groupBy fst
            |> List.map (fun (key, members) ->
                let members = List.map snd members

                key,
                (members
                 |> List.exists (fun (path, _) -> path.Length > budget),
                 members
                 |> List.map snd
                 |> List.maxBy String.length)
            )
            |> Map.ofList

        let placed =
            whole
            |> List.choose (fun (address, path) ->
                match memberOf address with
                | None when
                    path.Length
                    <= budget
                    ->
                    Some(Ok(address, path, None))
                | None -> None
                | Some(name, suffix) ->
                    let cut, longest = sharedStems[(moduleOf address, name)]

                    if not cut then
                        Some(Ok(address, path, None))
                    elif Set.contains (moduleOf address) blocked then
                        None
                    else
                        let directory = directoryOf own address

                        let ending =
                            suffix
                            + Extension

                        match
                            TreeStems.cut
                                (budget
                                 - directory.Length
                                 - (longest
                                    + Extension)
                                     .Length)
                                name
                        with
                        | Ok stem ->
                            Some(
                                Ok(
                                    address,
                                    directory
                                    + stem
                                    + ending,
                                    Some(name, stem)
                                )
                            )
                        // The member whose own path fits names no error: the one over the budget does.
                        | Error _ when
                            path.Length
                            <= budget
                            ->
                            None
                        | Error reason ->
                            Some(
                                Error(
                                    error
                                        "invalid_distribution_shape"
                                        (Some path)
                                        $"The path of {describe address} has {path.Length} characters, and no cut stem fits the path budget of {budget}: {reason}"
                                )
                            )
            )

        let errors =
            (tooLongModules
             |> List.map (fun (address, path) -> overBudget address path))
            @ (placed
               |> List.choose (
                   function
                   | Error e -> Some e
                   | Ok _ -> None
               ))

        let chosen =
            placed
            |> List.choose (
                function
                | Ok found -> Some found
                | Error _ -> None
            )

        let cutCollisions =
            chosen
            |> List.map (fun (address, path, _) -> address, path)
            |> collisions
            |> List.map (fun found ->
                { found with
                    Code = "invalid_distribution_shape"
                    Message = $"A cut stem collides with another stem: {found.Message}"
                }
            )

        match
            errors
            @ cutCollisions
        with
        | [] ->
            let paths =
                chosen
                |> List.map (fun (address, path, _) -> address, path)
                |> Map.ofList

            let fileNames =
                chosen
                |> List.choose (fun (address, _, cut) ->
                    cut
                    |> Option.map (fun entry -> moduleOf address, entry)
                )
                |> List.groupBy fst
                |> List.map (fun (moduleAddress, entries) ->
                    moduleAddress, Map.ofList (List.map snd entries)
                )
                |> Map.ofList

            Ok(paths, fileNames)
        | found -> Error found
