namespace Morphir.Configuration

/// Plans profile activation from explicit inputs without reading profile files.
module Profiles =
    let private baseValue (sources: SourceInput list) =
        sources
        |> List.filter (fun input -> input.Source.Role = SourceRole.RootPrimary)
        |> List.sortBy (fun input -> input.Source.Format, input.Source.Order, input.Source.Path)
        |> List.choose (fun input -> match input.Content with SourceContent.Available doc -> Some doc.Raw | _ -> None)
        |> List.fold ValueMerge.merge DocumentDefaults.emptyValue

    let private readNames path value =
        ConfigTree.tryFind [ "profiles"; path ] value
        |> Option.map (function
            | ConfigValue.Array items -> items |> List.choose (function ConfigValue.String name -> Some name | _ -> None)
            | _ -> [])

    let plan (sources: SourceInput list) (environment: Map<string, string>) (options: ProfileOptions) =
        match options.Selection with
        | ProfileSelection.Names names ->
            let includes = baseValue sources |> readNames "include" |> Option.defaultValue []
            { Enabled = true
              Active = includes @ names
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
              Active = names
              Diagnostics = [] }
        | ProfileSelection.Disabled ->
            { Enabled = false
              Active = []
              Diagnostics = [] }
