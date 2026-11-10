// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// <summary>A tool release metadata v1 platform: an operating system token and an architecture.</summary>
type ToolPlatform = { Os: string; Arch: string }

/// One artifact a v1 tool release lists: the target that holds it and the platform it runs on.
type ToolReleaseArtifact =
    { TargetPath: string
      Platform: ToolPlatform }

/// <summary>
/// A tool release metadata v1 release descriptor, <c>releases/&lt;tool-id&gt;/&lt;version&gt;.json</c>, as far as the
/// recipe binding checks read it. Its <c>channels</c> and <c>status</c> are the state at initial publication: the
/// profile says clients validate their shape but never use them; the current state is the target's
/// <c>custom.morphir</c>.
/// </summary>
type ToolRelease =
    { ToolId: ToolId
      Version: SemVer
      Channels: Channel list
      Status: RecipeStatus
      /// The requirement on the acquiring Morphir CLI, as written. A manager never evaluates it.
      MorphirCli: NonBlankText
      Artifacts: ToolReleaseArtifact list }

/// <summary>
/// The reader of a tool release metadata v1 release descriptor (the v1 profile and its
/// <c>tool-release-v1.schema.json</c>, at finos/morphir <c>9e1f853b7</c>). Every object is closed, as the schema's
/// <c>additionalProperties: false</c> says; refusals carry the reader codes and the member's path, and the recipe
/// binding check reports any of them as <c>release_descriptor_mismatch</c> at the offering.
/// </summary>
/// <remarks>Read but not used by the checks: <c>tool.name</c>, <c>channels</c>, <c>status</c>,
/// <c>compatibility.morphirCli</c> (validated as non-blank text, never evaluated), and each artifact's
/// <c>archive</c> and <c>launch</c> (validated in shape). No other version is read: <c>schemaVersion</c> is exactly
/// <c>1</c>.</remarks>
module ToolReleaseDocument =
    open Reading

    /// The profile's download bound for a release descriptor, applied before parsing.
    let limit = 1024 * 1024

    /// Fails when an object has a member outside <paramref name="known"/>: the schema's objects are closed.
    let private closed (known: string list) (o: ObjectNode) : Read<unit> =
        match o.Members |> List.tryFind (fun (name, _) -> not (List.contains name known)) with
        | Some(name, _) -> refuse ReaderCode.InvalidDocument (o.Path.Child name)
        | None -> Ok()

    let private nonBlank = validated (fun text -> NonBlankText.tryParse text |> Result.mapError (fun _ -> ReaderCode.InvalidValue))

    /// A string of the schema's <c>relativePath</c>: not empty, not absolute, no drive prefix, no empty, <c>.</c> or
    /// <c>..</c> segment, no backslash, no trailing <c>/</c>, and no NUL, line feed, carriage return, U+2028 or U+2029.
    let isRelativePath (text: string) =
        let segments = text.Split '/'

        text.Length > 0
        && not (text.Length >= 2 && System.Char.IsAsciiLetter text[0] && text[1] = ':')
        && segments |> Array.forall (fun segment -> segment.Length > 0 && segment <> "." && segment <> "..")
        && text |> Seq.forall (fun c -> c <> '\\' && c <> '\000' && c <> '\n' && c <> '\r' && c <> char 0x2028 && c <> char 0x2029)

    let private relativePath = validated (fun text -> if isRelativePath text then Ok text else Error ReaderCode.InvalidValue)

    /// The schema's architecture spelling: <c>[a-z0-9]</c>, then <c>[a-z0-9_-]</c>, ending in a letter or digit.
    let private isArch (text: string) =
        let alnum (c: char) = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')

        text.Length > 0
        && alnum text[0]
        && alnum text[text.Length - 1]
        && text |> Seq.forall (fun c -> alnum c || c = '_' || c = '-')

    let private platform (n: Node) : Read<ToolPlatform> =
        result {
            let! o = asObject n
            do! closed [ "os"; "arch" ] o
            let! os = required "os" (validated (fun text -> if Token.isToken 64 text then Ok text else Error ReaderCode.InvalidValue)) o
            let! arch = required "arch" (validated (fun text -> if isArch text then Ok text else Error ReaderCode.InvalidValue)) o
            return { Os = os; Arch = arch }
        }

    let private noNul = validated (fun (text: string) -> if text.Contains '\000' then Error ReaderCode.InvalidValue else Ok text)

    let private artifact (n: Node) : Read<ToolReleaseArtifact> =
        result {
            let! o = asObject n
            do! closed [ "targetPath"; "platform"; "archive"; "launch" ] o
            let! targetPath = required "targetPath" relativePath o
            let! platform = required "platform" platform o

            let! archive = objectMember "archive" o
            do! closed [ "format"; "entryPoint" ] archive
            let! _ = required "format" (tag [ "zip", (); "tar-gzip", (); "appimage", (); "raw", () ]) archive
            let! _ = required "entryPoint" relativePath archive

            let! launch = objectMember "launch" o
            do! closed [ "kind"; "path"; "args" ] launch
            let! _ = required "kind" (tag [ "executable", () ]) launch
            let! _ = required "path" relativePath launch
            let! _ = required "args" (array noNul) launch

            return { TargetPath = targetPath; Platform = platform }
        }

    /// The index of the first element equal, as a parsed value, to an earlier one.
    let private firstRepeat (values: Value list) =
        values |> List.indexed |> List.tryFind (fun (index, value) -> values |> List.take index |> List.contains value) |> Option.map fst

    let private body (o: ObjectNode) : Read<ToolRelease> =
        result {
            match tryMember "schemaVersion" o with
            | Some { Value = Value.Number "1" } -> ()
            | Some { Value = Value.Number _; Path = path } -> return! refuse ReaderCode.UnsupportedContractVersion path
            | _ -> return! refuse ReaderCode.InvalidDocument (o.Path.Child "schemaVersion")

            do! closed [ "schemaVersion"; "kind"; "tool"; "version"; "channels"; "status"; "compatibility"; "artifacts" ] o

            let! kind = required "kind" text o

            if kind <> "morphir-tool-release" then
                return! refuse ReaderCode.InvalidValue (o.Path.Child "kind")

            let! tool = objectMember "tool" o
            do! closed [ "id"; "name" ] tool
            let! toolId = required "id" (validated ToolId.tryParse) tool
            let! _ = required "name" nonBlank tool
            let! version = required "version" version o
            let! channels = required "channels" (distinctArray (validated Channel.tryParse)) o

            let! status =
                required "status" (tag [ "active", RecipeStatus.Active; "yanked", RecipeStatus.Yanked; "revoked", RecipeStatus.Revoked ]) o

            let! compatibility = objectMember "compatibility" o
            do! closed [ "morphirCli" ] compatibility
            let! morphirCli = required "morphirCli" (validated (fun text -> NonBlankText.tryParse text |> Result.mapError (fun _ -> ReaderCode.InvalidValue))) compatibility

            let! artifactNodes = required "artifacts" items o
            let! artifacts = artifactNodes |> Result.traverse artifact

            // The schema's uniqueItems: two equal artifacts are refused at the repetition.
            match firstRepeat (artifactNodes |> List.map _.Value) with
            | Some index -> return! refuse ReaderCode.InconsistentDocument ((o.Path.Child "artifacts").At index)
            | None -> ()

            // A yanked or revoked release lists no channel; an active or yanked one lists at least one artifact.
            if status <> RecipeStatus.Active && not channels.IsEmpty then
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "channels")

            if status <> RecipeStatus.Revoked && artifacts.IsEmpty then
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "artifacts")

            return
                { ToolId = toolId
                  Version = version
                  Channels = channels
                  Status = status
                  MorphirCli = morphirCli
                  Artifacts = artifacts }
        }

    /// <summary>Reads a v1 release descriptor from exact bytes, bounded at <see cref="limit"/> before parsing. No
    /// exception leaves it.</summary>
    let read (bytes: byte array) : Result<ToolRelease, ReaderRefusal> =
        try
            if bytes.Length > limit then
                refuse ReaderCode.InvalidDocument DocumentPath.root
            else
                Syntax.parseJson bytes
                |> Result.bind (fun root -> asObject (node root DocumentPath.root))
                |> Result.bind body
        with _ ->
            refuse ReaderCode.InvalidDocument DocumentPath.root

/// <summary>A capability kind of a claim set's <c>extension.types</c>. The protocol reads kinds strictly: any other
/// is refused.</summary>
[<RequireQualifiedAccess>]
type CapabilityKind =
    | Frontend
    | Backend
    | Transform
    | Validator
    | Workspace

/// The identity a claim set states for its extension.
type ClaimedExtension =
    { Id: ExtensionId
      Name: NonBlankText
      Version: SemVer
      Types: CapabilityKind list }

/// What a claim set says about compiling: whether <c>compile</c> is <c>true</c>, and the ids of its languages.
type FrontendClaims = { Compile: bool; Languages: string list }

/// What a claim set says about generating: whether <c>generate</c> is <c>true</c>, and its targets.
type BackendClaims = { Generate: bool; Targets: string list }

/// <summary>
/// A capability claim set (<c>claimsVersion</c> <c>0.1.0-draft.2</c>), as far as the recipe binding checks read it:
/// what the extension claims about itself. It is not evidence that anyone ran the artifact; that is the
/// artifact's claim check, a separate fact.
/// </summary>
type ClaimSet =
    { ProtocolVersions: string list
      Extension: ClaimedExtension
      Frontend: FrontendClaims option
      Backend: BackendClaims option
      /// <summary><c>capabilities.modelTransport.tuples</c> as parsed values, which the binding check compares
      /// exactly.</summary>
      ModelTuples: Value list }

/// <summary>
/// The reader of a capability claim set, at <c>claimsVersion</c> <c>0.1.0-draft.2</c> only (the extension protocol,
/// "Capability claims", at finos/morphir <c>9e1f853b7</c>). <c>extension.types</c> is read strictly;
/// <c>capabilities</c> is carried as the protocol says, and only the members the checks use are read, each refused
/// when it has the wrong shape. Unknown members are ignored. A <c>critical</c> path this reader does not read is
/// refused, so a member that changes meaning is never ignored.
/// </summary>
/// <remarks>Left unread: <c>extension.name</c> beyond being non-blank; every capability member except
/// <c>frontend.compile</c>, <c>frontend.languages[].id</c>, <c>backend.generate</c>, <c>backend.targets</c> and
/// <c>modelTransport.tuples</c>. Provisional, the stricter reading: <c>requires.host</c> is refused (it must be in
/// <c>critical</c>, and no binding check evaluates a requirement on the host), <c>protocolVersions</c> must be
/// non-empty and distinct, and <c>extension.types</c> distinct.</remarks>
module ClaimSetDocument =
    open Reading

    /// The current claim set format; no other is read or converted.
    let claimsVersion = "0.1.0-draft.2"

    /// The member paths this reader reads, which a <c>critical</c> list may name.
    let understoodPaths =
        set
            [ "claimsVersion"
              "protocolVersions"
              "extension"
              "extension.id"
              "extension.name"
              "extension.version"
              "extension.types"
              "capabilities"
              "capabilities.frontend"
              "capabilities.frontend.compile"
              "capabilities.frontend.languages"
              "capabilities.frontend.languages.id"
              "capabilities.backend"
              "capabilities.backend.generate"
              "capabilities.backend.targets"
              "capabilities.modelTransport"
              "capabilities.modelTransport.tuples"
              "critical" ]

    let private boolean (n: Node) : Read<bool> =
        match n.Value with
        | Value.Boolean b -> Ok b
        | _ -> refuse ReaderCode.InvalidDocument n.Path

    let private nonBlank (n: Node) : Read<string> =
        text n |> Result.bind (fun s -> if System.String.IsNullOrWhiteSpace s then refuse ReaderCode.InvalidValue n.Path else Ok s)

    let private kinds =
        [ "frontend", CapabilityKind.Frontend
          "backend", CapabilityKind.Backend
          "transform", CapabilityKind.Transform
          "validator", CapabilityKind.Validator
          "workspace", CapabilityKind.Workspace ]

    let private extension (n: Node) : Read<ClaimedExtension> =
        result {
            let! o = asObject n
            let! id = required "id" extensionId o
            let! name = required "name" (validated NonBlankText.tryParse) o
            let! version = required "version" version o
            let! types = required "types" (distinctArray (tag kinds)) o
            return { Id = id; Name = name; Version = version; Types = types }
        }

    let private frontend (n: Node) : Read<FrontendClaims> =
        result {
            let! o = asObject n
            let! compile = optional "compile" boolean o
            let languageId (l: Node) = asObject l |> Result.bind (required "id" text)
            let! languages = optional "languages" (array languageId) o
            return { Compile = defaultArg compile false; Languages = defaultArg languages [] }
        }

    let private backend (n: Node) : Read<BackendClaims> =
        result {
            let! o = asObject n
            let! generate = optional "generate" boolean o
            let! targets = optional "targets" (array text) o
            return { Generate = defaultArg generate false; Targets = defaultArg targets [] }
        }

    let private tuples (n: Node) : Read<Value list> =
        result {
            let! o = asObject n
            let tuple (t: Node) = asObject t |> Result.map (fun _ -> t.Value)
            let! tuples = optional "tuples" (array tuple) o
            return defaultArg tuples []
        }

    /// The claim set at a node, with refusals under its path.
    let internal readAt (n: Node) : Read<ClaimSet> =
        result {
            let! o = asObject n

            match tryMember "claimsVersion" o with
            | Some { Value = Value.String version } when version = claimsVersion -> ()
            | Some { Value = Value.String _; Path = path } -> return! refuse ReaderCode.UnsupportedContractVersion path
            | _ -> return! refuse ReaderCode.InvalidDocument (o.Path.Child "claimsVersion")

            let! critical = optional "critical" (distinctArray text) o
            let critical = defaultArg critical []

            match critical |> List.tryFind (understoodPaths.Contains >> not) with
            | Some unknown -> return! refuse ReaderCode.UnsupportedCriticalMember (o.Path.Child unknown)
            | None -> ()

            // requires.host is a requirement on the host that must be listed as critical; it is never understood here.
            match tryMember "requires" o with
            | Some requires ->
                let! r = asObject requires

                if has "host" r then
                    return! refuse ReaderCode.InconsistentDocument (o.Path.Child "critical")
            | None -> ()

            let! protocolVersions = required "protocolVersions" (distinctArray nonBlank) o

            if protocolVersions.IsEmpty then
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "protocolVersions")

            let! extension = required "extension" extension o
            let! capabilities = objectMember "capabilities" o
            let! frontend = optional "frontend" frontend capabilities
            let! backend = optional "backend" backend capabilities
            let! modelTuples = optional "modelTransport" tuples capabilities

            return
                { ProtocolVersions = protocolVersions
                  Extension = extension
                  Frontend = frontend
                  Backend = backend
                  ModelTuples = defaultArg modelTuples [] }
        }

    /// <summary>Reads a claim set from a parsed value, with paths from its root. No exception leaves it.</summary>
    let readValue (value: Value) : Result<ClaimSet, ReaderRefusal> =
        try
            readAt (node value DocumentPath.root)
        with _ ->
            refuse ReaderCode.InvalidDocument DocumentPath.root
