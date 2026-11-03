namespace Morphir.Configuration

/// Plans profile activation from explicit inputs without reading profile files.
module Profiles =
    let plan (_sources: SourceInput list) (_environment: Map<string, string>) (options: ProfileOptions) =
        match options.Selection with
        | ProfileSelection.Names names ->
            { Enabled = true
              Active = names
              Diagnostics = [] }
        | ProfileSelection.Automatic
        | ProfileSelection.Disabled ->
            { Enabled = false
              Active = []
              Diagnostics = [] }
