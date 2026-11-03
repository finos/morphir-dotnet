namespace Morphir.Configuration

/// Plans profile activation from explicit inputs without reading profile files.
module Profiles =
    let private asciiTrim (text: string) =
        text.Trim([| ' '; '\t'; '\n'; '\r'; '\u000b'; '\u000c' |])

    let private isProfileName (name: string) =
        not (System.String.IsNullOrEmpty name)
        && name.Length
           <= 64
        && name.[0]
           >= 'a'
        && name.[0]
           <= 'z'
        && name
           |> Seq.forall (fun character ->
               (character
                >= 'a'
                && character
                   <= 'z')
               || (character
                   >= '0'
                   && character
                      <= '9')
               || character = '_'
               || character = '-'
           )

    let private nameErrors source path names =
        names
        |> List.choose (fun name ->
            if isProfileName name then
                None
            else
                Some
                    {
                        Code = "config.profiles.name"
                        Message =
                            "Profile names must match [a-z][a-z0-9_-]{0,63}: "
                            + name
                        Severity = DiagnosticSeverity.Error
                        Source = source
                        KeyPath = path
                        Position = None
                    }
        )

    let private selectorSource role name = {
        Path = name
        Role = role
        Format = DocumentFormat.Memory
        Order = 0
        Profile = None
    }

    let private baseDocuments (sources: SourceInput list) =
        sources
        |> List.filter (fun input -> input.Source.Role = SourceRole.RootPrimary)
        |> List.sortBy (fun input -> input.Source.Format, input.Source.Order, input.Source.Path)
        |> List.choose (fun input ->
            match input.Content with
            | SourceContent.Available doc -> Some doc
            | _ -> None
        )

    // Control nulls are literal metadata and must survive until control validation.
    let rec private mergeControls lower higher =
        match lower, higher with
        | ConfigValue.Object left, ConfigValue.Object right ->
            right
            |> Map.fold
                (fun result key value ->
                    let merged =
                        Map.tryFind key result
                        |> Option.map (fun previous -> mergeControls previous value)
                        |> Option.defaultValue value

                    Map.add key merged result
                )
                left
            |> ConfigValue.Object
        | _ -> higher

    let private baseValue sources =
        baseDocuments sources
        |> List.map (fun doc -> doc.Raw)
        |> List.fold mergeControls DocumentDefaults.emptyValue

    let private diagnostic sources path code message =
        let origin =
            baseDocuments sources
            |> List.rev
            |> List.find (fun doc ->
                ConfigTree.tryFind path doc.Raw
                |> Option.isSome
            )

        {
            Code = code
            Message = message
            Severity = DiagnosticSeverity.Error
            Source = origin.Source
            KeyPath = path
            Position = Map.tryFind path origin.Positions
        }

    let private authorityErrors (sources: SourceInput list) (environment: Map<string, string>) =
        let rootPaths =
            sources
            |> List.filter (fun input -> input.Source.Role = SourceRole.RootPrimary)
            |> List.map (fun input -> input.Source.Path)
            |> Set.ofList

        let fileErrors =
            sources
            |> List.collect (fun input ->
                let authorized =
                    input.Source.Role = SourceRole.RootPrimary
                    || (input.Source.Role = SourceRole.Selected
                        && Set.contains input.Source.Path rootPaths)

                match authorized, input.Content with
                | false, SourceContent.Available doc ->
                    let paths =
                        match ConfigTree.tryFind [ "profiles" ] doc.Raw with
                        | None -> []
                        | Some(ConfigValue.Object fields) when not fields.IsEmpty ->
                            fields
                            |> Map.toList
                            |> List.map (fun (key, _) -> [ "profiles"; key ])
                        | Some _ -> [ [ "profiles" ] ]

                    paths
                    |> List.map (fun path -> {
                        Code = "config.profiles.authority"
                        Message =
                            "Profile activation controls belong only to discovered root primary documents."
                        Severity = DiagnosticSeverity.Error
                        Source = input.Source
                        KeyPath = path
                        Position = Map.tryFind path doc.Positions
                    })
                | _ -> []
            )

        let environmentErrors =
            environment
            |> Map.toList
            |> List.choose (fun (name, _) ->
                let path =
                    if name.StartsWith("MORPHIR_", System.StringComparison.Ordinal) then
                        name.Substring("MORPHIR_".Length).Split("__")
                        |> Array.map (fun segment -> segment.ToLowerInvariant())
                        |> Array.toList
                    else
                        []

                match path with
                | "profiles" :: _ :: _ ->
                    Some
                        {
                            Code = "config.profiles.authority"
                            Message =
                                "Environment values cannot supply profile activation controls."
                            Severity = DiagnosticSeverity.Error
                            Source = {
                                Path = name
                                Role = SourceRole.Environment
                                Format = DocumentFormat.Memory
                                Order = 0
                                Profile = None
                            }
                            KeyPath = path
                            Position = None
                        }
                | _ -> None
            )

        fileErrors
        @ environmentErrors

    let private controlErrors sources value =
        match ConfigTree.tryFind [ "profiles" ] value with
        | None -> []
        | Some(ConfigValue.Object fields) ->
            let arrayErrors =
                [ "active"; "default"; "include" ]
                |> List.collect (fun key ->
                    let path = [ "profiles"; key ]

                    match ConfigTree.tryFind path value with
                    | Some(ConfigValue.Array items) when
                        items
                        |> List.forall (
                            function
                            | ConfigValue.String _ -> true
                            | _ -> false
                        )
                        ->
                        let origin = diagnostic sources path "" ""

                        items
                        |> List.choose (
                            function
                            | ConfigValue.String name -> Some name
                            | _ -> None
                        )
                        |> nameErrors origin.Source path
                        |> List.map (fun error ->
                            { error with
                                Position = origin.Position
                            }
                        )
                    | None -> []
                    | Some _ -> [
                        diagnostic
                            sources
                            path
                            "config.profiles.shape"
                            "Profile controls require an array of literal strings."
                      ]
                )

            let unknownErrors =
                fields
                |> Map.toList
                |> List.choose (fun (key, _) ->
                    if List.contains key [ "active"; "default"; "include"; "groups" ] then
                        None
                    else
                        Some(
                            diagnostic
                                sources
                                [ "profiles"; key ]
                                "config.profiles.unknown"
                                ("Unknown profile control: "
                                 + key)
                        )
                )

            let groupErrors =
                match Map.tryFind "groups" fields with
                | None -> []
                | Some(ConfigValue.Object groups) ->
                    groups
                    |> Map.toList
                    |> List.collect (fun (name, members) ->
                        let path = [ "profiles"; "groups"; name ]
                        let origin = diagnostic sources path "" ""

                        let validateNames names =
                            nameErrors origin.Source path names
                            |> List.map (fun error ->
                                { error with
                                    Position = origin.Position
                                }
                            )

                        let groupNameErrors = validateNames [ name ]

                        match members with
                        | ConfigValue.Array items when
                            items
                            |> List.forall (
                                function
                                | ConfigValue.String _ -> true
                                | _ -> false
                            )
                            ->
                            groupNameErrors
                            @ (items
                               |> List.choose (
                                   function
                                   | ConfigValue.String memberName -> Some memberName
                                   | _ -> None
                               )
                               |> validateNames)
                            @ (if items.Length > 64 then
                                   [
                                       diagnostic
                                           sources
                                           path
                                           "config.profiles.members"
                                           "Profile groups permit at most 64 members."
                                   ]
                               else
                                   [])
                        | _ ->
                            groupNameErrors
                            @ [
                                diagnostic
                                    sources
                                    path
                                    "config.profiles.shape"
                                    "Profile group members require an array of literal strings."
                            ]
                    )
                | Some _ -> [
                    diagnostic
                        sources
                        [ "profiles"; "groups" ]
                        "config.profiles.shape"
                        "Profile groups requires a table."
                  ]

            arrayErrors
            @ unknownErrors
            @ groupErrors
        | Some _ -> [
            diagnostic sources [ "profiles" ] "config.profiles.shape" "Profiles requires a table."
          ]

    let private readNames path value =
        ConfigTree.tryFind [ "profiles"; path ] value
        |> Option.map (
            function
            | ConfigValue.Array items ->
                items
                |> List.choose (
                    function
                    | ConfigValue.String name -> Some name
                    | _ -> None
                )
            | _ -> []
        )

    let private groupGraph value =
        match ConfigTree.tryFind [ "profiles"; "groups" ] value with
        | Some(ConfigValue.Object groups) ->
            groups
            |> Map.map (fun _ members ->
                match members with
                | ConfigValue.Array items ->
                    items
                    |> List.choose (
                        function
                        | ConfigValue.String name -> Some name
                        | _ -> None
                    )
                | _ -> []
            )
        | _ -> Map.empty

    let private graphErrors sources value (options: ProfileOptions) =
        let groups = groupGraph value
        let mutable heights = Map.empty

        let rec visit stack name =
            match Map.tryFind name heights with
            | Some height -> Ok height
            | None when List.contains name stack ->
                let cycle =
                    (stack
                     |> List.skipWhile ((<>) name))
                    @ [ name ]

                Error(
                    diagnostic
                        sources
                        [ "profiles"; "groups"; name ]
                        "config.profiles.cycle"
                        ("Profile group cycle: "
                         + System.String.Join(" -> ", cycle))
                )
            | None ->
                match Map.tryFind name groups with
                | None -> Ok 0
                | Some members ->
                    let rec childrenHeight children height =
                        match children with
                        | [] -> Ok height
                        | child :: rest ->
                            match
                                visit
                                    (stack
                                     @ [ name ])
                                    child
                            with
                            | Error error -> Error error
                            | Ok childHeight ->
                                childrenHeight
                                    rest
                                    (max
                                        height
                                        (1
                                         + childHeight))

                    childrenHeight members 1
                    |> Result.bind (fun height ->
                        if height > min 32 options.MaxDepth then
                            Error(
                                diagnostic
                                    sources
                                    [ "profiles"; "groups"; name ]
                                    "config.profiles.depth"
                                    "Profile group graph exceeds the depth bound."
                            )
                        else
                            heights <- Map.add name height heights
                            Ok height
                    )

        if groups.Count > 64 then
            [
                diagnostic
                    sources
                    [ "profiles"; "groups" ]
                    "config.profiles.groups"
                    "Profile controls permit at most 64 groups."
            ]
        else
            groups
            |> Map.toList
            |> List.tryPick (fun (name, _) ->
                match visit [] name with
                | Error error -> Some error
                | Ok _ -> None
            )
            |> Option.toList

    let private expandWithPaths value roots =
        let mutable memo = Map.empty

        let keepLastNames names =
            names
            |> List.rev
            |> List.distinctBy fst
            |> List.rev

        let rec append stack (name, path) =
            let descendants =
                match Map.tryFind name memo with
                | Some expanded -> expanded
                | None when Set.contains name stack -> []
                | None ->
                    let children =
                        match ConfigTree.tryFind [ "profiles"; "groups"; name ] value with
                        | Some(ConfigValue.Array members) ->
                            members
                            |> List.choose (
                                function
                                | ConfigValue.String child -> Some child
                                | _ -> None
                            )
                        | _ -> []

                    let expanded =
                        children
                        |> List.fold
                            (fun output child ->
                                keepLastNames (
                                    output
                                    @ append
                                        (Set.add name stack)
                                        (child, [ "profiles"; "groups"; name ])
                                )
                            )
                            []

                    memo <- Map.add name expanded memo
                    expanded

            keepLastNames (
                (name, path)
                :: descendants
            )

        roots
        |> List.fold
            (fun output name ->
                keepLastNames (
                    output
                    @ append Set.empty name
                )
            )
            []

    let private expand value roots =
        expandWithPaths
            value
            (roots
             |> List.map (fun name -> name, []))
        |> List.map fst

    let private boundActive
        (options: ProfileOptions)
        sources
        value
        roots
        source
        path
        position
        (plan: ProfilePlan)
        =
        if plan.Active.Length > min 128 options.MaxExpansions then
            let failurePath =
                expandWithPaths value roots
                |> List.tryItem (max 0 (min 128 options.MaxExpansions))
                |> Option.map snd
                |> Option.defaultValue path

            let source, path, position =
                if
                    failurePath
                    <> path
                    && ConfigTree.tryFind failurePath value
                       |> Option.isSome
                then
                    let origin = diagnostic sources failurePath "" ""
                    origin.Source, failurePath, origin.Position
                else
                    source, path, position

            { plan with
                Active = []
                Diagnostics =
                    plan.Diagnostics
                    @ [
                        {
                            Code = "config.profiles.active"
                            Message = "Expanded profiles exceed the distinct active-name bound."
                            Severity = DiagnosticSeverity.Error
                            Source = source
                            KeyPath = path
                            Position = position
                        }
                    ]
            }
        else
            plan

    let private enabledPlan value roots enabled diagnostics = {
        Enabled = enabled
        Active =
            if
                diagnostics
                |> List.exists (fun error -> error.Severity = DiagnosticSeverity.Error)
            then
                []
            else
                expand value roots
        Diagnostics = diagnostics
    }

    let plan
        (sources: SourceInput list)
        (environment: Map<string, string>)
        (options: ProfileOptions)
        =
        match options.Selection with
        | ProfileSelection.Automatic when
            environment
            |> Map.tryFind "MORPHIR_PROFILES"
            |> Option.exists (fun text -> asciiTrim text = "")
              ->
              {
                  Enabled = false
                  Active = []
                  Diagnostics = []
              }
        | ProfileSelection.Names names ->
            let value = baseValue sources

            let includes =
                value
                |> readNames "include"
                |> Option.defaultValue []

            (nameErrors (selectorSource SourceRole.Explicit "<profiles>") [ "profiles" ] names
             @ authorityErrors sources environment
             @ controlErrors sources value
             @ graphErrors sources value options)
            |> enabledPlan
                value
                (includes
                 @ names)
                true
            |> boundActive
                options
                sources
                value
                ((includes
                  |> List.map (fun name -> name, [ "profiles"; "include" ]))
                 @ (names
                    |> List.map (fun name -> name, [ "profiles" ])))
                (selectorSource SourceRole.Explicit "<profiles>")
                [ "profiles" ]
                None
        | ProfileSelection.Automatic ->
            let environmentNames =
                environment
                |> Map.tryFind "MORPHIR_PROFILES"
                |> Option.map (fun value ->
                    value.Split(',')
                    |> Array.map asciiTrim
                    |> Array.toList
                )

            let baseValue = baseValue sources

            let baseNames =
                readNames "active" baseValue
                |> Option.orElseWith (fun () -> readNames "default" baseValue)
                |> Option.defaultValue []

            let names =
                match environmentNames with
                | Some [] -> []
                | selected ->
                    let includes =
                        readNames "include" baseValue
                        |> Option.defaultValue []

                    includes
                    @ (selected
                       |> Option.defaultValue baseNames)

            ((environmentNames
              |> Option.map (
                  nameErrors (selectorSource SourceRole.Environment "MORPHIR_PROFILES") [
                      "profiles"
                  ]
              )
              |> Option.defaultValue [])
             @ authorityErrors sources environment
             @ controlErrors sources baseValue
             @ graphErrors sources baseValue options)
            |> enabledPlan baseValue names true
            |> (fun plan ->
                let selected =
                    environmentNames
                    |> Option.defaultValue baseNames

                let includes =
                    readNames "include" baseValue
                    |> Option.defaultValue []

                let roots path =
                    (includes
                     |> List.map (fun name -> name, [ "profiles"; "include" ]))
                    @ (selected
                       |> List.map (fun name -> name, path))

                match environmentNames with
                | Some _ ->
                    boundActive
                        options
                        sources
                        baseValue
                        (roots [ "profiles" ])
                        (selectorSource SourceRole.Environment "MORPHIR_PROFILES")
                        [ "profiles" ]
                        None
                        plan
                | None ->
                    let path =
                        if
                            ConfigTree.tryFind [ "profiles"; "active" ] baseValue
                            |> Option.isSome
                        then
                            [ "profiles"; "active" ]
                        else
                            [ "profiles"; "default" ]

                    if
                        ConfigTree.tryFind path baseValue
                        |> Option.isSome
                    then
                        let origin = diagnostic sources path "" ""

                        boundActive
                            options
                            sources
                            baseValue
                            (roots path)
                            origin.Source
                            path
                            origin.Position
                            plan
                    else
                        boundActive
                            options
                            sources
                            baseValue
                            (roots [ "profiles" ])
                            (selectorSource SourceRole.Defaults "<profiles>")
                            [ "profiles" ]
                            None
                            plan
            )
        | ProfileSelection.Disabled -> {
            Enabled = false
            Active = []
            Diagnostics = []
          }
