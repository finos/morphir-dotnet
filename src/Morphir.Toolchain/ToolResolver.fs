// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open Semver

/// A tool release request: one exact version, or one channel.
[<RequireQualifiedAccess>]
type ToolRequest =
    | Exact of version: SemVer
    | OnChannel of channel: Channel

/// The current authenticated state of one tool release: the <c>custom.morphir</c> of its release target.
type ToolReleaseState =
    { Target: string
      ToolId: ToolId
      Version: SemVer
      Channels: Channel list
      Status: RecipeStatus
      /// The requirement on the acquiring Morphir CLI, as single comparators that must all hold.
      MorphirCli: Comparator list
      Platforms: ToolPlatform list }

/// The release a request selects: its descriptor target and exact version.
type ToolSelection = { Target: string; Version: SemVer }

/// <summary>
/// Tool release metadata v1 resolution (the v1 profile, "Channel and exact-version resolution", at finos/morphir
/// <c>9e1f853b7</c>), as a pure function over the authenticated targets a TUF client supplies. It reads each release
/// target's <c>custom.morphir</c>, which the profile says is enough to resolve a release before its descriptor is
/// downloaded.
/// </summary>
/// <remarks>
/// SECURITY: this code has not had a security review. It selects from metadata that it does not verify. It does not
/// read or check the release descriptor; a caller that installs the selection must check the descriptor's immutable
/// values against this state (<c>release_descriptor_mismatch</c>), as the recipe host binding check does. A manager
/// acquiring an offering through a recipe never calls this resolver: it checks the recipe's exact bindings instead.
/// Provisional readings: <c>compatibility.morphirCli</c> is a list of single comparators separated by commas, each in
/// the comparator grammar of the toolchain contract and met by the Rust <c>semver</c> and npm prerelease rule; a
/// requirement outside that grammar, a malformed state of the requested tool, and two releases of one precedence are
/// refused with <c>release_descriptor_mismatch</c>.
/// </remarks>
module ToolResolver =

    let private refuse code : Result<'T, TargetRefusal> = Error { Code = code; Path = DocumentPath.root }

    let private member' (name: string) (value: Value) =
        match value with
        | Value.Object members -> members |> List.tryFind (fst >> (=) name) |> Option.map snd
        | _ -> None

    let private text name value =
        match member' name value with
        | Some(Value.String s) -> Some s
        | _ -> None

    /// <summary>A CLI requirement: single comparators separated by commas, such as
    /// <c>&gt;=0.4.0-alpha.5, &lt;0.5.0</c>; <c>None</c> for anything else.</summary>
    let requirement (text: string) : Comparator list option =
        let parts = text.Split ',' |> Array.map (fun part -> part.Trim ' ')

        let parsed =
            parts
            |> Array.map (fun part -> if part.Length = 0 then None else Comparator.tryParse part |> Result.toOption)

        if parsed |> Array.forall Option.isSome then Some(parsed |> Array.choose id |> List.ofArray) else None

    let private state (target: string) (morphir: Value) : ToolReleaseState option =
        let all (parse: string -> Result<'T, ReaderCode>) (values: Value list) =
            let parsed = values |> List.map (function Value.String s -> parse s |> Result.toOption | _ -> None)
            if List.forall Option.isSome parsed then Some(List.choose id parsed) else None

        let platform value =
            match text "os" value, text "arch" value with
            | Some os, Some arch -> Some { Os = os; Arch = arch }
            | _ -> None

        let channels =
            match member' "channels" morphir with
            | Some(Value.Array items) -> all Channel.tryParse items
            | _ -> None

        let platforms =
            match member' "platforms" morphir with
            | Some(Value.Array items) ->
                let parsed = items |> List.map platform
                if List.forall Option.isSome parsed then Some(List.choose id parsed) else None
            | _ -> None

        let status =
            match text "status" morphir with
            | Some "active" -> Some RecipeStatus.Active
            | Some "yanked" -> Some RecipeStatus.Yanked
            | Some "revoked" -> Some RecipeStatus.Revoked
            | _ -> None

        match
            member' "schemaVersion" morphir,
            text "toolId" morphir |> Option.bind (ToolId.tryParse >> Result.toOption),
            text "version" morphir |> Option.bind (SemVer.tryParse >> Result.toOption),
            channels,
            status,
            member' "compatibility" morphir |> Option.bind (text "morphirCli") |> Option.bind requirement,
            platforms
        with
        | Some(Value.Number "1"), Some toolId, Some version, Some channels, Some status, Some cli, Some platforms ->
            Some
                { Target = target
                  ToolId = toolId
                  Version = version
                  Channels = channels
                  Status = status
                  MorphirCli = cli
                  Platforms = platforms }
        | _ -> None

    /// <summary>The release states of one tool: every authenticated target whose <c>custom.morphir.kind</c> is
    /// <c>tool-release</c> for the tool id. A malformed state of the tool, or two releases of one SemVer precedence
    /// (build metadata ignored), is <c>release_descriptor_mismatch</c>; targets of other kinds are ignored, as a v1
    /// client ignores every kind it does not define.</summary>
    let releases (targets: Map<string, AuthenticatedTarget>) (toolId: ToolId) : Result<ToolReleaseState list, TargetRefusal> =
        let ofTool =
            [ for KeyValue(target, entry) in targets do
                  match entry.Custom |> Option.bind (member' "morphir") with
                  | Some morphir when text "kind" morphir = Some "tool-release" && text "toolId" morphir = Some toolId.Text ->
                      yield state target morphir
                  | _ -> () ]

        if ofTool |> List.exists Option.isNone then
            refuse (Code.Trust TrustCode.ReleaseDescriptorMismatch)
        else
            let states = List.choose id ofTool

            // Two versions have one precedence exactly when their text without build metadata is equal (canonical
            // SemVer, no leading zeros), so the check is a hash set of that text, not a pairwise comparison, which
            // was quadratic in the releases of a tool (review of #582, R1).
            let precedence (state: ToolReleaseState) =
                match state.Version.Text.IndexOf '+' with
                | -1 -> state.Version.Text
                | at -> state.Version.Text.Substring(0, at)

            let ambiguous = (firstRepeatBy precedence states).IsSome

            if ambiguous then refuse (Code.Trust TrustCode.ReleaseDescriptorMismatch) else Ok states

    /// Whether a release is on a requested channel: stable admits final versions only; preview and insiders admit the
    /// preview family; a named preview admits exactly its segment.
    let private onChannel (channel: Channel) (release: ToolReleaseState) =
        match channel with
        | Channel.Stable -> List.contains Channel.Stable release.Channels && not release.Version.SemVersion.IsPrerelease
        | Channel.Preview
        | Channel.Insiders ->
            release.Channels
            |> List.exists (function
                | Channel.Preview
                | Channel.Insiders
                | Channel.NamedPreview _ -> true
                | Channel.Stable -> false)
        | Channel.NamedPreview _ -> List.contains channel release.Channels

    /// <summary>The release a request selects for a platform and the acquiring CLI's version: the requested tool's
    /// releases that match the request, without revoked ones (an exact request for a revoked release is
    /// <c>release_revoked</c>) and, for a channel, without yanked ones; then those whose CLI requirement holds; then the
    /// greatest precedence with an artifact for the platform. Nothing selected is <c>no_compatible_release</c>.</summary>
    let resolve
        (targets: Map<string, AuthenticatedTarget>)
        (toolId: ToolId)
        (request: ToolRequest)
        (platform: ToolPlatform)
        (cli: SemVer)
        : Result<ToolSelection, TargetRefusal> =
        result {
            let! releases = releases targets toolId

            let matching =
                match request with
                | ToolRequest.Exact version -> releases |> List.filter (fun release -> release.Version = version)
                | ToolRequest.OnChannel channel ->
                    releases |> List.filter (fun release -> release.Status = RecipeStatus.Active && onChannel channel release)

            if matching |> List.exists (fun release -> release.Status = RecipeStatus.Revoked) then
                return! refuse (Code.Trust TrustCode.ReleaseRevoked)

            let selected =
                matching
                |> List.filter (fun release -> Comparator.allMet release.MorphirCli cli && List.contains platform release.Platforms)
                |> List.sortWith (fun a b -> SemVersion.ComparePrecedence(b.Version.SemVersion, a.Version.SemVersion))
                |> List.tryHead

            match selected with
            | Some release -> return { Target = release.Target; Version = release.Version }
            | None -> return! refuse (Code.Artifact ArtifactCode.NoCompatibleRelease)
        }
