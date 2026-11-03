module Morphir.Configuration.Tests.ResolutionTests

open Expecto
open Morphir.Configuration

let obj fields = ConfigValue.Object(Map.ofList fields)
let text value = ConfigValue.String value

let source path role format = {
    Path = path
    Role = role
    Format = format
    Order = 0
    Profile = None
}

let supplied identity value = {
    Source = identity
    Required = false
    Content =
        SourceContent.Available
            {
                Source = identity
                Raw = value
                Values = value
                Positions = Map.empty
                Diagnostics = []
                LegacyFields = []
            }
}

[<Tests>]
let tests =
    testList "Resolution" [
        test "format order merges keys and reports the winning source" {
            let json = source "morphir.json" SourceRole.RootPrimary DocumentFormat.Json
            let toml = source "morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = [
                        supplied toml (obj [ "tool", obj [ "path", text "toml" ] ])
                        supplied
                            json
                            (obj [ "tool", obj [ "path", text "json"; "name", text "kept" ] ])
                    ]
                }

            let result = Resolver.resolve input

            Expect.equal
                (ConfigTree.tryFind [ "tool"; "path" ] result.Effective)
                (Some(text "toml"))
                "TOML wins"

            Expect.equal
                (ConfigTree.tryFind [ "tool"; "name" ] result.Effective)
                (Some(text "kept"))
                "JSON-only key survives"

            Expect.equal result.Origins.[[ "tool"; "path" ]].Source toml "origin follows winner"
        }
        test "runtime overrides beat every ordinary source boundary" {
            let roles = [
                SourceRole.Defaults
                SourceRole.System
                SourceRole.Global
                SourceRole.RootPrimary
                SourceRole.Selected
                SourceRole.MemberPrimary
                SourceRole.UserOverride
            ]

            let sources =
                roles
                |> List.map (fun role ->
                    supplied
                        (source (string role) role DocumentFormat.Memory)
                        (obj [ "tool", text (string role) ])
                )

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = List.rev sources
                        Environment = Map.ofList [ "MORPHIR_TOOL", "environment" ]
                        Explicit = obj [ "tool", text "cli" ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "tool" ] result.Effective)
                (Some(text "cli"))
                "explicit wins"

            Expect.equal
                result.Origins.[[ "tool" ]].Source.Role
                SourceRole.Explicit
                "explicit origin"
        }
        test "selected discovered JSON contributes only once after TOML" {
            let json = source "morphir.json" SourceRole.RootPrimary DocumentFormat.Json
            let toml = source "morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml
            let value = obj [ "tool", text "json" ]

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied json value
                            supplied toml (obj [ "tool", text "toml"; "extra", text "kept" ])
                            supplied { json with Role = SourceRole.Selected } value
                        ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "tool" ] result.Effective)
                (Some(text "json"))
                "selected JSON wins"

            let contributions =
                result.Sources
                |> List.filter (fun report ->
                    report.Source.Path = json.Path
                    && report.Contributed
                )

            Expect.equal contributions.Length 1 "one ordinary contribution"
        }
        test "member identity never inherits root or machine ownership subtrees" {
            let project name =
                obj [
                    "project", obj [ "name", text name; "future", text "private" ]
                    "workspace", obj [ "root", text name; "future", text "private" ]
                    "tool", obj [ "shared", text name ]
                ]

            let memberSource = source "member.toml" SourceRole.MemberPrimary DocumentFormat.Toml

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Scope = ProjectScope.Member
                        OwningWorkspace =
                            Some
                                {
                                    Root = "/root"
                                    OutputDirectory = Some "out"
                                }
                        Sources = [
                            supplied
                                (source "global" SourceRole.Global DocumentFormat.Toml)
                                (project "machine")
                            supplied
                                (source "root" SourceRole.RootPrimary DocumentFormat.Toml)
                                (project "root")
                            supplied
                                memberSource
                                (obj [
                                    "project", obj [ "name", text "member" ]
                                    "workspace", obj [ "out_dir", text "bad" ]
                                ])
                        ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "project"; "name" ] result.Effective)
                (Some(text "member"))
                "member identity"

            Expect.isNone
                (ConfigTree.tryFind [ "project"; "future" ] result.Effective)
                "root future metadata excluded"

            Expect.isNone
                (ConfigTree.tryFind [ "workspace"; "future" ] result.Effective)
                "entire subtree excluded"

            Expect.equal
                (ConfigTree.tryFind [ "project"; "source_directory" ] result.Effective)
                (Some(text "src"))
                "member default source"

            Expect.equal
                result.OwningWorkspace.Value.OutputDirectory
                (Some "out")
                "owning workspace unchanged"

            Expect.isTrue
                (result.Diagnostics
                 |> List.exists (fun d ->
                     d.Code = "config.member_output"
                     && d.Source = memberSource
                 ))
                "relocation diagnosed"
        }
        test "environment nesting preserves underscores and shorter paths win deterministically" {
            let environment =
                Map.ofList [
                    "MORPHIR_IR__FORMAT_VERSION", "4"
                    "MORPHIR_TOOL__STRICT_MODE", "true"
                    "MORPHIR_TOOL", "opaque"
                    "MORPHIR_HOME", "/private"
                    "MORPHIR_ITEMS", "[1,true,{\"x\":2}]"
                ]

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Environment = environment
                    }

            Expect.equal
                (ConfigTree.tryFind [ "ir"; "format_version" ] result.Effective)
                (Some(ConfigValue.Integer 4L))
                "typed version"

            Expect.equal
                (ConfigTree.tryFind [ "tool" ] result.Effective)
                (Some(text "opaque"))
                "shorter path wins"

            Expect.isNone
                (ConfigTree.tryFind [ "home" ] result.Effective)
                "operational variable excluded"

            Expect.equal
                (ConfigTree.tryFind [ "items" ] result.Effective)
                (Some(
                    ConfigValue.Array [
                        ConfigValue.Integer 1L
                        ConfigValue.Boolean true
                        obj [ "x", ConfigValue.Integer 2L ]
                    ]
                ))
                "structured environment values"
        }
        test "environment secret references replace lower objects atomically" {
            let lower = source "morphir.json" SourceRole.RootPrimary DocumentFormat.Json
            let secret = ConfigValue.SecretReference(Map.ofList [ "env", text "TOKEN" ])

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied
                                lower
                                (obj [
                                    "custom", obj [ "credential", obj [ "stale", text "value" ] ]
                                ])
                        ]
                        Environment =
                            Map.ofList [ "MORPHIR_CUSTOM__CREDENTIAL", "{\"env\":\"TOKEN\"}" ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "custom"; "credential" ] result.Effective)
                (Some secret)
                "the environment reference retains its type and replaces every lower field"

            Expect.equal
                result.Origins.[[ "custom"; "credential" ]].Source.Role
                SourceRole.Environment
                "the reference is an environment-owned leaf"
        }
        test "logging file remains an ordinary setting when levels are overlaid" {
            let lower = source "logging.json" SourceRole.RootPrimary DocumentFormat.Json
            let raw = obj [ "logging", obj [ "file", text "app.log" ] ]
            let document = Normalization.normalize lower raw Map.empty

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            {
                                Source = lower
                                Required = false
                                Content = SourceContent.Available document
                            }
                        ]
                        Environment = Map.ofList [ "MORPHIR_LOGGING", "{\"level\":\"debug\"}" ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "logging"; "file" ] result.Effective)
                (Some(text "app.log"))
                "logging.file is an output path and survives the ordinary map overlay"
        }
        test "empty arrays and shape changes erase stale winning children" {
            let lower = source "lower" SourceRole.RootPrimary DocumentFormat.Json
            let higher = source "higher" SourceRole.RootPrimary DocumentFormat.Toml

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied
                                lower
                                (obj [
                                    "items", ConfigValue.Array [ text "a"; text "b" ]
                                    "shape", obj [ "old", text "gone" ]
                                ])
                            supplied
                                higher
                                (obj [ "items", ConfigValue.Array []; "shape", text "scalar" ])
                        ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "items" ] result.Effective)
                (Some(ConfigValue.Array []))
                "empty replaces all"

            Expect.isFalse
                (Map.containsKey [ "shape"; "old" ] result.Origins)
                "old shape origins removed"

            Expect.equal result.Origins.[[ "items" ]].Source higher "array has one winning origin"
        }
        test "null overlay keeps lower origin and arrays clean nested object nulls" {
            let lower = source "lower" SourceRole.RootPrimary DocumentFormat.Json

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied lower (obj [ "a", ConfigValue.Integer 1L ])
                            supplied
                                (source "higher" SourceRole.RootPrimary DocumentFormat.Toml)
                                (obj [
                                    "a", ConfigValue.Null
                                    "absent", ConfigValue.Null
                                    "items",
                                    ConfigValue.Array [
                                        obj [ "x", ConfigValue.Null ]
                                        ConfigValue.Null
                                    ]
                                ])
                        ]
                    }

            Expect.equal result.Origins.[[ "a" ]].Source lower "null overlay retains origin"

            Expect.isNone
                (ConfigTree.tryFind [ "absent" ] result.Effective)
                "null object fields absent"

            Expect.equal
                (ConfigTree.tryFind [ "items" ] result.Effective)
                (Some(ConfigValue.Array [ obj []; ConfigValue.Null ]))
                "null array elements retained"
        }
        test "secret reference shape is an atomic leaf" {
            let lower = source "lower" SourceRole.RootPrimary DocumentFormat.Json
            let higher = source "higher" SourceRole.RootPrimary DocumentFormat.Toml
            let secret = ConfigValue.SecretReference(Map.ofList [ "env", text "TOKEN" ])

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied lower (obj [ "credential", obj [ "other", text "gone" ] ])
                            supplied higher (obj [ "credential", secret ])
                        ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "credential" ] result.Effective)
                (Some secret)
                "reference retained without resolution"

            Expect.isFalse
                (Map.containsKey [ "credential"; "other" ] result.Origins)
                "child origin removed"

            Expect.equal result.Origins.[[ "credential" ]].Source higher "reference origin"
        }
        test "optional misses are reported and selected misses always error" {
            let optional = source "optional.toml" SourceRole.Global DocumentFormat.Toml
            let selected = source "selected.json" SourceRole.Selected DocumentFormat.Json

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            {
                                Source = optional
                                Content = SourceContent.Missing
                                Required = false
                            }
                            {
                                Source = selected
                                Content = SourceContent.Missing
                                Required = false
                            }
                        ]
                    }

            Expect.equal
                (result.Sources
                 |> List.filter (fun r -> r.Status = SourceStatus.Missing)
                 |> List.length)
                2
                "consulted misses retained"

            Expect.equal
                (result.Diagnostics
                 |> List.filter (fun d -> d.Severity = DiagnosticSeverity.Error)
                 |> List.map (fun d -> d.Source.Path))
                [ selected.Path ]
                "selected cannot be optional"
        }
        test "known values are validated at their winning source and position" {
            let identity = source "wrong.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let raw =
                obj [ "ir", obj [ "format_version", text "4" ]; "extra", ConfigValue.Boolean true ]

            let original = supplied identity raw

            let positioned =
                match original.Content with
                | SourceContent.Available document ->
                    { original with
                        Content =
                            SourceContent.Available
                                { document with
                                    Positions =
                                        Map.ofList [
                                            [ "ir"; "format_version" ], { Line = 3; Column = 2 }
                                        ]
                                }
                    }
                | _ -> original

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [ positioned ]
                    }

            let errors =
                result.Diagnostics
                |> List.filter (fun d -> d.Severity = DiagnosticSeverity.Error)

            Expect.isTrue
                (errors
                 |> List.exists (fun d ->
                     d.Source = identity
                     && d.KeyPath = [ "ir"; "format_version" ]
                     && d.Position = Some { Line = 3; Column = 2 }
                 ))
                "typed source diagnostic"

            Expect.equal
                (ConfigTree.tryFind [ "extra" ] result.Effective)
                (Some(ConfigValue.Boolean true))
                "unknown values retained"
        }
        test "null-only replacement objects keep provenance and fail known scalar validation" {
            let identity = source "null-shape.json" SourceRole.RootPrimary DocumentFormat.Json
            let path = [ "ir"; "format_version" ]

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied
                                identity
                                (obj [
                                    "ir", obj [ "format_version", obj [ "x", ConfigValue.Null ] ]
                                ])
                        ]
                    }

            Expect.equal
                (ConfigTree.tryFind path result.Effective)
                (Some(obj []))
                "the object replaces the default integer"

            Expect.isTrue
                (result.Diagnostics
                 |> List.exists (fun d ->
                     d.Code = "config.validation"
                     && d.KeyPath = path
                     && d.Source = identity
                 ))
                "the invalid object is diagnosed at its replacing source"
        }
        test "populated objects discard stale empty-object provenance" {
            let lower = source "empty.json" SourceRole.RootPrimary DocumentFormat.Json
            let higher = source "populated.toml" SourceRole.RootPrimary DocumentFormat.Toml
            let path = [ "tasks"; "bad" ]

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied lower (obj [ "tasks", obj [ "bad", obj [] ] ])
                            supplied
                                higher
                                (obj [
                                    "tasks",
                                    obj [
                                        "bad",
                                        obj [
                                            "run", text "echo"
                                            "cmd", ConfigValue.Array [ text "echo" ]
                                        ]
                                    ]
                                ])
                        ]
                    }

            Expect.isTrue
                (result.Diagnostics
                 |> List.exists (fun d ->
                     d.Code = "config.validation"
                     && d.KeyPath = path
                     && d.Source = higher
                 ))
                "the conflicting task fields are diagnosed at their owning source"

            Expect.isFalse
                (Map.containsKey path result.Origins)
                "a nonempty map has child origins instead of an obsolete leaf origin"
        }
        test "schema validates Elm extension whitespace patterns" {
            let identity = source "extension.toml" SourceRole.RootPrimary DocumentFormat.Toml
            let path = [ "frontend"; "elm"; "extension" ]

            for extension, invalid in [ " ", true; " .elm", true; ".elm ", true; ".elm", false ] do
                let result =
                    Resolver.resolve
                        { DocumentDefaults.resolutionInput with
                            Sources = [
                                supplied
                                    identity
                                    (obj [
                                        "frontend",
                                        obj [ "elm", obj [ "extension", text extension ] ]
                                    ])
                            ]
                        }

                Expect.equal
                    (result.Diagnostics
                     |> List.exists (fun d ->
                         d.Code = "config.validation"
                         && d.KeyPath = path
                         && d.Source = identity
                     ))
                    invalid
                    ("the pinned schema pattern applies to '"
                     + extension
                     + "'")
        }
        test "schema validates nested tasks and finite bounded known numbers without execution" {
            let raw =
                obj [
                    "tasks",
                    obj [
                        "bad",
                        obj [
                            "run", text "do-not-execute"
                            "cmd", ConfigValue.Array [ text "also-not-executed" ]
                        ]
                    ]
                    "ir", obj [ "format_version", ConfigValue.Integer 11L ]
                    "logging", obj [ "level", text "trace" ]
                ]

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied
                                (source "schema.json" SourceRole.RootPrimary DocumentFormat.Json)
                                raw
                        ]
                    }

            for path in [ [ "tasks"; "bad" ]; [ "ir"; "format_version" ]; [ "logging"; "level" ] ] do
                Expect.isTrue
                    (result.Diagnostics
                     |> List.exists (fun d ->
                         d.KeyPath = path
                         && d.Severity = DiagnosticSeverity.Error
                     ))
                    ("schema rejects "
                     + String.concat "." path)
        }
        test "mixed format warnings resist workspace suppression and show surviving JSON keys" {
            let json = source "morphir.json" SourceRole.RootPrimary DocumentFormat.Json
            let yaml = source "morphir.yaml" SourceRole.RootPrimary DocumentFormat.Yaml
            let toml = source "morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let sources = [
                supplied json (obj [ "json_only", text "kept" ])
                supplied yaml (obj [ "tool", text "yaml" ])
                supplied
                    toml
                    (obj [
                        "tool", text "toml"
                        "diagnostics",
                        obj [
                            "suppress",
                            ConfigValue.Array [
                                text "config.yaml_beside_toml"
                                text "config.json_keys_below_toml"
                            ]
                        ]
                    ])
            ]

            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = sources
                }

            let result = Resolver.resolve input

            Expect.isTrue
                (result.Diagnostics
                 |> List.exists (fun d ->
                     d.Code = "config.yaml_beside_toml"
                     && d.Source = yaml
                 ))
                "workspace cannot suppress YAML warning"

            Expect.isTrue
                (result.Diagnostics
                 |> List.exists (fun d ->
                     d.Code = "config.json_keys_below_toml"
                     && d.Source = json
                     && d.KeyPath = [ "json_only" ]
                 ))
                "JSON survivor origin named"

            let quiet =
                Resolver.resolve
                    { input with
                        Environment =
                            Map.ofList [
                                "MORPHIR_DIAGNOSTICS__SUPPRESS",
                                "config.yaml_beside_toml,config.json_keys_below_toml,unknown"
                            ]
                    }

            Expect.isFalse
                (quiet.Diagnostics
                 |> List.exists (fun d ->
                     d.Code = "config.yaml_beside_toml"
                     || d.Code = "config.json_keys_below_toml"
                 ))
                "host env suppression authorized"

            Expect.isTrue
                (quiet.Diagnostics
                 |> List.exists (fun d -> d.Code = "config.unknown_suppression"))
                "unknown code warns"
        }
        test "profile source order advances name after workspace and member formats" {
            let profile path role name value =
                supplied
                    { source path role DocumentFormat.Toml with
                        Profile = Some name
                    }
                    (obj [ "tool", text value ])

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Profiles =
                            { DocumentDefaults.profileOptions with
                                Selection = ProfileSelection.Names [ "postgres"; "metrics" ]
                            }
                        Sources = [
                            profile
                                "member-pg"
                                SourceRole.MemberProfile
                                "postgres"
                                "member postgres"
                            profile
                                "workspace-metrics"
                                SourceRole.WorkspaceProfile
                                "metrics"
                                "workspace metrics"
                        ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "tool" ] result.Effective)
                (Some(text "workspace metrics"))
                "later name's workspace beats earlier member"

            Expect.equal
                result.Origins.[[ "tool" ]].Source.Path
                "workspace-metrics"
                "winning profile path"
        }
        test "host defaults and legacy defaults have separate priorities below machine sources" {
            let defaults =
                supplied
                    (source "host-defaults.toml" SourceRole.Defaults DocumentFormat.Toml)
                    (obj [
                        "ir", obj [ "format_version", ConfigValue.Integer 3L ]
                        "pipeline", obj [ "backend", text "host-default" ]
                    ])

            let classicSource = source "classic.json" SourceRole.RootPrimary DocumentFormat.Json

            let classic =
                supplied classicSource (obj [ "project", obj [ "name", text "Classic" ] ])

            let classic =
                match classic.Content with
                | SourceContent.Available doc ->
                    { classic with
                        Content =
                            SourceContent.Available
                                { doc with
                                    LegacyFields = [
                                        {
                                            RawPath = []
                                            CanonicalPath = Some [ "pipeline"; "backend" ]
                                            Value = text "checkpoint"
                                            Disposition = "default"
                                        }
                                    ]
                                }
                    }
                | _ -> classic

            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = [ classic; defaults ]
                }

            let result = Resolver.resolve input

            Expect.equal
                (ConfigTree.tryFind [ "ir"; "format_version" ] result.Effective)
                (Some(ConfigValue.Integer 3L))
                "host defaults beat builtins"

            Expect.equal
                (ConfigTree.tryFind [ "pipeline"; "backend" ] result.Effective)
                (Some(text "checkpoint"))
                "legacy derived defaults beat host fallback"

            let machine =
                supplied
                    (source "global" SourceRole.Global DocumentFormat.Toml)
                    (obj [ "pipeline", obj [ "backend", text "custom" ] ])

            let result =
                Resolver.resolve
                    { input with
                        Sources =
                            machine
                            :: input.Sources
                    }

            Expect.equal
                (ConfigTree.tryFind [ "pipeline"; "backend" ] result.Effective)
                (Some(text "custom"))
                "global beats legacy fallback"

            let selected =
                match classic.Content with
                | SourceContent.Available document ->
                    let identity =
                        { classicSource with
                            Role = SourceRole.Selected
                        }

                    { classic with
                        Source = identity
                        Content = SourceContent.Available { document with Source = identity }
                    }
                | _ -> classic

            let result =
                Resolver.resolve
                    { input with
                        Sources = [ classic; selected ]
                    }

            Expect.equal
                (ConfigTree.tryFind [ "pipeline"; "backend" ] result.Effective)
                (Some(text "checkpoint"))
                "selecting classic retains derived default contribution"

            let result = Resolver.resolve { input with Sources = [ selected ] }

            Expect.equal
                (ConfigTree.tryFind [ "pipeline"; "backend" ] result.Effective)
                (Some(text "checkpoint"))
                "explicit classic file also retains defaults"
        }
    ]

[<Tests>]
let exactIntegerValidation =
    testCase
        "Resolution validates unbounded schema integers without floating coercion"
        (fun () ->
            let huge = System.Numerics.BigInteger.Pow(10I, 400)
            let values = obj [ "cache", obj [ "max_size", ConfigValue.BigInteger huge ] ]

            let document =
                supplied (source "large.json5" SourceRole.RootPrimary DocumentFormat.Json) values

            let result =
                Resolver.resolve
                    { DocumentDefaults.resolutionInput with
                        Sources = [ document ]
                    }

            Expect.isFalse
                (result.Diagnostics
                 |> List.exists (fun diagnostic -> diagnostic.Severity = DiagnosticSeverity.Error))
                "An integer with only a minimum bound must remain exact, even beyond the double range"
        )

[<Tests>]
let boundaryTests =
    let roles = [
        SourceRole.Defaults
        SourceRole.System
        SourceRole.Global
        SourceRole.RootPrimary
        SourceRole.Selected
        SourceRole.MemberPrimary
        SourceRole.WorkspaceProfile
        SourceRole.MemberProfile
        SourceRole.UserOverride
        SourceRole.Environment
        SourceRole.Explicit
    ]

    roles
    |> List.pairwise
    |> List.map (fun (lower, higher) ->
        testCase
            (string lower
             + " before "
             + string higher)
            (fun () ->
                let document role =
                    let identity =
                        { source (string role) role DocumentFormat.Memory with
                            Profile = Some "active"
                        }

                    supplied identity (obj [ "boundary", text (string role) ])

                let result =
                    Resolver.resolve
                        { DocumentDefaults.resolutionInput with
                            Profiles =
                                { DocumentDefaults.profileOptions with
                                    Selection = ProfileSelection.Names [ "active" ]
                                }
                            Sources = [ document higher; document lower ]
                        }

                Expect.equal
                    (ConfigTree.tryFind [ "boundary" ] result.Effective)
                    (Some(text (string higher)))
                    "each ladder boundary holds"
            )
    )
    |> testList "Resolution boundaries"
