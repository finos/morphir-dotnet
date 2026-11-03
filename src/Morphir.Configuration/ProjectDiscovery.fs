namespace Morphir.Configuration

module DiscoveryPolicy =
    let private identity path = { Path = path; Format = DocumentFormat.Memory; Role = SourceRole.RootPrimary; Order = 0; Profile = None }
    let private diagnostic path code severity message =
        { Code = code; Message = message; Severity = severity; Source = identity path; KeyPath = []; Position = None }
    let private candidates path =
        [ for stem in [ "morphir"; ".morphir/morphir"; ".config/morphir/config" ] do
            for ext in [ "json"; "json5"; "yaml"; "yml"; "toml" ] do
                yield ProjectPath.join path (stem + "." + ext)
          yield ProjectPath.join path "morphir-workspace.json" ]
    let private location path (snapshot: ProjectSnapshot) =
        let sources =
            candidates path |> List.choose (fun candidate ->
                match Map.tryFind candidate snapshot.Entries with
                | Some(ProjectEntry.File content) ->
                    let source = match content with SourceContent.Available d -> d.Source | _ -> identity candidate
                    Some { Source = source; Content = content; Required = true }
                | _ -> None)
            |> List.sortBy (fun source -> int source.Source.Format, if source.Source.Path.EndsWith("morphir-workspace.json") then 0 else 1)
        let values = sources |> List.fold (fun value source -> match source.Content with SourceContent.Available d -> ValueMerge.merge value d.Values | _ -> value) DocumentDefaults.emptyValue
        { Path = path; Values = values; Sources = sources; Diagnostics = [] }
    let private has key location = ConfigTree.tryFind [key] location.Values |> Option.isSome
    let private info (location: ProjectLocation) =
        { Path = location.Path; Name = (match ConfigTree.tryFind ["project"; "name"] location.Values with Some(ConfigValue.String name) -> Some name | _ -> None); Location = location; IsValid = true }
    let evaluate (request: DiscoveryRequest) (snapshot: ProjectSnapshot) =
        let missing paths = paths |> List.filter (fun path -> not (Map.containsKey path snapshot.Entries))
        let rec walk start path (nearest: ProjectLocation option) =
            let requests = missing (candidates path)
            if not requests.IsEmpty then DiscoveryStep.Requests requests
            else
                let current = location path snapshot
                let nearest = if nearest.IsNone && not current.Sources.IsEmpty then Some current else nearest
                if has "workspace" current then
                    let members = match ConfigTree.tryFind ["workspace";"members"] current.Values with Some(ConfigValue.Array items) -> items |> List.choose (function ConfigValue.String s -> Some(ProjectPath.join path s) | _ -> None) | _ -> []
                    let requests = missing (members |> List.collect candidates)
                    if not requests.IsEmpty then DiscoveryStep.Requests requests
                    else
                        DiscoveryStep.Complete { StartDirectory = start; Location = path; Workspace = Some { Root = path; OutputDirectory = Some ".morphir/out" }; Root = current; Projects = members |> List.map (fun p -> info (location p snapshot)); Sources = []; Diagnostics = [] }
                elif path = request.Boundary || ProjectPath.parent path = path then
                    let root = nearest |> Option.defaultValue { Path = start; Values = DocumentDefaults.emptyValue; Sources = []; Diagnostics = [] }
                    DiscoveryStep.Complete { StartDirectory = start; Location = root.Path; Workspace = None; Root = root; Projects = (if root.Sources.IsEmpty then [] else [info root]); Sources = []; Diagnostics = [] }
                else walk start (ProjectPath.parent path) nearest
        match Map.tryFind request.StartPath snapshot.Entries with
        | None -> DiscoveryStep.Requests [request.StartPath]
        | Some(ProjectEntry.File _) -> let start = ProjectPath.parent request.StartPath in walk start start None
        | _ -> walk request.StartPath request.StartPath None

    let select (discovery: ProjectDiscovery) (selector: string option) =
        let projects =
            match selector with
            | Some value -> discovery.Projects |> List.filter (fun project -> project.Path = ProjectPath.join discovery.Location value || project.Name = Some value)
            | None ->
                match discovery.Projects |> List.filter (fun project -> ProjectPath.contains project.Path discovery.StartDirectory) |> List.sortByDescending (fun p -> p.Path.Length) with
                | first :: _ -> [first]
                | [] -> discovery.Projects
        { Projects = projects; Diagnostics = [] }
