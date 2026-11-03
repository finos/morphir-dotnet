namespace Morphir.Configuration

[<RequireQualifiedAccess>]
type MachinePlatform =
    | Linux = 0
    | MacOS = 1
    | Windows = 2

/// Explicit host locations. Resolve relative MORPHIR_HOME against the host's
/// invocation directory before supplying Environment when absolute paths are needed.
type MachineLocationInput = {
    Platform: MachinePlatform
    HomeDirectory: string
    RoamingAppData: string option
    ProgramData: string option
    Environment: Map<string, string>
}

/// Candidate planning only. Hosts supply every path and perform all observations.
module ProjectSources =
    let private extensions = [
        "json", DocumentFormat.Json
        "json5", DocumentFormat.Json
        "yaml", DocumentFormat.Yaml
        "yml", DocumentFormat.Yaml
        "toml", DocumentFormat.Toml
    ]

    let private atStem role profile stem =
        extensions
        |> List.mapi (fun order (extension, format) -> {
            Path =
                stem
                + "."
                + extension
            Format = format
            Role = role
            Order = order
            Profile = profile
        })

    let machineCandidates (input: MachineLocationInput) isolated =
        if isolated then
            []
        else
            let nonempty value = not (System.String.IsNullOrEmpty value)

            let environment key =
                Map.tryFind key input.Environment
                |> Option.filter nonempty

            let home =
                environment "MORPHIR_HOME"
                |> Option.defaultValue (ProjectPath.join input.HomeDirectory ".morphir")
                |> ProjectPath.normalize

            let xdg =
                environment "XDG_CONFIG_HOME"
                |> Option.filter (fun value ->
                    value.StartsWith("/", System.StringComparison.Ordinal)
                )

            let system, globalDirectory =
                match input.Platform with
                | MachinePlatform.Linux ->
                    "/etc/morphir",
                    (xdg
                     |> Option.map (fun directory -> ProjectPath.join directory "morphir")
                     |> Option.defaultValue (ProjectPath.join input.HomeDirectory ".config/morphir"))
                | MachinePlatform.MacOS ->
                    "/etc/morphir",
                    (xdg
                     |> Option.map (fun directory -> ProjectPath.join directory "morphir")
                     |> Option.defaultValue (
                         ProjectPath.join input.HomeDirectory "Library/Application Support/morphir"
                     ))
                | MachinePlatform.Windows ->
                    (input.ProgramData
                     |> Option.filter nonempty
                     |> Option.defaultValue "C:/ProgramData"
                     |> fun directory -> ProjectPath.join directory "morphir"),
                    (input.RoamingAppData
                     |> Option.filter nonempty
                     |> Option.defaultValue (ProjectPath.join input.HomeDirectory "AppData/Roaming")
                     |> fun directory -> ProjectPath.join directory "morphir")
                | _ -> invalidArg "input" "Unsupported machine platform"

            [
                SourceRole.System, system
                SourceRole.Global, globalDirectory
                SourceRole.Global, home
            ]
            |> List.distinct
            |> List.collect (fun (role, directory) ->
                atStem role None (ProjectPath.join directory "morphir")
            )

    /// Adjacent candidates exist only for primary layouts. The caller passes one
    /// validated active profile name from Profiles.plan, or None for user files.
    let overlayCandidates (primaries: SourceInput list) role profile =
        let eligibleStem (source: SourceInput) =
            let existing =
                match source.Content with
                | SourceContent.Missing -> false
                | SourceContent.Available _
                | SourceContent.Invalid _ -> true

            if
                not existing
                || (source.Source.Role
                    <> SourceRole.RootPrimary
                    && source.Source.Role
                       <> SourceRole.MemberPrimary)
            then
                None
            else
                let path = ProjectPath.normalize source.Source.Path

                if path.EndsWith("/morphir-workspace.json", System.StringComparison.Ordinal) then
                    Some(ProjectPath.join (ProjectPath.parent path) "morphir")
                else
                    let extensionIndex = path.LastIndexOf('.')

                    if extensionIndex < 0 then
                        None
                    else
                        let stem = path.Substring(0, extensionIndex)

                        let extension =
                            path.Substring(
                                extensionIndex
                                + 1
                            )

                        let supported =
                            extensions
                            |> List.exists (fun (known, _) -> known = extension)

                        if
                            supported
                            && (stem.EndsWith("/morphir", System.StringComparison.Ordinal)
                                || stem.EndsWith(
                                    "/.config/morphir/config",
                                    System.StringComparison.Ordinal
                                ))
                        then
                            Some stem
                        else
                            None

        primaries
        |> List.choose eligibleStem
        |> List.distinct
        |> List.sort
        |> List.collect (fun stem ->
            let suffix =
                match profile with
                | Some name ->
                    ".profile."
                    + name
                | None -> ".user"

            atStem
                role
                profile
                (stem
                 + suffix)
        )

    /// Check one logical source inventory before resolution. Ordinary alternatives
    /// share a format group across layouts and machine directories.
    let ambiguities (sources: SourceInput list) =
        sources
        |> List.filter (fun source ->
            List.contains source.Source.Role [
                SourceRole.System
                SourceRole.Global
                SourceRole.RootPrimary
                SourceRole.MemberPrimary
                SourceRole.WorkspaceProfile
                SourceRole.MemberProfile
                SourceRole.UserOverride
                SourceRole.WorkspaceUserOverride
            ]
        )
        |> List.filter (fun source ->
            match source.Content with
            | SourceContent.Missing -> false
            | SourceContent.Available _
            | SourceContent.Invalid _ ->
                not (
                    (source.Source.Role = SourceRole.RootPrimary
                     || source.Source.Role = SourceRole.MemberPrimary)
                    && source.Source.Path.EndsWith(
                        "/morphir-workspace.json",
                        System.StringComparison.Ordinal
                    )
                )
        )
        |> List.distinctBy (fun source ->
            source.Source.Role, source.Source.Profile, source.Source.Path
        )
        |> List.groupBy (fun source ->
            source.Source.Role, source.Source.Profile, source.Source.Format
        )
        |> List.choose (fun (_, candidates) ->
            if List.length candidates < 2 then
                None
            else
                let candidates =
                    candidates
                    |> List.sortBy (fun source -> source.Source.Path)

                Some
                    {
                        Code = "config.ambiguous_sources"
                        Message =
                            "Ambiguous configuration alternatives: "
                            + (candidates
                               |> List.map (fun source -> source.Source.Path)
                               |> String.concat ", ")
                        Severity = DiagnosticSeverity.Error
                        Source = candidates.Head.Source
                        KeyPath = []
                        Position = None
                    }
        )
