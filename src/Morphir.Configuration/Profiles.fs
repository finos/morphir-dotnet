namespace Morphir.Configuration

/// Plans profile activation from explicit inputs without reading profile files.
module Profiles =
    let plan (_sources: SourceInput list) (environment: Map<string, string>) (options: ProfileOptions) =
        match options.Selection with
        | ProfileSelection.Names names ->
            { Enabled = true
              Active = names
              Diagnostics = [] }
        | ProfileSelection.Automatic ->
            let names =
                environment
                |> Map.tryFind "MORPHIR_PROFILES"
                |> Option.map (fun value ->
                    value.Split(',')
                    |> Array.map (fun name -> name.Trim())
                    |> Array.filter (fun name -> name <> "")
                    |> Array.toList)
                |> Option.defaultValue []
            { Enabled = not names.IsEmpty
              Active = names
              Diagnostics = [] }
        | ProfileSelection.Disabled ->
            { Enabled = false
              Active = []
              Diagnostics = [] }
