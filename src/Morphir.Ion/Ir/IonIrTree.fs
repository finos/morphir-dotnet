namespace Morphir.Ion

open System
open Morphir.IR
open Morphir.IR.Pipeline
open Morphir.Ion.IonNodes

/// <summary>Why an Ion document tree could not be read.</summary>
type IonIrTreeError =
    /// <summary>The tree is not a valid distribution. The error's path is the failing file's location.</summary>
    | Invalid of IonIrError
    /// <summary>A file could not be opened or read. This keeps the original cause.</summary>
    | SourceFailed of path: string * cause: exn

/// <summary>Reads a Morphir distribution stored as an Ion document tree.</summary>
/// <remarks>
/// The tree has <c>manifest.ion</c>, then <c>pkg/&lt;package&gt;/&lt;module&gt;/</c> for the distribution's own
/// modules and <c>deps/&lt;package&gt;/@/&lt;module&gt;/</c> for each dependency's. A file's path supplies the
/// package, module and name its elements may leave out.
/// </remarks>
[<RequireQualifiedAccess>]
module IonIrTree =
    /// One file of the tree, with the location it is addressed by.
    type private TreeFile = {
        Location: string
        Segments: string list
        File: VFile
    }

    /// The kinds of file a module directory holds.
    type private FileKind =
        | ModuleFile
        | TypeFile of stem: string
        | ValueFile of stem: string

    /// Where a file is, read from its path alone. Directory stems are already names.
    type private Placement =
        | OwnPlacement of directories: Name list
        | DependencyPlacement of package: Name list * modules: Name list

    type private NodeFile = {
        Source: TreeFile
        Placement: Placement
        Kind: FileKind
    }

    type private Classified =
        | ManifestFile
        | NodeFileEntry of NodeFile
        | IgnoredFile

    /// Where an element came from, so that an error in the datagram can name the file and the place in it.
    type private Provenance = {
        Location: string
        /// The element's position among the top-level values of its file.
        Index: int
        /// The elements a member list holds, in order, where they come from other files or other places.
        Children: Map<string, Provenance list>
        /// Whether the element is written inside the element that holds it, so that an error in it is placed
        /// by the holder's path to it.
        Inline: bool
    }

    /// A datagram element and where it came from.
    type private Element = { Value: IonValue; Origin: Provenance }

    /// A module's own directory and its files, in the order they are read.
    type private ModuleGroup = {
        Package: Name list
        ModuleNames: Name list
        DirectoryLocation: string
        Files: NodeFile list
    }

    /// How a module's elements are shaped.
    type private ModuleMode =
        /// One of the distribution's own modules.
        | OwnModule
        /// A fragment of a dependency's module specification, written as a top-level element with a package.
        | SpecificationFragment of package: Path
        /// A definition module inside an application's linked dependency.
        | LinkedModule of package: Path

    let private leaf location index = {
        Location = location
        Index = index
        Children = Map.empty
        Inline = false
    }

    let private treeError code (location: string option) message =
        Result.Error(
            Invalid
                {
                    Code = code
                    Message = message
                    Path = location
                    Location = None
                }
        )

    /// Runs work that may raise a failure in a file's own terms. The failure names the file, and keeps the place
    /// in that file as its location.
    let private guard (location: string) (work: unit -> 'a) : Result<'a, IonIrTreeError> =
        try
            Ok(work ())
        with IonIrFailure error ->
            Result.Error(
                Invalid
                    { error with
                        Path = Some location
                        Location = error.Path
                    }
            )

    let private traverse (action: 'a -> Result<'b, 'e>) (items: 'a list) : Result<'b list, 'e> =
        let rec loop accumulated remaining =
            match remaining with
            | [] -> Ok(List.rev accumulated)
            | item :: rest ->
                match action item with
                | Ok found ->
                    loop
                        (found
                         :: accumulated)
                        rest
                | Result.Error error -> Result.Error error

        loop [] items

    type private ResultBuilder() =
        member _.Bind(result, binder) = Result.bind binder result
        member _.Return value = Ok value
        member _.ReturnFrom(result: Result<_, _>) = result
        member _.Zero() = Ok()

    let private tree = ResultBuilder()

    // ---- Paths ----

    /// The segments of a path, as written. Only <c>/</c> separates them, since locations in a tree use it. A
    /// segment that is empty, <c>.</c> or <c>..</c> is kept here, and refused when the file is classified.
    let private segmentsOf (path: string) = List.ofArray (path.Split '/')

    /// Every file that has a location, in tree order, with the directories above it. A file with no path, or an
    /// empty one, has no location, and a directory whose path is empty or <c>.</c> adds no segment, as in
    /// <c>VFileTree</c>, where a tree's own path is <c>.</c>.
    let rec private collect (parent: string list) (content: TreeNode list) : TreeFile list =
        content
        |> List.collect (fun node ->
            match node with
            | TreeNode.File file ->
                match file.Path with
                | Some path when path <> "" ->
                    let segments =
                        parent
                        @ segmentsOf path

                    [
                        {
                            Location = String.Join("/", segments)
                            Segments = segments
                            File = file
                        }
                    ]
                | _ -> []
            | TreeNode.Directory directory ->
                collect
                    (if
                         directory.Path = ""
                         || directory.Path = "."
                     then
                         parent
                     else
                         parent
                         @ segmentsOf directory.Path)
                    directory.Content
        )

    let private fileKindOf (name: string) =
        if name = "module.ion" then
            Some ModuleFile
        elif name.EndsWith(".type.ion", StringComparison.Ordinal) then
            Some(
                TypeFile(
                    name.Substring(
                        0,
                        name.Length
                        - ".type.ion".Length
                    )
                )
            )
        elif name.EndsWith(".value.ion", StringComparison.Ordinal) then
            Some(
                ValueFile(
                    name.Substring(
                        0,
                        name.Length
                        - ".value.ion".Length
                    )
                )
            )
        else
            None

    /// Reads a directory stem, which is never cut.
    let private directoryName (stem: string) : Result<Name, string> =
        match TreeStems.parseStem stem with
        | Ok(WholeStem name) -> Ok name
        | Ok(CutStem _) ->
            Result.Error $"The directory '{stem}' is a cut stem, and only a file's stem is cut."
        | Result.Error message -> Result.Error message

    let private directoryNames (stems: string list) : Result<Name list, string> =
        traverse directoryName stems

    /// A segment a tree path never holds, with the reason.
    let private badSegment (segments: string list) : string option =
        segments
        |> List.tryPick (fun segment ->
            match segment with
            | "" -> Some "The path has an empty segment, which a tree path never holds."
            | "."
            | ".." -> Some $"The path has a '{segment}' segment, which a tree path never holds."
            | _ -> None
        )

    /// Reads a file's path, without the distribution's package. A path problem is a message.
    /// Under <c>pkg/</c> and <c>deps/</c> every file has a place. Elsewhere a file is not the tree's, and the
    /// tree ignores it.
    let private classify (file: TreeFile) : Result<Classified, string> =
        match badSegment file.Segments with
        | Some message -> Result.Error message
        | None ->
            match file.Segments with
            | [ "manifest.ion" ] -> Ok ManifestFile
            | area :: rest when
                (area = "pkg"
                 || area = "deps")
                && not rest.IsEmpty
                ->
                let name = List.last rest

                let directories =
                    List.take
                        (rest.Length
                         - 1)
                        rest

                match fileKindOf name with
                | None ->
                    Result.Error
                        $"'{name}' is not a tree file. A tree file is module.ion, <stem>.type.ion or <stem>.value.ion."
                | Some kind ->
                    let stemCheck =
                        match kind with
                        | ModuleFile -> Ok()
                        | TypeFile stem
                        | ValueFile stem ->
                            TreeStems.parseStem stem
                            |> Result.map ignore

                    match stemCheck with
                    | Result.Error message -> Result.Error message
                    | Ok() when area = "pkg" ->
                        directoryNames directories
                        |> Result.map (fun names ->
                            NodeFileEntry
                                {
                                    Source = file
                                    Placement = OwnPlacement names
                                    Kind = kind
                                }
                        )
                    | Ok() ->
                        TreeStems.splitDependency directories
                        |> Result.bind (fun split ->
                            match
                                directoryNames split.PackageDirectories,
                                directoryNames split.ModuleDirectories
                            with
                            | Ok _, Ok [] ->
                                Result.Error
                                    "A module path has at least one name, so this file belongs in deps/<package>/@/<module>/."
                            | Ok package, Ok modules ->
                                Ok(
                                    NodeFileEntry
                                        {
                                            Source = file
                                            Placement = DependencyPlacement(package, modules)
                                            Kind = kind
                                        }
                                )
                            | Result.Error message, _
                            | _, Result.Error message -> Result.Error message
                        )
            | _ -> Ok IgnoredFile

    // ---- Elements ----

    /// What a top-level value of a tree file is, from its annotations alone. The decoder checks the rest.
    type private ElementKind =
        | ModuleElement
        | TypeElement
        | ValueElement
        | PackageElement
        | FooterElement
        | OtherElement

    let private elementKind (names: string list) =
        match names with
        | [ "morphir_footer" ] -> FooterElement
        | [ "package"; _ ] -> PackageElement
        | [ "module"; "spec" ] -> ModuleElement
        | _ ->
            match List.tryLast names with
            | Some "module" -> ModuleElement
            | Some "type" -> TypeElement
            | Some "value" -> ValueElement
            | _ -> OtherElement

    let private structFields (value: IonValue) =
        match value.Data with
        | IonData.Struct fields -> fields
        | _ -> []

    let private stringValue (text: string) : IonValue = {
        Annotations = []
        Data = IonData.String text
    }

    /// Adds a text member that the element does not state.
    let private withText (name: string) (text: string) (value: IonValue) =
        { value with
            Data =
                IonData.Struct(
                    structFields value
                    @ [ IonSymbol.Text name, stringValue text ]
                )
        }

    let private mismatch location message =
        treeError "path_name_mismatch" (Some location) message

    /// Checks that what a member states agrees with the path.
    let private checkPathMember
        (location: string)
        (path: IonPath)
        (name: string)
        (expected: Path)
        (describe: string)
        (fields: Members)
        : Result<unit, IonIrTreeError> =
        match tryMember name fields with
        | None -> Ok()
        | Some stated ->
            let here = memberAt path name

            let found = guard location (fun () -> pathText here (text here stated))

            match found with
            | Result.Error error -> Result.Error error
            | Ok actual when actual = expected -> Ok()
            | Ok actual ->
                mismatch
                    location
                    $"The {name} '{CanonicalNames.pathToString actual}' is not the {describe} '{CanonicalNames.pathToString expected}' that the path names."

    /// Where the element gets its package: the distribution's own, or a dependency's.
    let private packageNameOf (names: Name list) = PackageName(Path names)

    /// Checks a stated package and module against the path.
    let private checkScope
        (location: string)
        (path: IonPath)
        (package: Path)
        (modulePath: Path option)
        (fields: Members)
        =
        tree {
            do! checkPathMember location path "package" package "package" fields

            match modulePath with
            | Some expected -> do! checkPathMember location path "module" expected "module" fields
            | None -> ()
        }

    /// Reads a file's values under the shared budget.
    let private readValues
        (budget: IonReadBudget)
        (file: TreeFile)
        : Result<IonValue list, IonIrTreeError> =
        match IonSources.readContent budget.Limits file.File.Content with
        | Result.Error cause -> Result.Error(SourceFailed(file.Location, cause))
        | Ok bytes ->
            // A short stack raises, for the caller to read again where there is room.
            match IonReader.readWithBudgetOrThrowOnShortStack budget bytes with
            | Result.Error error ->
                Result.Error(
                    Invalid
                        {
                            Code = IonReadError.code error
                            Message = IonReadError.describe error
                            Path = Some file.Location
                            Location = None
                        }
                )
            | Ok values ->
                // A tree file ends where the file ends, so a footer is never expected.
                guard
                    file.Location
                    (fun () ->
                        values
                        |> List.iteri (fun index value ->
                            if
                                annotationNames (itemAt Root index) value = [ "morphir_footer" ]
                            then
                                fail
                                    "invalid_document"
                                    (itemAt Root index)
                                    "A tree file has no morphir_footer. The end of the file ends it."
                        )

                        values
                    )

    let private canonical (names: Name list) =
        CanonicalNames.pathToString (Path names)

    let private nameText (name: Name) = CanonicalNames.nameToString name

    /// A module file's elements: module fragments, then types and values, each read against the path.
    let private moduleFileElements
        (file: TreeFile)
        (mode: ModuleMode)
        (package: Path)
        (modulePath: Path)
        (values: IonValue list)
        : Result<Element list * Element list * Element list, IonIrTreeError> =
        let location = file.Location

        let one (index: int, value: IonValue) =
            let path = itemAt Root index

            guard
                location
                (fun () ->
                    let names = annotationNames path value
                    let fields = members path value
                    names, fields
                )
            |> Result.bind (fun (names, fields) ->
                let origin = leaf location index

                match elementKind names with
                | ModuleElement -> tree {
                    do! checkScope location path package None fields
                    do! checkPathMember location path "name" modulePath "module" fields

                    let named =
                        if hasMember "name" fields then
                            value
                        else
                            withText "name" (CanonicalNames.pathToString modulePath) value

                    let scoped =
                        match mode with
                        | SpecificationFragment dependency when not (hasMember "package" fields) ->
                            withText "package" (CanonicalNames.pathToString dependency) named
                        | SpecificationFragment _
                        | OwnModule
                        | LinkedModule _ -> named

                    return ModuleElement, { Value = scoped; Origin = origin }
                  }
                | TypeElement
                | ValueElement as kind -> tree {
                    do! checkScope location path package (Some modulePath) fields
                    return kind, { Value = value; Origin = origin }
                  }
                | PackageElement
                | FooterElement
                | OtherElement ->
                    treeError
                        "invalid_document"
                        (Some location)
                        $"module.ion holds a module, its types and its values, found {spell names}."
            )

        values
        |> List.indexed
        |> traverse one
        |> Result.map (fun elements ->
            let pick wanted =
                elements
                |> List.filter (fun (kind, _) -> kind = wanted)
                |> List.map snd

            pick ModuleElement, pick TypeElement, pick ValueElement
        )

    /// The one type or value a node file holds, with the name its path gives it.
    let private nodeFileElement
        (file: TreeFile)
        (stem: string)
        (wanted: ElementKind)
        (package: Path)
        (modulePath: Path)
        (values: IonValue list)
        : Result<Element, IonIrTreeError> =
        let location = file.Location
        let label = if wanted = TypeElement then "type" else "value"
        let path = itemAt Root 0

        match values with
        | [ value ] ->
            guard location (fun () -> annotationNames path value, members path value)
            |> Result.bind (fun (names, fields) ->
                if
                    elementKind names
                    <> wanted
                then
                    treeError
                        "invalid_document"
                        (Some location)
                        $"A .{label}.ion file holds one {label}, found {spell names}."
                else
                    tree {
                        do! checkScope location path package (Some modulePath) fields

                        let! stated =
                            match tryMember "name" fields with
                            | None -> Ok None
                            | Some item ->
                                guard
                                    location
                                    (fun () -> Some(nameString (memberAt path "name") item))

                        let! named =
                            match TreeStems.parseStem stem, stated with
                            | Ok(WholeStem expected), Some actual when actual = expected ->
                                Ok value
                            | Ok(WholeStem expected), None ->
                                Ok(withText "name" (nameText expected) value)
                            | Ok(WholeStem expected), Some actual ->
                                mismatch
                                    location
                                    $"The name '{nameText actual}' is not the name '{nameText expected}' that the file name gives."
                            | Ok(CutStem(prefix, digest)), Some actual when
                                TreeStems.matchesCut prefix digest actual
                                ->
                                Ok value
                            | Ok(CutStem(prefix, digest)), Some actual ->
                                mismatch
                                    location
                                    $"The name '{nameText actual}' does not belong to the stem that begins '{prefix}' and ends '__{digest}'."
                            | Ok(CutStem _), None ->
                                mismatch
                                    location
                                    "A file whose name the path budget cut states its name, because the stem no longer spells it."
                            | Result.Error message, _ ->
                                treeError "invalid_tree_path" (Some location) message

                        return {
                            Value = named
                            Origin = leaf location 0
                        }
                    }
            )
        | [] ->
            treeError
                "invalid_document"
                (Some location)
                $"A .{label}.ion file holds one {label}, and this one is empty."
        | _ ->
            let count = values.Length.ToString(Globalization.CultureInfo.InvariantCulture)

            treeError
                "invalid_document"
                (Some location)
                $"A .{label}.ion file holds one {label}, and this one holds {count}."

    // ---- Nesting ----

    /// Appends elements to a member list of an element, keeping where each came from.
    let private appendChildren
        (listName: string)
        (children: Element list)
        (parent: Element)
        : Element =
        if children.IsEmpty then
            parent
        else
            let key = IonSymbol.Text listName
            let fields = structFields parent.Value

            // The members the parent already holds are written in the parent's own file.
            let own =
                { parent.Origin with
                    Children = Map.empty
                    Inline = true
                }

            let added =
                children
                |> List.map (fun child -> child.Value)

            let addedOrigins =
                children
                |> List.map (fun child -> child.Origin)

            let existing =
                fields
                |> List.tryPick (fun (name, item) -> if name = key then Some item else None)

            match existing with
            | None -> {
                Value =
                    { parent.Value with
                        Data =
                            IonData.Struct(
                                fields
                                @ [
                                    key,
                                    {
                                        Annotations = []
                                        Data = IonData.List added
                                    }
                                ]
                            )
                    }
                Origin =
                    { parent.Origin with
                        Children = Map.add listName addedOrigins parent.Origin.Children
                    }
              }
            | Some {
                       Annotations = []
                       Data = IonData.List items
                   } -> {
                Value =
                    { parent.Value with
                        Data =
                            IonData.Struct(
                                fields
                                |> List.map (fun (name, item) ->
                                    if name = key then
                                        name,
                                        { item with
                                            Data =
                                                IonData.List(
                                                    items
                                                    @ added
                                                )
                                        }
                                    else
                                        name, item
                                )
                            )
                    }
                Origin =
                    { parent.Origin with
                        Children =
                            Map.add
                                listName
                                ((items
                                  |> List.map (fun _ -> own))
                                 @ addedOrigins)
                                parent.Origin.Children
                    }
              }
            | Some _ ->
                fail
                    "invalid_type"
                    (memberAt (itemAt Root parent.Origin.Index) listName)
                    "Expected a list."

    /// Gives the first module element the types and values that other files state for the module.
    let private withMembers
        (types: Element list)
        (values: Element list)
        (element: Element)
        : Element =
        element
        |> appendChildren "types" types
        |> appendChildren "values" values

    // ---- Assembly ----

    type private Context = {
        Budget: IonReadBudget
        Header: IrHeader
    }

    /// Reads a module's files and returns its elements: the module elements, the first holding every member.
    let private moduleElements
        (context: Context)
        (mode: ModuleMode)
        (group: ModuleGroup)
        : Result<Element list, IonIrTreeError> =
        let package =
            match mode with
            | OwnModule ->
                let (PackageName own) = context.Header.Package
                own
            | SpecificationFragment dependency
            | LinkedModule dependency -> dependency

        let modulePath = Path group.ModuleNames

        let read (file: NodeFile) = tree {
            let! values = readValues context.Budget file.Source

            match file.Kind with
            | ModuleFile -> return! moduleFileElements file.Source mode package modulePath values
            | TypeFile stem ->
                let! element =
                    nodeFileElement file.Source stem TypeElement package modulePath values

                return [], [ element ], []
            | ValueFile stem ->
                let! element =
                    nodeFileElement file.Source stem ValueElement package modulePath values

                return [], [], [ element ]
        }

        tree {
            let! parts = traverse read group.Files

            let modules =
                parts
                |> List.collect (fun (found, _, _) -> found)

            let types =
                parts
                |> List.collect (fun (_, found, _) -> found)

            let values =
                parts
                |> List.collect (fun (_, _, found) -> found)

            let firstFile = (List.head group.Files).Source.Location

            let! heads =
                match modules, mode with
                | _ :: _, _ -> Ok modules
                | [], SpecificationFragment dependency ->
                    // A dependency's module with only type and value files has no module element to merge into.
                    Ok [
                        {
                            Value = {
                                Annotations = [ IonSymbol.Text "module"; IonSymbol.Text "spec" ]
                                Data =
                                    IonData.Struct [
                                        IonSymbol.Text "package",
                                        stringValue (CanonicalNames.pathToString dependency)
                                        IonSymbol.Text "name",
                                        stringValue (CanonicalNames.pathToString modulePath)
                                    ]
                            }
                            Origin = leaf firstFile 0
                        }
                    ]
                | [], (OwnModule | LinkedModule _) ->
                    treeError
                        "invalid_document"
                        (Some firstFile)
                        $"The module '{CanonicalNames.pathToString modulePath}' has no module.ion."

            let first = List.head heads

            let! nested = guard first.Origin.Location (fun () -> withMembers types values first)

            return
                nested
                :: List.tail heads
        }

    /// Builds the groups of module files for one area, ordered by their directories.
    let private groupModules (files: (NodeFile * Name list * Name list) list) : ModuleGroup list =
        let order (file: NodeFile) =
            match file.Kind with
            | ModuleFile -> 0
            | TypeFile _ -> 1
            | ValueFile _ -> 2

        files
        |> List.groupBy (fun (_, package, modules) ->
            canonical package
            + "|"
            + canonical modules
        )
        |> List.map (fun (key, members) ->
            let _, package, modules = List.head members

            let ordered =
                members
                |> List.map (fun (file, _, _) -> file)
                |> List.sortWith (fun a b ->
                    match compare (order a) (order b) with
                    | 0 -> String.CompareOrdinal(a.Source.Location, b.Source.Location)
                    | other -> other
                )

            let directory =
                ordered
                |> List.map (fun file ->
                    String.Join(
                        "/",
                        List.take
                            (file.Source.Segments.Length
                             - 1)
                            file.Source.Segments
                    )
                )
                |> List.min

            {
                Package = package
                ModuleNames = modules
                DirectoryLocation = directory
                Files = ordered
            }
        )
        |> List.sortWith (fun a b ->
            String.CompareOrdinal(a.DirectoryLocation, b.DirectoryLocation)
        )

    /// A dependency a manifest names, with its element.
    type private ManifestDependency = {
        Key: string
        Package: Path
        Element: Element
    }

    /// What the manifest holds: the header, every package element after it, and the ones that name a package.
    type private ManifestContent = {
        Header: IrHeader
        HeaderElement: Element
        Packages: Element list
        Named: ManifestDependency list
    }

    /// Reads the manifest: its header, and the package elements after it.
    let private readManifest
        (budget: IonReadBudget)
        (file: TreeFile)
        : Result<ManifestContent, IonIrTreeError> =
        tree {
            let! values = readValues budget file
            let location = file.Location

            return!
                guard
                    location
                    (fun () ->
                        match values with
                        | [] ->
                            fail
                                "invalid_document"
                                Root
                                "A document starts with a morphir:: header."
                        | first :: rest ->
                            let headerPath = itemAt Root 0

                            match first.Annotations with
                            | [ IonSymbol.Text "morphir" ] -> ()
                            | _ ->
                                fail
                                    "invalid_document"
                                    headerPath
                                    "A document starts with a morphir:: header."

                            let header =
                                IrHeader.read headerPath (IrHeader.fields headerPath first)

                            let packages =
                                rest
                                |> List.mapi (fun offset value ->
                                    let index = offset + 1
                                    let path = itemAt Root index
                                    let names = annotationNames path value

                                    match elementKind names with
                                    | PackageElement -> ()
                                    | ModuleElement
                                    | TypeElement
                                    | ValueElement
                                    | FooterElement
                                    | OtherElement ->
                                        fail
                                            "invalid_document"
                                            path
                                            $"manifest.ion holds the header and the dependencies, found {spell names}."

                                    let package =
                                        match tryMember "name" (members path value) with
                                        | Some item ->
                                            let here = memberAt path "name"
                                            Some(pathText here (text here item))
                                        | None -> None

                                    package,
                                    {
                                        Value = value
                                        Origin = leaf location index
                                    }
                                )

                            {
                                Header = header
                                HeaderElement = {
                                    Value = first
                                    Origin = leaf location 0
                                }
                                Packages =
                                    packages
                                    |> List.map snd
                                Named =
                                    packages
                                    |> List.choose (fun (package, element) ->
                                        package
                                        |> Option.map (fun found -> {
                                            Key = CanonicalNames.pathToString found
                                            Package = found
                                            Element = element
                                        })
                                    )
                            }
                    )
        }

    // ---- Error placement ----

    /// The steps of a rendered path such as <c>$[3].types[1].body</c>, each with the offset after it.
    let private pathSteps (path: string) : (Choice<int, string> * int) list =
        let steps = ResizeArray()
        let mutable position = 1
        let mutable scanning = true

        while scanning
              && position < path.Length do
            if
                path[position] = '['
                && position
                   + 1 < path.Length
                && Char.IsAsciiDigit
                    path[position
                         + 1]
            then
                let close = path.IndexOf(']', position)

                if close < 0 then
                    scanning <- false
                else
                    match
                        Int32.TryParse(
                            path.AsSpan(
                                position
                                + 1,
                                close
                                - position
                                - 1
                            ),
                            Globalization.NumberStyles.None,
                            Globalization.CultureInfo.InvariantCulture
                        )
                    with
                    | true, index ->
                        steps.Add(Choice1Of2 index, close + 1)
                        position <- close + 1
                    | _ -> scanning <- false
            elif path[position] = '.' then
                let mutable finish =
                    position
                    + 1

                while finish < path.Length
                      && path[finish]
                         <> '.'
                      && path[finish]
                         <> '[' do
                    finish <- finish + 1

                steps.Add(
                    Choice2Of2(
                        path.Substring(
                            position
                            + 1,
                            finish
                            - position
                            - 1
                        )
                    ),
                    finish
                )

                position <- finish
            else
                scanning <- false

        List.ofSeq steps

    /// Places an error from the datagram in the file and element it came from.
    let private place (origins: Provenance[]) (error: IonIrError) : IonIrError =
        match error.Path with
        | None -> error
        | Some path when path.StartsWith("$", StringComparison.Ordinal) ->
            let steps = pathSteps path

            let start, afterFirst =
                match steps with
                | (Choice1Of2 index, offset) :: _ when
                    index >= 0
                    && index < origins.Length
                    ->
                    origins[index], offset
                | _ -> origins[0], 1

            let rec descend (origin: Provenance) (offset: int) remaining =
                match remaining with
                | (Choice2Of2 listName, _) :: (Choice1Of2 index, after) :: rest ->
                    match Map.tryFind listName origin.Children with
                    // An inline member stays with its holder, which keeps the whole path to it.
                    | Some children when
                        index < children.Length
                        && not children[index].Inline
                        ->
                        descend children[index] after rest
                    | _ -> origin, offset
                | _ -> origin, offset

            let origin, consumed =
                descend start afterFirst (List.skip (min 1 steps.Length) steps)

            { error with
                Path = Some origin.Location
                Location =
                    Some(
                        "$["
                        + origin.Index.ToString(Globalization.CultureInfo.InvariantCulture)
                        + "]"
                        + path.Substring consumed
                    )
            }
        | Some _ -> error

    // ---- The reader ----

    let private rootManifests = [ "manifest.json"; "manifest.yaml"; "manifest.yml" ]

    /// A tree file with the package and module it belongs to, read against the distribution's own package.
    type private Located = {
        Node: NodeFile
        Package: Name list
        Modules: Name list
        IsDependency: bool
    }

    /// Finds which directories of a file's path are the module path.
    let private locate (ownPackage: Name list) (node: NodeFile) : Result<Located, IonIrTreeError> =
        match node.Placement with
        | DependencyPlacement(package, modules) ->
            Ok
                {
                    Node = node
                    Package = package
                    Modules = modules
                    IsDependency = true
                }
        | OwnPlacement directories ->
            let count = ownPackage.Length

            if
                directories.Length < count
                || List.take count directories
                   <> ownPackage
            then
                treeError
                    "invalid_tree_path"
                    (Some node.Source.Location)
                    $"A file under pkg/ belongs in the package directory '{canonical ownPackage}'."
            elif directories.Length = count then
                treeError
                    "invalid_tree_path"
                    (Some node.Source.Location)
                    $"A module path has at least one name, so this file belongs in pkg/{canonical ownPackage}/<module>/."
            else
                Ok
                    {
                        Node = node
                        Package = ownPackage
                        Modules = List.skip count directories
                        IsDependency = false
                    }

    /// The directory of a dependency's package, such as <c>deps/other/dep</c>, from one of its files.
    let private packageDirectoryOf (file: NodeFile) =
        let segments = file.Source.Segments

        let stop =
            segments
            |> List.tryFindIndex (fun segment -> segment = "@")
            |> Option.defaultValue segments.Length

        String.Join("/", List.take stop segments)

    /// Reads the files the manifest and the paths name, in the order of the datagram they stand for.
    /// It starts from nothing each time it runs, since a read that finds the stack short is run again.
    let private assemble
        (budget: IonReadBudget)
        (manifestFile: TreeFile)
        (nodes: NodeFile list)
        : Result<IrDocument, IonIrTreeError> =
        tree {
            let! manifest = readManifest budget manifestFile
            let header = manifest.Header
            let (PackageName(Path ownPackage)) = header.Package

            let! located =
                nodes
                |> traverse (locate ownPackage)

            let groups (wantDependency: bool) =
                located
                |> List.filter (fun file -> file.IsDependency = wantDependency)
                |> List.map (fun file -> file.Node, file.Package, file.Modules)
                |> groupModules

            let context = { Budget = budget; Header = header }
            let dependencyGroups = groups true

            // The manifest names each dependency, and that is the order of their modules.
            let named =
                manifest.Named
                |> List.distinctBy (fun found -> found.Key)

            let unnamed =
                dependencyGroups
                |> List.tryFind (fun group ->
                    not (
                        named
                        |> List.exists (fun found -> found.Key = canonical group.Package)
                    )
                )

            do!
                match unnamed with
                | Some group ->
                    let directory = packageDirectoryOf (List.head group.Files)

                    treeError
                        "invalid_tree_path"
                        (Some directory)
                        $"The directory '{directory}' holds the dependency '{canonical group.Package}', which manifest.ion does not name. Name it there with a package element."
                | None -> Ok()

            let modulesOf (mode: Path -> ModuleMode) (found: ManifestDependency) =
                dependencyGroups
                |> List.filter (fun group -> canonical group.Package = found.Key)
                |> traverse (moduleElements context (mode found.Package))
                |> Result.map List.concat

            let! manifestPackages, fragments =
                match header.Kind with
                | ApplicationKind ->
                    // A linked dependency's modules sit inside its package::def.
                    tree {
                        let! modulesByKey =
                            named
                            |> traverse (fun found ->
                                modulesOf LinkedModule found
                                |> Result.map (fun modules -> found.Key, modules)
                            )
                            |> Result.map Map.ofList

                        let modulesFor key =
                            Map.tryFind key modulesByKey
                            |> Option.defaultValue []

                        let! listedPackages =
                            manifest.Packages
                            |> traverse (fun package ->
                                match
                                    manifest.Named
                                    |> List.tryFind (fun found ->
                                        found.Element.Origin.Index = package.Origin.Index
                                    )
                                with
                                | Some found ->
                                    guard
                                        package.Origin.Location
                                        (fun () ->
                                            appendChildren
                                                "modules"
                                                (modulesFor found.Key)
                                                package
                                        )
                                | None -> Ok package
                            )

                        return listedPackages, []
                    }
                | LibraryKind
                | SpecsKind -> tree {
                    let! fragments =
                        named
                        |> traverse (modulesOf SpecificationFragment)
                        |> Result.map List.concat

                    return manifest.Packages, fragments
                  }

            let! own =
                groups false
                |> traverse (moduleElements context OwnModule)
                |> Result.map List.concat

            let elements =
                manifestPackages
                @ fragments
                @ own

            let footer: IonValue = {
                Annotations = [ IonSymbol.Text "morphir_footer" ]
                Data = IonData.Struct []
            }

            let origins = [|
                manifest.HeaderElement.Origin
                yield!
                    elements
                    |> List.map (fun element -> element.Origin)
                leaf manifestFile.Location 0
            |]

            let datagram =
                [ manifest.HeaderElement.Value ]
                @ (elements
                   |> List.map (fun element -> element.Value))
                @ [ footer ]

            return!
                IonIr.decode datagram
                |> Result.mapError (fun error -> Invalid(place origins error))
        }

    /// Whether a tree has <c>manifest.ion</c> at its root. This is the reader's own test, so a caller that asks
    /// first and the reader agree on which directories are the root.
    let internal hasManifest (source: VFileTree) : bool =
        collect [] source.Content
        |> List.exists (fun file -> file.Segments = [ "manifest.ion" ])

    /// <summary>Reads a tree, keeping the cause of a failure to read a file.</summary>
    /// <remarks>
    /// The tree is checked before any file is opened, except for what needs the manifest's package. Files are then
    /// opened one at a time, the manifest first, and the first problem ends the read. One budget covers every file.
    /// Where the caller's stack is too short for a deep file, the files are read again on a thread with room, with
    /// a new budget, so a source may be opened again.
    /// </remarks>
    let readDetailed
        (limits: IonReadLimits)
        (source: VFileTree)
        : Result<IrDocument, IonIrTreeError> =
        try
            let files = collect [] source.Content

            let atRoot (name: string) =
                files
                |> List.tryFind (fun file -> file.Segments = [ name ])

            match atRoot "manifest.ion" with
            | None -> treeError "missing_manifest" None "The tree has no manifest.ion at its root."
            | Some manifestFile ->
                match
                    rootManifests
                    |> List.filter (fun name -> (atRoot name).IsSome)
                with
                | _ :: _ as others ->
                    let named = String.Join(", ", others)

                    treeError
                        "ambiguous_manifest"
                        None
                        $"The tree's root has manifest.ion and {named}, so it is ambiguous. Keep one manifest."
                | [] ->
                    let classified =
                        files
                        |> List.sortWith (fun a b -> String.CompareOrdinal(a.Location, b.Location))
                        |> List.map (fun file -> file, classify file)

                    let firstProblem =
                        classified
                        |> List.tryPick (fun (file, result) ->
                            match result with
                            | Result.Error message -> Some(file, message)
                            | Ok _ -> None
                        )

                    match firstProblem with
                    | Some(file, message) ->
                        treeError "invalid_tree_path" (Some file.Location) message
                    | None ->
                        let nodes =
                            classified
                            |> List.choose (fun (_, result) ->
                                match result with
                                | Ok(NodeFileEntry node) -> Some node
                                | Ok ManifestFile
                                | Ok IgnoredFile
                                | Result.Error _ -> None
                            )

                        // The budget is made inside the work, since the work may run twice.
                        IonIr.withRoom
                            (fun error -> Result.Error(Invalid error))
                            (fun () -> assemble (IonReadBudget limits) manifestFile nodes)
        with
        | :? OutOfMemoryException -> reraise ()
        | error ->
            Result.Error(
                Invalid
                    {
                        Code = "ion_ir_decode_failed"
                        Message =
                            "The reader failed unexpectedly: "
                            + error.Message
                        Path = None
                        Location = None
                    }
            )


    /// <summary>Reads a tree.</summary>
    /// <remarks>
    /// A file that cannot be opened or read is the error <c>file_read_failed</c> here, with the file's location.
    /// <see cref="readDetailed"/> keeps the exception.
    /// </remarks>
    let read (limits: IonReadLimits) (source: VFileTree) : Result<IrDocument, IonIrError> =
        match readDetailed limits source with
        | Ok document -> Ok document
        | Result.Error(Invalid error) -> Result.Error error
        | Result.Error(SourceFailed(path, cause)) ->
            Result.Error
                {
                    Code = "file_read_failed"
                    Message =
                        "Could not read the file: "
                        + cause.Message
                    Path = Some path
                    Location = None
                }
