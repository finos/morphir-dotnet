// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open Semver

/// What authenticated TUF targets metadata says about one target. A TUF client produces it after verifying the
/// metadata; these checks never verify metadata themselves.
type AuthenticatedTarget =
    { Length: uint64
      Sha256: Sha256
      /// The target's <c>custom</c> object, if it has one.
      Custom: Value option }

/// A recipe target's current authenticated release state.
[<RequireQualifiedAccess>]
type RecipeStatus =
    | Active
    | Yanked
    | Revoked

/// The <c>custom.morphir</c> object of a recipe target.
type RecipeTargetState =
    { Preset: Token
      Version: SemVer
      Channels: Channel list
      Status: RecipeStatus }

/// A refusal from the authenticated recipe target checks: one stable code and the path it concerns. Refusals about
/// the recipe target itself have the empty path.
type TargetRefusal = { Code: Code; Path: DocumentPath }

/// <summary>
/// The rows of the toolset recipe page's recipe target table that need no metadata verification, as pure functions
/// over the authenticated metadata a TUF client supplies, and channel selection over the same metadata.
/// </summary>
module RecipeTargets =

    let private refuse code (path: DocumentPath) : Result<'T, TargetRefusal> = Error { Code = code; Path = path }
    let private trust code = refuse (Code.Trust code) DocumentPath.root

    /// <summary>A target's bytes against its authenticated entry: unlisted is <c>target_not_authenticated</c>, then
    /// the length is compared before the digest.</summary>
    let authenticate (listed: AuthenticatedTarget option) (bytes: byte array) : Result<AuthenticatedTarget, TargetRefusal> =
        match listed with
        | None -> trust TrustCode.TargetNotAuthenticated
        | Some target when uint64 bytes.LongLength <> target.Length -> trust TrustCode.TargetLengthMismatch
        | Some target when Sha256.ofBytes bytes <> target.Sha256 -> trust TrustCode.TargetDigestMismatch
        | Some target -> Ok target

    let private member' (name: string) (value: Value) =
        match value with
        | Value.Object members -> members |> List.tryFind (fst >> (=) name) |> Option.map snd
        | _ -> None

    let private text name value =
        match member' name value with
        | Some(Value.String s) -> Some s
        | _ -> None

    /// <summary>The <c>custom.morphir</c> state of a recipe target: <c>kind</c> <c>toolset-recipe</c>,
    /// <c>schemaVersion</c> <c>0.1.0-draft.1</c>, a preset token, a SemVer version, channels and a known status.
    /// Anything else is <c>release_descriptor_mismatch</c>.</summary>
    let state (target: AuthenticatedTarget) : Result<RecipeTargetState, TargetRefusal> =
        let morphir = target.Custom |> Option.bind (member' "morphir")

        let parsed =
            match morphir with
            | None -> None
            | Some morphir ->
                let channels =
                    match member' "channels" morphir with
                    | Some(Value.Array items) ->
                        items
                        |> List.map (function
                            | Value.String c -> Channel.tryParse c |> Result.toOption
                            | _ -> None)
                        |> fun parsed -> if List.forall Option.isSome parsed then Some(List.choose id parsed) else None
                    | _ -> None

                let status =
                    match text "status" morphir with
                    | Some "active" -> Some RecipeStatus.Active
                    | Some "yanked" -> Some RecipeStatus.Yanked
                    | Some "revoked" -> Some RecipeStatus.Revoked
                    | _ -> None

                match
                    text "kind" morphir,
                    text "schemaVersion" morphir,
                    text "preset" morphir |> Option.bind (Token.tryParse >> Result.toOption),
                    text "version" morphir |> Option.bind (SemVer.tryParse >> Result.toOption),
                    channels,
                    status
                with
                | Some "toolset-recipe", Some version, Some preset, Some release, Some channels, Some status when
                    version = ContractVersion.current
                    ->
                    Some
                        { Preset = preset
                          Version = release
                          Channels = channels
                          Status = status }
                | _ -> None

        match parsed with
        | Some state -> Ok state
        | None -> trust TrustCode.ReleaseDescriptorMismatch

    /// <summary>
    /// The recipe target checks, in the page's order, except the TUF client's own: the target path is portable
    /// (checked before anything is read), the target is authenticated with its length and digest, its
    /// <c>custom.morphir</c> state is well formed and not revoked, the document reads as a <c>0.1.0-draft.1</c>
    /// recipe, and its <c>preset</c> and <c>version</c> are the authenticated ones.
    /// </summary>
    let check (target: string) (listed: AuthenticatedTarget option) (bytes: byte array) : Result<Recipe * RecipeTargetState, TargetRefusal> =
        result {
            match TargetPath.tryParse target with
            | Error code -> return! refuse (Code.Reader code) DocumentPath.root
            | Ok _ -> ()

            let! authenticated = authenticate listed bytes
            let! state = state authenticated

            if state.Status = RecipeStatus.Revoked then
                return! trust TrustCode.ReleaseRevoked

            let! recipe =
                RecipeDocument.read bytes
                |> Result.mapError (fun refusal -> { Code = Code.Reader refusal.Code; Path = refusal.Path })

            if recipe.Preset <> state.Preset || recipe.Version <> state.Version then
                return! trust TrustCode.ReleaseDescriptorMismatch

            return recipe, state
        }

    /// <summary>
    /// Channel selection (toolset recipe page, "Authenticated recipe targets"): among the preset's recipe targets that
    /// are <c>active</c> and on the channel, the greatest SemVer version. <c>stable</c> admits only final versions;
    /// <c>preview</c> and <c>insiders</c> select from <c>preview</c>, any <c>preview/&lt;name&gt;</c> and
    /// <c>insiders</c>; a named preview selects only its own channel. Yanked and revoked recipes are skipped, and
    /// targets whose state does not read are not candidates. Nothing selected is <c>unsupported_artifact</c> at
    /// <c>toolset.preset</c>.
    /// </summary>
    let select (preset: Token) (channel: Channel) (targets: (string * AuthenticatedTarget) list) : Result<string, TargetRefusal> =
        let onChannel (state: RecipeTargetState) =
            match channel with
            | Channel.Stable -> List.contains Channel.Stable state.Channels && not state.Version.SemVersion.IsPrerelease
            | Channel.Preview
            | Channel.Insiders ->
                state.Channels
                |> List.exists (function
                    | Channel.Preview
                    | Channel.Insiders
                    | Channel.NamedPreview _ -> true
                    | Channel.Stable -> false)
            | Channel.NamedPreview _ -> List.contains channel state.Channels

        let candidates =
            [ for path, target in targets do
                  match state target with
                  | Ok state when state.Preset = preset && state.Status = RecipeStatus.Active && onChannel state ->
                      yield path, state.Version.SemVersion
                  | _ -> () ]

        match candidates with
        | [] -> refuse (Code.Artifact ArtifactCode.UnsupportedArtifact) ((DocumentPath.root.Child "toolset").Child "preset")
        | _ ->
            candidates
            |> List.reduce (fun best next -> if SemVersion.ComparePrecedence(snd next, snd best) > 0 then next else best)
            |> fst
            |> Ok
