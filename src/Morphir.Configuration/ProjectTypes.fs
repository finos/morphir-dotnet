namespace Morphir.Configuration

/// Canonical absolute host paths use '/' on every platform. No ambient state is read.
module ProjectPath =
    let normalize (path: string) =
        let path = path.Replace('\\', '/')

        let prefix, body =
            if path.StartsWith("//") then
                "//", path.TrimStart('/')
            elif path.StartsWith("/") then
                "/", path.TrimStart('/')
            elif
                path.Length
                >= 3
                && path.[1] = ':'
                && path.[2] = '/'
            then
                path.Substring(0, 3), path.Substring(3)
            else
                "", path

        let parts =
            body.Split('/', System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.fold
                (fun acc part ->
                    match part, acc with
                    | ".", _ -> acc
                    | "..", _ :: tail -> tail
                    | "..", [] -> []
                    | _, _ ->
                        part
                        :: acc
                )
                []
            |> List.rev

        prefix
        + String.concat "/" parts

    let isAbsolute (path: string) =
        path.StartsWith("/")
        || (path.Length
            >= 3
            && path.[1] = ':'
            && path.[2] = '/')

    let join (root: string) path =
        normalize (
            if isAbsolute path then
                path
            else
                root.TrimEnd('/')
                + "/"
                + path
        )

    let parent path =
        let path = normalize path
        let index = path.LastIndexOf('/')

        if index = 0 then
            "/"
        elif
            index = 2
            && path.[1] = ':'
        then
            path.Substring(0, 3)
        elif index > 0 then
            path.Substring(0, index)
        else
            path

    let contains root path =
        let root = normalize root
        let path = normalize path

        path = root
        || path.StartsWith(
            root.TrimEnd('/')
            + "/",
            System.StringComparison.Ordinal
        )

    let relative (root: string) (path: string) =
        let parts value =
            (normalize value).Split('/', System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList

        let rec removeCommon left right =
            match left, right with
            | l :: ls, r :: rs when l = r -> removeCommon ls rs
            | _ -> left, right

        let left, right = removeCommon (parts root) (parts path)

        let result =
            (List.replicate left.Length "..")
            @ right

        if result.IsEmpty then "." else String.concat "/" result

/// A host response. Links are evidence, never an instruction to follow them.
[<RequireQualifiedAccess>]
type ProjectEntry =
    | Missing
    | Link
    | Failure of string
    | Directory of children: string list
    | File of SourceContent

type ProjectSnapshot = { Entries: Map<string, ProjectEntry> }

/// MorphirHome is the global Morphir home (MORPHIR_HOME, else the home directory's .morphir). When supplied,
/// primary-named files directly in it never declare a project or workspace.
type DiscoveryRequest = {
    StartPath: string
    Boundary: string
    MorphirHome: string option
}

type ProjectLocation = {
    Path: string
    Values: ConfigValue
    Sources: SourceInput list
    Diagnostics: ConfigDiagnostic list
}

type ProjectInfo = {
    Path: string
    Name: string option
    Location: ProjectLocation
    IsValid: bool
}

type ProjectDiscovery = {
    StartDirectory: string
    Location: string
    Workspace: OwningWorkspace option
    Root: ProjectLocation
    Projects: ProjectInfo list
    Sources: SourceReport list
    Diagnostics: ConfigDiagnostic list
}

/// Re-evaluate after fulfilling the requested paths. Policies do not perform I/O.
[<RequireQualifiedAccess>]
type DiscoveryStep =
    | Requests of string list
    | Complete of ProjectDiscovery

type ProjectSelection = {
    Projects: ProjectInfo list
    Diagnostics: ConfigDiagnostic list
}
