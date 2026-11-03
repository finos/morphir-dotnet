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

    let private controlErrors sources value =
        [ "active"; "default"; "include" ]
        |> List.choose (fun key ->
            let path = [ "profiles"; key ]
            match ConfigTree.tryFind path value with
            | Some (ConfigValue.Array items) when items |> List.forall (function ConfigValue.String _ -> true | _ -> false) -> None
            | None -> None
            | Some _ -> Some (diagnostic sources path "config.profiles.shape" "Profile controls require an array of literal strings."))

    let private readNames path value =
        ConfigTree.tryFind [ "profiles"; path ] value
        |> Option.map (function
            | ConfigValue.Array items -> items |> List.choose (function ConfigValue.String name -> Some name | _ -> None)
            | _ -> [])

    let private keepLast names = names |> List.rev |> List.distinct |> List.rev

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
              Diagnostics = controlErrors sources value }
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
              Diagnostics = controlErrors sources baseValue }
        | ProfileSelection.Disabled ->
            { Enabled = false
              Active = []
              Diagnostics = [] }
