namespace Morphir.Configuration

/// Resolves explicitly supplied documents; no filesystem or ambient process state is consulted.
module Resolver =
    let private isBelow prefix path =
        List.length path
        >= List.length prefix
        && List.take (List.length prefix) path = prefix

    let private clearOrigins path origins =
        origins
        |> Map.filter (fun candidate _ -> not (isBelow path candidate))

    let rec private mergeAt path lower higher origins (document: ConfigurationDocument) =
        match lower, higher with
        | _, ConfigValue.Null -> lower, origins
        | ConfigValue.Object left, ConfigValue.Object right ->
            let values, winners =
                right
                |> Map.filter (fun _ value ->
                    value
                    <> ConfigValue.Null
                )
                |> Map.fold
                    (fun (values, winners) key value ->
                        let previous =
                            Map.tryFind key values
                            |> Option.defaultValue ConfigValue.Null

                        let merged, next =
                            mergeAt
                                (path
                                 @ [ key ])
                                previous
                                value
                                winners
                                document

                        Map.add key merged values, next
                    )
                    (left, origins)

            let winners =
                if Map.isEmpty values then
                    winners
                else
                    Map.remove path winners

            ConfigValue.Object values, winners
        | _, ConfigValue.Object fields when not (Map.isEmpty fields) ->
            let merged, winners =
                mergeAt path DocumentDefaults.emptyValue higher (clearOrigins path origins) document

            match merged with
            | ConfigValue.Object values when Map.isEmpty values ->
                merged,
                Map.add
                    path
                    {
                        Source = document.Source
                        Position = Map.tryFind path document.Positions
                    }
                    winners
            | _ -> merged, winners
        | _ ->
            let origin = {
                Source = document.Source
                Position = Map.tryFind path document.Positions
            }

            higher, Map.add path origin (clearOrigins path origins)

    let private identity path role = {
        Path = path
        Format = DocumentFormat.Memory
        Role = role
        Order = 0
        Profile = None
    }

    let private available source values = {
        Source = source
        Required = false
        Content =
            SourceContent.Available
                {
                    Source = source
                    Raw = values
                    Values = values
                    Positions = Map.empty
                    Diagnostics = []
                    LegacyFields = []
                }
    }

    let private standardDefaults =
        [
            [ "workspace"; "out_dir" ], ConfigValue.String ".morphir/out"
            [ "project"; "source_directory" ], ConfigValue.String "src"
            [ "frontend"; "language" ], ConfigValue.String "scheme"
            [ "ir"; "format_version" ], ConfigValue.Integer 4L
            [ "ir"; "layout" ], ConfigValue.String "single-file"
            [ "ir"; "format" ], ConfigValue.String "json"
        ]
        |> List.fold
            (fun tree (path, value) -> ConfigTree.set path value tree)
            DocumentDefaults.emptyValue
        |> available
            { identity "<defaults>" SourceRole.Defaults with
                Order = System.Int32.MinValue
            }

    let private sortSources active (sources: SourceInput list) =
        let rank (source: SourceIdentity) =
            match source.Role with
            | SourceRole.WorkspaceProfile
            | SourceRole.MemberProfile ->
                let profile =
                    active
                    |> List.tryFindIndex (fun name -> Some name = source.Profile)
                    |> Option.defaultValue System.Int32.MaxValue

                6, profile, (if source.Role = SourceRole.WorkspaceProfile then 0 else 1)
            | SourceRole.WorkspaceUserOverride -> 8, 0, 0
            | role -> int role, 0, 0

        sources
        |> List.sortBy (fun input ->
            let first, second =
                if input.Source.Role = SourceRole.Defaults then
                    input.Source.Order, int input.Source.Format
                else
                    int input.Source.Format, input.Source.Order

            rank input.Source, first, second, input.Source.Path
        )

    let private scopeDocument input rootPaths memberPaths (document: ConfigurationDocument) =
        let source = document.Source

        let inherited =
            input.Scope = ProjectScope.Member
            && (source.Role = SourceRole.RootPrimary
                || source.Role = SourceRole.WorkspaceProfile
                || source.Role = SourceRole.WorkspaceUserOverride
                || (source.Role = SourceRole.Selected
                    && Set.contains source.Path rootPaths))

        let machine =
            source.Role = SourceRole.System
            || source.Role = SourceRole.Global

        let memberOwned =
            source.Role = SourceRole.MemberPrimary
            || source.Role = SourceRole.MemberProfile
            || (source.Role = SourceRole.Selected
                && Set.contains source.Path memberPaths)

        let mutable values = ConfigTree.remove "profiles" document.Values
        let mutable diagnostics = []

        if
            inherited
            || machine
        then
            values <-
                values
                |> ConfigTree.remove "project"
                |> ConfigTree.remove "workspace"

        if memberOwned then
            for key in [ "out_dir"; "output_dir" ] do
                let path = [ "workspace"; key ]

                if
                    ConfigTree.tryFind path values
                    |> Option.isSome
                then
                    diagnostics <-
                        diagnostics
                        @ [
                            {
                                Code = "config.member_output"
                                Message = "Member settings cannot relocate owning workspace output."
                                Severity = DiagnosticSeverity.Warning
                                Source = source
                                KeyPath = path
                                Position = Map.tryFind path document.Positions
                            }
                        ]

                match values with
                | ConfigValue.Object fields ->
                    match Map.tryFind "workspace" fields with
                    | Some workspace ->
                        values <-
                            ConfigValue.Object(
                                Map.add "workspace" (ConfigTree.remove key workspace) fields
                            )
                    | None -> ()
                | _ -> ()

        if
            source.Role = SourceRole.WorkspaceProfile
            || source.Role = SourceRole.MemberProfile
        then
            match ConfigTree.tryFind [ "workspace" ] values with
            | Some workspace ->
                let filtered =
                    [ "root"; "members"; "exclude"; "default_member" ]
                    |> List.fold (fun tree key -> ConfigTree.remove key tree) workspace

                values <- ConfigTree.set [ "workspace" ] filtered values
            | None -> ()

        { document with Values = values }, diagnostics

    let private mixedFormatDiagnostics (input: ResolutionInput) =
        let codes = Set.ofList [ "config.yaml_beside_toml"; "config.json_keys_below_toml" ]
        let host = identity "<host-options>" SourceRole.Explicit

        let mutable suppressed =
            input.SuppressWarnings
            |> List.map (fun code -> code, host)

        let mutable errors = []

        match Map.tryFind "MORPHIR_DIAGNOSTICS__SUPPRESS" input.Environment with
        | Some value ->
            let source = identity "MORPHIR_DIAGNOSTICS__SUPPRESS" SourceRole.Environment

            suppressed <-
                suppressed
                @ (value.Split(',')
                   |> Array.map (fun code -> code.Trim(), source)
                   |> List.ofArray)
        | None -> ()

        for source in input.Sources do
            if
                source.Source.Role = SourceRole.System
                || source.Source.Role = SourceRole.Global
            then
                match source.Content with
                | SourceContent.Available document ->
                    let path = [ "diagnostics"; "suppress" ]

                    match ConfigTree.tryFind path document.Values with
                    | Some(ConfigValue.Array items) when
                        items
                        |> List.forall (
                            function
                            | ConfigValue.String _ -> true
                            | _ -> false
                        )
                        ->
                        suppressed <-
                            suppressed
                            @ (items
                               |> List.choose (
                                   function
                                   | ConfigValue.String code -> Some(code, source.Source)
                                   | _ -> None
                               ))
                    | Some _ ->
                        errors <-
                            errors
                            @ [
                                {
                                    Code = "config.suppression_shape"
                                    Message =
                                        "diagnostics.suppress must be an array of warning codes."
                                    Severity = DiagnosticSeverity.Error
                                    Source = source.Source
                                    KeyPath = path
                                    Position = Map.tryFind path document.Positions
                                }
                            ]
                    | None -> ()
                | _ -> ()

        let suppressed =
            suppressed
            |> List.filter (fun (code, _) -> code <> "")
            |> List.distinct

        let unknown =
            suppressed
            |> List.choose (fun (code, source) ->
                if Set.contains code codes then
                    None
                else
                    Some
                        {
                            Code = "config.unknown_suppression"
                            Message =
                                "Unknown configuration warning code: "
                                + code
                            Severity = DiagnosticSeverity.Warning
                            Source = source
                            KeyPath = [ "diagnostics"; "suppress" ]
                            Position = None
                        }
            )

        let enabled code =
            not (
                suppressed
                |> List.exists (fun (candidate, _) -> code = candidate)
            )

        let groups =
            input.Sources
            |> List.filter (fun source ->
                source.Source.Role = SourceRole.RootPrimary
                || source.Source.Role = SourceRole.MemberPrimary
            )
            |> List.groupBy (fun source -> source.Source.Role)

        let warnings =
            groups
            |> List.collect (fun (_, sources) ->
                let documents =
                    sortSources [] sources
                    |> List.choose (fun source ->
                        match source.Content with
                        | SourceContent.Available document -> Some document
                        | _ -> None
                    )

                match
                    documents
                    |> List.tryFind (fun document -> document.Source.Format = DocumentFormat.Toml)
                with
                | None -> []
                | Some toml ->
                    let yaml =
                        if enabled "config.yaml_beside_toml" then
                            documents
                            |> List.choose (fun document ->
                                if
                                    document.Source.Format
                                    <> DocumentFormat.Yaml
                                then
                                    None
                                else
                                    Some
                                        {
                                            Code = "config.yaml_beside_toml"
                                            Message =
                                                document.Source.Path
                                                + " beside "
                                                + toml.Source.Path
                                            Severity = DiagnosticSeverity.Warning
                                            Source = document.Source
                                            KeyPath = []
                                            Position = None
                                        }
                            )
                        else
                            []

                    let json =
                        if enabled "config.json_keys_below_toml" then
                            let _, winners =
                                documents
                                |> List.fold
                                    (fun (value, winners) document ->
                                        mergeAt
                                            []
                                            value
                                            (ConfigTree.remove "profiles" document.Values)
                                            winners
                                            document
                                    )
                                    (DocumentDefaults.emptyValue, Map.empty)

                            winners
                            |> Map.toList
                            |> List.choose (fun (path, origin) ->
                                if
                                    origin.Source.Format
                                    <> DocumentFormat.Json
                                then
                                    None
                                else
                                    Some
                                        {
                                            Code = "config.json_keys_below_toml"
                                            Message =
                                                origin.Source.Path
                                                + " supplies "
                                                + String.concat "." path
                                                + " below "
                                                + toml.Source.Path
                                            Severity = DiagnosticSeverity.Warning
                                            Source = origin.Source
                                            KeyPath = path
                                            Position = origin.Position
                                        }
                            )
                        else
                            []

                    yaml
                    @ json
            )

        errors
        @ unknown
        @ warnings

    let resolve (input: ResolutionInput) : DocumentResolution =
        let explicitSource =
            available
                { identity "<explicit>" SourceRole.Explicit with
                    Order = System.Int32.MaxValue
                }
                input.Explicit

        let supplied =
            input.Sources
            @ EnvironmentMapping.sources input.Environment
            @ [ explicitSource ]

        let plan = Profiles.plan supplied input.Environment input.Profiles

        let rootPaths =
            input.Sources
            |> List.filter (fun source -> source.Source.Role = SourceRole.RootPrimary)
            |> List.map (fun source -> source.Source.Path)
            |> Set.ofList

        let memberPaths =
            input.Sources
            |> List.filter (fun source -> source.Source.Role = SourceRole.MemberPrimary)
            |> List.map (fun source -> source.Source.Path)
            |> Set.ofList

        let selectedPaths =
            input.Sources
            |> List.filter (fun source -> source.Source.Role = SourceRole.Selected)
            |> List.map (fun source -> source.Source.Path)
            |> Set.ofList

        let legacyDefaults =
            input.Sources
            |> List.choose (fun source ->
                match source.Content with
                | SourceContent.Available document when
                    (input.Scope = ProjectScope.Root
                     && source.Source.Role = SourceRole.RootPrimary)
                    || (input.Scope = ProjectScope.Member
                        && source.Source.Role = SourceRole.MemberPrimary)
                    || (source.Source.Role = SourceRole.Selected
                        && (input.Scope = ProjectScope.Root
                            || not (Set.contains source.Source.Path rootPaths)))
                    ->
                    let values =
                        document.LegacyFields
                        |> List.filter (fun field -> field.Disposition = "default")
                        |> List.fold
                            (fun tree field ->
                                match field.CanonicalPath with
                                | Some path -> ConfigTree.set path field.Value tree
                                | None -> tree
                            )
                            DocumentDefaults.emptyValue

                    Some(
                        available
                            { source.Source with
                                Role = SourceRole.Defaults
                                Order = 1
                            }
                            values
                    )
                | _ -> None
            )
            |> List.distinctBy (fun source -> source.Source.Path)

        let initial = DocumentDefaults.emptyValue, Map.empty, [], []

        let effective, origins, reports, diagnostics =
            sortSources
                plan.Active
                (standardDefaults
                 :: legacyDefaults
                 @ supplied)
            |> List.fold
                (fun (effective, origins, reports, diagnostics) source ->
                    let profile =
                        source.Source.Role = SourceRole.WorkspaceProfile
                        || source.Source.Role = SourceRole.MemberProfile

                    let duplicateSelected =
                        source.Source.Role
                        <> SourceRole.Selected
                        && source.Source.Role
                           <> SourceRole.Defaults
                        && Set.contains source.Source.Path selectedPaths

                    let inactive =
                        profile
                        && (not plan.Enabled
                            || not (
                                source.Source.Profile
                                |> Option.exists (fun name -> List.contains name plan.Active)
                            ))

                    if
                        duplicateSelected
                        || inactive
                    then
                        let report = {
                            Source = source.Source
                            Status = SourceStatus.Skipped
                            Contributed = false
                            Detail =
                                if inactive then
                                    "Inactive profile"
                                else
                                    "Contributes once at selected layer"
                        }

                        effective,
                        origins,
                        reports
                        @ [ report ],
                        diagnostics
                    else
                        match source.Content with
                        | SourceContent.Available document ->
                            let document, scopedDiagnostics =
                                scopeDocument
                                    input
                                    rootPaths
                                    memberPaths
                                    { document with Source = source.Source }

                            let value, winners =
                                mergeAt [] effective document.Values origins document

                            let contributed =
                                value
                                <> effective
                                || winners
                                   <> origins

                            let report = {
                                Source = source.Source
                                Status = SourceStatus.Loaded
                                Contributed = contributed
                                Detail = "Loaded"
                            }

                            value,
                            winners,
                            reports
                            @ [ report ],
                            diagnostics
                            @ document.Diagnostics
                            @ scopedDiagnostics
                        | SourceContent.Missing ->
                            let report = {
                                Source = source.Source
                                Status = SourceStatus.Missing
                                Contributed = false
                                Detail = "Missing"
                            }

                            let errors =
                                if
                                    source.Required
                                    || source.Source.Role = SourceRole.Selected
                                then
                                    [
                                        {
                                            Code = "config.missing"
                                            Message =
                                                "Required configuration is missing: "
                                                + source.Source.Path
                                            Severity = DiagnosticSeverity.Error
                                            Source = source.Source
                                            KeyPath = []
                                            Position = None
                                        }
                                    ]
                                else
                                    []

                            effective,
                            origins,
                            reports
                            @ [ report ],
                            diagnostics
                            @ errors
                        | SourceContent.Invalid errors ->
                            let report = {
                                Source = source.Source
                                Status = SourceStatus.Failed
                                Contributed = false
                                Detail = "Invalid"
                            }

                            effective,
                            origins,
                            reports
                            @ [ report ],
                            diagnostics
                            @ errors
                )
                initial

        let effective = ConfigTree.clean effective

        {
            Effective = effective
            Origins = origins
            Sources = reports
            ActiveProfiles = plan.Active
            OwningWorkspace = input.OwningWorkspace
            Diagnostics =
                (plan.Diagnostics
                 @ diagnostics
                 @ Validation.validate effective origins
                 @ mixedFormatDiagnostics input)
                |> List.distinct
        }
