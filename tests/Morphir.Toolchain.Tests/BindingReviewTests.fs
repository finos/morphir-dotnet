// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The second review of #583: the binding check's cost, the order rules of the host and provider tables, the row
/// contents, the descriptor readers' rules and the resolver's rules, each pinned by a test that a mutation of the rule
/// fails.
/// </summary>
module Morphir.Toolchain.Tests.BindingReviewTests

open System.Text
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.ReleaseDescriptorTests

let recipeTarget = "toolchains/recipes/fsharp/1.0.0.json"
let frontendTarget = "extensions/fsharp-frontend/0.6.0/release.json"

let items (value: Value) =
    match value with
    | Value.Array items -> items
    | _ -> failwith "not an array"

/// An authenticated entry for served bytes, with an optional custom object.
let entryFor (bytes: byte array) (custom: Value option) =
    { Length = uint64 bytes.Length
      Sha256 = Sha256.ofBytes bytes
      Custom = custom }

/// Runs a read under a time budget on its own task, so a quadratic check fails within the budget instead of hanging.
let withinBudget (seconds: float) (what: string) (read: unit -> 'T) : 'T =
    let task = System.Threading.Tasks.Task.Run(fun () -> read ())

    if task.Wait(System.TimeSpan.FromSeconds seconds) then
        task.Result
    else
        failtestf "%s took longer than %.0f s" what seconds

let readRecipe (value: Value) =
    match RecipeDocument.read (bytesOf value) with
    | Ok recipe -> recipe
    | Error refusal -> failtestf "the recipe does not read: %A" refusal

/// The outcome of the binding check, with served bytes from the signed repository unless overridden.
let checkWith (listed: Map<string, AuthenticatedTarget>) (served: Map<string, byte array>) (recipe: Recipe) =
    let bytesOf (target: string) =
        match served.TryFind target with
        | Some bytes -> Some bytes
        | None -> SignedCaseTests.tryTargetBytes target

    match RecipeBindings.check (SignedCaseTests.repository ()) listed bytesOf recipe with
    | Ok _ -> "valid"
    | Error refusal -> $"{Code.name refusal.Code} @ {DocumentPath.render refusal.Path}"

/// A binding check's inputs: the recipe as a value, the authenticated targets, and the bytes served in place of the
/// signed repository's.
type Setup =
    { Recipe: Value
      Listed: Map<string, AuthenticatedTarget>
      Served: Map<string, byte array> }

let signed () =
    { Recipe = valueOf recipeTarget
      Listed = SignedCaseTests.authenticatedTargets "metadata"
      Served = Map.empty }

let rec replaceText (old: string) (replacement: string) (value: Value) =
    match value with
    | Value.String text when text = old -> Value.String replacement
    | Value.Object members -> Value.Object(members |> List.map (fun (name, item) -> name, replaceText old replacement item))
    | Value.Array items -> Value.Array(items |> List.map (replaceText old replacement))
    | other -> other

let private currentValue (setup: Setup) (target: string) =
    match setup.Served.TryFind target with
    | Some bytes ->
        match Syntax.parseJson bytes with
        | Ok value -> value
        | Error _ -> failtestf "%s does not parse" target
    | None -> valueOf target

/// Serves a target's edited value, listed with its new length and digest (its custom object kept), with every recipe
/// digest of its old bytes rebound to the new ones: the publisher bound the edited bytes.
let editTarget (target: string) (edit: Value -> Value) (setup: Setup) =
    let old = setup.Listed[target]
    let bytes = bytesOf (edit (currentValue setup target))

    let entry =
        { old with
            Length = uint64 bytes.Length
            Sha256 = Sha256.ofBytes bytes }

    { Recipe = replaceText old.Sha256.Text entry.Sha256.Text setup.Recipe
      Listed = setup.Listed.Add(target, entry)
      Served = setup.Served.Add(target, bytes) }

/// Serves other bytes for a target without listing them: the served bytes no longer match their entry.
let serve (target: string) (bytes: byte array) (setup: Setup) =
    { setup with Served = setup.Served.Add(target, bytes) }

let editRecipe (edit: Value -> Value) (setup: Setup) = { setup with Recipe = edit setup.Recipe }

let editListed (target: string) (edit: AuthenticatedTarget -> AuthenticatedTarget) (setup: Setup) =
    { setup with Listed = setup.Listed.Add(target, edit setup.Listed[target]) }

let unlist (target: string) (setup: Setup) = { setup with Listed = setup.Listed.Remove target }

/// The descriptor with an edit applied to each of its artifacts.
let eachArtifact (edit: Value -> Value) (descriptor: Value) =
    setAt [ "artifacts" ] (Value.Array(items (memberOf "artifacts" descriptor) |> List.map edit)) descriptor

/// The check's outcome as "valid" or "code @ path".
let outcomeOf (setup: Setup) =
    checkWith setup.Listed setup.Served (readRecipe setup.Recipe)

let expectOutcome (expected: string) (setup: Setup) = Expect.equal (outcomeOf setup) expected "the outcome"

let tuplePath = [ "claims"; "capabilities"; "modelTransport"; "tuples"; "0" ]

/// The value at a path of member names and array indexes.
let rec valueAt (path: string list) (value: Value) =
    match path, value with
    | [], _ -> value
    | head :: rest, Value.Array items -> valueAt rest (List.item (int head) items)
    | head :: rest, _ -> valueAt rest (memberOf head value)

let private textOf (value: Value) =
    match value with
    | Value.String text -> text
    | _ -> failwith "not a string"

/// The reviewer's shape (amplify.fsx): n compile operations, one dotnet-tool provider for each, all bound to one
/// descriptor whose artifacts' claims list all n languages, so every provider passes and the check runs to the end.
let private sharedDescriptor (n: int) (artifactCount: int) =
        testCase $"{n} providers sharing one descriptor of {n} languages and {artifactCount} artifacts" (fun () ->
            let recipe = valueOf recipeTarget
            let descriptor = valueOf frontendTarget
            let provider = items (memberOf "providers" recipe) |> List.item 1
            let languages = Value.Array [ for i in 1..n -> Value.Object [ "id", str $"l{i}" ] ]
            let artifact = (items (memberOf "artifacts" descriptor)).Head |> setAt [ "claims"; "capabilities"; "frontend"; "languages" ] languages
            let filename = textOf (memberOf "filename" artifact)
            let copies = [ for i in 1..artifactCount -> artifact |> setAt [ "filename" ] (Value.String $"{i}-{filename}") ]
            let descriptorBytes = bytesOf (setAt [ "artifacts" ] (Value.Array copies) descriptor)
            let entry = entryFor descriptorBytes None

            let edited =
                recipe
                |> setAt [ "providers" ] (Value.Array [ for i in 1..n -> provider |> setAt [ "language" ] (str $"l{i}") |> setAt [ "descriptorSha256" ] (str entry.Sha256.Text) ])
                |> setAt [ "operations" ] (Value.Array [ for i in 1..n -> Value.Object [ "operation", str "compile"; "language", str $"l{i}" ] ])

            let recipe = readRecipe edited
            let listed = SignedCaseTests.authenticatedTargets "metadata" |> Map.add frontendTarget entry
            let mutable fetches = 0

            let bytesOf (target: string) =
                if target = frontendTarget then
                    fetches <- fetches + 1
                    Some descriptorBytes
                else
                    SignedCaseTests.tryTargetBytes target

            let outcome =
                withinBudget 5.0 $"checking {n} providers" (fun () -> RecipeBindings.check (SignedCaseTests.repository ()) listed bytesOf recipe)

            Expect.isOk outcome "every provider binds"
            Expect.equal fetches 1 "the shared descriptor is fetched once")

[<Tests>]
let costTests =
    // Review of #583, A1: each provider fetched, authenticated and parsed its descriptor again, and scanned its claim
    // lists with List.contains.
    testList "The binding check reads each shared target once" [
        sharedDescriptor 4_000 1
        sharedDescriptor 2_000 6
        testCase "two offerings of one tool release read its descriptor once" (fun () ->
            let recipe = valueOf recipeTarget
            let host0 = (items (memberOf "hosts" recipe)).Head
            let twin = host0 |> setAt [ "implementation" ] (str "twin")
            let edited = recipe |> setAt [ "hosts" ] (Value.Array [ host0; twin ])
            let recipe = readRecipe edited
            let release = "releases/morphir-dotnet-native-aot/1.2.3.json"
            let mutable fetches = 0

            let bytesOf (target: string) =
                if target = release then fetches <- fetches + 1
                SignedCaseTests.tryTargetBytes target

            let outcome = RecipeBindings.check (SignedCaseTests.repository ()) (SignedCaseTests.authenticatedTargets "metadata") bytesOf recipe
            Expect.isOk outcome "both offerings bind"
            Expect.equal fetches 1 "the shared release descriptor is fetched once")

        testCase "two offerings of one tool at two versions read two releases" (fun () ->
            // The repository publishes no 1.2.4, so the second offering's release is not authenticated.
            let recipe = valueOf recipeTarget
            let host0 = (items (memberOf "hosts" recipe)).Head
            let later = host0 |> setAt [ "implementation" ] (str "twin") |> setAt [ "version" ] (str "1.2.4")
            signed () |> editRecipe (setAt [ "hosts" ] (Value.Array [ host0; later ])) |> expectOutcome "target_not_authenticated @ hosts[1].source")
    ]

[<Tests>]
let tupleTests =
    // Review of #583, A3: tuples were compared by their canonical bytes, after NFC, so KELVIN SIGN equalled K.
    let irVersion (version: string) =
        signed ()
        |> editRecipe (setAt [ "providers"; "0"; "modelTuple"; "irVersion" ] (str "4.0.0-K") >> setAt [ "providers"; "1"; "modelTuple"; "irVersion" ] (str "4.0.0-K"))
        |> editTarget frontendTarget (eachArtifact (setAt (tuplePath @ [ "irVersion" ]) (str version)))

    testList "A model tuple is compared as a parsed value" [
        testCase "the same text binds" (fun () -> irVersion "4.0.0-K" |> expectOutcome "valid")

        testCase "KELVIN SIGN is not K, though NFC maps one to the other" (fun () ->
            irVersion ("4.0.0-" + string (char 0x212A)) |> expectOutcome "release_descriptor_mismatch @ providers[0].modelTuple")

        testCase "member order does not matter" (fun () ->
            let reversed (tuple: Value) =
                match tuple with
                | Value.Object members -> Value.Object(List.rev members)
                | other -> other

            signed ()
            |> editTarget frontendTarget (eachArtifact (fun artifact -> setAt tuplePath (reversed (valueAt tuplePath artifact)) artifact))
            |> expectOutcome "valid")
    ]
[<Tests>]
let servedBytesTests =
    let releaseTarget = "releases/morphir-dotnet-native-aot/1.2.3.json"

    let checkServing (target: string) (serve: unit -> byte array option) =
        let bytesOf (name: string) =
            if name = target then serve () else SignedCaseTests.tryTargetBytes name

        let recipe = readRecipe (valueOf recipeTarget)

        match RecipeBindings.check (SignedCaseTests.repository ()) (SignedCaseTests.authenticatedTargets "metadata") bytesOf recipe with
        | Ok _ -> "valid"
        | Error refusal -> $"{Code.name refusal.Code} @ {DocumentPath.render refusal.Path}"

    // Review of #583, A4 (Some null) and B24 (a source that throws): neither may leave the check as an exception.
    testList "A source that serves no bytes has not served the target" [
        testCase "Some null for a descriptor" (fun () ->
            Expect.equal (checkServing frontendTarget (fun () -> Some(Unchecked.defaultof<byte array>))) "repository_unavailable @ providers[0].descriptorTarget" "the outcome")

        testCase "Some null for a host release" (fun () ->
            Expect.equal (checkServing releaseTarget (fun () -> Some(Unchecked.defaultof<byte array>))) "repository_unavailable @ hosts[0].source" "the outcome")

        testCase "a source that throws for a descriptor" (fun () ->
            Expect.equal (checkServing frontendTarget (fun () -> failwith "the source failed")) "repository_unavailable @ providers[0].descriptorTarget" "the outcome")

        testCase "a source that throws for a host release" (fun () ->
            Expect.equal (checkServing releaseTarget (fun () -> raise (System.IO.IOException "the source failed"))) "repository_unavailable @ hosts[0].source" "the outcome")
    ]
/// A tool-release record of the tool <c>tool</c> at a target, with its members.
let private recordAt (target: string) (members: (string * Value) list) =
    target,
    { Length = 1UL
      Sha256 = (match Sha256.tryParse (String.replicate 64 "a") with Ok s -> s | Error _ -> failwith "sha")
      Custom = Some(Value.Object [ "morphir", Value.Object members ]) }

let private members (version: string) (channels: string list) (status: string) =
    [ "schemaVersion", Value.Number "1"
      "kind", str "tool-release"
      "toolId", str "tool"
      "version", str version
      "channels", Value.Array(channels |> List.map str)
      "status", str status
      "compatibility", Value.Object [ "morphirCli", str ">=0.4.0" ]
      "platforms", Value.Array [ Value.Object [ "os", str "windows"; "arch", str "x86_64" ] ] ]

/// A record at the layout's path for its version.
let private placed (version: string) (channels: string list) (status: string) =
    recordAt $"releases/tool/{version}.json" (members version channels status)

let private tool = match ToolId.tryParse "tool" with Ok id -> id | Error _ -> failwith "tool"
let private version (text: string) = match SemVer.tryParse text with Ok v -> v | Error _ -> failwith text
let private onChannel (text: string) = match Channel.tryParse text with Ok c -> ToolRequest.OnChannel c | Error _ -> failwith text

/// The selection as "target = version", or the refusal's code.
let private resolveIn (records: (string * AuthenticatedTarget) list) (request: ToolRequest) =
    match ToolResolver.resolve (Map.ofList records) tool request { Os = "windows"; Arch = "x86_64" } (version "0.4.0") with
    | Ok selection -> $"{selection.Target} = {selection.Version.Text}"
    | Error refusal -> Code.name refusal.Code

[<Tests>]
let releasePathTests =
    // Review of #583, A5: a record is a release record of the layout only at releases/<tool-id>/<exact-version>.json.
    testList "The resolver reads release records only at their layout path" [
        testCase "a record at another path is ignored" (fun () ->
            let records =
                [ placed "1.0.0" [ "stable" ] "active"
                  recordAt "elsewhere/tool/1.1.0.json" (members "1.1.0" [ "stable" ] "active")
                  recordAt "releases/other/1.2.0.json" (members "1.2.0" [ "stable" ] "active")
                  recordAt "releases/tool/1.3.0/release.json" (members "1.3.0" [ "stable" ] "active") ]

            Expect.equal (resolveIn records (onChannel "stable")) "releases/tool/1.0.0.json = 1.0.0" "the channel"
            Expect.equal (resolveIn records (ToolRequest.Exact(version "1.1.0"))) "no_compatible_release" "exact")

        testCase "a record whose path names another version is ignored" (fun () ->
            let records =
                [ placed "1.0.0" [ "stable" ] "active"
                  recordAt "releases/tool/9.9.9.json" (members "1.1.0" [ "stable" ] "active") ]

            Expect.equal (resolveIn records (onChannel "stable")) "releases/tool/1.0.0.json = 1.0.0" "the channel")

        testCase "a misplaced record does not make a version ambiguous" (fun () ->
            let records =
                [ placed "1.0.0" [ "stable" ] "active"
                  recordAt "mirror/releases/tool/1.0.0.json" (members "1.0.0" [ "stable" ] "active") ]

            Expect.equal (resolveIn records (onChannel "stable")) "releases/tool/1.0.0.json = 1.0.0" "the channel")

        testCase "a record without a version is refused under the tool's release directory and ignored elsewhere" (fun () ->
            let unversioned = members "1.1.0" [ "stable" ] "active" |> List.filter (fst >> (<>) "version")
            let resolveWith (target: string) = resolveIn [ placed "1.0.0" [ "stable" ] "active"; recordAt target unversioned ] (onChannel "stable")
            Expect.equal (resolveWith "releases/tool/1.1.0.json") "release_descriptor_mismatch" "under the directory"
            Expect.equal (resolveWith "elsewhere/1.1.0.json") "releases/tool/1.0.0.json = 1.0.0" "elsewhere"
            Expect.equal (resolveWith "1.1.0.json") "releases/tool/1.0.0.json = 1.0.0" "at the root")

        testCase "a record whose target is its version file at the root is ignored" (fun () ->
            let records = [ placed "1.0.0" [ "stable" ] "active"; recordAt "1.1.0.json" (members "1.1.0" [ "stable" ] "active") ]
            Expect.equal (resolveIn records (onChannel "stable")) "releases/tool/1.0.0.json = 1.0.0" "the channel")

        testCase "a malformed record at its layout path is still refused" (fun () ->
            let records =
                [ placed "1.0.0" [ "stable" ] "active"
                  recordAt "releases/tool/1.1.0.json" (members "1.1.0" [ "stable" ] "retired") ]

            Expect.equal (resolveIn records (onChannel "stable")) "release_descriptor_mismatch" "malformed")
    ]
let private nativeRelease = "releases/morphir-dotnet-native-aot/1.2.3.json"
let private selfContainedRelease = "releases/morphir-dotnet-self-contained/1.2.3.json"
let private backendTarget = "extensions/fsharp-backend/0.1.0/release.json"
let private hostWinX64 = "artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip"
let private hostWinArm64 = "artifacts/morphir-dotnet-native-aot/1.2.3/win-arm64.zip"
let private frontendWinX64 = "extensions/fsharp-frontend/0.6.0/win-x64.zip"
let private frontendWinArm64 = "extensions/fsharp-frontend/0.6.0/win-arm64.zip"
let private frontendPackage = "packages/nuget/morphir.frontend.fsharp.extension/0.6.0/morphir.frontend.fsharp.extension.0.6.0.nupkg"
let private zeros = String.replicate 64 "0"

let private servedBytes (setup: Setup) (target: string) =
    match setup.Served.TryFind target with
    | Some bytes -> bytes
    | None -> SignedCaseTests.targetBytes target

/// Serves the target's bytes with one more space: longer than the authenticated length.
let private longer (target: string) (setup: Setup) = serve target (Array.append (servedBytes setup target) [| 32uy |]) setup

/// Serves the target's bytes with each z replaced by y: the authenticated length, another digest.
let private otherBytes (target: string) (setup: Setup) =
    serve target (servedBytes setup target |> Array.map (fun b -> if b = byte 'z' then byte 'y' else b)) setup

let private editCustom (target: string) (edit: Value -> Value) =
    editListed target (fun entry -> { entry with Custom = entry.Custom |> Option.map edit })

let private releaseState (name: string) (value: Value) = editCustom nativeRelease (setAt [ "morphir"; name ] value)
let private atRecipe (path: string list) (value: Value) = editRecipe (setAt path value)

/// Edits the frontend descriptor's artifact with a filename.
let private frontendArtifact (filename: string) (path: string list) (value: Value) =
    editTarget frontendTarget (fun descriptor ->
        let index = items (memberOf "artifacts" descriptor) |> List.findIndex (fun artifact -> memberOf "filename" artifact = str filename)
        setAt ("artifacts" :: string index :: path) value descriptor)

let private asWasm (filename: string) =
    editTarget frontendTarget (fun descriptor ->
        let index = items (memberOf "artifacts" descriptor) |> List.findIndex (fun artifact -> memberOf "filename" artifact = str filename)
        descriptor |> setAt [ "artifacts"; string index; "runtime" ] (str "wasm") |> removeAt [ "artifacts"; string index; "platform" ])

let private case (name: string) (expected: string) (edits: (Setup -> Setup) list) =
    testCase name (fun () -> edits |> List.fold (fun setup edit -> edit setup) (signed ()) |> expectOutcome expected)

[<Tests>]
let hostRowTests =
    // Review of #583, A2: each row of the host table reports its code at its path, and of two faults the earlier row
    // is reported (B05, B21, B22 and the adjacent pairs).
    testList "Host binding rows" [
        testList "one fault" [
            case "the signed recipe binds" "valid" []
            case "H1 the release target is absent" "target_not_authenticated @ hosts[0].source" [ unlist nativeRelease ]
            case "H1 the release target's kind is another" "target_not_authenticated @ hosts[0].source" [ releaseState "kind" (str "tool-artifact") ]
            case "H1 the release target states another version" "target_not_authenticated @ hosts[0].source" [ releaseState "version" (str "1.2.4") ]
            case "H1 the release target states another tool" "target_not_authenticated @ hosts[0].source" [ releaseState "toolId" (str "morphir-dotnet-self-contained") ]
            case "H1 the release target has no custom object" "target_not_authenticated @ hosts[0].source" [ editListed nativeRelease (fun entry -> { entry with Custom = None }) ]
            case "H2 the release is revoked" "release_revoked @ hosts[0].version" [ releaseState "status" (str "revoked") ]
            case "H3 the release bytes are longer" "target_length_mismatch @ hosts[0].source" [ longer nativeRelease ]
            case "H3 the release bytes have another digest" "target_digest_mismatch @ hosts[0].source" [ otherBytes nativeRelease ]
            case "H4 the release is not version 1" "release_descriptor_mismatch @ hosts[0].source" [ editTarget nativeRelease (setAt [ "schemaVersion" ] (Value.Number "2")) ]
            case "H5 the win-x64 target is absent" "target_not_authenticated @ hosts[0].platforms.win-x64.target" [ unlist hostWinX64 ]
            case "H6 the win-x64 sha256 is another" "target_digest_mismatch @ hosts[0].platforms.win-x64.sha256" [ atRecipe [ "hosts"; "0"; "platforms"; "win-x64"; "sha256" ] (str zeros) ]
            case "H7 the release does not list the win-x64 target" "release_descriptor_mismatch @ hosts[0].platforms.win-x64.target" [ editTarget nativeRelease (setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/x.zip")) ]
            case "H8 the listed artifact is for another platform" "artifact_platform_mismatch @ hosts[0].platforms.win-x64.target" [ editTarget nativeRelease (setAt [ "artifacts"; "0"; "platform"; "arch" ] (str "aarch64")) ]
            case "H9 the artifact target's kind is another" "release_descriptor_mismatch @ hosts[0].platforms.win-x64.target" [ editCustom hostWinX64 (setAt [ "morphir"; "kind" ] (str "tool-release")) ]
            case "H9 the artifact target's platform is another" "release_descriptor_mismatch @ hosts[0].platforms.win-x64.target" [ editCustom hostWinX64 (setAt [ "morphir"; "platform"; "arch" ] (str "aarch64")) ]
        ]

        testList "two faults report the earlier row" [
            case "H1 kind + H2 revoked" "target_not_authenticated @ hosts[0].source" [ releaseState "kind" (str "tool-artifact"); releaseState "status" (str "revoked") ]
            case "H2 revoked + H3 length" "release_revoked @ hosts[0].version" [ releaseState "status" (str "revoked"); longer nativeRelease ]
            case "H2 revoked + H4 not version 1" "release_revoked @ hosts[0].version" [ editTarget nativeRelease (setAt [ "schemaVersion" ] (Value.Number "2")); releaseState "status" (str "revoked") ]
            case "H3 length + H4 not version 1" "target_length_mismatch @ hosts[0].source" [ editTarget nativeRelease (setAt [ "schemaVersion" ] (Value.Number "2")); longer nativeRelease ]
            case "H4 not version 1 + H5 target absent" "release_descriptor_mismatch @ hosts[0].source" [ editTarget nativeRelease (setAt [ "schemaVersion" ] (Value.Number "2")); unlist hostWinX64 ]
            case "H5 target absent + H6 digest, one platform" "target_not_authenticated @ hosts[0].platforms.win-x64.target" [ unlist hostWinX64; atRecipe [ "hosts"; "0"; "platforms"; "win-x64"; "sha256" ] (str zeros) ]
            case "H6 digest + H7 unlisted, one platform" "target_digest_mismatch @ hosts[0].platforms.win-x64.sha256" [ atRecipe [ "hosts"; "0"; "platforms"; "win-x64"; "sha256" ] (str zeros); editTarget nativeRelease (setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/x.zip")) ]
            case "H7 unlisted + H9 metadata, one platform" "release_descriptor_mismatch @ hosts[0].platforms.win-x64.target" [ editTarget nativeRelease (setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/x.zip")); editCustom hostWinX64 (setAt [ "morphir"; "kind" ] (str "x")) ]
            case "H8 platform + H9 metadata, one platform" "artifact_platform_mismatch @ hosts[0].platforms.win-x64.target" [ editTarget nativeRelease (setAt [ "artifacts"; "0"; "platform"; "arch" ] (str "aarch64")); editCustom hostWinX64 (setAt [ "morphir"; "kind" ] (str "x")) ]
            // Platforms are checked one at a time, every row for one before the next (see "For the contract author").
            case "H7 at win-x64 + H5 at win-arm64" "release_descriptor_mismatch @ hosts[0].platforms.win-x64.target" [ editTarget nativeRelease (setAt [ "artifacts"; "0"; "targetPath" ] (str "artifacts/x.zip")); unlist hostWinArm64 ]
            case "H5 at win-arm64 + H6 at win-x64" "target_digest_mismatch @ hosts[0].platforms.win-x64.sha256" [ unlist hostWinArm64; atRecipe [ "hosts"; "0"; "platforms"; "win-x64"; "sha256" ] (str zeros) ]
            case "host 0 H9 + host 1 H1" "release_descriptor_mismatch @ hosts[0].platforms.win-x64.target" [ editCustom hostWinX64 (setAt [ "morphir"; "kind" ] (str "x")); unlist selfContainedRelease ]
            case "host 1 H1 + provider 0 repository: hosts before providers" "target_not_authenticated @ hosts[1].source" [ unlist selfContainedRelease; atRecipe [ "providers"; "0"; "repositoryId" ] (str zeros) ]
            case "a dotnet-tool host's digest + provider 0 repository" "target_digest_mismatch @ hosts[3].platforms.win-x64.sha256" [ atRecipe [ "hosts"; "3"; "platforms"; "win-x64"; "sha256" ] (str zeros); atRecipe [ "providers"; "0"; "repositoryId" ] (str zeros) ]
        ]

        // B08: version 1 platforms state no C library, so a musl platform has no version 1 platform. The reader
        // refuses a musl platform of a tool-release offering, so the check is reached only by a recipe value built in
        // code; it must still bind no glibc artifact to musl.
        testCase "a musl platform binds no glibc artifact" (fun () ->
            let recipe = readRecipe (valueOf recipeTarget)
            let host = recipe.Hosts.Head
            let glibc = host.Platforms |> List.find (fst >> (=) Platform.LinuxX64) |> snd
            let musl = { recipe with Hosts = [ { host with Platforms = host.Platforms @ [ Platform.LinuxMuslX64, glibc ] } ] }

            let outcome =
                match RecipeBindings.checkHosts (SignedCaseTests.repository ()) (SignedCaseTests.authenticatedTargets "metadata") SignedCaseTests.tryTargetBytes musl with
                | Ok _ -> "valid"
                | Error refusal -> $"{Code.name refusal.Code} @ {DocumentPath.render refusal.Path}"

            Expect.equal outcome "artifact_platform_mismatch @ hosts[0].platforms.linux-musl-x64.target" "musl"
            Expect.isNone (RecipeBindings.toolPlatform Platform.LinuxMuslArm64) "musl arm64")
    ]

[<Tests>]
let providerRowTests =
    testList "Provider binding rows" [
        testList "one fault" [
            case "P1 another repository" "repository_mismatch @ providers[0].repositoryId" [ atRecipe [ "providers"; "0"; "repositoryId" ] (str zeros) ]
            case "P2 the descriptor is absent" "target_not_authenticated @ providers[0].descriptorTarget" [ unlist frontendTarget ]
            case "P2 the descriptorSha256 is another" "target_digest_mismatch @ providers[0].descriptorSha256" [ atRecipe [ "providers"; "0"; "descriptorSha256" ] (str zeros) ]
            case "P3 the descriptor bytes are longer" "target_length_mismatch @ providers[0].descriptorTarget" [ longer frontendTarget ]
            case "P3 the descriptor bytes have another digest" "target_digest_mismatch @ providers[0].descriptorTarget" [ otherBytes frontendTarget ]
            case "P4 the descriptor is another draft" "release_descriptor_mismatch @ providers[0].descriptorTarget" [ editTarget frontendTarget (setAt [ "schemaVersion" ] (str "2.0.0-draft.1")) ]
            case "P5 the descriptor's extensionId is another" "release_descriptor_mismatch @ providers[0].extensionId" [ editTarget frontendTarget (setAt [ "extensionId" ] (str "other")) ]
            case "P6 the descriptor's version is another" "release_descriptor_mismatch @ providers[0].version" [ editTarget frontendTarget (setAt [ "version" ] (str "0.6.1")) ]
            case "P7 an artifact's claims name another version" "release_descriptor_mismatch @ providers[0].descriptorTarget" [ editTarget frontendTarget (setAt [ "artifacts"; "3"; "claims"; "extension"; "version" ] (str "0.6.1")) ]
            case "P7 an artifact's claims name another extension" "release_descriptor_mismatch @ providers[0].descriptorTarget" [ editTarget frontendTarget (setAt [ "artifacts"; "3"; "claims"; "extension"; "id" ] (str "other")) ]
            case "P8 the win-x64 artifact is absent" "target_not_authenticated @ providers[0].platforms.win-x64.artifactTarget" [ unlist frontendWinX64 ]
            case "P8 the win-x64 artifactSha256 is another" "target_digest_mismatch @ providers[0].platforms.win-x64.artifactSha256" [ atRecipe [ "providers"; "0"; "platforms"; "win-x64"; "artifactSha256" ] (str zeros) ]
            case "P9 the descriptor does not list the artifact" "release_descriptor_mismatch @ providers[0].platforms.win-x64.artifactTarget" [ frontendArtifact "win-x64.zip" [ "filename" ] (str "other.zip") ]
            case "P10 the listed artifact is WASM" "release_descriptor_mismatch @ providers[0].platforms.win-x64.artifactTarget" [ asWasm "win-x64.zip" ]
            case "P11 the listed digest is another" "target_digest_mismatch @ providers[0].platforms.win-x64.artifactSha256" [ frontendArtifact "win-x64.zip" [ "sha256" ] (str zeros) ]
            case "P12 the target triple is another" "artifact_platform_mismatch @ providers[0].platforms.win-x64.artifactTarget" [ frontendArtifact "win-x64.zip" [ "platform" ] (str "aarch64-pc-windows-msvc") ]
            case "P13 the claims list no frontend kind" "release_descriptor_mismatch @ providers[0].operation" [ frontendArtifact "win-x64.zip" [ "claims"; "extension"; "types" ] (Value.Array [ str "backend" ]) ]
            case "P14 the claims list another MEP version" "release_descriptor_mismatch @ providers[0].protocolVersion" [ frontendArtifact "win-x64.zip" [ "claims"; "protocolVersions" ] (Value.Array [ str "0.2.0-draft.2" ]) ]
            case "P15 compile is false, with the language listed" "release_descriptor_mismatch @ providers[0].language" [ frontendArtifact "win-x64.zip" [ "claims"; "capabilities"; "frontend"; "compile" ] (Value.Boolean false) ]
            case "P15 another language" "release_descriptor_mismatch @ providers[0].language" [ frontendArtifact "win-x64.zip" [ "claims"; "capabilities"; "frontend"; "languages"; "0"; "id" ] (str "elm") ]
            case "P15 generate is false, with the target listed" "release_descriptor_mismatch @ providers[2].target" [ editTarget backendTarget (eachArtifact (setAt [ "claims"; "capabilities"; "backend"; "generate" ] (Value.Boolean false))) ]
            case "P16 another irVersion" "release_descriptor_mismatch @ providers[0].modelTuple" [ frontendArtifact "win-x64.zip" (tuplePath @ [ "irVersion" ]) (str "3.0.0") ]
            case "P16 a tuple with another member" "release_descriptor_mismatch @ providers[0].modelTuple" [ frontendArtifact "win-x64.zip" (tuplePath @ [ "extra" ]) (str "x") ]
            case "a dotnet-tool package digest" "target_digest_mismatch @ providers[1].packageSha256" [ atRecipe [ "providers"; "1"; "packageSha256" ] (str zeros) ]
            // B20: a .NET tool package is described by every artifact's claims, so the last artifact's are checked too.
            case "a dotnet-tool whose last artifact claims another language" "release_descriptor_mismatch @ providers[0].language" [
                editRecipe (fun recipe -> setAt [ "providers" ] (Value.Array(items (memberOf "providers" recipe) |> List.tail)) recipe)
                frontendArtifact "linux-arm64.zip" [ "claims"; "capabilities"; "frontend"; "languages"; "0"; "id" ] (str "elm") ]
        ]

        testList "two faults report the earlier row" [
            case "P1 repository + P2 descriptor absent" "repository_mismatch @ providers[0].repositoryId" [ atRecipe [ "providers"; "0"; "repositoryId" ] (str zeros); unlist frontendTarget ]
            case "P2 descriptor absent + P2 descriptorSha256" "target_not_authenticated @ providers[0].descriptorTarget" [ unlist frontendTarget; atRecipe [ "providers"; "0"; "descriptorSha256" ] (str zeros) ]
            case "P2 descriptorSha256 + P3 length" "target_digest_mismatch @ providers[0].descriptorSha256" [ atRecipe [ "providers"; "0"; "descriptorSha256" ] (str zeros); longer frontendTarget ]
            case "P3 length + P4 another draft" "target_length_mismatch @ providers[0].descriptorTarget" [ editTarget frontendTarget (setAt [ "schemaVersion" ] (str "2.0.0-draft.1")); longer frontendTarget ]
            case "P4 another draft + P5 extensionId" "release_descriptor_mismatch @ providers[0].descriptorTarget" [ editTarget frontendTarget (setAt [ "schemaVersion" ] (str "2.0.0-draft.1") >> setAt [ "extensionId" ] (str "other")) ]
            case "P5 extensionId + P6 version" "release_descriptor_mismatch @ providers[0].extensionId" [ editTarget frontendTarget (setAt [ "extensionId" ] (str "other") >> setAt [ "version" ] (str "0.6.1")) ]
            case "P6 version + P7 claims identity" "release_descriptor_mismatch @ providers[0].version" [ editTarget frontendTarget (setAt [ "version" ] (str "0.6.1")) ]
            case "P7 claims identity + P8 artifact absent" "release_descriptor_mismatch @ providers[0].descriptorTarget" [ editTarget frontendTarget (setAt [ "artifacts"; "3"; "claims"; "extension"; "id" ] (str "other")); unlist frontendWinX64 ]
            case "P8 artifact absent + P9 unlisted" "target_not_authenticated @ providers[0].platforms.win-x64.artifactTarget" [ unlist frontendWinX64; frontendArtifact "win-x64.zip" [ "filename" ] (str "other.zip") ]
            case "P9 unlisted + P11 listed digest elsewhere" "release_descriptor_mismatch @ providers[0].platforms.win-x64.artifactTarget" [ frontendArtifact "win-x64.zip" [ "filename" ] (str "other.zip"); frontendArtifact "other.zip" [ "sha256" ] (str zeros) ]
            case "P10 runtime + P11 listed digest" "release_descriptor_mismatch @ providers[0].platforms.win-x64.artifactTarget" [ asWasm "win-x64.zip"; frontendArtifact "win-x64.zip" [ "sha256" ] (str zeros) ]
            case "P11 listed digest + P12 triple" "target_digest_mismatch @ providers[0].platforms.win-x64.artifactSha256" [ frontendArtifact "win-x64.zip" [ "sha256" ] (str zeros); frontendArtifact "win-x64.zip" [ "platform" ] (str "aarch64-pc-windows-msvc") ]
            case "P12 triple + P13 kind" "artifact_platform_mismatch @ providers[0].platforms.win-x64.artifactTarget" [ frontendArtifact "win-x64.zip" [ "platform" ] (str "aarch64-pc-windows-msvc"); frontendArtifact "win-x64.zip" [ "claims"; "extension"; "types" ] (Value.Array [ str "backend" ]) ]
            case "P13 kind + P14 protocol" "release_descriptor_mismatch @ providers[0].operation" [ frontendArtifact "win-x64.zip" [ "claims"; "extension"; "types" ] (Value.Array [ str "backend" ]); frontendArtifact "win-x64.zip" [ "claims"; "protocolVersions" ] (Value.Array [ str "0.2.0-draft.2" ]) ]
            case "P14 protocol + P15 operand" "release_descriptor_mismatch @ providers[0].protocolVersion" [ frontendArtifact "win-x64.zip" [ "claims"; "protocolVersions" ] (Value.Array [ str "0.2.0-draft.2" ]); frontendArtifact "win-x64.zip" [ "claims"; "capabilities"; "frontend"; "compile" ] (Value.Boolean false) ]
            case "P15 operand + P16 model" "release_descriptor_mismatch @ providers[0].language" [ frontendArtifact "win-x64.zip" [ "claims"; "capabilities"; "frontend"; "compile" ] (Value.Boolean false); frontendArtifact "win-x64.zip" (tuplePath @ [ "irVersion" ]) (str "3.0.0") ]
            // A process provider's platforms are checked one at a time, the claim rows with the artifact rows.
            case "P13 kind at win-x64 + P8 artifact absent at win-arm64" "release_descriptor_mismatch @ providers[0].operation" [ frontendArtifact "win-x64.zip" [ "claims"; "extension"; "types" ] (Value.Array [ str "backend" ]); unlist frontendWinArm64 ]
            case "provider 0 P16 + provider 2 P1" "release_descriptor_mismatch @ providers[0].modelTuple" [ frontendArtifact "win-x64.zip" (tuplePath @ [ "irVersion" ]) (str "3.0.0"); atRecipe [ "providers"; "2"; "repositoryId" ] (str zeros) ]
            case "a dotnet-tool package absent + a claims fault" "target_not_authenticated @ providers[0].packageTarget" [
                editRecipe (fun recipe -> setAt [ "providers" ] (Value.Array(items (memberOf "providers" recipe) |> List.tail)) recipe)
                unlist frontendPackage
                frontendArtifact "linux-arm64.zip" [ "claims"; "capabilities"; "frontend"; "languages"; "0"; "id" ] (str "elm") ]
        ]
    ]
let private readerOutcome (result: Result<'T, ReaderRefusal>) =
    match result with
    | Ok _ -> "read"
    | Error refusal -> $"{Code.readerName refusal.Code} @ {DocumentPath.render refusal.Path}"

/// The bytes padded with trailing spaces to a length.
let private paddedTo (length: int) (bytes: byte array) =
    Array.append bytes (Array.create (length - bytes.Length) (byte ' '))

[<Tests>]
let readerRuleTests =
    let nativeAot () = valueOf nativeRelease
    let readRelease (edit: Value -> Value) = ToolReleaseDocument.read (bytesOf (edit (nativeAot ()))) |> readerOutcome

    // Review of #583, A2: reader rules no test pinned (R10 to R13, E04, E09).
    testList "Release descriptor reader rules" [
        testCase "R10 a target path with a drive prefix is refused" (fun () ->
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "targetPath" ] (str "C:/artifacts/x.zip"))) "invalid_value @ artifacts[0].targetPath" "C:/"
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "launch"; "path" ] (str "c:morphir.exe"))) "invalid_value @ artifacts[0].launch.path" "c:")

        testCase "R11 an arch that ends in a hyphen or underscore is refused" (fun () ->
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "platform"; "arch" ] (str "x86-"))) "invalid_value @ artifacts[0].platform.arch" "x86-"
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "platform"; "arch" ] (str "x86_"))) "invalid_value @ artifacts[0].platform.arch" "x86_"
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "platform"; "arch" ] (str "x86_64-v2"))) "read" "an inner hyphen")

        testCase "R12 a v1 descriptor of exactly 1 MiB is read" (fun () ->
            let bytes = paddedTo ToolReleaseDocument.limit (SignedCaseTests.targetBytes nativeRelease)
            Expect.equal (ToolReleaseDocument.read bytes |> readerOutcome) "read" "1 MiB"
            Expect.equal (ToolReleaseDocument.read (paddedTo (ToolReleaseDocument.limit + 1) bytes) |> readerOutcome) "invalid_document @ " "1 MiB + 1")

        testCase "R13 the archive object is closed" (fun () ->
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "archive"; "extra" ] (str "x"))) "invalid_document @ artifacts[0].archive.extra" "archive"
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "launch"; "extra" ] (str "x"))) "invalid_document @ artifacts[0].launch.extra" "launch"
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "platform"; "extra" ] (str "x"))) "invalid_document @ artifacts[0].platform.extra" "platform")

        testCase "E04 an absent claimCheck reads as unchecked, never probed" (fun () ->
            let read (edit: Value -> Value) =
                match ExtensionReleaseDocument.read (bytesOf (edit (valueOf frontendTarget))) with
                | Ok release -> release.Artifacts.Head.ClaimCheck
                | Error refusal -> failtestf "refused: %A" refusal

            Expect.equal (read (removeAt [ "artifacts"; "0"; "claimCheck" ])) DescriptorClaimCheck.Unchecked "absent"
            Expect.equal (read (setAt [ "artifacts"; "0"; "claimCheck" ] (str "probed"))) DescriptorClaimCheck.Probed "probed"
            Expect.equal (read id) DescriptorClaimCheck.Unchecked "unchecked")

        testCase "E04 a bound artifact without claimCheck is bound as unchecked" (fun () ->
            let setup = signed () |> editTarget frontendTarget (eachArtifact (removeAt [ "claimCheck" ]))

            match RecipeBindings.check (SignedCaseTests.repository ()) setup.Listed (fun t -> setup.Served.TryFind t |> Option.orElse (SignedCaseTests.tryTargetBytes t)) (readRecipe setup.Recipe) with
            | Ok bound ->
                let checks = [ for provider in bound.Providers.[0..1] do for _, claims in provider.Artifacts do for claim in claims -> claim.ClaimCheck ]
                Expect.isNonEmpty checks "claims"
                Expect.allEqual checks DescriptorClaimCheck.Unchecked "every claim check"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "E09 an extension descriptor over 1 MiB is refused before it is parsed" (fun () ->
            let bytes = SignedCaseTests.targetBytes frontendTarget
            Expect.equal (ExtensionReleaseDocument.read (paddedTo ExtensionReleaseDocument.limit bytes) |> readerOutcome) "read" "1 MiB"
            Expect.equal (ExtensionReleaseDocument.read (paddedTo (ExtensionReleaseDocument.limit + 1) bytes) |> readerOutcome) "invalid_document @ " "1 MiB + 1")
    ]

[<Tests>]
let resolverRuleTests =
    let withCli (cli: string) (version: string) =
        recordAt $"releases/tool/{version}.json" (members version [ "stable" ] "active" |> List.map (fun (name, value) -> if name = "compatibility" then name, Value.Object [ "morphirCli", str cli ] else name, value))

    // Review of #583, A2: resolver rules no test pinned (T01, T08, T09, T12, T13), with several releases per channel.
    testList "Tool release resolution rules" [
        testCase "T01 a yanked release is never selected by a channel, even if its state lists one" (fun () ->
            let records = [ placed "1.0.0" [ "stable" ] "active"; placed "1.1.0" [ "stable" ] "yanked"; placed "1.2.0" [] "yanked" ]
            Expect.equal (resolveIn records (onChannel "stable")) "releases/tool/1.0.0.json = 1.0.0" "stable"
            Expect.equal (resolveIn records (ToolRequest.Exact(version "1.1.0"))) "releases/tool/1.1.0.json = 1.1.0" "exact")

        testCase "T08 insiders and preview select one family: every preview channel" (fun () ->
            let records =
                [ placed "2.0.0-preview.1" [ "insiders" ] "active"
                  placed "2.0.0-preview.2" [ "preview/fast" ] "active"
                  placed "2.0.0-preview.3" [ "preview" ] "active"
                  placed "1.0.0" [ "stable" ] "active" ]

            Expect.equal (resolveIn records (onChannel "insiders")) "releases/tool/2.0.0-preview.3.json = 2.0.0-preview.3" "insiders selects preview"
            Expect.equal (resolveIn records (onChannel "preview")) "releases/tool/2.0.0-preview.3.json = 2.0.0-preview.3" "preview"

            let insidersOnly = [ placed "2.0.0-preview.1" [ "insiders" ] "active"; placed "2.0.0-preview.0" [ "preview" ] "active" ]
            Expect.equal (resolveIn insidersOnly (onChannel "preview")) "releases/tool/2.0.0-preview.1.json = 2.0.0-preview.1" "preview selects insiders")

        testCase "T09 a named preview selects only its own segment" (fun () ->
            let records =
                [ placed "2.0.0-preview.1" [ "preview/fast" ] "active"
                  placed "2.0.0-preview.2" [ "preview/fast" ] "active"
                  placed "2.0.0-preview.5" [ "preview/slow" ] "active"
                  placed "2.0.0-preview.6" [ "preview" ] "active"
                  placed "2.0.0-preview.7" [ "insiders" ] "active" ]

            Expect.equal (resolveIn records (onChannel "preview/fast")) "releases/tool/2.0.0-preview.2.json = 2.0.0-preview.2" "fast"
            Expect.equal (resolveIn records (onChannel "preview/slow")) "releases/tool/2.0.0-preview.5.json = 2.0.0-preview.5" "slow"
            Expect.equal (resolveIn records (onChannel "preview/other")) "no_compatible_release" "other")

        testCase "T12 a release state of another schemaVersion, or none, is refused" (fun () ->
            let withSchema (schema: Value option) =
                let rest = members "1.1.0" [ "stable" ] "active" |> List.filter (fst >> (<>) "schemaVersion")
                let state = match schema with Some value -> ("schemaVersion", value) :: rest | None -> rest
                [ placed "1.0.0" [ "stable" ] "active"; recordAt "releases/tool/1.1.0.json" state ]

            Expect.equal (resolveIn (withSchema (Some(Value.Number "2"))) (onChannel "stable")) "release_descriptor_mismatch" "2"
            Expect.equal (resolveIn (withSchema (Some(str "1"))) (onChannel "stable")) "release_descriptor_mismatch" "\"1\""
            Expect.equal (resolveIn (withSchema None) (onChannel "stable")) "release_descriptor_mismatch" "absent")

        testCase "T13 a CLI requirement with an empty comparator is refused" (fun () ->
            for cli in [ ">=0.4.0, "; ", >=0.4.0"; ">=0.4.0,,<0.5.0"; "" ] do
                Expect.equal (resolveIn [ withCli cli "1.0.0" ] (onChannel "stable")) "release_descriptor_mismatch" $"'{cli}'"
                Expect.isNone (ToolResolver.requirement cli) $"'{cli}' parses"

            Expect.equal (resolveIn [ withCli ">=0.4.0, <0.5.0" "1.0.0" ] (onChannel "stable")) "releases/tool/1.0.0.json = 1.0.0" "two comparators")
    ]
[<Tests>]
let listingTests =
    // A1 indexes a release's artifacts by target path; a target listed twice is still bound by its first listing, as
    // List.tryFind did before.
    testList "A release that lists a target twice" [
        case "binds by its first listing" "valid" [
            editTarget nativeRelease (fun release ->
                let artifacts = items (memberOf "artifacts" release)
                let second = artifacts.Head |> setAt [ "platform"; "arch" ] (str "aarch64")
                setAt [ "artifacts" ] (Value.Array(artifacts.Head :: second :: artifacts.Tail)) release) ]

        case "refuses when its first listing is for another platform" "artifact_platform_mismatch @ hosts[0].platforms.win-x64.target" [
            editTarget nativeRelease (fun release ->
                let artifacts = items (memberOf "artifacts" release)
                let first = artifacts.Head |> setAt [ "platform"; "arch" ] (str "aarch64")
                setAt [ "artifacts" ] (Value.Array(first :: artifacts)) release) ]
    ]
/// A string of the characters with these code points, written so that no editor normalizes them.
let private chars (codes: int list) = codes |> List.map (char >> string) |> String.concat ""

[<Tests>]
let schemaReadingTests =
    let readRelease (edit: Value -> Value) = ToolReleaseDocument.read (bytesOf (edit (valueOf nativeRelease))) |> readerOutcome

    // Review of #583, A6: where the v1 profile has no rule of its own the reader matches tool-release-v1.schema.json;
    // the bounds the toolchain contract states for tool ids and versions are kept.
    testList "The v1 reader against tool-release-v1.schema.json" [
        testCase "an os longer than 64 characters is read, as the schema reads it" (fun () ->
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "platform"; "os" ] (str (String.replicate 65 "w")))) "read" "65"
            Expect.equal (readRelease (setAt [ "artifacts"; "0"; "platform"; "os" ] (str "Windows"))) "invalid_value @ artifacts[0].platform.os" "not a token")

        testCase "a tool id over 182 characters and a version over 256 are refused, as the toolchain contract bounds them" (fun () ->
            Expect.equal (readRelease (setAt [ "tool"; "id" ] (str (String.replicate 183 "t")))) "invalid_value @ tool.id" "183"
            Expect.equal (readRelease (setAt [ "tool"; "id" ] (str (String.replicate 182 "t")))) "read" "182"
            Expect.equal (readRelease (setAt [ "version" ] (str ("1.2.3-" + String.replicate 251 "a")))) "invalid_value @ version" "257"
            Expect.equal (readRelease (setAt [ "version" ] (str "1234567890123456.0.0"))) "invalid_value @ version" "16 digits")

        testCase "tool.name and morphirCli need a character outside ECMA-262 white space" (fun () ->
            // ECMA-262 WhiteSpace and LineTerminator: TAB, VT, FF, ZWNBSP, the Zs category, LF, CR, LS and PS.
            let ecma =
                [ 0x09; 0x0B; 0x0C; 0xFEFF; 0x20; 0xA0; 0x1680; 0x2000; 0x2001; 0x2002; 0x2003; 0x2004; 0x2005; 0x2006
                  0x2007; 0x2008; 0x2009; 0x200A; 0x202F; 0x205F; 0x3000; 0x0A; 0x0D; 0x2028; 0x2029 ]

            for name, path in [ "tool.name", [ "tool"; "name" ]; "compatibility.morphirCli", [ "compatibility"; "morphirCli" ] ] do
                Expect.equal (readRelease (setAt path (str (chars [ 0xFEFF ])))) $"invalid_value @ {name}" $"{name}: ZWNBSP"
                Expect.equal (readRelease (setAt path (str (chars ecma)))) $"invalid_value @ {name}" $"{name}: ECMA white space only"

                // NEL, the Mongolian vowel separator (Cf since Unicode 6.3) and ZWSP are not ECMA white space.
                for code in [ 0x85; 0x180E; 0x200B; int 'x' ] do
                    Expect.equal (readRelease (setAt path (str (chars [ code ])))) "read" $"{name}: U+{code:X4}")

        testCase "a schemaVersion equal to 1 as a number is read, however it is spelled" (fun () ->
            for spelling in [ "1"; "1.0"; "1e0"; "1E0"; "10e-1"; "0.1e1"; "1.000e+0"; "100E-2" ] do
                Expect.equal (readRelease (setAt [ "schemaVersion" ] (Value.Number spelling))) "read" spelling

            for spelling in [ "2"; "1.5"; "0"; "-1"; "-1.0"; "1e400"; "1e-400"; "11e-1"; "0.99999999999999999999999999999"; "1e99999999999" ] do
                Expect.equal (readRelease (setAt [ "schemaVersion" ] (Value.Number spelling))) "unsupported_contract_version @ schemaVersion" spelling)
    ]
[<Tests>]
let constructionTests =
    // Review of #583, A8: with public constructors any caller could build a RecipeDigest without authenticating
    // anything, so "authenticated" held by convention. Only the binding check produces these values now.
    testList "Only the binding check produces bound values" [
        for t, properties in
            [ typeof<RecipeDigest>, [ "RepositoryId"; "Target"; "Sha256" ]
              typeof<BoundHost>, [ "Offering"; "Release"; "Artifacts" ]
              typeof<BoundClaims>, [ "Claims"; "ClaimCheck" ]
              typeof<BoundProvider>, [ "Provider"; "Descriptor"; "Release"; "Artifacts" ]
              typeof<BoundRecipe>, [ "Hosts"; "Providers" ] ] ->
            testCase t.Name (fun () ->
                Expect.isEmpty (t.GetConstructors()) "no public constructor"

                let readable = t.GetProperties() |> Array.filter (fun p -> p.CanRead && not p.CanWrite) |> Array.map _.Name |> Set.ofArray
                Expect.isTrue (Set.isSubset (set properties) readable) $"its members are public and read-only: {readable}"
                Expect.isEmpty (t.GetProperties() |> Array.filter _.CanWrite) "nothing is writable")
    ]