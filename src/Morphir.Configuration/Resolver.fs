namespace Morphir.Configuration

/// Resolves explicitly supplied documents; no filesystem or ambient process state is consulted.
module Resolver =
    let private isBelow prefix path =
        List.length path >= List.length prefix && List.take (List.length prefix) path = prefix

    let private clearOrigins path origins =
        origins |> Map.filter (fun candidate _ -> not (isBelow path candidate))

    let rec private mergeAt path lower higher origins (document: ConfigurationDocument) =
        match lower, higher with
        | _, ConfigValue.Null -> lower, origins
        | ConfigValue.Object left, ConfigValue.Object right ->
            let values, winners =
                right
                |> Map.filter (fun _ value -> value <> ConfigValue.Null)
                |> Map.fold (fun (values, winners) key value ->
                    let previous = Map.tryFind key values |> Option.defaultValue ConfigValue.Null
                    let merged, next = mergeAt (path @ [key]) previous value winners document
                    Map.add key merged values, next) (left, origins)
            ConfigValue.Object values, winners
        | _, ConfigValue.Object fields when not (Map.isEmpty fields) ->
            mergeAt path DocumentDefaults.emptyValue higher (clearOrigins path origins) document
        | _ ->
            let origin =
                { Source = document.Source
                  Position = Map.tryFind path document.Positions }
            higher, Map.add path origin (clearOrigins path origins)

    let private identity path role =
        {Path = path; Format = DocumentFormat.Memory; Role = role; Order = 0; Profile = None}

    let private available source values =
        {Source = source; Required = false
         Content = SourceContent.Available {Source = source; Raw = values; Values = values; Positions = Map.empty; Diagnostics = []; LegacyFields = []}}

    let private standardDefaults =
        [ ["workspace"; "out_dir"], ConfigValue.String ".morphir/out"
          ["project"; "source_directory"], ConfigValue.String "src"
          ["frontend"; "language"], ConfigValue.String "scheme"
          ["ir"; "format_version"], ConfigValue.Integer 4L
          ["ir"; "layout"], ConfigValue.String "single-file"
          ["ir"; "format"], ConfigValue.String "json" ]
        |> List.fold (fun tree (path, value) -> ConfigTree.set path value tree) DocumentDefaults.emptyValue
        |> available (identity "<defaults>" SourceRole.Defaults)

    let private sortSources active (sources: SourceInput list) =
        let rank (source: SourceIdentity) =
            match source.Role with
            | SourceRole.WorkspaceProfile | SourceRole.MemberProfile ->
                let profile = active |> List.tryFindIndex (fun name -> Some name = source.Profile) |> Option.defaultValue System.Int32.MaxValue
                6, profile, (if source.Role = SourceRole.WorkspaceProfile then 0 else 1)
            | SourceRole.WorkspaceUserOverride -> 8, 0, 0
            | role -> int role, 0, 0
        sources |> List.sortBy (fun input -> rank input.Source, int input.Source.Format, input.Source.Order, input.Source.Path)

    let private scopeDocument input rootPaths memberPaths (document: ConfigurationDocument) =
        let source = document.Source
        let inherited =
            input.Scope = ProjectScope.Member &&
            (source.Role = SourceRole.RootPrimary || source.Role = SourceRole.WorkspaceProfile ||
             source.Role = SourceRole.WorkspaceUserOverride ||
             (source.Role = SourceRole.Selected && Set.contains source.Path rootPaths))
        let machine = source.Role = SourceRole.System || source.Role = SourceRole.Global
        let memberOwned =
            source.Role = SourceRole.MemberPrimary || source.Role = SourceRole.MemberProfile ||
            (source.Role = SourceRole.Selected && Set.contains source.Path memberPaths)
        let mutable values = ConfigTree.remove "profiles" document.Values
        let mutable diagnostics = []
        if inherited || machine then
            values <- values |> ConfigTree.remove "project" |> ConfigTree.remove "workspace"
        if memberOwned then
            for key in ["out_dir"; "output_dir"] do
                let path = ["workspace"; key]
                if ConfigTree.tryFind path values |> Option.isSome then
                    diagnostics <- diagnostics @
                        [{Code = "config.member_output"; Message = "Member settings cannot relocate owning workspace output."
                          Severity = DiagnosticSeverity.Warning; Source = source; KeyPath = path; Position = Map.tryFind path document.Positions}]
                match values with
                | ConfigValue.Object fields ->
                    match Map.tryFind "workspace" fields with
                    | Some workspace -> values <- ConfigValue.Object (Map.add "workspace" (ConfigTree.remove key workspace) fields)
                    | None -> ()
                | _ -> ()
        if source.Role = SourceRole.WorkspaceProfile || source.Role = SourceRole.MemberProfile then
            match ConfigTree.tryFind ["workspace"] values with
            | Some workspace ->
                let filtered = ["root"; "members"; "exclude"; "default_member"] |> List.fold (fun tree key -> ConfigTree.remove key tree) workspace
                values <- ConfigTree.set ["workspace"] filtered values
            | None -> ()
        {document with Values = values}, diagnostics

    let resolve (input: ResolutionInput) : DocumentResolution =
        let explicitSource =
            available {identity "<explicit>" SourceRole.Explicit with Order = System.Int32.MaxValue} input.Explicit
        let supplied = input.Sources @ EnvironmentMapping.sources input.Environment @ [explicitSource]
        let plan = Profiles.plan supplied input.Environment input.Profiles
        let rootPaths = input.Sources |> List.filter (fun source -> source.Source.Role = SourceRole.RootPrimary) |> List.map (fun source -> source.Source.Path) |> Set.ofList
        let memberPaths = input.Sources |> List.filter (fun source -> source.Source.Role = SourceRole.MemberPrimary) |> List.map (fun source -> source.Source.Path) |> Set.ofList
        let selectedPaths = input.Sources |> List.filter (fun source -> source.Source.Role = SourceRole.Selected) |> List.map (fun source -> source.Source.Path) |> Set.ofList
        let legacyDefaults =
            input.Sources
            |> List.choose (fun source ->
                match source.Content with
                | SourceContent.Available document when
                    (input.Scope = ProjectScope.Root && source.Source.Role = SourceRole.RootPrimary) ||
                    (input.Scope = ProjectScope.Member && source.Source.Role = SourceRole.MemberPrimary) ->
                    let values =
                        document.LegacyFields
                        |> List.filter (fun field -> field.Disposition = "default")
                        |> List.fold (fun tree field ->
                            match field.CanonicalPath with
                            | Some path -> ConfigTree.set path field.Value tree
                            | None -> tree) DocumentDefaults.emptyValue
                    Some (available {source.Source with Role = SourceRole.Defaults; Order = 1} values)
                | _ -> None)
        let initial = DocumentDefaults.emptyValue, Map.empty, [], []
        let effective, origins, reports, diagnostics =
            sortSources plan.Active (standardDefaults :: legacyDefaults @ supplied)
            |> List.fold (fun (effective, origins, reports, diagnostics) source ->
                let profile = source.Source.Role = SourceRole.WorkspaceProfile || source.Source.Role = SourceRole.MemberProfile
                let duplicateSelected = source.Source.Role <> SourceRole.Selected && Set.contains source.Source.Path selectedPaths
                let inactive = profile && (not plan.Enabled || not (source.Source.Profile |> Option.exists (fun name -> List.contains name plan.Active)))
                if duplicateSelected || inactive then
                    let report = {Source = source.Source; Status = SourceStatus.Skipped; Contributed = false; Detail = if inactive then "Inactive profile" else "Contributes once at selected layer"}
                    effective, origins, reports @ [report], diagnostics
                else
                    match source.Content with
                    | SourceContent.Available document ->
                        let document, scopedDiagnostics = scopeDocument input rootPaths memberPaths {document with Source = source.Source}
                        let value, winners = mergeAt [] effective document.Values origins document
                        let contributed = value <> effective || winners <> origins
                        let report = { Source = source.Source; Status = SourceStatus.Loaded; Contributed = contributed; Detail = "Loaded" }
                        value, winners, reports @ [report], diagnostics @ document.Diagnostics @ scopedDiagnostics
                    | SourceContent.Missing ->
                        let report = { Source = source.Source; Status = SourceStatus.Missing; Contributed = false; Detail = "Missing" }
                        let errors =
                            if source.Required || source.Source.Role = SourceRole.Selected then
                                [{ Code = "config.missing"; Message = "Required configuration is missing: " + source.Source.Path
                                   Severity = DiagnosticSeverity.Error; Source = source.Source; KeyPath = []; Position = None }]
                            else []
                        effective, origins, reports @ [report], diagnostics @ errors
                    | SourceContent.Invalid errors ->
                        let report = { Source = source.Source; Status = SourceStatus.Failed; Contributed = false; Detail = "Invalid" }
                        effective, origins, reports @ [report], diagnostics @ errors) initial
        { Effective = ConfigTree.clean effective
          Origins = origins
          Sources = reports
          ActiveProfiles = plan.Active
          OwningWorkspace = input.OwningWorkspace
          Diagnostics = (plan.Diagnostics @ diagnostics) |> List.distinct }
