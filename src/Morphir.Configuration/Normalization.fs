namespace Morphir.Configuration

/// Pure normalization shared by typed in-memory hosts and format adapters.
module Normalization =
    let private legacyFields = [
        "name", [ "project"; "name" ]
        "sourceDirectory", [ "project"; "source_directory" ]
        "exposedModules", [ "project"; "exposed_modules" ]
    ]

    let private nonempty value =
        match value with
        | ConfigValue.Null -> false
        | ConfigValue.Array values -> not values.IsEmpty
        | ConfigValue.Object values -> not values.IsEmpty
        | ConfigValue.String value -> not (System.String.IsNullOrWhiteSpace value)
        | _ -> true

    let private nonemptyString value =
        match value with
        | ConfigValue.String text -> not (System.String.IsNullOrEmpty text)
        | _ -> false

    let private secretReference fields =
        if
            Map.count fields
            <> 1
        then
            false
        else
            match Map.toList fields with
            | [ ("env" | "file"), value ] -> nonemptyString value
            | [ "command", ConfigValue.Array(program :: arguments) ] ->
                nonemptyString program
                && (arguments
                    |> List.forall (
                        function
                        | ConfigValue.String _ -> true
                        | _ -> false
                    ))
            | [ "keyring", ConfigValue.Object value ] ->
                Map.count value = 2
                && (Map.tryFind "service" value
                    |> Option.exists nonemptyString)
                && (Map.tryFind "account" value
                    |> Option.exists nonemptyString)
            | _ -> false

    let rec private normalizeSecrets value =
        match value with
        | ConfigValue.Object fields ->
            let normalized = Map.map (fun _ child -> normalizeSecrets child) fields

            if secretReference normalized then
                ConfigValue.SecretReference normalized
            else
                ConfigValue.Object normalized
        | ConfigValue.Array values -> ConfigValue.Array(List.map normalizeSecrets values)
        | _ -> value

    let normalize source raw positions =
        let legacy =
            legacyFields
            |> List.exists (fun (key, _) ->
                ConfigTree.tryFind [ key ] raw
                |> Option.isSome
            )

        let mutable values = raw
        let mutable canonicalPositions = positions
        let mutable notes = []
        let mutable diagnostics = []

        let note rawPath canonicalPath value disposition =
            notes <-
                notes
                @ [
                    {
                        RawPath = rawPath
                        CanonicalPath = canonicalPath
                        Value = value
                        Disposition = disposition
                    }
                ]

        let error code message path =
            diagnostics <-
                diagnostics
                @ [
                    {
                        Code = code
                        Message = message
                        Severity = DiagnosticSeverity.Error
                        Source = source
                        KeyPath = path
                        Position = Map.tryFind path positions
                    }
                ]

        if legacy then
            for key in [ "project"; "frontend" ] do
                match ConfigTree.tryFind [ key ] raw with
                | Some(ConfigValue.Object _)
                | None -> ()
                | Some _ ->
                    error
                        "config.legacy_value"
                        ("Canonical '"
                         + key
                         + "' must be an object before classic normalization")
                        [ key ]

            for key in [ "name"; "sourceDirectory" ] do
                match ConfigTree.tryFind [ key ] raw with
                | Some(ConfigValue.String _)
                | None -> ()
                | Some _ ->
                    error
                        "config.legacy_value"
                        ("Legacy '"
                         + key
                         + "' must be a string")
                        [ key ]

            match ConfigTree.tryFind [ "exposedModules" ] raw with
            | Some(ConfigValue.Array modules) when
                modules
                |> List.forall (
                    function
                    | ConfigValue.String _ -> true
                    | _ -> false
                )
                ->
                ()
            | _ ->
                error
                    "config.legacy_value"
                    "Classic manifests require top-level 'exposedModules' as an array of strings"
                    [ "exposedModules" ]

            for key, target in legacyFields do
                match ConfigTree.tryFind [ key ] raw with
                | Some value ->
                    if
                        ConfigTree.tryFind target raw
                        |> Option.isSome
                    then
                        let canonical = System.String.Join(".", target)

                        error
                            "config.legacy_conflict"
                            ("Legacy '"
                             + key
                             + "' conflicts with canonical '"
                             + canonical
                             + "', even when equal")
                            [ key ]

                    values <-
                        ConfigTree.remove key values
                        |> ConfigTree.set target value

                    note [ key ] (Some target) value "normalized"

                    match Map.tryFind [ key ] positions with
                    | Some position ->
                        canonicalPositions <- Map.add target position canonicalPositions
                    | None -> ()
                | None -> ()

            for path, fallback in
                [
                    [ "project"; "name" ], ConfigValue.String "user"
                    [ "project"; "source_directory" ], ConfigValue.String "src"
                    [ "frontend"; "language" ], ConfigValue.String "elm"
                ] do
                if
                    ConfigTree.tryFind path values
                    |> Option.isNone
                then
                    values <- ConfigTree.set path fallback values
                    note [] (Some path) fallback "derived"

            // Checkpoint defaults form a separate low-priority layer in the resolver.
            // They must not override system/global pipeline settings.
            for path, fallback in
                [
                    [ "pipeline"; "backend" ], ConfigValue.String "checkpoint"
                    [ "pipeline"; "checkpoint_format" ], ConfigValue.String "morphir-json"
                ] do
                note [] (Some path) fallback "default"

            for key in [ "dependencies"; "localDependencies"; "decorations" ] do
                match ConfigTree.tryFind [ key ] raw with
                | Some(ConfigValue.Object _) when key = "dependencies" -> ()
                | Some value ->
                    values <- ConfigTree.remove key values
                    note [ key ] None value "unsupported"

                    if nonempty value then
                        diagnostics <-
                            diagnostics
                            @ [
                                {
                                    Code = "config.legacy_unsupported"
                                    Message =
                                        ("Legacy field '"
                                         + key
                                         + "' is not consumed; raw values are retained for loss accounting")
                                    Severity = DiagnosticSeverity.Warning
                                    Source = source
                                    KeyPath = [ key ]
                                    Position = Map.tryFind [ key ] positions
                                }
                            ]
                | None -> ()

        {
            Source = source
            Raw = raw
            Values = normalizeSecrets values
            Positions = canonicalPositions
            Diagnostics = diagnostics
            LegacyFields = notes
        }
