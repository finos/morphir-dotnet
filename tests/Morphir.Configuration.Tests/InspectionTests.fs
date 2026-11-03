module Morphir.Configuration.Tests.InspectionTests

open Expecto
open Morphir.Configuration
open Morphir.Configuration.Tests.ResolutionTests

[<Tests>]
let tests =
    testList "Inspection" [
        test "scoped queries do not fill misses from effective configuration" {
            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied
                                (source "global.toml" SourceRole.Global DocumentFormat.Toml)
                                (obj [ "custom", text "global" ])
                        ]
                    }

            Expect.equal
                (Inspection.get QueryScope.Effective [ "custom" ] details).Value
                (Some(text "global"))
                "effective value"

            Expect.isFalse
                (Inspection.get QueryScope.Workspace [ "custom" ] details).Found
                "scoped miss"
        }
        test "quoted key paths roundtrip escaped literal segments" {
            let segments = [ "codegen"; "custom.generator"; "a\"b\\c\n"; "" ]
            Expect.equal (KeyPath.parse (KeyPath.format segments)) (Ok segments) "JSON escaping"

            Expect.equal
                (KeyPath.parse "codegen.\"custom.generator\".enabled")
                (Ok [ "codegen"; "custom.generator"; "enabled" ])
                "literal dot"

            for malformed in
                [ ""; ".x"; "x."; "x..y"; "x[0]"; "x y"; "\"unterminated"; "\"a\"b"; "\"\\q\"" ] do
                Expect.isError (KeyPath.parse malformed) "invalid path"
        }
        test "shared inspection redacts secret references and sensitive containers recursively" {
            let original =
                obj [
                    "custom",
                    obj [
                        "password", text "do-not-print"
                        "apiKey", text "key-value"
                        "records", ConfigValue.Array [ obj [ "token", text "nested-token" ] ]
                        "reference",
                        ConfigValue.SecretReference(
                            Map.ofList [ "command", ConfigValue.Array [ text "never-execute" ] ]
                        )
                        "safe", text "visible"
                    ]
                ]

            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Explicit = original
                    }

            let result = Inspection.get QueryScope.Effective [ "custom" ] details
            let safe = result.Value.Value

            Expect.equal
                (ConfigTree.tryFind [ "password" ] safe)
                (Some(text "[REDACTED]"))
                "password"

            Expect.equal
                (ConfigTree.tryFind [ "apiKey" ] safe)
                (Some(text "[REDACTED]"))
                "camel key"

            Expect.equal
                (ConfigTree.tryFind [ "reference" ] safe)
                (Some(text "[REDACTED]"))
                "opaque reference"

            Expect.equal
                (ConfigTree.tryFind [ "records" ] safe)
                (Some(ConfigValue.Array [ obj [ "token", text "[REDACTED]" ] ]))
                "array child"

            Expect.equal (ConfigTree.tryFind [ "safe" ] safe) (Some(text "visible")) "safe value"

            Expect.equal
                (ConfigTree.tryFind [ "custom"; "password" ] details.Resolution.Effective)
                (Some(text "do-not-print"))
                "canonical unchanged"
        }
        test "list preserves empty values and whole arrays in canonical sorted entries" {
            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            supplied
                                (source "test.toml" SourceRole.RootPrimary DocumentFormat.Toml)
                                (obj [
                                    "z", text ""
                                    "a", ConfigValue.Array []
                                    "empty", obj []
                                    "tree", obj [ "q.r", text "nested" ]
                                ])
                        ]
                    }

            let result = Inspection.list QueryScope.Project details

            Expect.equal
                (result.Entries
                 |> List.map (fun entry -> entry.Key))
                [ "a"; "empty"; "tree.\"q.r\""; "z" ]
                "ordered atomic leaves"

            Expect.isTrue
                (Inspection.get QueryScope.Project [ "a" ] details).Found
                "empty array present"

            Expect.isTrue
                (Inspection.get QueryScope.Project [ "z" ] details).Found
                "empty string present"

            Expect.isFalse (Inspection.get QueryScope.Project [ "missing" ] details).Found "absent"

            Expect.isFalse
                (Inspection.get QueryScope.Project [ "a"; "0" ] details).Found
                "no array indexing"

            Expect.equal
                (Inspection.show QueryScope.Project details).Value
                (Some(
                    (details.Layers
                     |> List.find (fun layer -> layer.Document.Source.Role = SourceRole.RootPrimary))
                        .Document.Values
                ))
                "nested tree"

            Expect.equal
                (result.Entries.Head.Origins.Head.Origin.Source.Path)
                "test.toml"
                "array origin"
        }
        test "path reports retain ordered loaded missing and failed sources" {
            let missing = {
                Source = source "missing.toml" SourceRole.Global DocumentFormat.Toml
                Content = SourceContent.Missing
                Required = false
            }

            let failed =
                { missing with
                    Source = source "bad.toml" SourceRole.System DocumentFormat.Toml
                    Content = SourceContent.Invalid []
                }

            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Sources = [ missing; failed ]
                    }

            let reports = (Inspection.path QueryScope.Effective details).Sources

            Expect.equal
                (reports
                 |> List.map (fun report -> report.Status))
                [
                    SourceStatus.Loaded
                    SourceStatus.Failed
                    SourceStatus.Missing
                    SourceStatus.Loaded
                ]
                "resolution order"

            Expect.equal
                ((Inspection.path QueryScope.Global details).Sources
                 |> List.map (fun report -> report.Source.Path))
                [ "missing.toml" ]
                "scoped sources"
        }
        test "explain retains replaced contributions and identifies member winner" {
            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Scope = ProjectScope.Member
                        Sources = [
                            supplied
                                (source "root.toml" SourceRole.RootPrimary DocumentFormat.Toml)
                                (obj [
                                    "custom",
                                    obj [ "items", ConfigValue.Array [ text "one"; text "two" ] ]
                                ])
                            supplied
                                (source "member.toml" SourceRole.MemberPrimary DocumentFormat.Toml)
                                (obj [ "custom", obj [ "items", ConfigValue.Array [] ] ])
                        ]
                    }

            let result = Inspection.explain QueryScope.Effective [ "custom"; "items" ] details

            Expect.equal
                (result.Contributions
                 |> List.map (fun item -> item.Source.Path, item.Winner))
                [ "root.toml", false; "member.toml", true ]
                "source history"

            Expect.equal result.Value (Some(ConfigValue.Array [])) "complete replacement"
        }
        test "scoped source values precede inheritance filters and never add defaults" {
            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Scope = ProjectScope.Member
                        OwningWorkspace =
                            Some
                                {
                                    Root = "/repo"
                                    OutputDirectory = None
                                }
                        Sources = [
                            supplied
                                (source "root.toml" SourceRole.RootPrimary DocumentFormat.Toml)
                                (obj [
                                    "project", obj [ "name", text "root" ]
                                    "custom", text "root"
                                ])
                            supplied
                                (source "member.toml" SourceRole.MemberPrimary DocumentFormat.Toml)
                                (obj [ "project", obj [ "name", text "member" ] ])
                        ]
                    }

            Expect.equal
                (Inspection.get QueryScope.Workspace [ "project"; "name" ] details).Value
                (Some(text "root"))
                "inspect workspace own document"

            Expect.equal
                (Inspection.get QueryScope.Project [ "project"; "name" ] details).Value
                (Some(text "member"))
                "member identity"

            Expect.isFalse
                (Inspection.get QueryScope.Project [ "project"; "source_directory" ] details)
                    .Found
                "no defaults"

            Expect.equal
                (Inspection.get (QueryScope.File "root.toml") [ "custom" ] details).Value
                (Some(text "root"))
                "file scope"

            Expect.isFalse
                (Inspection.get (QueryScope.File "other.toml") [ "custom" ] details).Found
                "file miss"
        }
        test "selected personal files retain their inspection scope" {
            let personal =
                source "morphir.user.toml" SourceRole.UserOverride DocumentFormat.Toml

            let selected =
                { personal with
                    Role = SourceRole.Selected
                }

            let values = obj [ "custom", text "personal" ]

            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Sources = [ supplied personal values; supplied selected values ]
                    }

            Expect.equal
                (Inspection.get QueryScope.User [ "custom" ] details).Value
                (Some(text "personal"))
                "selected ownership"

            Expect.equal
                ((Inspection.path QueryScope.User details).Sources
                 |> List.filter (fun report -> report.Status = SourceStatus.Loaded)
                 |> List.length)
                1
                "selected contribution only once"
        }
        test "explanation distinguishes a blocked descendant and surviving composite origins" {
            let lower =
                supplied
                    (source "lower.toml" SourceRole.RootPrimary DocumentFormat.Toml)
                    (obj [ "custom", obj [ "left", text "one"; "right", text "two" ] ])

            let higher =
                supplied
                    (source "higher.toml" SourceRole.UserOverride DocumentFormat.Toml)
                    (obj [ "custom", text "blocked" ])

            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Sources = [ lower; higher ]
                    }

            let result = Inspection.explain QueryScope.Effective [ "custom"; "left" ] details
            Expect.isFalse result.Found "scalar blocks descendant"

            Expect.equal
                (result.Contributions
                 |> List.map (fun item -> item.Value, item.Winner))
                [ Some(text "one"), false; None, false ]
                "blocking contribution"

            let composite =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            lower
                            { higher with
                                Content =
                                    (supplied
                                        higher.Source
                                        (obj [ "custom", obj [ "left", text "changed" ] ]))
                                        .Content
                            }
                        ]
                    }

            Expect.equal
                ((Inspection.explain QueryScope.Effective [ "custom" ] composite).Contributions
                 |> List.map (fun item -> item.Winner))
                [ true; true ]
                "several surviving object origins"
        }
        test "diagnostics never present raw parser or validation values" {
            let identity = source "invalid.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let diagnostic = {
                Code = "config.parse"
                Message = "SECRET INPUT"
                Severity = DiagnosticSeverity.Error
                Source = identity
                KeyPath = [ "custom" ]
                Position = Some { Line = 2; Column = 3 }
            }

            let details =
                Resolver.resolveDetailed
                    { DocumentDefaults.resolutionInput with
                        Sources = [
                            {
                                Source = identity
                                Content = SourceContent.Invalid [ diagnostic ]
                                Required = true
                            }
                        ]
                    }

            let result = Inspection.path QueryScope.Effective details
            Expect.isFalse (result.Diagnostics.Head.Message.Contains "SECRET INPUT") "safe message"
            Expect.equal result.Diagnostics.Head.Position diagnostic.Position "position preserved"
        }
        test "malformed Unicode escapes return a key-path usage error" {
            Expect.isError (KeyPath.parse "\"\\uD800\"") "unpaired high surrogate"
            Expect.isError (KeyPath.parse "\"\\uDC00\"") "unpaired low surrogate"
        }
    ]
