namespace Morphir.Configuration

[<RequireQualifiedAccess>]
type QueryScope =
    | Effective
    | Workspace
    | Project
    | User
    | Global
    | System
    | File of string

type InspectionOrigin = { Key: string; Origin: ValueOrigin }

type InspectionEntry = {
    Key: string
    Path: string list
    Value: ConfigValue
    Origins: InspectionOrigin list
}

type InspectionContribution = {
    Source: SourceIdentity
    Value: ConfigValue option
    Origins: InspectionOrigin list
    Winner: bool
}

type InspectionResult = {
    Operation: string
    Scope: QueryScope
    Key: string option
    Found: bool
    Value: ConfigValue option
    Entries: InspectionEntry list
    Sources: SourceReport list
    Contributions: InspectionContribution list
    Diagnostics: ConfigDiagnostic list
}

/// Canonical case-sensitive paths. Quoted segments use JSON string escaping.
module KeyPath =
    let private plain character =
        (character
         >= 'a'
         && character
            <= 'z')
        || (character
            >= 'A'
            && character
               <= 'Z')
        || (character
            >= '0'
            && character
               <= '9')
        || character = '_'
        || character = '-'

    let format segments =
        segments
        |> List.map (fun (segment: string) ->
            if
                segment.Length > 0
                && Seq.forall plain segment
            then
                segment
            else
                "\""
                + System.Text.Json.JsonEncodedText.Encode(segment).ToString()
                + "\""
        )
        |> String.concat "."

    let parse (text: string) =
        let invalid =
            Error
                "Expected dot-separated keys or JSON double-quoted segments; arrays are whole values."

        if System.String.IsNullOrEmpty text then
            invalid
        else
            let mutable index = 0
            let mutable segments = []
            let mutable valid = true

            while valid
                  && index < text.Length do
                let start = index

                if text.[index] = '\"' then
                    index <- index + 1
                    let mutable escaped = false
                    let mutable closed = false

                    while index < text.Length
                          && not closed do
                        let character = text.[index]

                        if
                            not escaped
                            && character = '\"'
                        then
                            closed <- true

                        if
                            not escaped
                            && character = '\\'
                        then
                            escaped <- true
                        else
                            escaped <- false

                        index <- index + 1

                    if not closed then
                        valid <- false
                    else
                        try
                            use json =
                                System.Text.Json.JsonDocument.Parse(
                                    text.Substring(
                                        start,
                                        index
                                        - start
                                    )
                                )

                            segments <-
                                json.RootElement.GetString()
                                :: segments
                        with
                        | :? System.Text.Json.JsonException
                        | :? System.InvalidOperationException -> valid <- false
                else
                    while index < text.Length
                          && plain text.[index] do
                        index <- index + 1

                    if start = index then
                        valid <- false
                    else
                        segments <-
                            text.Substring(
                                start,
                                index
                                - start
                            )
                            :: segments

                if index < text.Length then
                    if
                        text.[index]
                        <> '.'
                        || index + 1 = text.Length
                    then
                        valid <- false
                    else
                        index <- index + 1

            if valid then Ok(List.rev segments) else invalid

module Inspection =
    let private sensitive path =
        path
        |> List.exists (fun (segment: string) ->
            let name =
                segment
                |> Seq.filter System.Char.IsLetterOrDigit
                |> Seq.toArray
                |> System.String
                |> fun text -> text.ToLowerInvariant()

            [
                "password"
                "passphrase"
                "secret"
                "token"
                "credential"
                "privatekey"
                "apikey"
                "accesskey"
                "connectionstring"
                "authorization"
            ]
            |> List.exists name.Contains
        )

    /// Safe presentation only. Canonical values remain available separately.
    let rec redact path value =
        if sensitive path then
            ConfigValue.String "[REDACTED]"
        else
            match value with
            | ConfigValue.SecretReference _ -> ConfigValue.String "[REDACTED]"
            | ConfigValue.Object fields ->
                fields
                |> Map.map (fun key value ->
                    redact
                        (path
                         @ [ key ])
                        value
                )
                |> ConfigValue.Object
            | ConfigValue.Array items ->
                items
                |> List.map (redact path)
                |> ConfigValue.Array
            | _ -> value

    /// Parser and validation messages can contain input values; presentation retains safe metadata.
    let safeDiagnostics diagnostics =
        diagnostics
        |> List.map (fun diagnostic ->
            { diagnostic with
                Message = "Configuration diagnostic; consult the code, key and source metadata."
            }
        )

    let private matches (details: ResolutionDetails) scope (source: SourceIdentity) =
        let roleMatches role =
            match scope with
            | QueryScope.Effective -> true
            | QueryScope.Workspace ->
                role = SourceRole.RootPrimary
                && details.Resolution.OwningWorkspace.IsSome
            | QueryScope.Project ->
                role = (if details.Scope = ProjectScope.Member then
                            SourceRole.MemberPrimary
                        else
                            SourceRole.RootPrimary)
            | QueryScope.User -> role = SourceRole.UserOverride
            | QueryScope.Global -> role = SourceRole.Global
            | QueryScope.System -> role = SourceRole.System
            | QueryScope.File _ -> false

        match scope with
        | QueryScope.File path -> source.Path = path
        | _ ->
            roleMatches source.Role
            || (source.Role = SourceRole.Selected
                && details.Resolution.Sources
                   |> List.exists (fun report ->
                       report.Source.Path = source.Path
                       && roleMatches report.Source.Role
                   ))

    let private select scope (details: ResolutionDetails) =
        match scope with
        | QueryScope.Effective -> details.Resolution.Effective, details.Resolution.Origins
        | _ ->
            details.Layers
            |> List.filter (fun layer -> matches details scope layer.Document.Source)
            |> List.map (fun layer ->
                { layer.Document with
                    Values = ConfigTree.remove "profiles" layer.Document.Values
                }
            )
            |> Resolver.mergeDocuments

    let private below prefix path =
        List.length path
        >= List.length prefix
        && List.take (List.length prefix) path = prefix

    let private originsFor path origins =
        origins
        |> Map.toList
        |> List.filter (fun (candidate, _) -> below path candidate)
        |> List.map (fun (candidate, origin) -> {
            Key = KeyPath.format candidate
            Origin = origin
        })

    let private reports scope (details: ResolutionDetails) =
        details.Resolution.Sources
        |> List.filter (fun report -> matches details scope report.Source)

    let private result operation scope (details: ResolutionDetails) : InspectionResult = {
        Operation = operation
        Scope = scope
        Key = None
        Found = true
        Value = None
        Entries = []
        Sources = reports scope details
        Contributions = []
        Diagnostics = safeDiagnostics details.Resolution.Diagnostics
    }

    let get scope path (details: ResolutionDetails) =
        let value, origins = select scope details

        let found =
            ConfigTree.tryFind path value
            |> Option.map (redact path)

        let entries =
            found
            |> Option.map (fun value -> [
                {
                    Key = KeyPath.format path
                    Path = path
                    Value = value
                    Origins = originsFor path origins
                }
            ])
            |> Option.defaultValue []

        { result "get" scope details with
            Key = Some(KeyPath.format path)
            Found = found.IsSome
            Value = found
            Entries = entries
        }

    let private entries value origins =
        let rec leaves path value =
            match value with
            | ConfigValue.Object fields when not (Map.isEmpty fields) ->
                fields
                |> Map.toList
                |> List.collect (fun (key, value) ->
                    leaves
                        (path
                         @ [ key ])
                        value
                )
            | _ when path.IsEmpty -> []
            | _ -> [
                {
                    Key = KeyPath.format path
                    Path = path
                    Value = redact path value
                    Origins = originsFor path origins
                }
              ]

        leaves [] value

    let list scope details =
        let value, origins = select scope details

        { result "list" scope details with
            Entries = entries value origins
        }

    let show scope details =
        let value, origins = select scope details

        { result "show" scope details with
            Value = Some(redact [] value)
            Entries = entries value origins
        }

    let path scope details = result "path" scope details

    let explain scope key details =
        let query = get scope key details
        let _, winners = select scope details
        let winnerOrigins = originsFor key winners

        let contributions =
            details.Layers
            |> List.filter (fun layer -> matches details scope layer.Document.Source)
            |> List.choose (fun layer ->
                let value =
                    if scope = QueryScope.Effective then
                        layer.Applied
                    else
                        ConfigTree.remove "profiles" layer.Document.Values
                // Resolve each contribution in isolation to keep null overlays out of origins.
                let clean, origins =
                    Resolver.mergeDocuments [ { layer.Document with Values = value } ]

                let supplied = ConfigTree.tryFind key clean

                let blocks =
                    [
                        1 .. List.length key
                             - 1
                    ]
                    |> List.exists (fun length ->
                        match ConfigTree.tryFind (List.take length key) clean with
                        | Some(ConfigValue.Object _)
                        | None -> false
                        | Some _ -> true
                    )

                if
                    supplied.IsNone
                    && not blocks
                then
                    None
                else
                    Some
                        {
                            Source = layer.Document.Source
                            Value =
                                supplied
                                |> Option.map (redact key)
                            Origins = originsFor key origins
                            Winner =
                                winnerOrigins
                                |> List.exists (fun origin ->
                                    origin.Origin.Source = layer.Document.Source
                                )
                        }
            )

        { query with
            Operation = "explain"
            Contributions = contributions
        }
