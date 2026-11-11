// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// TEST-ONLY. The deterministic generator of the signed toolchain repository, ported from finos/morphir's
/// <c>crates/morphir-toolchain-contract/tests/support/repository.rs</c> at <c>dde74f94</c>, so the committed fixtures
/// can be checked against it after that crate is removed. Every signing key is the Ed25519 key whose 32-byte seed is the
/// SHA-256 of a public UTF-8 label, so anyone can recompute it: these keys authenticate nothing outside the tests.
/// <para>Every input is the generator's own: the labels, constants, offerings, variants and edits are carried over from
/// that file, never read from the committed output. Targets are this profile's canonical JSON, as there; the reference
/// TUF encoder is a test-only differential encoder for the <c>keyOrderReferenceTuf</c> set and the
/// <c>canonical-json</c> cases, and is not this profile's encoding.</para>
/// </summary>
module Morphir.Toolchain.Tests.FixtureGenerator

open System
open System.Collections.Generic
open System.Text
open System.Text.Json.Nodes
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Math.EC.Rfc8032
open Morphir.Toolchain

[<Literal>]
let KeyLabelPrefix = "morphir-toolchain-contract TEST-ONLY fixture key: "

let SpecVersion = "1.0.36"
let Never = "2999-01-01T00:00:00Z"
let ReferenceTime = "2030-01-01T00:00:00Z"
let HostVersion = "1.2.3"
/// What an offering's version 1 descriptor publishes in compatibility.morphirCli: no version satisfies it.
let OfferingMorphirCli = "<0.0.0-0"

// ----- JSON values, built and edited as serde_json's json! values are -------------------------------------------------

let s (text: string) : JsonNode = JsonValue.Create text |> nonNull :> JsonNode
let n (value: int64) : JsonNode = JsonValue.Create value :> JsonNode
let b (value: bool) : JsonNode = JsonValue.Create value :> JsonNode

let o (members: (string * JsonNode) list) : JsonNode =
    let node = JsonObject()

    for name, value in members do
        node[name] <- value

    node :> JsonNode

let a (items: JsonNode list) : JsonNode = JsonArray(Array.ofList items) :> JsonNode
let strings (items: string list) = a (items |> List.map s)
let copy (node: JsonNode) = node.DeepClone()

/// The member at a path, which must exist.
let rec at (path: string list) (node: JsonNode) : JsonNode =
    match path with
    | [] -> node
    | head :: rest ->
        let child =
            match node with
            | :? JsonArray as array -> array[int head]
            | _ -> node.AsObject()[head]

        at rest (nonNull child)

/// Sets the member at a path; a missing last member is added, as serde_json's index assignment does.
let set (path: string list) (value: JsonNode) (node: JsonNode) =
    let parent = at (List.take (path.Length - 1) path) node
    let last = List.last path

    match parent with
    | :? JsonArray as array -> array[int last] <- value
    | _ -> parent.AsObject()[last] <- value

let toValue (node: JsonNode) =
    match Syntax.parseJson (Encoding.UTF8.GetBytes(node.ToJsonString())) with
    | Ok value -> value
    | Error refusal -> failwithf "a generated value does not parse: %A" refusal

/// This profile's canonical JSON, the one targets and signatures use.
let canonical (node: JsonNode) =
    match Canonical.bytes (toValue node) with
    | Ok bytes -> bytes
    | Error refusal -> failwithf "fixture JSON is not canonical: %A" refusal

let canonicalText (node: JsonNode) = Encoding.UTF8.GetString(canonical node)
let digestOfBytes (bytes: byte array) = (Sha256.ofBytes bytes).Text
let digestOfText (text: string) = digestOfBytes (Encoding.UTF8.GetBytes text)

/// <summary>TEST-ONLY. The canonical JSON securesystemslib's <c>encode_canonical</c> (python-tuf) writes: object
/// members sorted by their raw keys (as Rust sorts strings, by their UTF-8 bytes), strings not normalized, and only
/// <c>"</c> and <c>\</c> escaped. It differs from this profile's encoding in key order when a key is a prefix of another
/// followed by a character below <c>"</c>, or holds <c>"</c> or <c>\</c>, and in NFC. Ported from
/// <c>reference_tuf_canonical</c>; it is differential evidence, not an encoding this repository adopts.</summary>
let referenceTufCanonical (value: Value) : byte array =
    let out = StringBuilder()

    let str (text: string) =
        out.Append '"' |> ignore

        for c in text do
            if c = '"' || c = '\\' then out.Append '\\' |> ignore
            out.Append c |> ignore

        out.Append '"' |> ignore

    let rec write (value: Value) =
        match value with
        | Value.Null -> out.Append "null" |> ignore
        | Value.Boolean flag -> out.Append(if flag then "true" else "false") |> ignore
        | Value.Number spelling -> out.Append spelling |> ignore
        | Value.String text -> str text
        | Value.Array items ->
            out.Append '[' |> ignore

            items
            |> List.iteri (fun position item ->
                if position > 0 then out.Append ',' |> ignore
                write item)

            out.Append ']' |> ignore
        | Value.Object members ->
            out.Append '{' |> ignore

            members
            |> List.sortWith (fun (left, _) (right, _) -> compareUtf8 left right)
            |> List.iteri (fun position (key, item) ->
                if position > 0 then out.Append ',' |> ignore
                str key
                out.Append ':' |> ignore
                write item)

            out.Append '}' |> ignore
        | Value.Other kind -> failwithf "no JSON value for %s" kind

    write value
    Encoding.UTF8.GetBytes(out.ToString())

// ----- keys ---------------------------------------------------------------------------------------------------------

type FixtureKey =
    { Role: string
      Label: string
      Seed: byte array
      PublicHex: string }

    member this.TufKey =
        o [ "keytype", s "ed25519"; "scheme", s "ed25519"; "keyval", o [ "public", s this.PublicHex ] ]

    member this.KeyId = digestOfBytes (canonical this.TufKey)

let deriveKey (role: string) (name: string) =
    let label = KeyLabelPrefix + name
    let seed = Convert.FromHexString((Sha256.ofBytes (Encoding.UTF8.GetBytes label)).Text)

    { Role = role
      Label = label
      Seed = seed
      PublicHex = Convert.ToHexStringLower(Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded()) }

type Keys =
    { Root: FixtureKey list
      Targets: FixtureKey
      Snapshot: FixtureKey
      Timestamp: FixtureKey }

    member this.All = this.Root @ [ this.Targets; this.Snapshot; this.Timestamp ]

let deriveKeys () =
    { Root = [ deriveKey "root" "root-1"; deriveKey "root" "root-2" ]
      Targets = deriveKey "targets" "targets-1"
      Snapshot = deriveKey "snapshot" "snapshot-1"
      Timestamp = deriveKey "timestamp" "timestamp-1" }

let private ed25519 (key: FixtureKey) (message: byte array) =
    let signature = Array.zeroCreate<byte> 64
    Ed25519PrivateKeyParameters(key.Seed, 0).Sign(Ed25519.Algorithm.Ed25519, null, message, 0, message.Length, signature, 0)
    Convert.ToHexStringLower signature

/// Signs signed over the bytes encode gives for it.
let signEncoded (signed: JsonNode) (keys: FixtureKey list) (encode: JsonNode -> byte array) =
    let message = encode signed
    o [ "signatures", a [ for key in keys -> o [ "keyid", s key.KeyId; "sig", s (ed25519 key message) ] ]; "signed", signed ]

let sign (signed: JsonNode) (keys: FixtureKey list) = signEncoded signed keys canonical

let private role (keys: FixtureKey list) (threshold: int64) =
    o [ "keyids", strings [ for key in keys -> key.KeyId ]; "threshold", n threshold ]

let root (keys: Keys) =
    let signed =
        o [ "_type", s "root"
            "spec_version", s SpecVersion
            "version", n 1L
            "expires", s Never
            "consistent_snapshot", b true
            "keys", o [ for key in keys.All -> key.KeyId, key.TufKey ]
            "roles",
            o [ "root", role keys.Root 2L
                "targets", role [ keys.Targets ] 1L
                "snapshot", role [ keys.Snapshot ] 1L
                "timestamp", role [ keys.Timestamp ] 1L ] ]

    sign signed keys.Root

let private metaOf (version: JsonNode) (bytes: byte array) =
    o [ "version", copy version; "length", n (int64 bytes.Length); "hashes", o [ "sha256", s (digestOfBytes bytes) ] ]

let meta (envelope: JsonNode) = metaOf (at [ "signed"; "version" ] envelope) (canonical envelope)

// ----- targets ------------------------------------------------------------------------------------------------------

/// One published target: its bytes and optional custom.morphir metadata.
type Target = { Bytes: string; Custom: JsonNode option }

type Targets = Dictionary<string, Target>

let newTargets () = Targets(StringComparer.Ordinal)

let cloneTargets (targets: Targets) =
    let clone = newTargets ()

    for KeyValue(path, target) in targets do
        clone[path] <- { target with Custom = target.Custom |> Option.map copy }

    clone

let digestOf (targets: Targets) (path: string) = digestOfText targets[path].Bytes

/// Signs a complete metadata set (targets, snapshot, timestamp) at a version, with the targets signed object edited
/// first and signed over the bytes encode gives for it.
let metadataWith (keys: Keys) (targets: Targets) (version: int64) (timestampExpires: string) (edit: JsonNode -> unit) (encode: JsonNode -> byte array) =
    // Publication refuses a target path that is not NFC: the canonical encoder would sign it under its NFC spelling.
    for path in targets.Keys do
        if not (Nfc.isNormalized path) then failwithf "%s is not NFC" path

    let entries =
        o
            [ for KeyValue(path, target) in targets ->
                  let entry = o [ "length", n (int64 (Encoding.UTF8.GetByteCount target.Bytes)); "hashes", o [ "sha256", s (digestOfText target.Bytes) ] ]

                  match target.Custom with
                  | Some custom -> set [ "custom" ] (o [ "morphir", copy custom ]) entry
                  | None -> ()

                  path, entry ]

    let targetsSigned =
        o [ "_type", s "targets"; "spec_version", s SpecVersion; "version", n version; "expires", s Never; "targets", entries ]

    edit targetsSigned
    let targets = signEncoded targetsSigned [ keys.Targets ] encode

    let snapshot =
        sign
            (o [ "_type", s "snapshot"; "spec_version", s SpecVersion; "version", n version; "expires", s Never; "meta", o [ "targets.json", meta targets ] ])
            [ keys.Snapshot ]

    let timestamp =
        sign
            (o [ "_type", s "timestamp"; "spec_version", s SpecVersion; "version", n version; "expires", s timestampExpires; "meta", o [ "snapshot.json", meta snapshot ] ])
            [ keys.Timestamp ]

    o [ "timestamp", timestamp; "snapshot", snapshot; "targets", targets ]

let metadata keys targets version expires = metadataWith keys targets version expires ignore canonical

// ----- host offerings -----------------------------------------------------------------------------------------------

type Offering = { Deployment: string; ToolId: string; Name: string }

/// The three separately published archive offerings. dotnet-tool is published as a NuGet package.
let ArchiveOfferings =
    [ { Deployment = "native-aot"; ToolId = "morphir-dotnet-native-aot"; Name = "Morphir .NET Native AOT (TEST-ONLY fixture)" }
      { Deployment = "self-contained"; ToolId = "morphir-dotnet-self-contained"; Name = "Morphir .NET self-contained (TEST-ONLY fixture)" }
      { Deployment = "framework-dependent"; ToolId = "morphir-dotnet-framework-dependent"; Name = "Morphir .NET framework-dependent (TEST-ONLY fixture)" } ]

/// A one-platform release whose artifact metadata disagrees with it.
let MismatchedOffering =
    { Deployment = "native-aot"; ToolId = "morphir-dotnet-metadata-mismatch"; Name = "Morphir .NET artifact metadata mismatch (TEST-ONLY fixture)" }

let ToolPackage = "packages/nuget/morphir.tool/1.2.3/morphir.tool.1.2.3.nupkg"

let platform (name: string) =
    match Platform.tryParse name with
    | Ok p -> p
    | Error _ -> failwithf "no platform %s" name

/// The first-delivery platforms, in their order.
let platforms () = [ "win-x64"; "win-arm64"; "osx-x64"; "osx-arm64"; "linux-x64"; "linux-arm64" ] |> List.map platform

let hostTarget (toolId: string) (p: Platform) =
    let extension = if Platform.isWindows p then "zip" else "tar.gz"
    $"artifacts/{toolId}/{HostVersion}/{Platform.name p}.{extension}"

let synthetic (what: string) = $"TEST-ONLY synthetic {what}. Not a released download.\n"

let v1Platform (p: Platform) =
    match RecipeBindings.toolPlatform p with
    | Some(os, arch) -> o [ "os", s os; "arch", s arch ]
    | None -> failwith "first-delivery platforms have v1 spellings"

let addToolReleaseWith (targets: Targets) (offering: Offering) (on: Platform list) (metadataPlatform: Platform -> Platform) (edit: JsonNode -> JsonNode -> unit) =
    let artifacts =
        [ for p in on ->
              let executable = if Platform.isWindows p then "morphir.exe" else "morphir"

              o [ "targetPath", s (hostTarget offering.ToolId p)
                  "platform", v1Platform p
                  "archive", o [ "format", s (if Platform.isWindows p then "zip" else "tar-gzip"); "entryPoint", s executable ]
                  "launch", o [ "kind", s "executable"; "path", s executable; "args", a [] ] ] ]

    // Release descriptors record the state at initial publication.
    let descriptor =
        o [ "schemaVersion", n 1L
            "kind", s "morphir-tool-release"
            "tool", o [ "id", s offering.ToolId; "name", s offering.Name ]
            "version", s HostVersion
            "channels", strings [ "stable" ]
            "status", s "active"
            "compatibility", o [ "morphirCli", s OfferingMorphirCli ]
            "artifacts", a artifacts ]

    let custom =
        o [ "schemaVersion", n 1L
            "kind", s "tool-release"
            "toolId", s offering.ToolId
            "version", s HostVersion
            "channels", strings [ "stable" ]
            "status", s "active"
            "compatibility", o [ "morphirCli", s OfferingMorphirCli ]
            "platforms", a [ for p in on -> v1Platform p ] ]

    edit descriptor custom
    targets[$"releases/{offering.ToolId}/{HostVersion}.json"] <- { Bytes = canonicalText descriptor; Custom = Some custom }

    for p in on do
        targets[hostTarget offering.ToolId p] <-
            { Bytes = synthetic $"{offering.ToolId} {HostVersion} {Platform.name p} archive"
              Custom =
                Some(
                    o [ "schemaVersion", n 1L
                        "kind", s "tool-artifact"
                        "toolId", s offering.ToolId
                        "version", s HostVersion
                        "platform", v1Platform (metadataPlatform p) ]
                ) }

let addToolRelease targets offering on metadataPlatform =
    addToolReleaseWith targets offering on metadataPlatform (fun _ _ -> ())

let modelFormat () = o [ "profile", s "morphir-ir"; "codec", s "morphir-json"; "irVersion", s "4.0.0" ]

let builtInGenerate () =
    o [ "operation", s "generate"; "target", s "fsharp"; "extensionId", s "morphir.fsharp"; "version", s HostVersion; "modelInput", modelFormat () ]

/// The in-process F# frontend of the managed offerings. The Native AOT offering has none.
let BuiltInFrontendId = "morphir-dotnet.fsharp"

let builtInCompile () =
    o [ "operation", s "compile"; "language", s "fsharp"; "extensionId", s BuiltInFrontendId; "version", s HostVersion; "modelOutput", modelFormat () ]

let builtIns (deployment: string) =
    if deployment = "native-aot" then a [ builtInGenerate () ] else a [ builtInCompile (); builtInGenerate () ]

let glibc () =
    o [ "kind", s "native"; "name", s "glibc"; "versions", strings [ ">=2.28.0" ]; "platforms", strings [ "linux-x64"; "linux-arm64" ] ]

let runtime () =
    o [ "kind", s "runtime"; "name", s "Microsoft.NETCore.App"; "versions", strings [ ">=10.0.0"; "<11.0.0" ] ]

let sdk () =
    o [ "kind", s "sdk"; "name", s "Microsoft.NET.Sdk"; "versions", strings [ ">=10.0.100" ]; "phase", s "install" ]

let archiveOffering (targets: Targets) (offering: Offering) (on: Platform list) =
    let platformBindings =
        o [ for p in on ->
                let target = hostTarget offering.ToolId p
                Platform.name p, o [ "target", s target; "sha256", s (digestOf targets target) ] ]

    let prerequisites =
        if offering.Deployment = "framework-dependent" then a [ runtime (); glibc () ] else a [ glibc () ]

    o [ "implementation", s "dotnet"
        "version", s HostVersion
        "deployment", s offering.Deployment
        "execution", s (if offering.Deployment = "native-aot" then "native-aot" else "jit")
        "source", o [ "kind", s "tool-release"; "toolId", s offering.ToolId ]
        "protocolVersions", strings [ "0.2.0-draft.1" ]
        "wasmAbis", a []
        "prerequisites", prerequisites
        "builtIns", builtIns offering.Deployment
        "platforms", platformBindings ]

/// The recipe host offerings, bound to the targets already published.
let hostOfferings (targets: Targets) =
    let packageDigest = digestOf targets ToolPackage

    let tool =
        o [ "implementation", s "dotnet"
            "version", s HostVersion
            "deployment", s "dotnet-tool"
            "execution", s "jit"
            "source", o [ "kind", s "dotnet-tool"; "packageId", s "Morphir.Tool"; "command", s "dotnet-morphir" ]
            "protocolVersions", strings [ "0.2.0-draft.1" ]
            "wasmAbis", a []
            "prerequisites", a [ runtime (); sdk () ]
            "builtIns", builtIns "dotnet-tool"
            "platforms", o [ for p in platforms () -> Platform.name p, o [ "target", s ToolPackage; "sha256", s packageDigest ] ] ]

    [ for offering in ArchiveOfferings -> archiveOffering targets offering (platforms ()) ] @ [ tool ]

let recipePath (preset: string) (version: string) = $"toolchains/recipes/{preset}/{version}.json"

/// Publishes a recipe document as an authenticated target; custom adjusts its authenticated state.
let addRecipe (targets: Targets) (recipe: JsonNode) (channels: string list) (status: string) (custom: JsonNode -> unit) =
    let preset = (at [ "preset" ] recipe).GetValue<string>()
    let version = (at [ "version" ] recipe).GetValue<string>()

    let state =
        o [ "kind", s "toolset-recipe"
            "schemaVersion", s "0.1.0-draft.1"
            "preset", s preset
            "version", s version
            "channels", strings channels
            "status", s status ]

    custom state
    targets[recipePath preset version] <- { Bytes = canonicalText recipe; Custom = Some state }

/// A recipe that maps the four offerings and keeps the built-in F# generator.
let offeringRecipe (targets: Targets) (preset: string) =
    o [ "schemaVersion", s "0.1.0-draft.1"
        "preset", s preset
        "version", s "1.0.0"
        "operations", a [ o [ "operation", s "generate"; "target", s "fsharp" ] ]
        "hosts", a (hostOfferings targets)
        "providers", a [] ]

// ----- F# extension releases ----------------------------------------------------------------------------------------

let FrontendId = "morphir-fsharp-frontend"
let FrontendVersion = "0.6.0"
let FrontendDir = "extensions/fsharp-frontend/0.6.0"
let FrontendPackage = "packages/nuget/morphir.frontend.fsharp.extension/0.6.0/morphir.frontend.fsharp.extension.0.6.0.nupkg"
let BackendId = "morphir-fsharp-backend"
let BackendVersion = "0.1.0"
let BackendDir = "extensions/fsharp-backend/0.1.0"
let ListedDigestDir = "extensions/fsharp-frontend-listed-digest/0.6.0"
let WasmDir = "extensions/fsharp-frontend-wasm/0.6.0"
let WasmFile = "morphir-bytes-v1.wasm"
let NotAReleaseTool = "morphir-dotnet-not-a-release"

/// Host releases that are each wrong in one way: the preset of the recipe that binds each, its tool, and how its
/// descriptor and then its release target state differ from an offering's.
let HostReleaseVariants: (string * Offering * (JsonNode -> JsonNode -> unit)) list =
    [ "negative-host-release-status",
      { Deployment = "native-aot"; ToolId = "morphir-dotnet-unknown-status"; Name = "Morphir .NET unknown release status (TEST-ONLY fixture)" },
      (fun _ custom -> set [ "status" ] (s "withdrawn") custom)
      "negative-host-release-tool-id",
      { Deployment = "native-aot"; ToolId = "morphir-dotnet-other-tool-id"; Name = "Morphir .NET descriptor for another tool (TEST-ONLY fixture)" },
      (fun descriptor _ -> set [ "tool"; "id" ] (s "morphir-dotnet-native-aot") descriptor)
      "negative-host-release-version",
      { Deployment = "native-aot"; ToolId = "morphir-dotnet-other-version"; Name = "Morphir .NET descriptor for another version (TEST-ONLY fixture)" },
      (fun descriptor _ -> set [ "version" ] (s "1.2.4") descriptor)
      "negative-host-release-cli-range",
      { Deployment = "native-aot"; ToolId = "morphir-dotnet-cli-range"; Name = "Morphir .NET caret CLI requirement (TEST-ONLY fixture)" },
      (fun descriptor custom ->
          set [ "compatibility"; "morphirCli" ] (s "^0.4.0") descriptor
          set [ "compatibility"; "morphirCli" ] (s "^0.4.0") custom) ]

/// A version 1 tool release that is not a .NET offering, which the version 1 client selects.
let V1Control = { Deployment = "native-aot"; ToolId = "morphir-v1-control"; Name = "Morphir version 1 client control (TEST-ONLY fixture)" }
let V1ControlMorphirCli = ">=0.4.0-0, <0.5.0"
let ChannelPreset = "channel-example"

let ChannelRecipes =
    [ "1.0.0", [ "stable" ], "active"
      "1.1.0", [ "stable" ], "active"
      "1.2.0", [ "stable" ], "yanked"
      "1.3.0", [ "stable" ], "revoked"
      "2.0.0-rc.1", [ "stable"; "preview" ], "active"
      "2.0.0-preview.2", [ "preview/next" ], "active" ]

let extensionArtifact (dir: string) (p: Platform) = $"{dir}/{Platform.name p}.zip"

let tuple (method': string) (ir: string) =
    o [ "operation", s method'; "profile", s "morphir-ir"; "codec", s "morphir-json"; "envelopeVersion", s "0.1.0-draft.1"; "irVersion", s ir ]

/// What an extension release's artifacts claim.
type Claims =
    { Id: string
      Types: string list
      Protocols: string list
      Capabilities: JsonNode }

let frontendClaims () =
    { Id = FrontendId
      Types = [ "frontend" ]
      Protocols = [ "0.2.0-draft.1" ]
      Capabilities =
        o [ "frontend", o [ "languages", a [ o [ "id", s "fsharp"; "fileExtensions", strings [ ".fs" ] ] ]; "compile", b true ]
            "modelTransport", o [ "tuples", a [ tuple "morphir.frontend.compileModel" "4.0.0" ] ] ] }

let backendClaims () =
    { Id = BackendId
      Types = [ "backend" ]
      Protocols = [ "0.2.0-draft.1" ]
      Capabilities =
        o [ "backend", o [ "targets", strings [ "fsharp" ]; "generate", b true ]
            "modelTransport", o [ "tuples", a [ tuple "morphir.backend.generateModel" "4.0.0" ] ] ] }

/// One artifact of a version-2 extension release.
type ReleaseArtifact =
    | Process of Platform
    | Wasm of filename: string

/// Rust's trim_start_matches: every leading repetition of the prefix is removed.
let private trimStartMatches (prefix: string) (text: string) =
    let mutable rest = text

    while rest.StartsWith(prefix, StringComparison.Ordinal) do
        rest <- rest.Substring prefix.Length

    rest

/// Publishes a version-2 extension release descriptor with the listed artifacts; edit adjusts the descriptor first.
let addRelease (targets: Targets) (dir: string) (storeId: string) (version: string) (claims: Claims) (artifacts: ReleaseArtifact list) (edit: JsonNode -> unit) =
    let entries =
        [ for artifact in artifacts ->
              let filename, what =
                  match artifact with
                  | Process p -> $"{Platform.name p}.zip", $"{Platform.name p} process archive"
                  | Wasm filename -> filename, "portable WASM module"

              let path = $"{dir}/{filename}"
              targets[path] <- { Bytes = synthetic $"{storeId} {version} {what}"; Custom = None }

              let entry =
                  o [ "runtime", s "process"
                      "filename", s filename
                      "sha256", s (digestOf targets path)
                      "claims",
                      o [ "claimsVersion", s "0.1.0-draft.2"
                          "protocolVersions", strings claims.Protocols
                          "extension", o [ "id", s claims.Id; "name", s $"{claims.Id} (TEST-ONLY fixture)"; "version", s version; "types", strings claims.Types ]
                          "capabilities", copy claims.Capabilities ]
                      "claimCheck", s "unchecked" ]

              match artifact with
              | Process p -> set [ "platform" ] (s (RecipeBindings.targetTriple p)) entry
              | Wasm _ -> set [ "runtime" ] (s "wasm") entry

              entry ]

    let descriptor =
        o [ "schemaVersion", s "2.0.0-draft.2"
            "extensionId", s storeId
            "shortId", s (trimStartMatches "morphir-" storeId)
            "version", s version
            "platformDifferences", s "none"
            "artifacts", a entries ]

    edit descriptor
    targets[$"{dir}/release.json"] <- { Bytes = canonicalText descriptor; Custom = None }

let addExtensionRelease targets dir storeId version claims (on: Platform list) =
    addRelease targets dir storeId version claims (on |> List.map Process) ignore

let processPlatforms (targets: Targets) (dir: string) (on: Platform list) =
    o [ for p in on ->
            let path = extensionArtifact dir p
            Platform.name p, o [ "artifactTarget", s path; "artifactSha256", s (digestOf targets path) ] ]

let descriptorBinding (targets: Targets) (dir: string) =
    let path = $"{dir}/release.json"
    path, digestOf targets path

let frontendProvider (targets: Targets) (repository: string) (dir: string) (on: Platform list) =
    let descriptor, digest = descriptorBinding targets dir

    o [ "operation", s "compile"; "language", s "fsharp"; "delivery", s "process"
        "extensionId", s FrontendId; "version", s FrontendVersion
        "repositoryId", s repository
        "descriptorTarget", s descriptor
        "descriptorSha256", s digest
        "protocolVersion", s "0.2.0-draft.1"
        "modelTuple", tuple "morphir.frontend.compileModel" "4.0.0"
        "prerequisites", a [ glibc () ]
        "platforms", processPlatforms targets dir on ]

let toolFrontendProvider (targets: Targets) (repository: string) (dir: string) =
    let descriptor, digest = descriptorBinding targets dir

    o [ "operation", s "compile"; "language", s "fsharp"; "delivery", s "dotnet-tool"
        "extensionId", s FrontendId; "version", s FrontendVersion
        "repositoryId", s repository
        "descriptorTarget", s descriptor
        "descriptorSha256", s digest
        "packageId", s "Morphir.Frontend.FSharp.Extension"
        "command", s "morphir-fsharp-frontend"
        "packageTarget", s FrontendPackage
        "packageSha256", s (digestOf targets FrontendPackage)
        "protocolVersion", s "0.2.0-draft.1"
        "modelTuple", tuple "morphir.frontend.compileModel" "4.0.0"
        "prerequisites", a [ runtime (); sdk () ] ]

let wasmFrontendProvider (targets: Targets) (repository: string) (artifact: string) =
    let descriptor, digest = descriptorBinding targets WasmDir

    o [ "operation", s "compile"; "language", s "fsharp"; "delivery", s "wasm"
        "abi", s "morphir-bytes-v1"
        "extensionId", s FrontendId; "version", s FrontendVersion
        "repositoryId", s repository
        "descriptorTarget", s descriptor; "descriptorSha256", s digest
        "artifactTarget", s artifact; "artifactSha256", s (digestOf targets artifact)
        "protocolVersion", s "0.2.0-draft.1"
        "modelTuple", tuple "morphir.frontend.compileModel" "4.0.0" ]

let backendProvider (targets: Targets) (repository: string) (dir: string) (on: Platform list) =
    let descriptor, digest = descriptorBinding targets dir

    o [ "operation", s "generate"; "target", s "fsharp"; "delivery", s "process"
        "extensionId", s BackendId; "version", s BackendVersion
        "repositoryId", s repository
        "descriptorTarget", s descriptor
        "descriptorSha256", s digest
        "protocolVersion", s "0.2.0-draft.1"
        "modelTuple", tuple "morphir.backend.generateModel" "4.0.0"
        "prerequisites", a [ glibc () ]
        "platforms", processPlatforms targets dir on ]

/// The F# recipe: the four offerings, the F# frontend as a process guest and as the .NET tool, and the F# backend.
let fsharpRecipe (targets: Targets) (preset: string) (repository: string) =
    o [ "schemaVersion", s "0.1.0-draft.1"
        "preset", s preset
        "version", s "1.0.0"
        "operations", a [ o [ "operation", s "compile"; "language", s "fsharp" ]; o [ "operation", s "generate"; "target", s "fsharp" ] ]
        "hosts", a (hostOfferings targets)
        "providers",
        a [ frontendProvider targets repository FrontendDir (platforms ())
            toolFrontendProvider targets repository FrontendDir
            backendProvider targets repository BackendDir (platforms ()) ] ]

/// The repository identity, which the F# recipe's providers name.
let repositoryId () = digestOfBytes (canonical (at [ "signed" ] (root (deriveKeys ()))))

let private withClaims (edit: Claims -> Claims) = edit (frontendClaims ())

/// Frontend releases whose claims are each wrong in exactly one way, and the preset that binds each.
let claimVariants () =
    [ "negative-claims-language",
      "extensions/fsharp-frontend-elm-claims/0.6.0",
      FrontendId,
      withClaims (fun claims ->
          let capabilities = copy claims.Capabilities
          set [ "frontend"; "languages" ] (a [ o [ "id", s "elm"; "fileExtensions", strings [ ".elm" ] ] ]) capabilities
          { claims with Capabilities = capabilities })
      "negative-claims-tuple",
      "extensions/fsharp-frontend-ir3-claims/0.6.0",
      FrontendId,
      withClaims (fun claims ->
          let capabilities = copy claims.Capabilities
          set [ "modelTransport"; "tuples" ] (a [ tuple "morphir.frontend.compileModel" "3.0.0" ]) capabilities
          { claims with Capabilities = capabilities })
      "negative-claims-kind", "extensions/fsharp-frontend-backend-kind/0.6.0", FrontendId, withClaims (fun claims -> { claims with Types = [ "backend" ] })
      "negative-claims-protocol",
      "extensions/fsharp-frontend-other-protocol/0.6.0",
      FrontendId,
      withClaims (fun claims -> { claims with Protocols = [ "0.2.0" ] })
      "negative-claims-identity",
      "extensions/fsharp-frontend-claims-identity/0.6.0",
      FrontendId,
      withClaims (fun claims -> { claims with Id = "morphir-other-frontend" })
      "negative-descriptor-identity",
      "extensions/other-frontend/0.6.0",
      "morphir-other-frontend",
      withClaims (fun claims -> { claims with Id = "morphir-other-frontend" })
      "negative-claims-no-protocol", "extensions/fsharp-frontend-no-protocol/0.6.0", FrontendId, withClaims (fun claims -> { claims with Protocols = [] }) ]

/// Frontend releases whose descriptor is wrong in one way: the preset, the directory, its platforms and the edit.
let DescriptorVariants: (string * string * string list * (JsonNode -> unit)) list =
    [ "negative-descriptor-later-draft",
      "extensions/fsharp-frontend-later-draft/0.6.0",
      [ "win-x64" ],
      (fun descriptor -> set [ "schemaVersion" ] (s "2.0.0-draft.3") descriptor)
      // The first draft's member names, which the existing reader converts and a binding check never reads.
      "negative-descriptor-earlier-draft",
      "extensions/fsharp-frontend-earlier-draft/0.6.0",
      [ "win-x64" ],
      (fun descriptor ->
          set [ "schemaVersion" ] (s "2.0.0-draft.1") descriptor

          for artifact in (at [ "artifacts" ] descriptor).AsArray() do
              let entry = (nonNull artifact).AsObject()
              let claims = nonNull entry["claims"]
              entry.Remove "claims" |> ignore
              let claimSet = claims.AsObject()
              claimSet.Remove "claimsVersion" |> ignore
              claimSet["statementVersion"] <- s "0.1.0-draft.1"
              entry["statement"] <- claims
              entry.Remove "claimCheck" |> ignore
              entry["statementSource"] <- s "declared")
      "negative-claims-critical-unread",
      "extensions/fsharp-frontend-critical-unread/0.6.0",
      [ "win-x64" ],
      (fun descriptor ->
          let claims = at [ "artifacts"; "0"; "claims" ] descriptor
          set [ "capabilities"; "frontend"; "incremental" ] (b false) claims
          set [ "critical" ] (strings [ "capabilities.frontend.incremental" ]) claims)
      "negative-descriptor-duplicate-filename",
      "extensions/fsharp-frontend-duplicate-filename/0.6.0",
      [ "win-x64"; "osx-x64" ],
      (fun descriptor ->
          let first = copy (at [ "artifacts"; "0"; "filename" ] descriptor)
          set [ "artifacts"; "1"; "filename" ] first descriptor)
      "negative-claims-per-platform",
      "extensions/fsharp-frontend-claims-per-platform/0.6.0",
      [ "win-x64"; "osx-x64" ],
      (fun descriptor ->
          set [ "artifacts"; "0"; "claims"; "capabilities"; "frontend"; "languages" ] (a [ o [ "id", s "elm"; "fileExtensions", strings [ ".elm" ] ] ]) descriptor) ]

/// A published target whose path is non-ASCII NFC text, written by code point so no editor can normalize it.
let NonAsciiTarget = "extensions/fsharp-frontend/0.6.0/notes/caf" + string (char 0xE9) + "-na" + string (char 0xEF) + "ve-" + string (char 0x5B57) + ".txt"

/// The published content of the test-only repository.
let published () : Targets =
    let targets = newTargets ()

    for offering in ArchiveOfferings do
        addToolRelease targets offering (platforms ()) id

    let windows = platform "win-x64"
    addToolRelease targets MismatchedOffering [ windows ] (fun _ -> platform "osx-x64")
    targets[ToolPackage] <- { Bytes = synthetic "Morphir.Tool 1.2.3 NuGet package"; Custom = None }

    let valid = offeringRecipe targets "fsharp-builtin"

    // Recipes that are validly signed and still wrong.
    let bind (preset: string) (binding: JsonNode) =
        let recipe = offeringRecipe targets preset
        set [ "hosts"; "0"; "platforms"; "win-x64" ] binding recipe
        recipe

    let native = hostTarget "morphir-dotnet-native-aot" windows
    let arm = hostTarget "morphir-dotnet-native-aot" (platform "win-arm64")
    let managed = hostTarget "morphir-dotnet-self-contained" windows

    let hostNegatives =
        [ bind "negative-digest-substituted" (o [ "target", s native; "sha256", s (String.replicate 64 "0") ])
          bind "negative-platform-substituted" (o [ "target", s arm; "sha256", s (digestOf targets arm) ])
          bind "negative-deployment-substituted" (o [ "target", s managed; "sha256", s (digestOf targets managed) ])
          bind
              "negative-unauthenticated-target"
              (o [ "target", s "artifacts/morphir-dotnet-native-aot/1.2.3/win-x64-unpublished.zip"; "sha256", s (String.replicate 64 "1") ]) ]

    let mismatched =
        let recipe = offeringRecipe targets "negative-artifact-metadata"
        set [ "hosts" ] (a [ archiveOffering targets MismatchedOffering [ windows ] ]) recipe
        recipe

    addRecipe targets valid [ "stable" ] "active" ignore

    for negative in hostNegatives @ [ mismatched ] do
        addRecipe targets negative [] "active" ignore

    // Recipe target state that disagrees with, or refuses, the document.
    let wrongKind = offeringRecipe targets "negative-recipe-contract-version"
    addRecipe targets wrongKind [] "active" (fun state -> set [ "schemaVersion" ] (s "0.1.0-draft.2") state)
    let wrongPreset = offeringRecipe targets "negative-recipe-preset"
    addRecipe targets wrongPreset [] "active" (fun state -> set [ "version" ] (s "1.0.1") state)
    let revokedRecipe = offeringRecipe targets "negative-recipe-revoked"
    addRecipe targets revokedRecipe [] "revoked" ignore
    // An authenticated recipe whose document is a later contract draft.
    let unreadable = offeringRecipe targets "negative-recipe-unreadable"
    set [ "schemaVersion" ] (s "0.1.0-draft.2") unreadable
    addRecipe targets unreadable [] "active" ignore

    // Host offerings whose release descriptor is unpublished, or published with bytes that are not a v1 release.
    let unpublishedSource = offeringRecipe targets "negative-host-source-unpublished"
    set [ "hosts"; "0"; "source"; "toolId" ] (s "morphir-dotnet-unpublished") unpublishedSource
    addRecipe targets unpublishedSource [] "active" ignore

    targets[$"releases/{NotAReleaseTool}/{HostVersion}.json"] <-
        { Bytes =
            canonicalText (
                o [ "schemaVersion", n 1L
                    "kind", s "morphir-tool-release"
                    "note", s "TEST-ONLY: the authenticated state says tool-release, the bytes are not one" ]
            )
          Custom =
            Some(
                o [ "schemaVersion", n 1L
                    "kind", s "tool-release"
                    "toolId", s NotAReleaseTool
                    "version", s HostVersion
                    "channels", a []
                    "status", s "active"
                    "compatibility", o [ "morphirCli", s OfferingMorphirCli ]
                    "platforms", a [ v1Platform windows ] ]
            ) }

    let notV1 = offeringRecipe targets "negative-host-release-not-v1"
    set [ "hosts"; "0"; "source"; "toolId" ] (s NotAReleaseTool) notV1
    addRecipe targets notV1 [] "active" ignore

    // Host releases with an unknown status, a descriptor for another tool or version, and a CLI requirement outside
    // the binding check's grammar.
    for preset, offering, edit in HostReleaseVariants do
        addToolReleaseWith targets offering [ windows ] id edit
        let recipe = offeringRecipe targets preset
        set [ "hosts"; "0"; "source"; "toolId" ] (s offering.ToolId) recipe
        addRecipe targets recipe [] "active" ignore

    // A release the version 1 client selects, beside the offerings it never selects.
    addToolReleaseWith targets V1Control [ windows ] id (fun descriptor custom ->
        set [ "compatibility"; "morphirCli" ] (s V1ControlMorphirCli) descriptor
        set [ "compatibility"; "morphirCli" ] (s V1ControlMorphirCli) custom)

    // Recipes of one preset on several channels and statuses, for channel selection.
    for version, channels, status in ChannelRecipes do
        let recipe = offeringRecipe targets ChannelPreset
        set [ "version" ] (s version) recipe
        addRecipe targets recipe channels status ignore

    addExtensionRelease targets FrontendDir FrontendId FrontendVersion (frontendClaims ()) (platforms ())
    addExtensionRelease targets BackendDir BackendId BackendVersion (backendClaims ()) (platforms ())
    targets[FrontendPackage] <- { Bytes = synthetic "Morphir.Frontend.FSharp.Extension 0.6.0 NuGet package"; Custom = None }
    targets[NonAsciiTarget] <- { Bytes = synthetic "release notes with a non-ASCII NFC target path"; Custom = None }

    for _, dir, storeId, claims in claimVariants () do
        addExtensionRelease targets dir storeId FrontendVersion claims [ windows ]

    for _, dir, on, edit in DescriptorVariants do
        addRelease targets dir FrontendId FrontendVersion (frontendClaims ()) (on |> List.map (platform >> Process)) edit

    let backendWrongTarget = "extensions/fsharp-backend-csharp-claims/0.1.0"
    let csharp = backendClaims ()
    let csharpCapabilities = copy csharp.Capabilities
    set [ "backend"; "targets" ] (strings [ "csharp" ]) csharpCapabilities
    addExtensionRelease targets backendWrongTarget BackendId BackendVersion { csharp with Capabilities = csharpCapabilities } [ windows ]

    // A release whose descriptor lists its win-x64 archive with another digest.
    addRelease targets ListedDigestDir FrontendId FrontendVersion (frontendClaims ()) [ Process windows ] (fun descriptor ->
        set [ "artifacts"; "0"; "sha256" ] (s (String.replicate 64 "8")) descriptor)
    // A portable WASM frontend for the morphir-bytes-v1 ABI.
    addRelease targets WasmDir FrontendId FrontendVersion (frontendClaims ()) [ Wasm WasmFile ] ignore

    let repository = repositoryId ()
    let fsharp = fsharpRecipe targets "fsharp" repository
    addRecipe targets fsharp [ "stable" ] "active" ignore

    // Validly signed F# recipes that each break one provider binding rule.
    let provider (preset: string) (edit: JsonNode -> unit) =
        let recipe = fsharpRecipe targets preset repository
        edit (at [ "providers" ] recipe)
        recipe

    let osx = extensionArtifact FrontendDir (platform "osx-arm64")
    let backendDescriptor = digestOf targets $"{BackendDir}/release.json"
    let windowsOnly = [ windows ]

    let negatives =
        [ provider "negative-frontend-platform-substituted" (fun providers ->
              set [ "0"; "platforms"; "win-x64" ] (o [ "artifactTarget", s osx; "artifactSha256", s (digestOf targets osx) ]) providers)
          provider "negative-frontend-artifact-digest" (fun providers ->
              set [ "0"; "platforms"; "win-x64"; "artifactSha256" ] (s (String.replicate 64 "2")) providers)
          provider "negative-descriptor-digest-substituted" (fun providers -> set [ "0"; "descriptorSha256" ] (s backendDescriptor) providers)
          provider "negative-backend-target" (fun providers ->
              set [ "2" ] (backendProvider targets repository backendWrongTarget windowsOnly) providers)
          provider "negative-artifact-runtime" (fun providers ->
              let descriptor, digest = descriptorBinding targets FrontendDir
              let artifact = extensionArtifact FrontendDir windows

              set
                  [ "0" ]
                  (o [ "operation", s "compile"; "language", s "fsharp"; "delivery", s "wasm"
                       "abi", s "morphir-bytes-v1"
                       "extensionId", s FrontendId; "version", s FrontendVersion
                       "repositoryId", s repository
                       "descriptorTarget", s descriptor; "descriptorSha256", s digest
                       "artifactTarget", s artifact; "artifactSha256", s (digestOf targets artifact)
                       "protocolVersion", s "0.2.0-draft.1"
                       "modelTuple", tuple "morphir.frontend.compileModel" "4.0.0" ])
                  providers)
          provider "negative-tool-claims-language" (fun providers ->
              set [ "1" ] (toolFrontendProvider targets repository "extensions/fsharp-frontend-elm-claims/0.6.0") providers)
          provider "negative-provider-repository" (fun providers -> set [ "0"; "repositoryId" ] (s (String.replicate 64 "6")) providers)
          provider "negative-descriptor-unpublished" (fun providers ->
              set [ "0"; "descriptorTarget" ] (s $"{FrontendDir}/missing-release.json") providers)
          provider "negative-descriptor-not-a-release" (fun providers ->
              let archive = extensionArtifact FrontendDir windows
              set [ "0"; "descriptorTarget" ] (s archive) providers
              set [ "0"; "descriptorSha256" ] (s (digestOf targets archive)) providers)
          provider "negative-provider-version" (fun providers -> set [ "0"; "version" ] (s "0.6.1") providers)
          provider "negative-artifact-not-listed" (fun providers ->
              let other = extensionArtifact BackendDir windows
              set [ "0"; "platforms"; "win-x64" ] (o [ "artifactTarget", s other; "artifactSha256", s (digestOf targets other) ]) providers)
          provider "negative-listed-artifact-digest" (fun providers ->
              set [ "0" ] (frontendProvider targets repository ListedDigestDir windowsOnly) providers)
          provider "negative-package-unpublished" (fun providers ->
              set [ "1"; "packageTarget" ] (s "packages/nuget/morphir.frontend.fsharp.extension/0.6.0/missing.nupkg") providers)
          provider "negative-package-digest" (fun providers -> set [ "1"; "packageSha256" ] (s (String.replicate 64 "5")) providers)
          provider "wasm-frontend" (fun providers ->
              set [ "0" ] (wasmFrontendProvider targets repository $"{WasmDir}/{WasmFile}") providers)
          provider "negative-wasm-artifact-not-listed" (fun providers ->
              set [ "0" ] (wasmFrontendProvider targets repository (extensionArtifact FrontendDir windows)) providers)
          provider "negative-process-bound-to-wasm" (fun providers ->
              let descriptor, digest = descriptorBinding targets WasmDir
              let module' = $"{WasmDir}/{WasmFile}"
              set [ "0"; "descriptorTarget" ] (s descriptor) providers
              set [ "0"; "descriptorSha256" ] (s digest) providers
              set [ "0"; "platforms" ] (o [ "win-x64", o [ "artifactTarget", s module'; "artifactSha256", s (digestOf targets module') ] ]) providers) ]

    let claimNegatives =
        [ for preset, dir, _, _ in claimVariants () ->
              provider preset (fun providers ->
                  set [ "0" ] (frontendProvider targets repository dir windowsOnly) providers

                  if preset = "negative-descriptor-identity" then
                      set [ "0"; "extensionId" ] (s FrontendId) providers) ]

    let descriptorNegatives =
        [ for preset, dir, on, _ in DescriptorVariants ->
              let bound = on |> List.map platform

              provider preset (fun providers ->
                  set [ "0" ] (frontendProvider targets repository dir bound) providers

                  if preset = "negative-claims-per-platform" then
                      // The win-x64 claims fail before the osx-x64 artifact is checked: claim rows run per platform.
                      set [ "0"; "platforms"; "osx-x64"; "artifactSha256" ] (s (String.replicate 64 "3")) providers) ]

    for negative in negatives @ claimNegatives @ descriptorNegatives do
        addRecipe targets negative [] "active" ignore

    targets

/// The same targets after the publisher learns that the F# recipe must be revoked.
let recipeRevoked (targets: Targets) =
    let revoked = cloneTargets targets
    let custom = (revoked[recipePath "fsharp" "1.0.0"]).Custom.Value
    set [ "status" ] (s "revoked") custom
    set [ "channels" ] (a []) custom
    revoked

/// The same targets after the publisher learns that the native-aot 1.2.3 release must be revoked.
let revoked (targets: Targets) =
    let revoked = cloneTargets targets
    let custom = (revoked["releases/morphir-dotnet-native-aot/1.2.3.json"]).Custom.Value
    set [ "status" ] (s "revoked") custom
    set [ "channels" ] (a []) custom
    revoked

/// Custom members whose key order differs between this profile's canonical JSON and the reference TUF encoder's.
let keyOrderCustom (signed: JsonNode) =
    set
        [ "targets"; NonAsciiTarget; "custom" ]
        (o [ "morphir", o [ "keyOrder", o [ "a b", n 2L; "a", n 1L ]; "keyOrderQuote", o [ "a#", n 1L; "a\"", n 2L ] ] ])
        signed

/// The reference TUF encoder over a JSON node, for signing the keyOrderReferenceTuf set.
let referenceEncode (node: JsonNode) = referenceTufCanonical (toValue node)

/// The complete repository fixture document.
let fixture () =
    let keys = deriveKeys ()
    let targets = published ()
    let trustedRoot = root keys
    let repository = digestOfBytes (canonical (at [ "signed" ] trustedRoot))

    o [ "testOnly",
        s "TEST-ONLY. Every key in this repository is the Ed25519 key whose seed is the SHA-256 of the UTF-8 label recorded in testOnlyKeys, so anyone can sign with it. Never trust this repository, its keys or its repositoryId outside tests."
        "contractVersion", s "0.1.0-draft.1"
        "profile", s "tool-release-metadata-v1"
        "referenceTime", s ReferenceTime
        "repositoryId", s repository
        "testOnlyKeys", a [ for key in keys.All -> o [ "role", s key.Role; "label", s key.Label; "keyId", s key.KeyId; "public", s key.PublicHex ] ]
        "trustedRoot", trustedRoot
        "metadata", metadata keys targets 1L Never
        "learnedRevocation", metadata keys (revoked targets) 2L Never
        "recipeRevocation", metadata keys (recipeRevoked targets) 2L Never
        // NFC signed text, which loads, and a case variant of a defined member, which a toolchain reader refuses.
        "customText",
        metadataWith keys targets 2L Never (fun signed -> set [ "targets"; NonAsciiTarget; "custom" ] (o [ "morphir", o [ "label", s ("caf" + string (char 0xE9)) ] ]) signed) canonical
        "caseVariantMember",
        metadataWith
            keys
            targets
            2L
            Never
            (fun signed ->
                let length = copy (at [ "targets"; NonAsciiTarget; "length" ] signed)
                set [ "targets"; NonAsciiTarget; "Length" ] length signed)
            canonical
        "keyOrder", metadataWith keys targets 2L Never keyOrderCustom canonical
        "keyOrderReferenceTuf", metadataWith keys targets 2L Never keyOrderCustom referenceEncode
        "rejectionMetadata", o [ "expiredTimestamp", copy (at [ "timestamp" ] (metadata keys targets 2L "2020-01-01T00:00:00Z")) ]
        "targetFiles", o [ for KeyValue(path, target) in targets -> path, s target.Bytes ] ]

// ----- frozen locks -------------------------------------------------------------------------------------------------

/// The requests whose frozen locks are committed beside the repository, as exact TOML text.
let Frozen =
    let common (deployment: string) =
        "schemaVersion = \"0.1.0-draft.1\"\nplatforms = [\"win-x64\", \"win-arm64\", \"osx-x64\", \"osx-arm64\", \"linux-x64\", \"linux-arm64\"]\n[host]\nimplementation = \"dotnet\"\ndeployment = \""
        + deployment
        + "\"\nchannel = \"stable\"\n[toolset]\npreset = \"fsharp\"\n"

    [ "fsharp-self-contained", common "self-contained"
      "fsharp-native-aot", common "native-aot"
      "fsharp-native-aot-external-backend",
      common "native-aot" + "[[toolset.providers]]\noperation = \"generate\"\ntarget = \"fsharp\"\ndelivery = \"process\"\n"
      "fsharp-framework-dependent-tool-frontend",
      common "framework-dependent" + "[[toolset.providers]]\noperation = \"compile\"\nlanguage = \"fsharp\"\ndelivery = \"dotnet-tool\"\n" ]

let private protocolMembers (protocol: Protocol) =
    let t = protocol.Tuple

    [ "protocolVersion", s (ProtocolVersion.text protocol.Version)
      "modelTuple",
      o [ "operation", s (ModelOperation.text t.Operation)
          "profile", s t.Profile.Text
          "codec", s t.Codec.Text
          "envelopeVersion", s t.EnvelopeVersion.Text
          "irVersion", s t.IrVersion.Text ] ]

let private modelFormatJson (model: ModelFormat) =
    o [ "profile", s model.Profile.Text; "codec", s model.Codec.Text; "irVersion", s model.IrVersion.Text ]

/// A locked provider in the lock's JSON shape (the generator's provider_json).
let providerJson (provider: LockedProvider) =
    let operation =
        match provider.Operation with
        | Operation.Compile language -> [ "operation", s "compile"; "language", s language.Text ]
        | Operation.Generate target -> [ "operation", s "generate"; "target", s target.Text ]

    let pair (prefix: string) (binding: TargetBinding) =
        [ $"{prefix}Target", s binding.Target.Text; $"{prefix}Sha256", s binding.Sha256.Text ]

    let release (r: ReleaseBinding) =
        [ "repositoryId", s r.RepositoryId.Text
          "descriptorTarget", s r.DescriptorTarget.Text
          "descriptorSha256", s r.DescriptorSha256.Text ]

    let delivery =
        match provider.Delivery with
        | LockedDelivery.BuiltIn model ->
            let member' =
                match provider.Operation with
                | Operation.Compile _ -> "modelOutput"
                | Operation.Generate _ -> "modelInput"

            [ member', modelFormatJson model ]
        | LockedDelivery.Process(r, artifact, protocol) -> release r @ pair "artifact" artifact @ protocolMembers protocol
        | LockedDelivery.Wasm(abi, r, artifact, protocol) -> [ "abi", s abi.Text ] @ release r @ pair "artifact" artifact @ protocolMembers protocol
        | LockedDelivery.DotnetTool(r, package, protocol) ->
            release r
            @ [ "packageId", s package.PackageId.Text; "command", s package.Command.Text ]
            @ pair "package" { Target = package.Target; Sha256 = package.Sha256 }
            @ protocolMembers protocol

    o (
        operation
        @ [ "delivery", s (Delivery.name (LockedProvider.delivery provider))
            "extensionId", s provider.ExtensionId.Text
            "version", s provider.Version.Text ]
        @ delivery
    )

/// Each frozen request resolved against the F# recipe, with its lock.
let frozenLocks (targets: Targets) =
    let path = recipePath "fsharp" "1.0.0"
    let recipeBytes = Encoding.UTF8.GetBytes targets[path].Bytes

    let recipe =
        match RecipeDocument.read recipeBytes with
        | Ok recipe -> recipe
        | Error refusal -> failwithf "the generated F# recipe does not read: %A" refusal

    [ for name, requestText in Frozen ->
          let request =
              match RequestDocument.read (Encoding.UTF8.GetBytes requestText) with
              | Ok request -> request
              | Error refusal -> failwithf "%s does not read: %A" name refusal

          let host = recipe.Hosts |> List.find (fun host -> host.Deployment = request.Host.Deployment)

          let closures =
              o [ for p in Option.defaultValue [] request.Platforms ->
                      match Resolution.resolve recipe request p with
                      | Ok resolved ->
                          Platform.name p,
                          o [ "hostTarget", s resolved.Closure.Host.Target.Text
                              "hostSha256", s resolved.Closure.Host.Sha256.Text
                              "providers", a (resolved.Closure.Providers |> List.map providerJson) ]
                      | Error failure -> failwithf "%s on %s does not resolve: %A" name (Platform.name p) failure ]

          let lock =
              o [ "schemaVersion", s "0.1.0-draft.1"
                  "critical", strings [ "host"; "platforms" ]
                  "requestSha256", s request.Identity.Text
                  "recipe", o [ "repositoryId", s (repositoryId ()); "target", s path; "sha256", s (digestOfBytes recipeBytes) ]
                  "host", o [ "implementation", s host.Implementation.Text; "version", s host.Version.Text; "deployment", s host.Deployment.Text ]
                  "platforms", closures ]

          name, requestText, lock ]

/// A frozen lock whose win-x64 frontend artifact is not one the recipe binds.
let lockWithUnboundProvider (lock: JsonNode) =
    let lock = copy lock
    set [ "platforms"; "win-x64"; "providers"; "0"; "artifactSha256" ] (s (String.replicate 64 "4")) lock
    lock

/// Frozen-lock variants that each break one lock-against-recipe rule, by file name under signed/negative/.
let lockRecipeNegatives (lock: JsonNode) (targets: Targets) =
    let hostNotOffered = copy lock
    set [ "host"; "version" ] (s "1.2.4") hostNotOffered

    let hostArtifact = copy lock
    let other = hostTarget "morphir-dotnet-self-contained" (platform "win-x64")
    set [ "platforms"; "win-x64"; "hostTarget" ] (s other) hostArtifact
    set [ "platforms"; "win-x64"; "hostSha256" ] (s (digestOf targets other)) hostArtifact

    // Every closure drops the generator, so the lock stays consistent while its operations are not the recipe's.
    let operations = copy lock

    for KeyValue(_, closure) in (at [ "platforms" ] operations).AsObject() do
        let providers = (at [ "providers" ] (nonNull closure)).AsArray()

        for index in providers.Count - 1 .. -1 .. 0 do
            if (at [ "operation" ] (nonNull providers[index])).GetValue<string>() = "generate" then
                providers.RemoveAt index

    // Another repository named by the lock's recipe reference, and by one provider on every platform.
    let recipeRepository = copy lock
    set [ "recipe"; "repositoryId" ] (s (String.replicate 64 "6")) recipeRepository
    let providerRepository = copy lock

    for KeyValue(_, closure) in (at [ "platforms" ] providerRepository).AsObject() do
        set [ "providers"; "0"; "repositoryId" ] (s (String.replicate 64 "6")) (nonNull closure)

    // A closure for a platform the offering does not list: the win-x64 closure, recorded again as linux-musl-x64.
    let platformNotOffered = copy lock
    set [ "platforms"; "linux-musl-x64" ] (copy (at [ "platforms"; "win-x64" ] platformNotOffered)) platformNotOffered
    // A recipe target the repository does not list at all.
    let recipeUnlisted = copy lock
    set [ "recipe"; "target" ] (s (recipePath "fsharp" "9.9.9")) recipeUnlisted

    [ "lock-host-not-offered.lock.json", hostNotOffered
      "lock-host-artifact-substituted.lock.json", hostArtifact
      "lock-operations-differ.lock.json", operations
      "lock-recipe-repository.lock.json", recipeRepository
      "lock-provider-repository.lock.json", providerRepository
      "lock-platform-not-offered.lock.json", platformNotOffered
      "lock-recipe-unlisted.lock.json", recipeUnlisted ]
