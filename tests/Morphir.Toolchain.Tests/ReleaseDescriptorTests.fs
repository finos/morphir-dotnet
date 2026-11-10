// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The readers of the documents a recipe binding names: the tool release metadata v1 host release descriptor, the
/// extension release descriptor (<c>2.0.0-draft.2</c>) and the capability claim set (<c>0.1.0-draft.2</c>), over the
/// TEST-ONLY signed repository's target files.
/// </summary>
module Morphir.Toolchain.Tests.ReleaseDescriptorTests

open System.Text
open Expecto
open Morphir.Toolchain

/// A target file of the signed repository as a parsed value.
let valueOf (target: string) =
    match Syntax.parseJson (SignedCaseTests.targetBytes target) with
    | Ok value -> value
    | Error _ -> failtestf "%s does not parse" target

/// The value with the member at a path replaced; a missing last member is added.
let rec setAt (path: string list) (replacement: Value) (value: Value) =
    match path, value with
    | [], _ -> replacement
    | [ head ], Value.Object members when not (members |> List.exists (fst >> (=) head)) -> Value.Object(members @ [ head, replacement ])
    | head :: rest, Value.Object members ->
        Value.Object(members |> List.map (fun (name, item) -> if name = head then name, setAt rest replacement item else name, item))
    | head :: rest, Value.Array items ->
        let index = int head
        Value.Array(items |> List.mapi (fun i item -> if i = index then setAt rest replacement item else item))
    | _ -> failwithf "cannot set %A" path

/// The value with the member at a path removed.
let rec removeAt (path: string list) (value: Value) =
    match path, value with
    | [ last ], Value.Object members -> Value.Object(members |> List.filter (fst >> (<>) last))
    | head :: rest, Value.Object members ->
        Value.Object(members |> List.map (fun (name, item) -> if name = head then name, removeAt rest item else name, item))
    | head :: rest, Value.Array items ->
        let index = int head
        Value.Array(items |> List.mapi (fun i item -> if i = index then removeAt rest item else item))
    | _ -> failwithf "cannot remove %A" path

let bytesOf (value: Value) = JsonText.toBytes value

let memberOf (name: string) (value: Value) =
    match value with
    | Value.Object members -> members |> List.find (fst >> (=) name) |> snd
    | _ -> failwithf "not an object at %s" name
let str (s: string) = Value.String s

let private refusedWith (code: ReaderCode) (path: string) (result: Result<'T, ReaderRefusal>) =
    match result with
    | Error refusal -> Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (code, path) "the refusal"
    | Ok _ -> failtestf "expected %A at %s, but it read" code path

let private releases =
    [ "releases/morphir-dotnet-framework-dependent/1.2.3.json"
      "releases/morphir-dotnet-metadata-mismatch/1.2.3.json"
      "releases/morphir-dotnet-native-aot/1.2.3.json"
      "releases/morphir-dotnet-self-contained/1.2.3.json" ]

let private nativeAot () = valueOf "releases/morphir-dotnet-native-aot/1.2.3.json"

[<Tests>]
let toolReleaseTests =
    testList "Tool release metadata v1 descriptors" [
        testCase "every release of the signed repository reads" (fun () ->
            for target in releases do
                match ToolReleaseDocument.read (SignedCaseTests.targetBytes target) with
                | Ok release ->
                    Expect.equal release.Version.Text "1.2.3" $"{target}: version"
                    Expect.isNonEmpty release.Artifacts $"{target}: artifacts"
                | Error refusal -> failtestf "%s does not read: %A" target refusal)

        testCase "the native-aot release names its tool, platforms and targets" (fun () ->
            match ToolReleaseDocument.read (bytesOf (nativeAot ())) with
            | Ok release ->
                Expect.equal release.ToolId.Text "morphir-dotnet-native-aot" "tool id"
                Expect.equal release.Status RecipeStatus.Active "status at publication"

                Expect.equal
                    (release.Artifacts |> List.map (fun a -> a.TargetPath, a.Platform.Os, a.Platform.Arch) |> List.head)
                    ("artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip", "windows", "x86_64")
                    "the first artifact"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "the fixture's deliberate non-release is refused" (fun () ->
            // Its "note" member is outside the closed schema, refused before the missing members.
            refusedWith ReaderCode.InvalidDocument "note" (ToolReleaseDocument.read (SignedCaseTests.targetBytes "releases/morphir-dotnet-not-a-release/1.2.3.json")))

        testList "a descriptor outside the version 1 schema is refused" [
            for name, edit, code, path in
                [ "schemaVersion 2", setAt [ "schemaVersion" ] (Value.Number "2"), ReaderCode.UnsupportedContractVersion, "schemaVersion"
                  "schemaVersion as a string", setAt [ "schemaVersion" ] (str "1"), ReaderCode.InvalidDocument, "schemaVersion"
                  "another kind", setAt [ "kind" ] (str "morphir-tool"), ReaderCode.InvalidValue, "kind"
                  "a member the schema does not define", setAt [ "note" ] (str "x"), ReaderCode.InvalidDocument, "note"
                  "a tool member the schema does not define", setAt [ "tool"; "extra" ] (str "x"), ReaderCode.InvalidDocument, "tool.extra"
                  "a tool id that is not a portable token", setAt [ "tool"; "id" ] (str "Morphir"), ReaderCode.InvalidValue, "tool.id"
                  "a blank tool name", setAt [ "tool"; "name" ] (str "  "), ReaderCode.InvalidValue, "tool.name"
                  "a version that is not SemVer", setAt [ "version" ] (str "1.2"), ReaderCode.InvalidValue, "version"
                  "an unknown channel", setAt [ "channels" ] (Value.Array [ str "beta" ]), ReaderCode.InvalidValue, "channels[0]"
                  "a repeated channel", setAt [ "channels" ] (Value.Array [ str "stable"; str "stable" ]), ReaderCode.InconsistentDocument, "channels[1]"
                  "an unknown status", setAt [ "status" ] (str "retired"), ReaderCode.UnsupportedCase, "status"
                  "a revoked release with channels", setAt [ "status" ] (str "revoked"), ReaderCode.InconsistentDocument, "channels"
                  "a yanked release with channels", setAt [ "status" ] (str "yanked"), ReaderCode.InconsistentDocument, "channels"
                  "an active release without artifacts", setAt [ "artifacts" ] (Value.Array []), ReaderCode.InconsistentDocument, "artifacts"
                  "a blank morphirCli", setAt [ "compatibility"; "morphirCli" ] (str ""), ReaderCode.InvalidValue, "compatibility.morphirCli"
                  "no compatibility", removeAt [ "compatibility" ], ReaderCode.InvalidDocument, "compatibility"
                  "a target path with a backslash", setAt [ "artifacts"; "0"; "targetPath" ] (str @"artifacts\x.zip"), ReaderCode.InvalidValue, "artifacts[0].targetPath"
                  "a target path with a .. segment", setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/../x.zip"), ReaderCode.InvalidValue, "artifacts[0].targetPath"
                  "a target path ending in a line feed", setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/x.zip\n"), ReaderCode.InvalidValue, "artifacts[0].targetPath"
                  "an arch that is not a token", setAt [ "artifacts"; "0"; "platform"; "arch" ] (str "X86"), ReaderCode.InvalidValue, "artifacts[0].platform.arch"
                  "an unknown archive format", setAt [ "artifacts"; "0"; "archive"; "format" ] (str "rar"), ReaderCode.UnsupportedCase, "artifacts[0].archive.format"
                  "a launch kind other than executable", setAt [ "artifacts"; "0"; "launch"; "kind" ] (str "script"), ReaderCode.UnsupportedCase, "artifacts[0].launch.kind"
                  "a launch argument with NUL", setAt [ "artifacts"; "0"; "launch"; "args" ] (Value.Array [ str "a\u0000b" ]), ReaderCode.InvalidValue, "artifacts[0].launch.args[0]"
                  "two equal artifacts",
                  (fun v ->
                      match v with
                      | Value.Object _ ->
                          let first = match memberOf "artifacts" v with Value.Array(a :: _) -> a | _ -> failwith "artifacts"
                          setAt [ "artifacts" ] (Value.Array [ first; first ]) v
                      | _ -> v),
                  ReaderCode.InconsistentDocument,
                  "artifacts[1]" ] ->
                testCase name (fun () -> refusedWith code path (ToolReleaseDocument.read (bytesOf (edit (nativeAot ())))))
        ]

        testCase "a descriptor over 1 MiB is refused before it is parsed" (fun () ->
            refusedWith ReaderCode.InvalidDocument "" (ToolReleaseDocument.read (Array.create (1024 * 1024 + 1) (byte ' '))))
    ]

/// The claim set of the first artifact of an extension release descriptor of the signed repository.
let private claimsOf (target: string) =
    match memberOf "artifacts" (valueOf target) with
    | Value.Array(first :: _) -> memberOf "claims" first
    | _ -> failwith "artifacts"

let private frontendClaims () = claimsOf "extensions/fsharp-frontend/0.6.0/release.json"
let private readClaims (value: Value) = ClaimSetDocument.readValue value

[<Tests>]
let claimSetTests =
    testList "Capability claim sets" [
        testCase "the frontend's claims read" (fun () ->
            match readClaims (frontendClaims ()) with
            | Ok claims ->
                Expect.equal claims.Extension.Id.Text "morphir-fsharp-frontend" "id"
                Expect.equal claims.Extension.Version.Text "0.6.0" "version"
                Expect.equal claims.Extension.Types [ CapabilityKind.Frontend ] "types"
                Expect.equal claims.ProtocolVersions [ "0.2.0-draft.1" ] "protocol versions"
                Expect.equal claims.Frontend (Some { Compile = true; Languages = set [ "fsharp" ] }) "frontend"
                Expect.equal claims.Backend None "no backend"
                Expect.equal claims.ModelTuples.Length 1 "one tuple"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "the backend's claims read" (fun () ->
            match readClaims (claimsOf "extensions/fsharp-backend/0.1.0/release.json") with
            | Ok claims ->
                Expect.equal claims.Extension.Types [ CapabilityKind.Backend ] "types"
                Expect.isSome claims.Backend "backend"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "every claim set of the signed repository's descriptors reads" (fun () ->
            use repository = System.Text.Json.JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "repository.json")

            for target in repository.RootElement.GetProperty("targetFiles").EnumerateObject() do
                if target.Name.EndsWith "/release.json" && target.Name.StartsWith "extensions/" then
                    match memberOf "artifacts" (valueOf target.Name) with
                    | Value.Array artifacts ->
                        for artifact in artifacts do
                            match readClaims (memberOf "claims" artifact) with
                            | Ok _ -> ()
                            | Error refusal -> failtestf "%s: %A" target.Name refusal
                    | _ -> failtestf "%s has no artifacts" target.Name)

        testList "a claim set outside 0.1.0-draft.2 is refused" [
            for name, edit, code, path in
                [ "claimsVersion 0.1.0-draft.1", setAt [ "claimsVersion" ] (str "0.1.0-draft.1"), ReaderCode.UnsupportedContractVersion, "claimsVersion"
                  "no claimsVersion", removeAt [ "claimsVersion" ], ReaderCode.InvalidDocument, "claimsVersion"
                  "an unknown capability kind", setAt [ "extension"; "types" ] (Value.Array [ str "linter" ]), ReaderCode.UnsupportedCase, "extension.types[0]"
                  "a repeated capability kind", setAt [ "extension"; "types" ] (Value.Array [ str "frontend"; str "frontend" ]), ReaderCode.InconsistentDocument, "extension.types[1]"
                  "an extension version that is not SemVer", setAt [ "extension"; "version" ] (str "0.6"), ReaderCode.InvalidValue, "extension.version"
                  "a blank extension name", setAt [ "extension"; "name" ] (str " "), ReaderCode.InvalidValue, "extension.name"
                  "no protocol versions", setAt [ "protocolVersions" ] (Value.Array []), ReaderCode.InconsistentDocument, "protocolVersions"
                  "compile that is not a boolean", setAt [ "capabilities"; "frontend"; "compile" ] (str "true"), ReaderCode.InvalidDocument, "capabilities.frontend.compile"
                  "a language without an id", setAt [ "capabilities"; "frontend"; "languages" ] (Value.Array [ Value.Object [] ]), ReaderCode.InvalidDocument, "capabilities.frontend.languages[0].id"
                  "tuples that are not an array", setAt [ "capabilities"; "modelTransport"; "tuples" ] (str "x"), ReaderCode.InvalidDocument, "capabilities.modelTransport.tuples"
                  "a critical member this reader does not understand", setAt [ "critical" ] (Value.Array [ str "capabilities.frontend.incremental" ]), ReaderCode.UnsupportedCriticalMember, "capabilities.frontend.incremental"
                  "requires.host, whose host requirement no binding check evaluates",
                  (setAt [ "requires" ] (Value.Object [ "host", Value.Array [ str ">=0.4.0" ] ]) >> setAt [ "critical" ] (Value.Array [ str "requires.host" ])),
                  ReaderCode.UnsupportedCriticalMember,
                  "requires.host"
                  "requires.host that critical does not list", setAt [ "requires" ] (Value.Object [ "host", Value.Array [ str ">=0.4.0" ] ]), ReaderCode.InconsistentDocument, "critical" ] ->
                testCase name (fun () -> refusedWith code path (readClaims (edit (frontendClaims ()))))
        ]

        testCase "unknown members and capabilities are ignored, as the protocol says" (fun () ->
            let edited =
                frontendClaims ()
                |> setAt [ "note" ] (str "x")
                |> setAt [ "extension"; "description" ] (str "x")
                |> setAt [ "capabilities"; "frontend"; "incremental" ] (Value.Boolean false)
                |> setAt [ "capabilities"; "workspace" ] (Value.Object [ "discover", Value.Boolean true ])

            Expect.isOk (readClaims edited) "read")
    ]

let private frontendDescriptor () = valueOf "extensions/fsharp-frontend/0.6.0/release.json"
let private readDescriptor (value: Value) = ExtensionReleaseDocument.read (bytesOf value)

[<Tests>]
let extensionReleaseTests =
    testList "Extension release descriptors" [
        testCase "every extension descriptor of the signed repository reads" (fun () ->
            use repository = System.Text.Json.JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "repository.json")

            for target in repository.RootElement.GetProperty("targetFiles").EnumerateObject() do
                if target.Name.EndsWith "/release.json" && target.Name.StartsWith "extensions/" then
                    match ExtensionReleaseDocument.read (SignedCaseTests.targetBytes target.Name) with
                    | Ok release -> Expect.isNonEmpty release.Artifacts $"{target.Name}: artifacts"
                    | Error refusal -> failtestf "%s: %A" target.Name refusal)

        testCase "the frontend descriptor names its identity and six process artifacts" (fun () ->
            match readDescriptor (frontendDescriptor ()) with
            | Ok release ->
                Expect.equal (release.ExtensionId.Text, release.ShortId.Text, release.Version.Text) ("morphir-fsharp-frontend", "fsharp-frontend", "0.6.0") "identity"
                Expect.equal release.Artifacts.Length 6 "artifacts"
                let first = release.Artifacts.Head
                Expect.equal (first.Runtime, first.Platform, first.Filename) (ArtifactRuntime.Process, Some "x86_64-pc-windows-msvc", "win-x64.zip") "the first artifact"
                // What the publisher states about the claims is its own fact; reading them is no probe.
                Expect.equal first.ClaimCheck DescriptorClaimCheck.Unchecked "claim check"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "the WASM descriptor has one portable artifact" (fun () ->
            match ExtensionReleaseDocument.read (SignedCaseTests.targetBytes "extensions/fsharp-frontend-wasm/0.6.0/release.json") with
            | Ok release -> Expect.equal (release.Artifacts |> List.map (fun a -> a.Runtime, a.Platform)) [ ArtifactRuntime.Wasm, None ] "one WASM artifact"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "bytes that are not JSON, such as an archive, are refused" (fun () ->
            refusedWith ReaderCode.InvalidDocument "" (ExtensionReleaseDocument.read (SignedCaseTests.targetBytes "extensions/fsharp-frontend/0.6.0/win-x64.zip")))

        testList "a descriptor outside 2.0.0-draft.2 is refused" [
            for name, edit, code, path in
                [ "schemaVersion 2.0.0-draft.1", setAt [ "schemaVersion" ] (str "2.0.0-draft.1"), ReaderCode.UnsupportedContractVersion, "schemaVersion"
                  "a version 1 descriptor", setAt [ "schemaVersion" ] (Value.Number "1"), ReaderCode.InvalidDocument, "schemaVersion"
                  "a shortId that is not a portable token", setAt [ "shortId" ] (str "FSharp"), ReaderCode.InvalidValue, "shortId"
                  "an extensionId that is not an id", setAt [ "extensionId" ] (str "Morphir FSharp"), ReaderCode.InvalidValue, "extensionId"
                  "a version that is not SemVer", setAt [ "version" ] (str "0.6"), ReaderCode.InvalidValue, "version"
                  "no artifacts", setAt [ "artifacts" ] (Value.Array []), ReaderCode.InconsistentDocument, "artifacts"
                  "an unknown runtime", setAt [ "artifacts"; "0"; "runtime" ] (str "native"), ReaderCode.UnsupportedCase, "artifacts[0].runtime"
                  "a process artifact without a platform", removeAt [ "artifacts"; "0"; "platform" ], ReaderCode.InconsistentDocument, "artifacts[0].platform"
                  "a WASM artifact with a platform", setAt [ "artifacts"; "0"; "runtime" ] (str "wasm"), ReaderCode.InconsistentDocument, "artifacts[0].platform"
                  "a filename with a directory", setAt [ "artifacts"; "0"; "filename" ] (str "x/win-x64.zip"), ReaderCode.InvalidValue, "artifacts[0].filename"
                  "a filename of ..", setAt [ "artifacts"; "0"; "filename" ] (str ".."), ReaderCode.InvalidValue, "artifacts[0].filename"
                  "a repeated filename", setAt [ "artifacts"; "1"; "filename" ] (str "win-x64.zip"), ReaderCode.InconsistentDocument, "artifacts[1].filename"
                  "an artifact digest that is not SHA-256", setAt [ "artifacts"; "0"; "sha256" ] (str "abc"), ReaderCode.InvalidValue, "artifacts[0].sha256"
                  "an artifact without claims", removeAt [ "artifacts"; "0"; "claims" ], ReaderCode.InvalidDocument, "artifacts[0].claims"
                  "claims of another claimsVersion", setAt [ "artifacts"; "0"; "claims"; "claimsVersion" ] (str "0.1.0-draft.1"), ReaderCode.UnsupportedContractVersion, "artifacts[0].claims.claimsVersion"
                  "an unknown claim check", setAt [ "artifacts"; "0"; "claimCheck" ] (str "trusted"), ReaderCode.UnsupportedCase, "artifacts[0].claimCheck"
                  "a critical member this reader does not read", setAt [ "critical" ] (Value.Array [ str "gitCommit" ]), ReaderCode.UnsupportedCriticalMember, "gitCommit"
                  "requires.host, which no binding check evaluates",
                  (setAt [ "requires" ] (Value.Object [ "host", Value.Array [ str ">=0.4.0" ] ]) >> setAt [ "critical" ] (Value.Array [ str "requires.host" ])),
                  ReaderCode.UnsupportedCriticalMember,
                  "requires.host" ] ->
                testCase name (fun () -> refusedWith code path (readDescriptor (edit (frontendDescriptor ()))))
        ]

        testCase "unknown members are ignored, and a critical member it reads is accepted" (fun () ->
            let edited =
                frontendDescriptor ()
                |> setAt [ "gitCommit" ] (str (String.replicate 40 "a"))
                |> setAt [ "note" ] (str "x")
                |> setAt [ "artifacts"; "0"; "note" ] (str "x")
                |> setAt [ "critical" ] (Value.Array [ str "artifacts.sha256"; str "artifacts.claims.capabilities.frontend.compile" ])

            Expect.isOk (readDescriptor edited) "read")

        testCase "a descriptor over 1 MiB is refused before it is parsed" (fun () ->
            refusedWith ReaderCode.InvalidDocument "" (ExtensionReleaseDocument.read (Array.create (1024 * 1024 + 1) (byte ' '))))
    ]

/// A synthetic authenticated tool-release target for the resolver's own cases.
let private release (version: string) (channels: string list) (status: string) (cli: string) (platforms: (string * string) list) =
    $"releases/tool/{version}.json",
    { Length = 1UL
      Sha256 = (match Sha256.tryParse (String.replicate 64 "a") with Ok s -> s | Error _ -> failwith "sha")
      Custom =
        Some(
            Value.Object
                [ "morphir",
                  Value.Object
                      [ "schemaVersion", Value.Number "1"
                        "kind", str "tool-release"
                        "toolId", str "tool"
                        "version", str version
                        "channels", Value.Array(channels |> List.map str)
                        "status", str status
                        "compatibility", Value.Object [ "morphirCli", str cli ]
                        "platforms", Value.Array [ for os, arch in platforms -> Value.Object [ "os", str os; "arch", str arch ] ] ] ]
        ) }

let private windows = [ "windows", "x86_64" ]
let private semver (text: string) = match SemVer.tryParse text with Ok v -> v | Error _ -> failwith text
let private toolId = match ToolId.tryParse "tool" with Ok id -> id | Error _ -> failwith "tool"
let private channel (text: string) = match Channel.tryParse text with Ok c -> c | Error _ -> failwith text

let private resolve (targets: (string * AuthenticatedTarget) list) (request: ToolRequest) (cli: string) =
    ToolResolver.resolve (Map.ofList targets) toolId request { Os = "windows"; Arch = "x86_64" } (semver cli)
    |> Result.map (fun selection -> selection.Version.Text)
    |> Result.mapError (fun refusal -> Code.name refusal.Code)

[<Tests>]
let toolResolverTests =
    testList "Tool release metadata v1 resolution" [
        testCase "stable selects the greatest final version on stable" (fun () ->
            let targets =
                [ release "1.0.0" [ "stable" ] "active" ">=0.4.0" windows
                  release "1.1.0" [ "stable" ] "active" ">=0.4.0" windows
                  release "2.0.0-rc.1" [ "stable" ] "active" ">=0.4.0" windows ]

            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Stable) "0.4.0") (Ok "1.1.0") "1.1.0")

        testCase "preview and insiders select the preview family; a named preview only its own channel" (fun () ->
            let targets =
                [ release "1.0.0" [ "stable" ] "active" ">=0.4.0" windows
                  release "2.0.0-preview.1" [ "preview/fast" ] "active" ">=0.4.0" windows
                  release "2.0.0-preview.2" [ "insiders" ] "active" ">=0.4.0" windows ]

            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Preview) "0.4.0") (Ok "2.0.0-preview.2") "preview"
            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Insiders) "0.4.0") (Ok "2.0.0-preview.2") "insiders"
            Expect.equal (resolve targets (ToolRequest.OnChannel(channel "preview/fast")) "0.4.0") (Ok "2.0.0-preview.1") "named")

        testCase "a yanked release is skipped by channels and selected exactly; a revoked one is refused exactly" (fun () ->
            let targets =
                [ release "1.0.0" [ "stable" ] "active" ">=0.4.0" windows
                  release "1.1.0" [] "yanked" ">=0.4.0" windows
                  release "1.2.0" [] "revoked" ">=0.4.0" windows ]

            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Stable) "0.4.0") (Ok "1.0.0") "channel"
            Expect.equal (resolve targets (ToolRequest.Exact(semver "1.1.0")) "0.4.0") (Ok "1.1.0") "exact yanked"
            Expect.equal (resolve targets (ToolRequest.Exact(semver "1.2.0")) "0.4.0") (Error "release_revoked") "exact revoked")

        testCase "the CLI requirement filters releases, comparator by comparator" (fun () ->
            let targets =
                [ release "1.0.0" [ "stable" ] "active" ">=0.4.0-alpha.5, <0.5.0" windows
                  release "1.1.0" [ "stable" ] "active" ">=0.5.0" windows ]

            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Stable) "0.4.2") (Ok "1.0.0") "0.4.2"
            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Stable) "0.5.1") (Ok "1.1.0") "0.5.1"
            // <0.0.0-0 matches no version, so no CLI selects such a release.
            let offering = [ release "1.2.3" [ "stable" ] "active" "<0.0.0-0" windows ]
            Expect.equal (resolve offering (ToolRequest.OnChannel Channel.Stable) "0.4.0") (Error "no_compatible_release") "no CLI")

        testCase "a release without the platform is not selected" (fun () ->
            let targets = [ release "1.0.0" [ "stable" ] "active" ">=0.4.0" [ "linux", "x86_64" ] ]
            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Stable) "0.4.0") (Error "no_compatible_release") "no artifact")

        testCase "two releases of one precedence are refused" (fun () ->
            let targets =
                [ release "1.0.0+a" [ "stable" ] "active" ">=0.4.0" windows
                  release "1.0.0+b" [ "stable" ] "active" ">=0.4.0" windows ]

            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Stable) "0.4.0") (Error "release_descriptor_mismatch") "ambiguous")

        testCase "a CLI requirement outside the comparator grammar is refused (provisional)" (fun () ->
            let targets = [ release "1.0.0" [ "stable" ] "active" "^0.4" windows ]
            Expect.equal (resolve targets (ToolRequest.OnChannel Channel.Stable) "0.4.0") (Error "release_descriptor_mismatch") "^0.4")
    ]

/// Runs a read under a time budget on its own task, so a quadratic check fails within the budget instead of hanging.
let private withinBudget (seconds: float) (what: string) (read: unit -> 'T) : 'T =
    let task = System.Threading.Tasks.Task.Run(fun () -> read ())

    if task.Wait(System.TimeSpan.FromSeconds seconds) then
        task.Result
    else
        failtestf "%s took longer than %.0f s" what seconds

[<Tests>]
let linearTests =
    // Review of #582, R1, applied to the readers and the resolver of this slice: a descriptor may be 1 MiB and the
    // targets metadata 8 MiB, so a check of each element against the earlier ones is a hang inside the bounds.
    testList "Descriptor and resolver checks are linear" [
        testCase "a v1 descriptor of 6,000 artifacts within 1 MiB reads in linear time" (fun () ->
            let artifact (i: int) =
                $"{{\"targetPath\":\"a/x\",\"platform\":{{\"os\":\"linux\",\"arch\":\"x86_64\"}},\"archive\":{{\"format\":\"raw\",\"entryPoint\":\"e\"}},\"launch\":{{\"kind\":\"executable\",\"path\":\"e\",\"args\":[\"{i}\"]}}}}"

            let text =
                "{\"schemaVersion\":1,\"kind\":\"morphir-tool-release\",\"tool\":{\"id\":\"t\",\"name\":\"T\"},\"version\":\"1.0.0\",\"channels\":[],\"status\":\"active\",\"compatibility\":{\"morphirCli\":\">=0.4.0\"},\"artifacts\":["
                + String.concat "," [ for i in 1..6_000 -> artifact i ]
                + "]}"

            let bytes = Encoding.UTF8.GetBytes text
            Expect.isLessThan bytes.Length ToolReleaseDocument.limit "within the bound"

            match withinBudget 5.0 "reading 6,000 artifacts" (fun () -> ToolReleaseDocument.read bytes) with
            | Ok release -> Expect.equal release.Artifacts.Length 6_000 "every artifact"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "two artifacts equal as parsed values, with their members in another order, are refused" (fun () ->
            let release = valueOf "releases/morphir-dotnet-native-aot/1.2.3.json"
            let first = match memberOf "artifacts" release with Value.Array(a :: _) -> a | _ -> failwith "artifacts"
            let reordered = match first with Value.Object members -> Value.Object(List.rev members) | other -> other
            let edited = setAt [ "artifacts" ] (Value.Array [ first; reordered ]) release
            refusedWith ReaderCode.InconsistentDocument "artifacts[1]" (ToolReleaseDocument.read (bytesOf edited)))

        testCase "a repeated filename among many artifacts is refused at the repetition" (fun () ->
            let descriptor = valueOf "extensions/fsharp-frontend/0.6.0/release.json"
            let first = match memberOf "artifacts" descriptor with Value.Array(a :: _) -> a | _ -> failwith "artifacts"
            let named (i: int) = setAt [ "filename" ] (str $"a{i}.zip") first
            let artifacts = [ for i in 1..300 -> named i ] @ [ named 1 ]
            let edited = setAt [ "artifacts" ] (Value.Array artifacts) descriptor
            refusedWith ReaderCode.InconsistentDocument "artifacts[300].filename" (readDescriptor edited))

        testCase "the resolver selects among 60,000 releases in linear time" (fun () ->
            let targets = Map.ofList [ for i in 1..60_000 -> release $"1.0.{i}" [ "stable" ] "active" ">=0.4.0" windows ]

            let resolved =
                withinBudget 20.0 "resolving among 60,000 releases" (fun () ->
                    ToolResolver.resolve targets toolId (ToolRequest.OnChannel Channel.Stable) { Os = "windows"; Arch = "x86_64" } (semver "0.4.0"))

            match resolved with
            | Ok selection -> Expect.equal selection.Version.Text "1.0.60000" "the greatest"
            | Error refusal -> failtestf "refused: %A" refusal)
    ]
