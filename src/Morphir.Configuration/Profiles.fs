namespace Morphir.Configuration

/// Plans profile activation from explicit inputs without reading profile files.
module Profiles =
    let plan (sources: SourceInput list) (environment: Map<string, string>) (options: ProfileOptions) =
        match options.Selection with
        | ProfileSelection.Names names ->
            { Enabled = true
              Active = names
              Diagnostics = [] }
        | ProfileSelection.Automatic ->
            let environmentNames =
                environment
                |> Map.tryFind "MORPHIR_PROFILES"
                |> Option.map (fun value ->
                    value.Split(',')
                    |> Array.map (fun name -> name.Trim())
                    |> Array.filter (fun name -> name <> "")
                    |> Array.toList)
            let baseValue =
                sources
                |> List.filter (fun input -> input.Source.Role = SourceRole.RootPrimary)
                |> List.sortBy (fun input -> input.Source.Format, input.Source.Order, input.Source.Path)
                |> List.choose (fun input -> match input.Content with SourceContent.Available doc -> Some doc.Raw | _ -> None)
                |> List.fold ValueMerge.merge DocumentDefaults.emptyValue
            let baseNames =
                ConfigTree.tryFind [ "profiles"; "active" ] baseValue
                |> Option.orElseWith (fun () -> ConfigTree.tryFind [ "profiles"; "default" ] baseValue)
                |> Option.map (function
                    | ConfigValue.Array items -> items |> List.choose (function ConfigValue.String name -> Some name | _ -> None)
                    | _ -> [])
                |> Option.defaultValue []
            let names = environmentNames |> Option.defaultValue baseNames
            { Enabled = not names.IsEmpty
              Active = names
              Diagnostics = [] }
        | ProfileSelection.Disabled ->
            { Enabled = false
              Active = []
              Diagnostics = [] }
