module Morphir.Configuration.Tests.ProjectSourceTests

open Expecto
open Morphir.Configuration

let locations platform = {
    Platform = platform
    HomeDirectory = "/home/ada"
    RoamingAppData = None
    ProgramData = None
    Environment = Map.empty
}

let tomlPaths input =
    ProjectSources.machineCandidates input false
    |> List.filter (fun source -> source.Format = DocumentFormat.Toml)
    |> List.map (fun source -> source.Path)

let available path role format =
    let source = {
        Path = path
        Format = format
        Role = role
        Order = 0
        Profile = None
    }

    {
        Source = source
        Required = true
        Content =
            SourceContent.Available
                {
                    Source = source
                    Raw = DocumentDefaults.emptyValue
                    Values = DocumentDefaults.emptyValue
                    Positions = Map.empty
                    Diagnostics = []
                    LegacyFields = []
                }
    }

[<Tests>]
let tests =
    testList "Project sources" [
        test "Linux platform sources retain separate platform and Morphir home alternatives" {
            let input = {
                Platform = MachinePlatform.Linux
                HomeDirectory = "/home/ada"
                RoamingAppData = None
                ProgramData = None
                Environment = Map.empty
            }

            let candidates = ProjectSources.machineCandidates input false

            let toml =
                candidates
                |> List.filter (fun source -> source.Format = DocumentFormat.Toml)

            Expect.equal
                (toml
                 |> List.map (fun source -> source.Role, source.Path))
                [
                    SourceRole.System, "/etc/morphir/morphir.toml"
                    SourceRole.Global, "/home/ada/.config/morphir/morphir.toml"
                    SourceRole.Global, "/home/ada/.morphir/morphir.toml"
                ]
                "system and both global alternatives remain candidates"
        }
        test "macOS uses application support when XDG is absent" {
            Expect.equal
                (tomlPaths (locations MachinePlatform.MacOS))
                [
                    "/etc/morphir/morphir.toml"
                    "/home/ada/Library/Application Support/morphir/morphir.toml"
                    "/home/ada/.morphir/morphir.toml"
                ]
                "macOS global directory is platform-specific"
        }
        test "Windows uses explicit program data roaming and user profile locations" {
            let input =
                { locations MachinePlatform.Windows with
                    HomeDirectory = "C:/Users/Ada"
                    RoamingAppData = Some "D:/Roaming"
                    ProgramData = Some "D:/Shared"
                    Environment = Map.ofList [ "XDG_CONFIG_HOME", "/ignore/me" ]
                }

            Expect.equal
                (tomlPaths input)
                [
                    "D:/Shared/morphir/morphir.toml"
                    "D:/Roaming/morphir/morphir.toml"
                    "C:/Users/Ada/.morphir/morphir.toml"
                ]
                "Windows ignores XDG and retains both global alternatives"
        }
        test "Windows missing program data uses C ProgramData fallback" {
            let input =
                { locations MachinePlatform.Windows with
                    HomeDirectory = "C:/Users/Ada"
                    RoamingAppData = Some "C:/Users/Ada/AppData/Roaming"
                }

            Expect.equal
                (tomlPaths input
                 |> List.head)
                "C:/ProgramData/morphir/morphir.toml"
                "system fallback remains absolute"
        }
        test "Windows missing roaming path uses profile AppData fallback" {
            let input =
                { locations MachinePlatform.Windows with
                    HomeDirectory = "C:/Users/Ada"
                }

            Expect.equal
                (tomlPaths input
                 |> List.item 1)
                "C:/Users/Ada/AppData/Roaming/morphir/morphir.toml"
                "explicit profile anchors fallback"
        }
        for platform in [ MachinePlatform.Linux; MachinePlatform.MacOS ] do
            testCase (
                string platform
                + " accepts absolute XDG"
            )
            <| fun _ ->
                let input =
                    { locations platform with
                        Environment = Map.ofList [ "XDG_CONFIG_HOME", "/custom/config" ]
                    }

                Expect.equal
                    (tomlPaths input
                     |> List.item 1)
                    "/custom/config/morphir/morphir.toml"
                    "absolute XDG replaces the platform alternative"

            testCase (
                string platform
                + " ignores relative XDG"
            )
            <| fun _ ->
                let input =
                    { locations platform with
                        Environment = Map.ofList [ "XDG_CONFIG_HOME", "relative/config" ]
                    }

                Expect.equal
                    (tomlPaths input)
                    (tomlPaths (locations platform))
                    "relative XDG is ignored"
        test "MORPHIR_HOME replaces only the home alternative" {
            let input =
                { locations MachinePlatform.Linux with
                    Environment =
                        Map.ofList [
                            "MORPHIR_HOME", "/alternate/morphir"
                            "XDG_CONFIG_HOME", "/xdg"
                        ]
                }

            Expect.equal
                (tomlPaths input)
                [
                    "/etc/morphir/morphir.toml"
                    "/xdg/morphir/morphir.toml"
                    "/alternate/morphir/morphir.toml"
                ]
                "the platform directory survives relocation"
        }
        test "empty operational environment values retain explicit home defaults" {
            let input =
                { locations MachinePlatform.Linux with
                    Environment =
                        Map.ofList [ "MORPHIR_HOME", ""; "XDG_CONFIG_HOME", ""; "HOME", "" ]
                }

            Expect.equal
                (tomlPaths input)
                (tomlPaths (locations MachinePlatform.Linux))
                "host home is authoritative"
        }
        test "relative MORPHIR_HOME is retained for explicit host normalization" {
            let input =
                { locations MachinePlatform.Linux with
                    Environment = Map.ofList [ "MORPHIR_HOME", "custom/../morphir-home" ]
                }

            Expect.equal
                (tomlPaths input
                 |> List.last)
                "morphir-home/morphir.toml"
                "nonempty relative home is supported"
        }
        test "identical global alternatives are inventoried once" {
            let input =
                { locations MachinePlatform.Linux with
                    Environment = Map.ofList [ "MORPHIR_HOME", "/home/ada/.config/morphir" ]
                }

            Expect.equal
                (tomlPaths input
                 |> List.length)
                2
                "same physical candidate is not ambiguous with itself"
        }
        test "isolated loading does not request machine candidates" {
            Expect.isEmpty
                (ProjectSources.machineCandidates (locations MachinePlatform.Linux) true)
                "no machine reads"
        }
        for stem in [ "/repo/morphir"; "/repo/.morphir/morphir"; "/repo/.config/morphir/config" ] do
            testCase (
                stem
                + " has adjacent personal files in every format"
            )
            <| fun _ ->
                let primary =
                    available
                        (stem
                         + ".toml")
                        SourceRole.RootPrimary
                        DocumentFormat.Toml

                let overlays =
                    ProjectSources.overlayCandidates
                        [ primary ]
                        SourceRole.WorkspaceUserOverride
                        None

                Expect.equal
                    (overlays
                     |> List.map (fun source -> source.Path))
                    ([ "json"; "json5"; "yaml"; "yml"; "toml" ]
                     |> List.map (fun extension ->
                         stem
                         + ".user."
                         + extension
                     ))
                    "only the eligible layout is inspected"

                Expect.all
                    overlays
                    (fun source ->
                        source.Role = SourceRole.WorkspaceUserOverride
                        && source.Profile.IsNone
                    )
                    "workspace personal identity"

            testCase (
                stem
                + " requests only one active profile name"
            )
            <| fun _ ->
                let primary =
                    available
                        (stem
                         + ".json")
                        SourceRole.MemberPrimary
                        DocumentFormat.Json

                let overlays =
                    ProjectSources.overlayCandidates
                        [ primary ]
                        SourceRole.MemberProfile
                        (Some "ci")

                Expect.equal
                    (overlays
                     |> List.map (fun source -> source.Path))
                    ([ "json"; "json5"; "yaml"; "yml"; "toml" ]
                     |> List.map (fun extension ->
                         stem
                         + ".profile.ci."
                         + extension
                     ))
                    "inactive profile names are never inventoried"

                Expect.all
                    overlays
                    (fun source ->
                        source.Role = SourceRole.MemberProfile
                        && source.Profile = Some "ci"
                    )
                    "profile identity"
        test "supplemental workspace makes the root morphir stem eligible only once" {
            let primaries = [
                available "/repo/morphir-workspace.json" SourceRole.RootPrimary DocumentFormat.Json
                available "/repo/morphir.toml" SourceRole.RootPrimary DocumentFormat.Toml
            ]

            let overlays =
                ProjectSources.overlayCandidates primaries SourceRole.WorkspaceUserOverride None

            Expect.equal
                (overlays
                 |> List.length)
                5
                "supplemental aliases the ordinary root stem"

            Expect.equal overlays.Head.Path "/repo/morphir.user.json" "root personal stem"
        }
        test "absent primaries do not make personal files eligible" {
            let missing = {
                Source = {
                    Path = "/repo/morphir.toml"
                    Format = DocumentFormat.Toml
                    Role = SourceRole.RootPrimary
                    Order = 0
                    Profile = None
                }
                Content = SourceContent.Missing
                Required = false
            }

            Expect.isEmpty
                (ProjectSources.overlayCandidates [ missing ] SourceRole.UserOverride None)
                "personal files cannot identify roots"
        }
        test "mixed primary layouts expose only their eligible stems in deterministic order" {
            let primaries = [
                available "/repo/.morphir/morphir.yaml" SourceRole.RootPrimary DocumentFormat.Yaml
                available
                    "/repo/.config/morphir/config.toml"
                    SourceRole.RootPrimary
                    DocumentFormat.Toml
            ]

            let first =
                ProjectSources.overlayCandidates primaries SourceRole.WorkspaceProfile (Some "ci")

            let reversed =
                ProjectSources.overlayCandidates
                    (List.rev primaries)
                    SourceRole.WorkspaceProfile
                    (Some "ci")

            Expect.equal first reversed "host enumeration order is irrelevant"
            Expect.equal first.Length 10 "only two eligible layouts"

            Expect.all
                first
                (fun source -> not (source.Path.StartsWith("/repo/morphir.profile")))
                "root stem is ineligible"
        }
        test "global same-format conflicts include every alternative" {
            let sources = [
                available
                    "/home/ada/.config/morphir/morphir.toml"
                    SourceRole.Global
                    DocumentFormat.Toml
                available "/home/ada/.morphir/morphir.toml" SourceRole.Global DocumentFormat.Toml
            ]

            let diagnostics = ProjectSources.ambiguities sources
            Expect.equal diagnostics.Length 1 "one diagnostic per conflicting group"
            Expect.equal diagnostics.Head.Code "config.ambiguous_sources" "structured ambiguity"
            Expect.equal diagnostics.Head.Severity DiagnosticSeverity.Error "conflict fails loading"

            Expect.stringContains
                diagnostics.Head.Message
                sources.Head.Source.Path
                "first alternative"

            Expect.stringContains
                diagnostics.Head.Message
                sources.Tail.Head.Source.Path
                "second alternative"
        }
        test "cross-format global sources coexist and system is a separate scope" {
            let sources = [
                available "/global/morphir.json" SourceRole.Global DocumentFormat.Json
                available "/home/morphir.yaml" SourceRole.Global DocumentFormat.Yaml
                available "/home/morphir.toml" SourceRole.Global DocumentFormat.Toml
                available "/system/morphir.toml" SourceRole.System DocumentFormat.Toml
            ]

            Expect.isEmpty (ProjectSources.ambiguities sources) "ordinary format layering"
        }
        test "profile ambiguity is per active name and workspace or member scope" {
            let supplied name role path =
                let source = available path role DocumentFormat.Yaml

                { source with
                    Source =
                        { source.Source with
                            Profile = Some name
                        }
                }

            let one = supplied "ci" SourceRole.WorkspaceProfile "/repo/morphir.profile.ci.yaml"
            let two = supplied "ci" SourceRole.WorkspaceProfile "/repo/morphir.profile.ci.yml"

            let memberSource =
                supplied "ci" SourceRole.MemberProfile "/repo/app/morphir.profile.ci.yaml"

            let other =
                supplied "dev" SourceRole.WorkspaceProfile "/repo/morphir.profile.dev.yaml"

            let diagnostics = ProjectSources.ambiguities [ one; two; memberSource; other ]
            Expect.equal diagnostics.Length 1 "only same-name same-scope group conflicts"

            Expect.stringContains
                diagnostics.Head.Message
                two.Source.Path
                "alias extension included"
        }
        test "invalid documents remain ambiguity candidates while missing paths do not" {
            let one = available "/one/morphir.toml" SourceRole.Global DocumentFormat.Toml
            let two = available "/two/morphir.toml" SourceRole.Global DocumentFormat.Toml

            let invalid =
                { two with
                    Content = SourceContent.Invalid []
                }

            let missing =
                { two with
                    Content = SourceContent.Missing
                }

            Expect.equal
                (ProjectSources.ambiguities [ one; invalid ]
                 |> List.length)
                1
                "invalid existing file still conflicts"

            Expect.isEmpty
                (ProjectSources.ambiguities [ one; missing ])
                "optional absence does not conflict"

            Expect.isEmpty
                (ProjectSources.ambiguities [ one; one ])
                "a path cannot conflict with itself"
        }
        test "explicit nonprimary files do not grant adjacent overlay eligibility" {
            let selected = available "/repo/custom.toml" SourceRole.Selected DocumentFormat.Toml

            Expect.isEmpty
                (ProjectSources.overlayCandidates [ selected ] SourceRole.UserOverride None)
                "explicit selection does not declare a layout"
        }
    ]
