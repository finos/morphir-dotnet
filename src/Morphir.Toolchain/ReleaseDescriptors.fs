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
      /// The requirement on the acquiring Morphir CLI, as written: text with a character outside ECMA-262 white space,
      /// as the schema's <c>\S</c> requires. A manager never evaluates it.
      MorphirCli: string
      Artifacts: ToolReleaseArtifact list }

/// <summary>
/// The reader of a tool release metadata v1 release descriptor (the v1 profile and its
/// <c>tool-release-v1.schema.json</c>, at finos/morphir <c>9e1f853b7</c>). Every object is closed, as the schema's
/// <c>additionalProperties: false</c> says; refusals carry the reader codes and the member's path, and the recipe
/// binding check reports any of them as <c>release_descriptor_mismatch</c> at the offering.
/// </summary>
/// <remarks>Read but not used by the checks: <c>tool.name</c>, <c>channels</c>, <c>status</c>,
/// <c>compatibility.morphirCli</c> (validated as the schema's <c>\S</c>, never evaluated), and each artifact's
/// <c>archive</c> and <c>launch</c> (validated in shape). No other version is read: <c>schemaVersion</c> is the number
/// <c>1</c>, in any JSON spelling equal to it, as the schema's <c>const: 1</c> compares numbers (<c>1.0</c>,
/// <c>1e0</c>). Where the v1 profile has no rule of its own the reader follows the schema (review of #583, A6); it
/// keeps the bounds the toolchain contract states: a tool id of at most 182 characters and a version of at most 256
/// characters, with numeric parts of at most 15 digits.</remarks>
module ToolReleaseDocument =
    open Reading

    /// The profile's download bound for a release descriptor, applied before parsing.
    let limit = 1024 * 1024

    /// Fails when an object has a member outside <paramref name="known"/>: the schema's objects are closed.
    let private closed (known: string list) (o: ObjectNode) : Read<unit> =
        match o.Members |> List.tryFind (fun (name, _) -> not (List.contains name known)) with
        | Some(name, _) -> refuse ReaderCode.InvalidDocument (o.Path.Child name)
        | None -> Ok()

    /// ECMA-262 white space and line terminators, the <c>\s</c> of the schema's patterns: TAB, VT, FF, ZWNBSP, the
    /// Zs category, LF, CR, LS and PS. NEL (U+0085) is not among them, though .NET counts it as white space.
    let private isEcmaWhiteSpace (c: char) =
        match int c with
        | 0x09 | 0x0B | 0x0C | 0x20 | 0xA0 | 0xFEFF | 0x1680 | 0x202F | 0x205F | 0x3000 -> true
        | 0x0A | 0x0D | 0x2028 | 0x2029 -> true
        | code -> code >= 0x2000 && code <= 0x200A

    /// The schema's <c>"pattern": "\\S"</c> with <c>minLength</c> 1: some character is not ECMA-262 white space.
    let private schemaText =
        validated (fun (text: string) -> if text |> Seq.exists (isEcmaWhiteSpace >> not) then Ok text else Error ReaderCode.InvalidValue)

    /// <summary>Whether a JSON number spelling is the number 1, as JSON Schema's <c>const</c> compares numbers:
    /// <c>1</c>, <c>1.0</c>, <c>1e0</c> and <c>10e-1</c> are, <c>1.5</c> and <c>-1</c> are not. The digits are compared
    /// exactly, never through a floating-point value.</summary>
    let internal isOne (spelling: string) =
        let mantissa, exponent =
            match spelling.IndexOfAny [| 'e'; 'E' |] with
            | -1 -> spelling, "0"
            | at -> spelling.Substring(0, at), spelling.Substring(at + 1)

        let integer, fraction =
            match mantissa.IndexOf '.' with
            | -1 -> mantissa, ""
            | at -> mantissa.Substring(0, at), mantissa.Substring(at + 1)

        let negative, magnitude =
            match exponent with
            | e when e.StartsWith '-' -> true, e.Substring 1
            | e when e.StartsWith '+' -> false, e.Substring 1
            | e -> false, e

        let magnitude = magnitude.TrimStart '0'
        let digits = (integer + fraction).TrimStart '0'
        let significant = digits.TrimEnd '0'

        // The value is significant x 10^scale; it is 1 exactly when significant is "1" and scale is 0. A minus sign
        // stays in the digits, so a negative number is never "1". A document is at most 1 MiB, so a scale of 0 needs
        // an exponent of fewer than 8 digits.
        significant = "1"
        && magnitude.Length < 8
        && (let exponent = (if magnitude = "" then 0 else int magnitude) * (if negative then -1 else 1)
            exponent - fraction.Length + (digits.Length - significant.Length) = 0)

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
            // The schema's portableToken, which has no length bound; the toolchain contract states none for os.
            let! os = required "os" (validated (fun text -> if Token.isToken System.Int32.MaxValue text then Ok text else Error ReaderCode.InvalidValue)) o
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


    /// <summary>The index of the first element equal, as a parsed value, to an earlier one, in linear time: the
    /// earlier elements are kept in a hash set. A scan of the earlier ones was quadratic within the 1 MiB bound, and
    /// compared member order, which <c>uniqueItems</c> does not (review of #582, R1).</summary>
    let private firstRepeat (values: Value list) = firstRepeatBy ParsedValue.ordered values

    let private body (o: ObjectNode) : Read<ToolRelease> =
        result {
            match tryMember "schemaVersion" o with
            | Some { Value = Value.Number spelling } when isOne spelling -> ()
            | Some { Value = Value.Number _; Path = path } -> return! refuse ReaderCode.UnsupportedContractVersion path
            | _ -> return! refuse ReaderCode.InvalidDocument (o.Path.Child "schemaVersion")

            do! closed [ "schemaVersion"; "kind"; "tool"; "version"; "channels"; "status"; "compatibility"; "artifacts" ] o

            let! kind = required "kind" text o

            if kind <> "morphir-tool-release" then
                return! refuse ReaderCode.InvalidValue (o.Path.Child "kind")

            let! tool = objectMember "tool" o
            do! closed [ "id"; "name" ] tool
            let! toolId = required "id" (validated ToolId.tryParse) tool
            let! _ = required "name" schemaText tool
            let! version = required "version" version o
            let! channels = required "channels" (distinctArray (validated Channel.tryParse)) o

            let! status =
                required "status" (tag [ "active", RecipeStatus.Active; "yanked", RecipeStatus.Yanked; "revoked", RecipeStatus.Revoked ]) o

            let! compatibility = objectMember "compatibility" o
            do! closed [ "morphirCli" ] compatibility
            let! morphirCli = required "morphirCli" schemaText compatibility

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

/// What a claim set says about compiling: whether <c>compile</c> is <c>true</c>, and the ids of its languages, as a set,
/// so a binding check looks one up without scanning the list (review of #583, A1).
type FrontendClaims = { Compile: bool; Languages: Set<string> }

/// What a claim set says about generating: whether <c>generate</c> is <c>true</c>, and its targets, as a set.
type BackendClaims = { Generate: bool; Targets: Set<string> }

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
      /// <summary><c>capabilities.modelTransport.tuples</c> as parsed values, which the binding check compares as
      /// parsed values: member order does not matter, and text and number spelling are compared exactly.</summary>
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
/// <c>modelTransport.tuples</c>. As the toolset recipe page says (finos/morphir <c>837164580</c>): an empty or repeated
/// <c>protocolVersions</c> and a repeated capability kind are read, not refused, so an empty list fails the binding
/// check's protocol row; and <c>requires.host</c>, which must be listed in <c>critical</c>, is refused because no
/// binding row evaluates it (open question Q4).</remarks>
module ClaimSetDocument =
    open Reading

    /// The current claim set format; no other is read or converted.
    let claimsVersion = "0.1.0-draft.2"

    /// The member paths this reader reads, which a <c>critical</c> list may name.
    /// <summary>The claim set paths a <c>critical</c> list may name: exactly the members a binding check reads, as the
    /// toolset recipe page lists them ("What a binding check reads", at finos/morphir <c>837164580</c>). Any other path,
    /// even one a host understands, is refused.</summary>
    let understoodPaths =
        set
            [ "claimsVersion"
              "protocolVersions"
              "extension"
              "extension.id"
              "extension.version"
              "extension.types"
              "capabilities.frontend.compile"
              "capabilities.frontend.languages"
              "capabilities.frontend.languages.id"
              "capabilities.backend.generate"
              "capabilities.backend.targets"
              "capabilities.modelTransport.tuples" ]

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
            // A repeated kind changes no binding outcome and is not refused: whether the list may repeat is the claim set
            // contract's rule (toolset recipe page, at finos/morphir 837164580).
            let! types = required "types" (array (tag kinds)) o
            return { Id = id; Name = name; Version = version; Types = types }
        }

    let private frontend (n: Node) : Read<FrontendClaims> =
        result {
            let! o = asObject n
            let! compile = optional "compile" boolean o
            let languageId (l: Node) = asObject l |> Result.bind (required "id" text)
            let! languages = optional "languages" (array languageId) o
            return { Compile = defaultArg compile false; Languages = set (defaultArg languages []) }
        }

    let private backend (n: Node) : Read<BackendClaims> =
        result {
            let! o = asObject n
            let! generate = optional "generate" boolean o
            let! targets = optional "targets" (array text) o
            return { Generate = defaultArg generate false; Targets = set (defaultArg targets []) }
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

            // An empty list fails the provider-claims-protocol row, not the reading, and a repeated entry is not refused
            // (toolset recipe page, at finos/morphir 837164580).
            let! protocolVersions = required "protocolVersions" (array nonBlank) o

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

/// How an extension artifact runs.
[<RequireQualifiedAccess>]
type ArtifactRuntime =
    | Process
    | Wasm

/// <summary>What a descriptor states about an artifact's claims: <c>unchecked</c> when its writer did not run the
/// artifact to read them, <c>probed</c> when it did. It says nothing about who published the bytes; a recipe digest
/// authenticates those, and the two are separate facts.</summary>
[<RequireQualifiedAccess>]
type DescriptorClaimCheck =
    | Unchecked
    | Probed

/// One artifact of an extension release descriptor.
type DescribedArtifact =
    { Runtime: ArtifactRuntime
      /// The target triple of a process artifact; a WASM artifact has none.
      Platform: string option
      /// The file name, in the descriptor's own directory.
      Filename: string
      Sha256: Sha256
      Claims: ClaimSet
      ClaimCheck: DescriptorClaimCheck }

/// An extension release descriptor, version 2, as far as the recipe binding checks read it.
type ExtensionRelease =
    { ExtensionId: ExtensionId
      ShortId: Token
      Version: SemVer
      Artifacts: DescribedArtifact list }

/// <summary>
/// The reader of an extension release descriptor at <c>schemaVersion</c> <c>2.0.0-draft.2</c> only (extension
/// distribution, "Release descriptor, version 2", at finos/morphir <c>9e1f853b7</c>); no earlier format is read or
/// converted. Unknown members are ignored unless <c>critical</c> names them, as the extension formats say; a critical
/// path this reader does not read is refused.
/// </summary>
/// <remarks>Left unread: <c>gitCommit</c>, <c>platformDifferences</c> and <c>shortId</c> beyond its shape, which no
/// binding check uses. As the toolset recipe page says (finos/morphir <c>837164580</c>): the bound is 1 MiB; a
/// critical path is understood only when it names a member a binding check reads; <c>requires.host</c>, at the top or
/// on an artifact, is refused, because no binding check evaluates it; two artifacts with one filename are refused; and
/// <c>claimCheck</c> is not read, so no value of it is refused and only the word <c>probed</c> reads as probed.</remarks>
module ExtensionReleaseDocument =
    open Reading

    /// The one descriptor format read.
    let schemaVersion = "2.0.0-draft.2"

    /// The bound applied before parsing. Provisional: the distribution page states none.
    let limit = 1024 * 1024

    /// The paths an artifact's own critical list may name: the artifact members a binding check reads.
    let private artifactPaths = [ "runtime"; "platform"; "filename"; "sha256"; "claims" ]

    /// The paths a critical list may name: exactly the members read, written as that list writes them (toolset recipe
    /// page, "Critical paths", at finos/morphir 837164580). A claim set path belongs in the claim set's own list.
    let private understood (prefix: string) (paths: string list) (path: string) =
        List.contains path (paths |> List.map (fun p -> prefix + p))

    /// The critical list and requirement rules of one object.
    let private criticalAndRequires (isUnderstood: string -> bool) (o: ObjectNode) : Read<unit> =
        result {
            let! critical = optional "critical" (distinctArray text) o

            match defaultArg critical [] |> List.tryFind (isUnderstood >> not) with
            | Some unknown -> return! refuse ReaderCode.UnsupportedCriticalMember (o.Path.Child unknown)
            | None -> ()

            match tryMember "requires" o with
            | Some requires ->
                let! r = asObject requires

                if has "host" r then
                    return! refuse ReaderCode.InconsistentDocument (o.Path.Child "critical")
            | None -> ()
        }

    let private isFilename (text: string) =
        not (text.Contains '/') && text <> "." && text <> ".." && Result.isOk (TargetPath.tryParse text)

    let private artifact (n: Node) : Read<DescribedArtifact> =
        result {
            let! o = asObject n
            do! criticalAndRequires (understood "" artifactPaths) o
            let! runtime = required "runtime" (tag [ "process", ArtifactRuntime.Process; "wasm", ArtifactRuntime.Wasm ]) o
            let! platform = optional "platform" text o

            match runtime, platform with
            | ArtifactRuntime.Process, None
            | ArtifactRuntime.Wasm, Some _ -> return! refuse ReaderCode.InconsistentDocument (o.Path.Child "platform")
            | ArtifactRuntime.Process, Some triple when System.String.IsNullOrWhiteSpace triple ->
                return! refuse ReaderCode.InvalidValue (o.Path.Child "platform")
            | _ -> ()

            let! filename = required "filename" (validated (fun text -> if isFilename text then Ok text else Error ReaderCode.InvalidValue)) o
            let! sha = required "sha256" sha256 o
            let! claims = required "claims" ClaimSetDocument.readAt o

            // A binding check does not read claimCheck, so no value of it is refused; only the exact word probed is ever
            // read as probed (toolset recipe page, at finos/morphir 837164580).
            let claimCheck =
                match tryMember "claimCheck" o with
                | Some { Value = Value.String "probed" } -> Some DescriptorClaimCheck.Probed
                | _ -> None

            return
                { Runtime = runtime
                  Platform = platform
                  Filename = filename
                  Sha256 = sha
                  Claims = claims
                  ClaimCheck = defaultArg claimCheck DescriptorClaimCheck.Unchecked }
        }

    let private body (o: ObjectNode) : Read<ExtensionRelease> =
        result {
            match tryMember "schemaVersion" o with
            | Some { Value = Value.String version } when version = schemaVersion -> ()
            | Some { Value = Value.String _; Path = path } -> return! refuse ReaderCode.UnsupportedContractVersion path
            | _ -> return! refuse ReaderCode.InvalidDocument (o.Path.Child "schemaVersion")

            let topPaths = [ "schemaVersion"; "extensionId"; "version"; "artifacts" ]

            let isUnderstood (path: string) =
                List.contains path topPaths || understood "artifacts." artifactPaths path

            do! criticalAndRequires isUnderstood o
            let! extensionId = required "extensionId" extensionId o
            let! shortId = required "shortId" token o
            let! version = required "version" version o
            let! artifactNodes = required "artifacts" items o

            if artifactNodes.IsEmpty then
                return! refuse ReaderCode.InconsistentDocument (o.Path.Child "artifacts")

            let! artifacts = artifactNodes |> Result.traverse artifact

            // One filename names one artifact in the descriptor's directory.
            match firstRepeatBy _.Filename artifacts with
            | Some index -> return! refuse ReaderCode.InconsistentDocument (((o.Path.Child "artifacts").At index).Child "filename")
            | None -> ()

            return
                { ExtensionId = extensionId
                  ShortId = shortId
                  Version = version
                  Artifacts = artifacts }
        }

    /// <summary>Reads a descriptor from exact bytes, bounded at <see cref="limit"/> before parsing. No exception
    /// leaves it.</summary>
    let read (bytes: byte array) : Result<ExtensionRelease, ReaderRefusal> =
        try
            if bytes.Length > limit then
                refuse ReaderCode.InvalidDocument DocumentPath.root
            else
                Syntax.parseJson bytes
                |> Result.bind (fun root -> asObject (node root DocumentPath.root))
                |> Result.bind body
        with _ ->
            refuse ReaderCode.InvalidDocument DocumentPath.root
