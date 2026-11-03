module Morphir.Configuration.Tests.EditingTests

open Expecto
open Morphir.Configuration
open Morphir.Configuration.Tests.ResolutionTests

[<Tests>]
let tests =
    testList "Editing" [
        test "string is the default typed input and planning never mutates its input" {
            let target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml
            let original = obj [ "custom", text "before" ]

            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = [ supplied target original ]
                }

            let request = {
                Operation = EditOperation.Set("after", EditValueType.String)
                Key = "custom"
            }

            let result = Editing.plan target request input []
            Expect.isOk result "plan"

            let plan =
                Result.defaultWith (fun (error: EditFailure) -> failwith error.Code) result

            Expect.equal plan.After (Some(text "after")) "string input"
            Expect.equal plan.Effective.Value (Some(text "after")) "effective preview"

            Expect.equal
                (Resolver.resolve input).Effective
                (Resolver.resolve input).Effective
                "immutable input"

            Expect.equal
                (ConfigTree.tryFind [ "custom" ] original)
                (Some(text "before"))
                "original retained"
        }
        test "typed booleans arrays and objects replace complete entries" {
            let target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = [
                        supplied
                            target
                            (obj [ "custom", ConfigValue.Array [ text "old"; text "extra" ] ])
                    ]
                }

            for kind, raw, expected in
                [
                    EditValueType.Boolean, "false", ConfigValue.Boolean false
                    EditValueType.Integer, "42", ConfigValue.Integer 42L
                    EditValueType.Number, "2.5", ConfigValue.Number 2.5
                    EditValueType.Array, "[1]", ConfigValue.Array [ ConfigValue.Integer 1L ]
                    EditValueType.Object, "{\"a.b\":true}", obj [ "a.b", ConfigValue.Boolean true ]
                ] do
                let result =
                    Editing.plan
                        target
                        {
                            Operation = EditOperation.Set(raw, kind)
                            Key = "custom"
                        }
                        input
                        []

                Expect.isOk result "typed plan"

                let plan =
                    Result.defaultWith (fun (error: EditFailure) -> failwith error.Code) result

                Expect.equal plan.After (Some expected) "complete typed entry"
        }
        test "target selection promotes one lower-format layout and rejects conflicting selectors" {
            let lower =
                source
                    "/repo/.config/morphir/config.yaml"
                    SourceRole.RootPrimary
                    DocumentFormat.Yaml

            let location = {
                Path = "/repo"
                Values = obj []
                Sources = [ supplied lower (obj []) ]
                Diagnostics = []
            }

            let project = {
                Path = "/repo"
                Name = Some "App"
                Location = location
                IsValid = true
            }

            let discovery = {
                StartDirectory = "/repo/src"
                Location = "/repo"
                Workspace = None
                Root = location
                Projects = [ project ]
                Sources = []
                Diagnostics = []
            }

            let context = {
                StartDirectory = "/repo/src"
                Discovery = Some discovery
                SelectedProject = Some project
                SelectionDiagnostics = []
                MachineCandidates = []
                Sources = location.Sources
            }

            let target =
                Editing.selectTarget [] context
                |> Result.defaultWith (fun (error: EditFailure) -> failwith error.Code)

            Expect.equal
                target.Path
                "/repo/.config/morphir/config.toml"
                "corresponding TOML primary"

            Expect.isError
                (Editing.selectTarget [ EditScope.Workspace; EditScope.Project ] context)
                "one selector"

            Expect.isError
                (Editing.selectTarget [ EditScope.File "/repo/settings.yaml" ] context)
                "explicit non-TOML fails"
        }
        test "unset reveals lower JSON and environment still wins a scoped set" {
            let target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml
            let lower = source "/repo/morphir.json" SourceRole.RootPrimary DocumentFormat.Json

            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = [
                        supplied lower (obj [ "custom", text "lower" ])
                        supplied target (obj [ "custom", text "override" ])
                    ]
                }

            let unset =
                Editing.plan
                    target
                    {
                        Operation = EditOperation.Unset
                        Key = "custom"
                    }
                    input
                    []
                |> Result.defaultWith (fun (error: EditFailure) -> failwith error.Code)

            Expect.equal unset.After None "only target entry removed"
            Expect.equal unset.Effective.Value (Some(text "lower")) "inherited value reappears"
            Expect.equal unset.Effective.Contributions.Head.Source.Path lower.Path "lower origin"

            let envInput =
                { input with
                    Environment = Map.ofList [ "MORPHIR_CUSTOM", "environment" ]
                }

            let set =
                Editing.plan
                    target
                    {
                        Operation = EditOperation.Set("written", EditValueType.String)
                        Key = "custom"
                    }
                    envInput
                    []
                |> Result.defaultWith (fun (error: EditFailure) -> failwith error.Code)

            Expect.equal set.Effective.Value (Some(text "environment")) "effective winner"
            Expect.isTrue set.Changed "overridden write still succeeds"

            let absent =
                Editing.plan
                    target
                    {
                        Operation = EditOperation.Unset
                        Key = "missing"
                    }
                    input
                    []

            match absent with
            | Error error -> Expect.equal error.ExitCode 1 "not found"
            | Ok _ -> failtest "missing unset must fail"
        }
        test "known invalid edits cannot hide beneath an environment winner" {
            let target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let input =
                { DocumentDefaults.resolutionInput with
                    Environment = Map.ofList [ "MORPHIR_UI__COLOR", "true" ]
                }

            let result =
                Editing.plan
                    target
                    {
                        Operation = EditOperation.Set("bad-sensitive-input", EditValueType.String)
                        Key = "ui.color"
                    }
                    input
                    []

            Expect.isError result "validate authored value, not just effective winner"

            match result with
            | Error error ->
                Expect.equal error.ExitCode 2 "validation failure"

                Expect.isFalse
                    (sprintf "%A" error
                     |> fun text -> text.Contains "bad-sensitive-input")
                    "safe diagnostics"
            | Ok _ -> ()
        }
        test "typed input rejects nulls duplicates range loss and preserves numeric kind" {
            for kind, raw in
                [
                    EditValueType.Boolean, "False"
                    EditValueType.Integer, "1.5"
                    EditValueType.Integer, "9223372036854775808"
                    EditValueType.Array, "[null]"
                    EditValueType.Object, "{\"a\":1,\"a\":2}"
                    EditValueType.Array, "[9223372036854775808]"
                ] do
                Expect.isError (Editing.parseValue kind raw) "invalid complete input"

            Expect.equal
                (Editing.parseValue EditValueType.Number "2")
                (Ok(ConfigValue.Number 2.0))
                "explicit number"

            Expect.equal
                (Editing.parseValue EditValueType.Number "2.0")
                (Ok(ConfigValue.Number 2.0))
                "float token"
        }
        test "atomic ancestor traversal and malformed targets fail before a candidate exists" {
            let target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            for existing in
                [
                    text "scalar"
                    ConfigValue.Array [ text "array" ]
                    ConfigValue.SecretReference(Map.ofList [ "env", text "TOKEN" ])
                ] do
                let input =
                    { DocumentDefaults.resolutionInput with
                        Sources = [ supplied target (obj [ "custom", existing ]) ]
                    }

                Expect.isError
                    (Editing.plan
                        target
                        {
                            Operation = EditOperation.Set("x", EditValueType.String)
                            Key = "custom.child"
                        }
                        input
                        [])
                    "whole atomic values"

            Expect.isError
                (Editing.plan
                    { target with
                        Format = DocumentFormat.Yaml
                    }
                    {
                        Operation = EditOperation.Set("x", EditValueType.String)
                        Key = "custom"
                    }
                    DocumentDefaults.resolutionInput
                    [])
                "TOML only"

            let invalid = {
                Source = target
                Content = SourceContent.Invalid []
                Required = true
            }

            Expect.isError
                (Editing.plan
                    target
                    {
                        Operation = EditOperation.Set("x", EditValueType.String)
                        Key = "custom"
                    }
                    { DocumentDefaults.resolutionInput with
                        Sources = [ invalid ]
                    }
                    [])
                "malformed existing target must not be replaced"
        }
        test "numeric noops tolerate unrelated NaN and preserve signed zero changes" {
            let target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let input =
                { DocumentDefaults.resolutionInput with
                    Sources = [
                        supplied
                            target
                            (obj [
                                "custom", text "same"
                                "nan", ConfigValue.Number nan
                                "zero", ConfigValue.Number 0.0
                            ])
                    ]
                }

            let plan key raw kind =
                Editing.plan
                    target
                    {
                        Operation = EditOperation.Set(raw, kind)
                        Key = key
                    }
                    input
                    []
                |> Result.defaultWith (fun (error: EditFailure) -> failwith error.Code)

            Expect.isFalse (plan "custom" "same" EditValueType.String).Changed "semantic equality"
            Expect.isTrue (plan "zero" "-0.0" EditValueType.Number).Changed "sign bit retained"
        }
        test "virtual root targets workspace and its adjacent personal source" {
            let identity =
                source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let location = {
                Path = "/repo"
                Values = obj []
                Sources = [ supplied identity (obj []) ]
                Diagnostics = []
            }

            let owner = {
                Root = "/repo"
                OutputDirectory = None
            }

            let discovery = {
                StartDirectory = "/repo"
                Location = "/repo"
                Workspace = Some owner
                Root = location
                Projects = []
                Sources = []
                Diagnostics = []
            }

            let context = {
                StartDirectory = "/repo"
                Discovery = Some discovery
                SelectedProject = None
                SelectionDiagnostics = []
                MachineCandidates = []
                Sources = location.Sources
            }

            for selector, expected in
                [
                    [], "/repo/morphir.toml"
                    [ EditScope.Workspace ], "/repo/morphir.toml"
                    [ EditScope.User ], "/repo/morphir.user.toml"
                ] do
                let target =
                    Editing.selectTarget selector context
                    |> Result.defaultWith (fun (error: EditFailure) -> failwith error.Code)

                Expect.equal target.Path expected "one root-owned destination"

            Expect.isError
                (Editing.selectTarget [ EditScope.Project ] context)
                "virtual workspace is not a project"
        }
        test "machine targets use captured paths and reject same-format alternatives" {
            let machine = {
                Platform = MachinePlatform.Linux
                HomeDirectory = "/home/u"
                RoamingAppData = None
                ProgramData = None
                Environment = Map.empty
            }

            let candidates = ProjectSources.machineCandidates machine false

            let context = {
                StartDirectory = "/repo"
                Discovery = None
                SelectedProject = None
                SelectionDiagnostics = []
                MachineCandidates = candidates
                Sources = []
            }

            let choose scopes context =
                Editing.selectTarget scopes context
                |> Result.defaultWith (fun (error: EditFailure) -> failwith error.Code)

            Expect.equal
                (choose [ EditScope.Global ] context).Path
                "/home/u/.morphir/morphir.toml"
                "preferred global primary"

            Expect.equal
                (choose [ EditScope.System ] context).Path
                "/etc/morphir/morphir.toml"
                "system primary"

            let lower =
                source "/home/u/.config/morphir/morphir.yaml" SourceRole.Global DocumentFormat.Yaml

            let lowerContext =
                { context with
                    Sources = [ supplied lower (obj []) ]
                }

            Expect.equal
                (choose [ EditScope.Global ] lowerContext).Path
                "/home/u/.config/morphir/morphir.toml"
                "corresponding existing layout"

            let first =
                { lower with
                    Path = "/home/u/.config/morphir/morphir.toml"
                    Format = DocumentFormat.Toml
                }

            let second =
                { first with
                    Path = "/home/u/.morphir/morphir.toml"
                }

            Expect.isError
                (Editing.selectTarget
                    [ EditScope.Global ]
                    { context with
                        Sources = [ supplied first (obj []); supplied second (obj []) ]
                    })
                "same format ambiguity"

            Expect.isError (Editing.selectTarget [] context) "no default context"
        }
        test "quoted paths observations and safe effective explanations survive shared planning" {
            let target = source "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml

            let request = {
                Operation = EditOperation.Set("private-value", EditValueType.String)
                Key = "custom.\"a.b\".token"
            }

            let observations = [
                {
                    Path = target.Path
                    Kind = EditObservationKind.Missing
                    Identity = ""
                }
            ]

            let plan =
                Editing.plan target request DocumentDefaults.resolutionInput observations
                |> Result.defaultWith (fun (error: EditFailure) -> failwith error.Code)

            Expect.equal plan.Key [ "custom"; "a.b"; "token" ] "literal dot"
            Expect.equal plan.Observations observations "host evidence retained"
            Expect.equal plan.Effective.Value (Some(text "[REDACTED]")) "safe presentation"

            Expect.equal
                plan.After
                (Some(text "private-value"))
                "canonical application value retained separately"
        }
    ]
