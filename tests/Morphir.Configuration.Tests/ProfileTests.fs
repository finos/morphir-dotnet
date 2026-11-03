module Morphir.Configuration.Tests.ProfileTests

open Expecto
open Morphir.Configuration

let private names values = values |> List.map ConfigValue.String |> ConfigValue.Array

let private document role format path controls =
    let source =
        { Path = path
          Role = role
          Format = format
          Order = 0
          Profile = None }

    let values = ConfigValue.Object (Map.ofList [ "profiles", ConfigValue.Object (Map.ofList controls) ])

    { Source = source
      Required = false
      Content =
        SourceContent.Available
            { Source = source
              Raw = values
              Values = values
              Positions = Map.empty
              Diagnostics = []
              LegacyFields = [] } }

let private root controls = document SourceRole.RootPrimary DocumentFormat.Toml "morphir.toml" controls

[<Tests>]
let tests =
    testList "Profiles" [
        test "explicit selection replaces environment and base selection" {
            let sources = [ root [ "active", names [ "base" ] ] ]
            let env = Map.ofList [ "MORPHIR_PROFILES", "postgres" ]
            let options = { DocumentDefaults.profileOptions with Selection = ProfileSelection.Names [ "metrics" ] }
            let result = Profiles.plan sources env options
            Expect.equal result.Active [ "metrics" ] "the explicit host selector has authority"
        }
    ]
