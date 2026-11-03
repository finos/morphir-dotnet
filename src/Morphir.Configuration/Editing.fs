namespace Morphir.Configuration

[<RequireQualifiedAccess>]
type EditValueType =
    | String
    | Boolean
    | Integer
    | Number
    | Array
    | Object

[<RequireQualifiedAccess>]
type EditOperation =
    | Set of text: string * valueType: EditValueType
    | Unset

type EditRequest = {
    Operation: EditOperation
    Key: string
}

[<RequireQualifiedAccess>]
type EditScope =
    | Automatic
    | Workspace
    | Project
    | User
    | Global
    | System
    | File of string

/// Captured ownership and source inventory, supplied by the host without ambient discovery.
type EditTargetContext = {
    StartDirectory: string
    Discovery: ProjectDiscovery option
    SelectedProject: ProjectInfo option
    SelectionDiagnostics: ConfigDiagnostic list
    MachineCandidates: SourceIdentity list
    Sources: SourceInput list
}

[<RequireQualifiedAccess>]
type EditObservationKind =
    | Missing = 0
    | File = 1
    | Directory = 2
    | Link = 3

/// Opaque content or inventory identity supplied and checked by a host adapter.
type EditObservation = {
    Path: string
    Kind: EditObservationKind
    Identity: string
}

type EditFailure = {
    Code: string
    ExitCode: int
    Diagnostics: ConfigDiagnostic list
}

/// Canonical data is retained for application; Effective is a redacted presentation.
/// Planning does not read files, acquire locks, or change a supplied source.
type EditPlan = {
    Target: SourceIdentity
    Key: string list
    Operation: string
    Before: ConfigValue option
    After: ConfigValue option
    Candidate: ConfigValue
    CandidateInput: ResolutionInput
    Effective: InspectionResult
    Changed: bool
    Observations: EditObservation list
}

module Editing =
    let private failure code exitCode diagnostics =
        Error
            {
                Code = code
                ExitCode = exitCode
                Diagnostics = Inspection.safeDiagnostics diagnostics
            }

    let private exists (source: SourceInput) =
        match source.Content with
        | SourceContent.Missing -> false
        | SourceContent.Available _
        | SourceContent.Invalid _ -> true

    let private primary (role: SourceRole) (fallback: string) (sources: SourceInput list) =
        let sources =
            sources
            |> List.filter (fun source ->
                exists source
                && source.Source.Role = role
            )

        let toml =
            sources
            |> List.filter (fun source -> source.Source.Format = DocumentFormat.Toml)

        if toml.Length > 1 then
            failure "config.edit_ambiguous" 2 []
        else
            let source =
                (if toml.IsEmpty then
                     sources
                     |> List.sortByDescending (fun source ->
                         int source.Source.Format, source.Source.Order
                     )
                 else
                     toml)
                |> List.tryHead
                |> Option.map (fun source -> source.Source)

            let path =
                match source with
                | Some source when
                    source.Path.EndsWith("/morphir-workspace.json", System.StringComparison.Ordinal)
                    ->
                    ProjectPath.join (ProjectPath.parent source.Path) "morphir.toml"
                | Some source ->
                    source.Path.Substring(0, source.Path.LastIndexOf('.'))
                    + ".toml"
                | None -> fallback

            Ok
                {
                    Path = path
                    Format = DocumentFormat.Toml
                    Role = role
                    Order = 4
                    Profile = None
                }

    /// Selects one destination. The host must capture all candidates, including absences, as observations.
    let selectTarget selectors (context: EditTargetContext) =
        let invalid code = failure code 2 []

        if List.length selectors > 1 then
            invalid "config.edit_selectors"
        else
            let selector =
                List.tryHead selectors
                |> Option.defaultValue EditScope.Automatic

            let location role (location: ProjectLocation) =
                let sources =
                    location.Sources
                    |> List.map (fun source ->
                        { source with
                            Source = { source.Source with Role = role }
                        }
                    )

                primary role (ProjectPath.join location.Path "morphir.toml") sources

            let project () =
                if not context.SelectionDiagnostics.IsEmpty then
                    failure "config.edit_selection" 2 context.SelectionDiagnostics
                else
                    match context.SelectedProject with
                    | Some project when project.IsValid ->
                        let role =
                            match context.Discovery with
                            | Some discovery when
                                project.Path
                                <> discovery.Location
                                ->
                                SourceRole.MemberPrimary
                            | _ -> SourceRole.RootPrimary

                        location role project.Location
                    | _ -> invalid "config.edit_context"

            match selector with
            | EditScope.File path ->
                let path = ProjectPath.join context.StartDirectory path

                if not (path.EndsWith(".toml", System.StringComparison.OrdinalIgnoreCase)) then
                    invalid "config.edit_format"
                else
                    let known =
                        context.Sources
                        |> List.tryFind (fun source -> source.Source.Path = path)

                    let role =
                        known
                        |> Option.map (fun source -> source.Source.Role)
                        |> Option.defaultValue SourceRole.Selected

                    let alternate =
                        context.Sources
                        |> List.exists (fun source ->
                            source.Source.Path
                            <> path
                            && source.Source.Role = role
                            && role
                               <> SourceRole.Selected
                            && source.Source.Format = DocumentFormat.Toml
                            && exists source
                        )

                    if alternate then
                        invalid "config.edit_ambiguous"
                    else
                        Ok
                            {
                                Path = path
                                Format = DocumentFormat.Toml
                                Role = role
                                Order = 4
                                Profile = None
                            }
            | EditScope.Workspace ->
                match context.Discovery with
                | Some discovery when discovery.Workspace.IsSome ->
                    location SourceRole.RootPrimary discovery.Root
                | _ -> invalid "config.edit_context"
            | EditScope.Project -> project ()
            | EditScope.Automatic ->
                match context.Discovery with
                | Some discovery when
                    discovery.Workspace.IsSome
                    && context.StartDirectory = discovery.Location
                    ->
                    location SourceRole.RootPrimary discovery.Root
                | _ -> project ()
            | EditScope.User ->
                let owner =
                    match context.Discovery with
                    | Some discovery when
                        context.SelectedProject.IsNone
                        && context.SelectionDiagnostics.IsEmpty
                        && discovery.Workspace.IsSome
                        && context.StartDirectory = discovery.Location
                        ->
                        location SourceRole.RootPrimary discovery.Root
                    | _ -> project ()

                owner
                |> Result.bind (fun target ->
                    let stem =
                        target.Path.Substring(
                            0,
                            target.Path.Length
                            - 5
                        )

                    let personal =
                        context.Sources
                        |> List.filter (fun source ->
                            source.Source.Role = SourceRole.UserOverride
                            && ProjectPath.parent source.Source.Path = ProjectPath.parent
                                target.Path
                        )

                    primary
                        SourceRole.UserOverride
                        (stem
                         + ".user.toml")
                        personal
                )
            | EditScope.Global
            | EditScope.System ->
                let role =
                    if selector = EditScope.Global then
                        SourceRole.Global
                    else
                        SourceRole.System

                let candidates =
                    context.MachineCandidates
                    |> List.filter (fun source ->
                        source.Role = role
                        && source.Format = DocumentFormat.Toml
                    )

                match List.tryLast candidates with
                | None -> invalid "config.edit_context"
                | Some fallback -> primary role fallback.Path context.Sources

    /// JSON supplies composite/explicit scalar syntax only; output format never affects input typing.
    let parseValue kind (text: string) =
        let invalid () = failure "config.edit_value" 2 []

        if kind = EditValueType.String then
            Ok(ConfigValue.String text)
        else
            try
                use document =
                    System.Text.Json.JsonDocument.Parse(
                        text,
                        System.Text.Json.JsonDocumentOptions(MaxDepth = 128)
                    )

                let rec read (element: System.Text.Json.JsonElement) =
                    match element.ValueKind with
                    | System.Text.Json.JsonValueKind.String ->
                        Some(ConfigValue.String(element.GetString()))
                    | System.Text.Json.JsonValueKind.True -> Some(ConfigValue.Boolean true)
                    | System.Text.Json.JsonValueKind.False -> Some(ConfigValue.Boolean false)
                    | System.Text.Json.JsonValueKind.Number ->
                        match element.TryGetInt64() with
                        | true, value -> Some(ConfigValue.Integer value)
                        | _ when element.GetRawText().IndexOfAny([| '.'; 'e'; 'E' |]) < 0 -> None
                        | _ ->
                            match element.TryGetDouble() with
                            | true, value when System.Double.IsFinite value ->
                                Some(ConfigValue.Number value)
                            | _ -> None
                    | System.Text.Json.JsonValueKind.Array ->
                        let values =
                            element.EnumerateArray()
                            |> Seq.map read
                            |> Seq.toList

                        if List.forall Option.isSome values then
                            Some(ConfigValue.Array(List.choose id values))
                        else
                            None
                    | System.Text.Json.JsonValueKind.Object ->
                        let properties =
                            element.EnumerateObject()
                            |> Seq.toList

                        let values =
                            properties
                            |> List.map (fun field -> field.Name, read field.Value)

                        if
                            (properties
                             |> List.map (fun field -> field.Name)
                             |> List.distinct
                             |> List.length)
                            <> properties.Length
                            || values
                               |> List.exists (
                                   snd
                                   >> Option.isNone
                               )
                        then
                            None
                        else
                            Some(
                                ConfigValue.Object(
                                    values
                                    |> List.map (fun (key, value) -> key, value.Value)
                                    |> Map.ofList
                                )
                            )
                    | _ -> None

                match read document.RootElement, kind with
                | Some(ConfigValue.Boolean _ as value), EditValueType.Boolean
                | Some(ConfigValue.Integer _ as value), EditValueType.Integer -> Ok value
                | Some(ConfigValue.Number _), EditValueType.Number
                | Some(ConfigValue.Integer _), EditValueType.Number ->
                    Ok(ConfigValue.Number(document.RootElement.GetDouble()))
                | Some(ConfigValue.Array _ as value), EditValueType.Array
                | Some(ConfigValue.Object _ as value), EditValueType.Object -> Ok value
                | _ -> invalid ()
            with
            | :? System.Text.Json.JsonException
            | :? System.InvalidOperationException -> invalid ()

    let rec private remove path tree =
        match path, tree with
        | [ key ], ConfigValue.Object fields -> ConfigValue.Object(Map.remove key fields)
        | key :: rest, ConfigValue.Object fields ->
            match Map.tryFind key fields with
            | Some child -> ConfigValue.Object(Map.add key (remove rest child) fields)
            | None -> tree
        | _ -> tree

    let rec private same left right =
        match left, right with
        | ConfigValue.Number left, ConfigValue.Number right ->
            (System.Double.IsNaN left
             && System.Double.IsNaN right)
            || System.BitConverter.DoubleToInt64Bits left = System.BitConverter.DoubleToInt64Bits
                right
        | ConfigValue.Object left, ConfigValue.Object right
        | ConfigValue.SecretReference left, ConfigValue.SecretReference right ->
            Map.count left = Map.count right
            && (left
                |> Map.forall (fun key value ->
                    Map.tryFind key right
                    |> Option.exists (same value)
                ))
        | ConfigValue.Array left, ConfigValue.Array right ->
            left.Length = right.Length
            && List.forall2 same left right
        | _ -> left = right

    let rec private traversable path tree =
        match path, tree with
        | [], _ -> true
        | [ _ ], ConfigValue.Object _ -> true
        | key :: rest, ConfigValue.Object fields ->
            Map.tryFind key fields
            |> Option.map (traversable rest)
            |> Option.defaultValue true
        | _ -> false

    let private planValidTarget
        (target: SourceIdentity)
        (request: EditRequest)
        (input: ResolutionInput)
        observations
        =
        match KeyPath.parse request.Key with
        | Error _ -> failure "config.edit_key" 2 []
        | Ok path ->
            let original =
                input.Sources
                |> List.tryPick (fun source ->
                    if
                        source.Source.Path
                        <> target.Path
                    then
                        None
                    else
                        match source.Content with
                        | SourceContent.Available document -> Some document.Raw
                        | _ -> None
                )
                |> Option.defaultValue DocumentDefaults.emptyValue

            let before = ConfigTree.tryFind path original

            let parsed =
                match request.Operation with
                | EditOperation.Set(text, kind) ->
                    parseValue kind text
                    |> Result.map Some
                | EditOperation.Unset when before.IsSome -> Ok None
                | EditOperation.Unset -> failure "config.edit_not_found" 1 []

            let normalized = Normalization.normalize target original Map.empty

            if not (traversable path normalized.Values) then
                failure "config.edit_path_shape" 2 []
            else
                parsed
                |> Result.bind (fun value ->
                    let candidate =
                        match value with
                        | Some value -> ConfigTree.set path value original
                        | None -> remove path original

                    let document = Normalization.normalize target candidate Map.empty

                    let source = {
                        Source = target
                        Content = SourceContent.Available document
                        Required = false
                    }

                    let sources =
                        if
                            input.Sources
                            |> List.exists (fun source -> source.Source.Path = target.Path)
                        then
                            input.Sources
                            |> List.map (fun old ->
                                if old.Source.Path = target.Path then
                                    { old with
                                        Content =
                                            SourceContent.Available
                                                { document with Source = old.Source }
                                        Required = false
                                    }
                                else
                                    old
                            )
                        else
                            input.Sources
                            @ [ source ]

                    let candidateInput = { input with Sources = sources }
                    let details = Resolver.resolveDetailed candidateInput

                    let authoredErrors =
                        Validation.validate
                            document.Values
                            (Map.ofList [ [], { Source = target; Position = None } ])

                    let diagnostics =
                        document.Diagnostics
                        @ authoredErrors
                        @ details.Resolution.Diagnostics
                        @ ProjectSources.ambiguities sources

                    if
                        diagnostics
                        |> List.exists (fun diagnostic ->
                            diagnostic.Severity = DiagnosticSeverity.Error
                        )
                    then
                        failure "config.edit_validation" 2 diagnostics
                    else
                        Ok
                            {
                                Target = target
                                Key = path
                                Operation = (if value.IsSome then "set" else "unset")
                                Before = before
                                After = value
                                Candidate = candidate
                                CandidateInput = candidateInput
                                Effective = Inspection.explain QueryScope.Effective path details
                                Changed = not (same original candidate)
                                Observations = observations
                            }
                )

    let plan (target: SourceIdentity) (request: EditRequest) (input: ResolutionInput) observations =
        if
            target.Format
            <> DocumentFormat.Toml
            || not (target.Path.EndsWith(".toml", System.StringComparison.OrdinalIgnoreCase))
        then
            failure "config.edit_format" 2 []
        elif
            input.Sources
            |> List.exists (fun source ->
                source.Source.Path = target.Path
                && (
                    match source.Content with
                    | SourceContent.Invalid _ -> true
                    | _ -> false
                )
            )
        then
            failure "config.edit_source_invalid" 2 []
        else
            planValidTarget target request input observations

    /// Safe presentation for raw edit values, including unevaluated reference-shaped objects.
    let redactValue path value =
        Normalization.normalizeSecrets path value
        |> Inspection.redact path
