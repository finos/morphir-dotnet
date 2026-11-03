namespace Morphir.Configuration

module DiscoveryPolicy =
    let private identity (path: string) =
        let format =
            if path.EndsWith(".toml", System.StringComparison.Ordinal) then DocumentFormat.Toml
            elif path.EndsWith(".yaml", System.StringComparison.Ordinal) || path.EndsWith(".yml", System.StringComparison.Ordinal) then DocumentFormat.Yaml
            else DocumentFormat.Json
        { Path = path; Format = format; Role = SourceRole.RootPrimary; Order = 1; Profile = None }
    let private diagnostic path code severity message =
        { Code = code; Message = message; Severity = severity; Source = identity path; KeyPath = []; Position = None }
    let candidates path =
        [ for stem in [ "morphir"; ".morphir/morphir"; ".config/morphir/config" ] do
            for ext in [ "json"; "json5"; "yaml"; "yml"; "toml" ] do
                yield ProjectPath.join path (stem + "." + ext)
          yield ProjectPath.join path "morphir-workspace.json" ]
    let private location path (snapshot: ProjectSnapshot) =
        let sources =
            candidates path |> List.choose (fun candidate ->
                let source = { identity candidate with Order = (if candidate.EndsWith("/morphir-workspace.json") then 0 else 1) }
                match Map.tryFind candidate snapshot.Entries with
                | Some(ProjectEntry.File content) ->
                    let content = match content with SourceContent.Available d -> SourceContent.Available { d with Source = source } | _ -> content
                    Some { Source = source; Content = content; Required = true }
                | Some(ProjectEntry.Directory _) -> Some { Source = source; Content = SourceContent.Invalid [diagnostic candidate "discovery.invalid_candidate" DiagnosticSeverity.Error "Primary path is a directory"]; Required = true }
                | Some(ProjectEntry.Failure message) -> Some { Source = source; Content = SourceContent.Invalid [diagnostic candidate "discovery.read_error" DiagnosticSeverity.Error message]; Required = true }
                | _ -> None)
            |> List.sortBy (fun source -> int source.Source.Format, source.Source.Order, source.Source.Path)
        let ambiguity =
            sources
            |> List.filter (fun source -> not (source.Source.Path.EndsWith("/morphir-workspace.json")))
            |> List.groupBy (fun source -> source.Source.Format)
            |> List.choose (fun (_, sources) ->
                if sources.Length < 2 then None else
                Some(diagnostic path "discovery.ambiguous" DiagnosticSeverity.Error ("Ambiguous primary candidates: " + (sources |> List.map (fun s -> s.Source.Path) |> String.concat ", "))))
        let validation = Resolver.resolve { DocumentDefaults.resolutionInput with Sources = sources }
        let diagnostics = ambiguity @ validation.Diagnostics
        let values = sources |> List.fold (fun value source -> match source.Content with SourceContent.Available d -> ValueMerge.merge value d.Values | _ -> value) DocumentDefaults.emptyValue
        { Path = path; Values = ConfigTree.clean values; Sources = sources; Diagnostics = diagnostics }
    let private reports path (snapshot: ProjectSnapshot) =
        candidates path |> List.choose (fun candidate ->
            Map.tryFind candidate snapshot.Entries |> Option.map (fun entry ->
                let status, detail =
                    match entry with
                    | ProjectEntry.Missing -> SourceStatus.Missing, "Optional primary absent"
                    | ProjectEntry.Link -> SourceStatus.Skipped, "Discovered link is not followed"
                    | ProjectEntry.File(SourceContent.Available _) -> SourceStatus.Loaded, "Primary inspected"
                    | ProjectEntry.File SourceContent.Missing -> SourceStatus.Missing, "Primary disappeared"
                    | ProjectEntry.File(SourceContent.Invalid _) | ProjectEntry.Failure _ -> SourceStatus.Failed, "Primary inspection failed"
                    | ProjectEntry.Directory _ -> SourceStatus.Failed, "Primary path is a directory"
                { Source = identity candidate; Status = status; Contributed = false; Detail = detail }))
    let private has key (location: ProjectLocation) = ConfigTree.tryFind [key] location.Values |> Option.isSome
    let private errors diagnostics = diagnostics |> List.exists (fun d -> d.Severity = DiagnosticSeverity.Error)
    let private info (location: ProjectLocation) =
        { Path = location.Path
          Name = (match ConfigTree.tryFind ["project"; "name"] location.Values with Some(ConfigValue.String name) -> Some name | _ -> None)
          Location = location
          IsValid = has "project" location && not (errors location.Diagnostics) }
    let private strings path value =
        match ConfigTree.tryFind path value with
        | Some(ConfigValue.Array items) -> items |> List.choose (function ConfigValue.String s -> Some s | _ -> None)
        | _ -> []
    let private text path fallback value =
        match ConfigTree.tryFind path value with Some(ConfigValue.String s) -> s | _ -> fallback
    let private emptyLocation path = { Path = path; Values = DocumentDefaults.emptyValue; Sources = []; Diagnostics = [] }
    let private result start (root: ProjectLocation) workspace projects reports diagnostics =
        { StartDirectory = start; Location = root.Path; Workspace = workspace; Root = root; Projects = projects; Sources = reports; Diagnostics = diagnostics }

    // Segment glob matching is case-sensitive and independent of the native platform.
    let private segmentMatch (pattern: string) (value: string) =
        let rec loop p v =
            if p = pattern.Length then v = value.Length
            elif pattern.[p] = '*' then loop (p + 1) v || (v < value.Length && loop p (v + 1))
            elif v < value.Length && (pattern.[p] = '?' || pattern.[p] = value.[v]) then loop (p + 1) (v + 1)
            else false
        loop 0 0
    let private globMatch (pattern: string) (path: string) =
        let split (s: string) = s.Replace('\\', '/').Split('/', System.StringSplitOptions.RemoveEmptyEntries) |> Array.toList |> List.filter ((<>) ".")
        let rec loop patterns parts =
            match patterns, parts with
            | [], [] -> true
            | "**" :: tail, _ -> loop tail parts || (match parts with _ :: rest -> loop patterns rest | [] -> false)
            | pattern :: tail, part :: rest when segmentMatch pattern part -> loop tail rest
            | _ -> false
        loop (split pattern) (if path = "." then [] else split path)
    let private isGlob (pattern: string) = pattern.Contains('*') || pattern.Contains('?')
    let private reserved (path: string) =
        let name = path.Substring(path.LastIndexOf('/') + 1)
        List.contains name [ ".git"; ".hg"; ".svn"; ".morphir"; ".config"; "node_modules"; "bin"; "obj"; "dist"; "build"; "target" ]

    let evaluate (request: DiscoveryRequest) (snapshot: ProjectSnapshot) =
        let missing paths = paths |> List.distinct |> List.filter (fun path -> not (Map.containsKey path snapshot.Entries))
        let complete start root workspace projects evidence diagnostics =
            let observed = evidence |> List.map (fun report -> report.Source.Path) |> Set.ofList
            let extra = snapshot.Entries |> Map.toList |> List.choose (fun (path, entry) ->
                if Set.contains path observed then None else
                let status = match entry with ProjectEntry.Failure _ -> Some SourceStatus.Failed | ProjectEntry.Link -> Some SourceStatus.Skipped | _ -> None
                status |> Option.map (fun status -> { Source = {identity path with Format=DocumentFormat.Memory}; Status=status; Contributed=false; Detail=(match entry with ProjectEntry.Failure message -> message | _ -> "Discovered link is not followed") }))
            DiscoveryStep.Complete(result start root workspace projects (evidence @ extra) diagnostics)
        let rec inventory root pending found diagnostics =
            match pending with
            | [] -> Ok(List.rev found, diagnostics)
            | path :: tail ->
                match Map.tryFind path snapshot.Entries with
                | None -> Error [path]
                | Some(ProjectEntry.Directory children) ->
                    let safe = children |> List.filter (fun p -> ProjectPath.contains request.Boundary p && ProjectPath.parent p = path && not (reserved p)) |> List.sort
                    inventory root (safe @ tail) (path :: found) diagnostics
                | Some(ProjectEntry.Failure message) -> inventory root tail found (diagnostics @ [diagnostic path "discovery.read_error" DiagnosticSeverity.Error message])
                | _ -> inventory root tail found diagnostics
        let members start (root: ProjectLocation) evidence diagnostics =
            let basePath = ProjectPath.join root.Path (text ["workspace";"root"] "." root.Values)
            let output = ProjectPath.join basePath (text ["workspace";"out_dir"] ".morphir/out" root.Values)
            let workspace = Some { Root = basePath; OutputDirectory = Some output }
            let patterns = strings ["workspace";"members"] root.Values
            let excludes = strings ["workspace";"exclude"] root.Values
            let literals = patterns |> List.filter (not << isGlob) |> List.map (ProjectPath.join basePath)
            let outside = basePath :: literals |> List.filter (not << ProjectPath.contains request.Boundary)
            if not outside.IsEmpty then
                complete start root workspace [] evidence (diagnostics @ [diagnostic root.Path "discovery.boundary" DiagnosticSeverity.Error ("Workspace paths escape host boundary: " + String.concat ", " outside)])
            else
                let directories =
                    if patterns |> List.exists isGlob then inventory basePath [basePath] [] []
                    else Ok([], [])
                match directories with
                | Error requests -> DiscoveryStep.Requests requests
                | Ok (directories, listingDiagnostics) ->
                    let paths =
                        directories |> List.filter (fun path -> patterns |> List.exists (fun pattern -> globMatch pattern (ProjectPath.relative basePath path)))
                        |> List.append literals
                        |> List.filter (fun path -> not (excludes |> List.exists (fun pattern -> globMatch pattern (ProjectPath.relative basePath path))))
                        |> List.filter (fun path -> path <> root.Path)
                        |> List.distinct |> List.sort
                    let directoryRequests = missing paths
                    let requests =
                        if not directoryRequests.IsEmpty then directoryRequests
                        else paths |> List.collect (fun path ->
                            match Map.find path snapshot.Entries with ProjectEntry.Directory _ -> candidates path | _ -> []) |> missing
                    if not requests.IsEmpty then DiscoveryStep.Requests requests
                    else
                        let projects = paths |> List.choose (fun path ->
                            let memberLocation =
                                match Map.find path snapshot.Entries with ProjectEntry.Directory _ -> location path snapshot | _ -> emptyLocation path
                            let literal = List.contains path literals
                            let extra =
                                match Map.find path snapshot.Entries with
                                | ProjectEntry.Missing when literal -> [diagnostic path "discovery.missing_member" DiagnosticSeverity.Warning ("Declared member is absent: " + path)]
                                | ProjectEntry.Directory _ when literal && not (has "project" memberLocation) -> [diagnostic path "discovery.member_configuration" DiagnosticSeverity.Error ("Declared member has no project configuration: " + path)]
                                | ProjectEntry.Failure message -> [diagnostic path "discovery.read_error" DiagnosticSeverity.Error message]
                                | ProjectEntry.Link when literal -> [diagnostic path "discovery.link" DiagnosticSeverity.Warning "Declared member link is not followed"]
                                | ProjectEntry.File _ when literal -> [diagnostic path "discovery.member_configuration" DiagnosticSeverity.Error "Member path must be a directory"]
                                | _ -> []
                            if literal || not memberLocation.Sources.IsEmpty then
                                Some(info { memberLocation with Diagnostics = memberLocation.Diagnostics @ extra })
                            else None)
                        let projects = (if has "project" root then info root :: projects else projects) |> List.sortBy (fun p -> p.Path)
                        let duplicates =
                            projects |> List.choose (fun p -> p.Name |> Option.map (fun name -> name, p.Path))
                            |> List.groupBy fst |> List.choose (fun (name, paths) ->
                                if paths.Length < 2 then None else Some(diagnostic root.Path "discovery.duplicate_name" DiagnosticSeverity.Warning ("Duplicate project name '" + name + "': " + (paths |> List.map snd |> String.concat ", "))))
                        let memberEvidence = paths |> List.collect (fun path -> reports path snapshot)
                        complete start root workspace projects (evidence @ memberEvidence) (diagnostics @ listingDiagnostics @ (projects |> List.collect (fun p -> p.Location.Diagnostics)) @ duplicates |> List.distinct)
        let rec walk start path (nearest: ProjectLocation option) evidence diagnostics =
            let requests = missing (candidates path)
            if not requests.IsEmpty then DiscoveryStep.Requests requests
            else
                let current = location path snapshot
                let evidence = evidence @ reports path snapshot
                let diagnostics = diagnostics @ current.Diagnostics
                let nearest = if nearest.IsNone && not current.Sources.IsEmpty then Some current else nearest
                if errors current.Diagnostics then complete start current None [] evidence diagnostics
                elif has "workspace" current then members start current evidence diagnostics
                elif path = request.Boundary || ProjectPath.parent path = path then
                    let root = nearest |> Option.defaultValue (emptyLocation start)
                    complete start root None (if has "project" root then [info root] else []) evidence diagnostics
                else walk start (ProjectPath.parent path) nearest evidence diagnostics
        if not (ProjectPath.isAbsolute request.StartPath && ProjectPath.isAbsolute request.Boundary && ProjectPath.contains request.Boundary request.StartPath) then
            complete request.StartPath (emptyLocation request.StartPath) None [] [] [diagnostic request.StartPath "discovery.boundary" DiagnosticSeverity.Error "Start path must be absolute and inside the host boundary"]
        else
            match Map.tryFind request.StartPath snapshot.Entries with
            | None -> DiscoveryStep.Requests [request.StartPath]
            | Some(ProjectEntry.File _) -> let start = ProjectPath.parent request.StartPath in walk start start None [] []
            | Some(ProjectEntry.Directory _) -> walk request.StartPath request.StartPath None [] []
            | Some entry ->
                let code, message = match entry with ProjectEntry.Failure message -> "discovery.read_error", message | ProjectEntry.Link -> "discovery.link", "Invocation link is not followed" | _ -> "discovery.start_missing", "Invocation path is absent"
                complete request.StartPath (emptyLocation request.StartPath) None [] [] [diagnostic request.StartPath code DiagnosticSeverity.Error message]

    /// Selection is independent of inventory. Invalid inventory entries remain visible but cannot be selected.
    let select (discovery: ProjectDiscovery) (selector: string option) =
        let root = discovery.Workspace |> Option.map (fun w -> w.Root) |> Option.defaultValue discovery.Location
        let resolve value =
            let path = ProjectPath.join root value
            let paths = discovery.Projects |> List.filter (fun p -> p.Path = path)
            if not paths.IsEmpty then paths else discovery.Projects |> List.filter (fun p -> p.Name = Some value)
        let defaultValue = ConfigTree.tryFind ["workspace";"default_member"] discovery.Root.Values
        let defaults, diagnostics =
            match defaultValue with
            | None -> [], []
            | Some(ConfigValue.String value) ->
                let projects = resolve value
                if projects.Length = 1 && projects.Head.IsValid then projects, []
                else [], [diagnostic discovery.Location "discovery.invalid_default" DiagnosticSeverity.Error ("Invalid default member: " + value)]
            | Some _ -> [], [diagnostic discovery.Location "discovery.invalid_default" DiagnosticSeverity.Error "default_member must be a string"]
        match selector with
        | Some value ->
            let projects = resolve value
            if projects.Length <> 1 || not projects.Head.IsValid then
                let code = if projects.Length > 1 then "discovery.ambiguous_selector" else "discovery.unknown_selector"
                { Projects = []; Diagnostics = diagnostics @ [diagnostic discovery.Location code DiagnosticSeverity.Error ("Cannot select project '" + value + "': " + (projects |> List.map (fun p -> p.Path) |> String.concat ", "))] }
            else { Projects = projects; Diagnostics = diagnostics }
        | None ->
            let valid = discovery.Projects |> List.filter (fun p -> p.IsValid)
            let containing = valid |> List.filter (fun p -> ProjectPath.contains p.Path discovery.StartDirectory) |> List.sortByDescending (fun p -> p.Path.Length)
            let selected = match containing with first :: _ -> [first] | [] -> if defaults.IsEmpty then valid else defaults
            { Projects = selected; Diagnostics = diagnostics }
