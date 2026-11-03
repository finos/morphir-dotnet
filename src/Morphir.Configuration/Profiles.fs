namespace Morphir.Configuration

/// Plans profile activation from explicit inputs without reading profile files.
module Profiles =
    let private baseDocuments (sources: SourceInput list) =
        sources
        |> List.filter (fun input -> input.Source.Role = SourceRole.RootPrimary)
        |> List.sortBy (fun input -> input.Source.Format, input.Source.Order, input.Source.Path)
        |> List.choose (fun input -> match input.Content with SourceContent.Available doc -> Some doc | _ -> None)

    // Control nulls are literal metadata and must survive until control validation.
    let rec private mergeControls lower higher =
        match lower, higher with
        | ConfigValue.Object left, ConfigValue.Object right ->
            right
            |> Map.fold (fun result key value ->
                let merged = Map.tryFind key result |> Option.map (fun previous -> mergeControls previous value) |> Option.defaultValue value
                Map.add key merged result) left
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
            |> List.find (fun doc -> ConfigTree.tryFind path doc.Raw |> Option.isSome)
        { Code = code
          Message = message
          Severity = DiagnosticSeverity.Error
          Source = origin.Source
          KeyPath = path
          Position = Map.tryFind path origin.Positions }

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
                    || (input.Source.Role = SourceRole.Selected && Set.contains input.Source.Path rootPaths)
                match authorized, input.Content with
                | false, SourceContent.Available doc ->
                    let paths =
                        match ConfigTree.tryFind [ "profiles" ] doc.Raw with
                        | None -> []
                        | Some (ConfigValue.Object fields) when not fields.IsEmpty ->
                            fields |> Map.toList |> List.map (fun (key, _) -> [ "profiles"; key ])
                        | Some _ -> [ [ "profiles" ] ]
                    paths |> List.map (fun path ->
                        { Code = "config.profiles.authority"
                          Message = "Profile activation controls belong only to discovered root primary documents."
                          Severity = DiagnosticSeverity.Error
                          Source = input.Source
                          KeyPath = path
                          Position = Map.tryFind path doc.Positions })
                | _ -> [])
        let environmentErrors =
            environment
            |> Map.toList
            |> List.choose (fun (name, _) ->
                if name.StartsWith("MORPHIR_PROFILES__", System.StringComparison.Ordinal) then
                    Some
                        { Code = "config.profiles.authority"
                          Message = "Environment values cannot supply profile activation controls."
                          Severity = DiagnosticSeverity.Error
                          Source = { Path = name; Role = SourceRole.Environment; Format = DocumentFormat.Memory; Order = 0; Profile = None }
                          KeyPath = name.Substring("MORPHIR_".Length).Split("__") |> Array.map (fun segment -> segment.ToLowerInvariant()) |> Array.toList
                          Position = None }
                else None)
        fileErrors @ environmentErrors

    let private controlErrors sources value =
        match ConfigTree.tryFind [ "profiles" ] value with
        | None -> []
        | Some (ConfigValue.Object fields) ->
            let arrayErrors =
                [ "active"; "default"; "include" ]
                |> List.choose (fun key ->
                    let path = [ "profiles"; key ]
                    match ConfigTree.tryFind path value with
                    | Some (ConfigValue.Array items) when items |> List.forall (function ConfigValue.String _ -> true | _ -> false) -> None
                    | None -> None
                    | Some _ -> Some (diagnostic sources path "config.profiles.shape" "Profile controls require an array of literal strings."))
            let unknownErrors =
                fields
                |> Map.toList
                |> List.choose (fun (key, _) ->
                    if List.contains key [ "active"; "default"; "include"; "groups" ] then None
                    else Some (diagnostic sources [ "profiles"; key ] "config.profiles.unknown" ("Unknown profile control: " + key)))
            arrayErrors @ unknownErrors
        | Some _ -> [ diagnostic sources [ "profiles" ] "config.profiles.shape" "Profiles requires a table." ]

    let private readNames path value =
        ConfigTree.tryFind [ "profiles"; path ] value
        |> Option.map (function
            | ConfigValue.Array items -> items |> List.choose (function ConfigValue.String name -> Some name | _ -> None)
            | _ -> [])

    let private keepLast names = names |> List.rev |> List.distinct |> List.rev

    let private groupGraph value =
        match ConfigTree.tryFind [ "profiles"; "groups" ] value with
        | Some (ConfigValue.Object groups) ->
            groups |> Map.map (fun _ members ->
                match members with
                | ConfigValue.Array items -> items |> List.choose (function ConfigValue.String name -> Some name | _ -> None)
                | _ -> [])
        | _ -> Map.empty

    let private graphErrors sources value (options: ProfileOptions) =
        let groups = groupGraph value
        let mutable heights = Map.empty
        let rec visit stack name =
            match Map.tryFind name heights with
            | Some height -> Ok height
            | None when List.contains name stack ->
                let cycle = (stack |> List.skipWhile ((<>) name)) @ [ name ]
                Error (diagnostic sources [ "profiles"; "groups"; name ] "config.profiles.cycle" ("Profile group cycle: " + System.String.Join(" -> ", cycle)))
            | None ->
                match Map.tryFind name groups with
                | None -> Ok 0
                | Some members ->
                    let rec childrenHeight children height =
                        match children with
                        | [] -> Ok height
                        | child :: rest ->
                            match visit (stack @ [ name ]) child with
                            | Error error -> Error error
                            | Ok childHeight -> childrenHeight rest (max height (1 + childHeight))
                    childrenHeight members 1
                    |> Result.bind (fun height ->
                        if height > min 32 options.MaxDepth then
                            Error (diagnostic sources [ "profiles"; "groups"; name ] "config.profiles.depth" "Profile group graph exceeds the depth bound.")
                        else
                            heights <- Map.add name height heights
                            Ok height)
        groups
        |> Map.toList
        |> List.tryPick (fun (name, _) -> match visit [] name with Error error -> Some error | Ok _ -> None)
        |> Option.toList

    let private expand value roots =
        let mutable memo = Map.empty
        let rec append stack name =
            match Map.tryFind name memo with
            | Some expanded -> expanded
            | None when Set.contains name stack -> [ name ]
            | None ->
                let children =
                    match ConfigTree.tryFind [ "profiles"; "groups"; name ] value with
                    | Some (ConfigValue.Array members) ->
                        members
                        |> List.choose (function ConfigValue.String child -> Some child | _ -> None)
                    | _ -> []
                let expanded =
                    children
                    |> List.fold (fun output child -> keepLast (output @ append (Set.add name stack) child)) [ name ]
                memo <- Map.add name expanded memo
                expanded
        roots |> List.fold (fun output name -> keepLast (output @ append Set.empty name)) []

    let plan (sources: SourceInput list) (environment: Map<string, string>) (options: ProfileOptions) =
        match options.Selection with
        | ProfileSelection.Automatic when
            environment |> Map.tryFind "MORPHIR_PROFILES" |> Option.exists System.String.IsNullOrWhiteSpace ->
            { Enabled = false
              Active = []
              Diagnostics = [] }
        | ProfileSelection.Names names ->
            let value = baseValue sources
            let includes = value |> readNames "include" |> Option.defaultValue []
            { Enabled = true
              Active = expand value (includes @ names)
              Diagnostics = authorityErrors sources environment @ controlErrors sources value @ graphErrors sources value options }
        | ProfileSelection.Automatic ->
            let environmentNames =
                environment
                |> Map.tryFind "MORPHIR_PROFILES"
                |> Option.map (fun value ->
                    value.Split(',')
                    |> Array.map (fun name -> name.Trim())
                    |> Array.filter (fun name -> name <> "")
                    |> Array.toList)
            let baseValue = baseValue sources
            let baseNames =
                readNames "active" baseValue
                |> Option.orElseWith (fun () -> readNames "default" baseValue)
                |> Option.defaultValue []
            let names =
                match environmentNames with
                | Some [] -> []
                | selected ->
                    let includes = readNames "include" baseValue |> Option.defaultValue []
                    includes @ (selected |> Option.defaultValue baseNames)
            { Enabled = not names.IsEmpty
              Active = expand baseValue names
              Diagnostics = authorityErrors sources environment @ controlErrors sources baseValue @ graphErrors sources baseValue options }
        | ProfileSelection.Disabled ->
            { Enabled = false
              Active = []
              Diagnostics = [] }
