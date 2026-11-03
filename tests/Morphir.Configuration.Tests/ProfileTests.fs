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
        test "present environment selection replaces base selection" {
            let sources = [ root [ "active", names [ "base" ] ] ]
            let env = Map.ofList [ "MORPHIR_PROFILES", " postgres, metrics " ]
            let result = Profiles.plan sources env DocumentDefaults.profileOptions
            Expect.equal result.Active [ "postgres"; "metrics" ] "trimmed comma-separated environment names win"
        }
        test "root active controls replace root defaults" {
            let sources = [ root [ "active", names [ "active" ]; "default", names [ "default" ] ] ]
            let result = Profiles.plan sources Map.empty DocumentDefaults.profileOptions
            Expect.equal result.Active [ "active" ] "base active is preferred to the fallback selector"
        }
        test "base includes still activate with an explicit empty selection" {
            let sources = [ root [ "include", names [ "metrics" ] ] ]
            let options = { DocumentDefaults.profileOptions with Selection = ProfileSelection.Names [] }
            let result = Profiles.plan sources Map.empty options
            Expect.equal result.Active [ "metrics" ] "an empty explicit list permits policy includes"
        }
        test "base include activates before automatic root selection" {
            let sources = [ root [ "include", names [ "metrics" ]; "active", names [ "postgres" ] ] ]
            let result = Profiles.plan sources Map.empty DocumentDefaults.profileOptions
            Expect.equal result.Active [ "metrics"; "postgres" ] "base includes precede active roots"
        }
        test "higher null control fails typing with its winning source" {
            let sources =
                [ document SourceRole.RootPrimary DocumentFormat.Json "morphir.json" [ "active", names [ "lower" ] ]
                  root [ "active", ConfigValue.Null ] ]
            let result = Profiles.plan sources Map.empty DocumentDefaults.profileOptions
            Expect.isTrue
                (result.Diagnostics |> List.exists (fun diagnostic ->
                    diagnostic.Severity = DiagnosticSeverity.Error
                    && diagnostic.Source.Path = "morphir.toml"
                    && diagnostic.KeyPath = [ "profiles"; "active" ]))
                "a higher null does not resurrect a lower selector"
        }
        test "whitespace environment disables malformed controls and includes" {
            let sources = [ root [ "active", ConfigValue.Null; "include", names [ "metrics" ] ] ]
            let result = Profiles.plan sources (Map.ofList [ "MORPHIR_PROFILES", " \t " ]) DocumentDefaults.profileOptions
            Expect.equal (result.Enabled, result.Active, result.Diagnostics) (false, [], []) "disabled selection never interprets controls"
        }
        test "groups expand root first and keep duplicate names last" {
            let groups = ConfigValue.Object (Map.ofList [ "production", names [ "postgres"; "metrics" ] ])
            let sources = [ root [ "include", names [ "metrics" ]; "groups", groups ] ]
            let options = { DocumentDefaults.profileOptions with Selection = ProfileSelection.Names [ "production"; "postgres" ] }
            let result = Profiles.plan sources Map.empty options
            Expect.equal result.Active [ "production"; "metrics"; "postgres" ] "root first depth-first expansion uses last occurrences"
        }
        test "inactive group cycles fail with the declaring control origin" {
            let groups = ConfigValue.Object (Map.ofList [ "a", names [ "b" ]; "b", names [ "a" ] ])
            let result = Profiles.plan [ root [ "groups", groups ] ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue
                (result.Diagnostics |> List.exists (fun diagnostic ->
                    diagnostic.Code = "config.profiles.cycle"
                    && diagnostic.Source.Path = "morphir.toml"
                    && diagnostic.Message.Contains("a -> b -> a")))
                "an inactive cycle cannot be hidden by selection"
        }
        test "inactive group depth is bounded" {
            let groups =
                [ for index in 1 .. 33 ->
                    "g" + string index,
                    names (if index = 33 then [] else [ "g" + string (index + 1) ]) ]
                |> Map.ofList
                |> ConfigValue.Object
            let result = Profiles.plan [ root [ "groups", groups ] ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.depth")) "33 group levels exceed the qualified bound"
        }
        test "member controls fail before ownership filtering" {
            let source = document SourceRole.MemberPrimary DocumentFormat.Json "member/morphir.json" [ "active", names [ "metrics" ] ]
            let result = Profiles.plan [ source ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue
                (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.authority" && diagnostic.Source.Path = "member/morphir.json"))
                "non-root controls cannot change activation"
        }
        test "profile controls require a table at the root" {
            let source = root []
            let malformed = ConfigValue.Object (Map.ofList [ "profiles", ConfigValue.Null ])
            let source =
                match source.Content with
                | SourceContent.Available doc -> { source with Content = SourceContent.Available { doc with Raw = malformed; Values = malformed } }
                | _ -> failwith "test source must be available"
            let result = Profiles.plan [ source ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.shape" && diagnostic.KeyPath = [ "profiles" ])) "a null table is a literal shape error"
        }
        test "unknown profile controls are rejected" {
            let result = Profiles.plan [ root [ "typo", names [ "dev" ] ] ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.unknown" && diagnostic.KeyPath = [ "profiles"; "typo" ])) "misspelled controls must not disappear"
        }
        test "explicit names reject unsafe and interpolated spellings" {
            let options = { DocumentDefaults.profileOptions with Selection = ProfileSelection.Names [ "../secret"; "${PROFILE}"; "Upper"; "" ] }
            let result = Profiles.plan [] Map.empty options
            Expect.equal (result.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Code = "config.profiles.name") |> List.length) 4 "each supplied name is a literal lowercase ASCII identifier"
        }
        test "environment CSV rejects empty elements with variable origin" {
            let result = Profiles.plan [] (Map.ofList [ "MORPHIR_PROFILES", "dev,,metrics" ]) DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.name" && diagnostic.Source.Path = "MORPHIR_PROFILES")) "empty elements fail at the selector boundary"
        }
        test "environment trimming is ASCII only" {
            let result = Profiles.plan [] (Map.ofList [ "MORPHIR_PROFILES", "\u00a0" ]) DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.name")) "Unicode whitespace is an invalid name rather than a disable control"
        }
        test "base names are validated even below an explicit selector" {
            let sources = [ root [ "default", names [ "bad/name" ] ] ]
            let options = { DocumentDefaults.profileOptions with Selection = ProfileSelection.Names [ "dev" ] }
            let result = Profiles.plan sources Map.empty options
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.name" && diagnostic.KeyPath = [ "profiles"; "default" ] && diagnostic.Source.Path = "morphir.toml")) "all literal controls are checked before selection"
        }
        test "groups require a table of literal name arrays" {
            let groups = ConfigValue.Object (Map.ofList [ "production", ConfigValue.String "dev" ])
            let result = Profiles.plan [ root [ "groups", groups ] ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.shape" && diagnostic.KeyPath = [ "profiles"; "groups"; "production" ])) "a scalar cannot replace a group member list"
        }
        test "inactive group and member names are validated" {
            let groups = ConfigValue.Object (Map.ofList [ "Bad", names [ "../unsafe" ] ])
            let result = Profiles.plan [ root [ "groups", groups ] ] Map.empty DocumentDefaults.profileOptions
            Expect.equal (result.Diagnostics |> List.filter (fun diagnostic -> diagnostic.Code = "config.profiles.name") |> List.length) 2 "group names and their members use the same name boundary"
        }
        test "inactive group member counts are bounded" {
            let groups = ConfigValue.Object (Map.ofList [ "production", names (List.replicate 65 "dev") ])
            let result = Profiles.plan [ root [ "groups", groups ] ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.members")) "65 members exceed the 64-member declaration bound even when duplicated"
        }
        test "inactive group definition counts are bounded" {
            let groups = [ for index in 1 .. 65 -> "g" + string index, names [] ] |> Map.ofList |> ConfigValue.Object
            let result = Profiles.plan [ root [ "groups", groups ] ] Map.empty DocumentDefaults.profileOptions
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.groups")) "65 groups exceed the declaration bound"
        }
        test "distinct active names are bounded" {
            let options = { DocumentDefaults.profileOptions with Selection = ProfileSelection.Names [ for index in 1 .. 129 -> "p" + string index ] }
            let result = Profiles.plan [] Map.empty options
            Expect.isTrue (result.Diagnostics |> List.exists (fun diagnostic -> diagnostic.Code = "config.profiles.active" && diagnostic.Source.Role = SourceRole.Explicit)) "129 distinct names exceed the active bound"
        }
    ]
