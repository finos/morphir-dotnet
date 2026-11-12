// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.StateTransactionTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open IoTestSupport

let utf8 (value: string) = Encoding.UTF8.GetBytes value

let sample schema =
    use cases =
        JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "ParentContracts", "package-cases.json")
            )
        )

    let first =
        cases.RootElement.GetProperty("cases").EnumerateArray()
        |> Seq.head

    first.GetProperty("schemaDocuments").EnumerateArray()
    |> Seq.find (fun item ->
        item.GetProperty("schema").GetString() = schema
        && item.GetProperty("valid").GetBoolean()
    )
    |> fun item -> nonNull (JsonNode.Parse(item.GetProperty("document").GetRawText()))

let bytes (value: JsonNode) = utf8 (value.ToJsonString())

let getResult result =
    match result with
    | Ok value -> value
    | Error(failure: InstallationFailure) -> failtest (failure.ToString())

let refuseResult code result =
    match result with
    | Error(failure: InstallationFailure) -> Expect.equal failure.Code code failure.Message
    | Ok _ -> failtest $"Expected {code}"

let emptyCatalog =
    utf8
        "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/catalog.schema.json\",\"extensions\":[]}"

[<Tests>]
let readers =
    testLabel "PlatformSpecific" <| testList "StateReaders" [
        test "valid published catalog and lock have consistent bindings" {
            let catalog =
                StateDocuments.readCatalog (bytes (sample "catalog.schema.json"))
                |> getResult

            let locked =
                StateDocuments.readLock (bytes (sample "lock.schema.json"))
                |> getResult

            StateDocuments.bind catalog.Entries.Head locked
            |> getResult
            |> ignore
        }
        test "catalog matching schema without required semantic fields refuses" {
            StateDocuments.readCatalog (
                utf8
                    "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/catalog.schema.json\"}"
            )
            |> refuseResult "extension.invalid_document"
        }
        test "open catalog preserves unknown optional selected fields" {
            let node = sample "catalog.schema.json"
            let entry = nonNull ((nonNull node["extensions"])[0])
            entry["future"] <- JsonValue.Create "keep"

            let catalog =
                StateDocuments.readCatalog (bytes node)
                |> getResult

            Expect.isTrue
                (catalog.Entries.Head.Json.ToString().Length > 0)
                "parsed selected value retained"

            Expect.equal
                (SharedJson.optional "future" (SharedJson.members "entry" catalog.Entries.Head.Json))
                (Some(Morphir.Toolchain.Value.String "keep"))
                "not discarded"
        }
        test "closed lock refuses nested unknown claims fields" {
            let node = sample "lock.schema.json"
            let index = nonNull node["index"]
            let claims = nonNull index["claims"]
            claims["future"] <- JsonValue.Create true

            StateDocuments.readLock (bytes node)
            |> refuseResult "extension.invalid_document"
        }
        test "lock args must match selected catalog exactly" {
            let catalog =
                StateDocuments.readCatalog (bytes (sample "catalog.schema.json"))
                |> getResult

            let node = sample "lock.schema.json"
            node["args"] <- JsonArray(JsonValue.Create "--changed")

            let locked =
                StateDocuments.readLock (bytes node)
                |> getResult

            StateDocuments.bind catalog.Entries.Head locked
            |> refuseResult "extension.lock_mismatch"
        }
        test "duplicate catalog ids refuse" {
            let node = sample "catalog.schema.json"
            let entries = (nonNull node["extensions"]).AsArray()
            entries.Add((nonNull entries[0]).DeepClone())

            StateDocuments.readCatalog (bytes node)
            |> refuseResult "extension.duplicate_id"
        }
        test "unknown optional nested catalog metadata preserves while binding known lock facts" {
            let node = sample "catalog.schema.json"
            let entry = nonNull ((nonNull node["extensions"])[0])
            (nonNull entry["index"])["future"] <- JsonValue.Create 42

            let catalog =
                StateDocuments.readCatalog (bytes node)
                |> getResult

            let locked =
                StateDocuments.readLock (bytes (sample "lock.schema.json"))
                |> getResult

            StateDocuments.bind catalog.Entries.Head locked
            |> getResult
            |> ignore
        }
        test "unknown entry critical path refuses even when catalog root is understood" {
            let node = sample "catalog.schema.json"
            let entry = nonNull ((nonNull node["extensions"])[0])
            entry["critical"] <- JsonArray(JsonValue.Create "future")

            StateDocuments.readCatalog (bytes node)
            |> refuseResult "extension.unsupported_critical_member"
        }
        test "I-JSON exact integer range applies even to unknown optional metadata" {
            let node = sample "catalog.schema.json"
            node["future"] <- JsonValue.Create 9007199254740992L

            StateDocuments.readCatalog (bytes node)
            |> refuseResult "extension.invalid_document"
        }
        test "index reader validates published artifact binding instead of only its schema" {
            let node = sample "index-record.schema.json"

            let locked =
                StateDocuments.readLock (bytes (sample "lock.schema.json"))
                |> getResult

            let provenance =
                SharedJson.members "index" locked.Entry.Index
                |> List.filter (fun (name, _) ->
                    name
                    <> "claims"
                )
                |> Morphir.Toolchain.Value.Object

            StateDocuments.readIndex (bytes node) provenance
            |> getResult
            |> ignore

            node["version"] <- JsonValue.Create "9.9.9"

            StateDocuments.readIndex (bytes node) provenance
            |> refuseResult "extension.claims_mismatch"
        }
        test "catalog host requirements cannot be silently ignored without critical consent" {
            let node = sample "catalog.schema.json"
            let entry = nonNull ((nonNull node["extensions"])[0])
            entry["requires"] <- JsonNode.Parse "{\"host\":[\">=9.0.0\"]}"

            StateDocuments.readCatalog (bytes node)
            |> refuseResult "extension.invalid_document"
        }
        test "extension channel tokens are not restricted to the separate toolchain channel enum" {
            let node = sample "lock.schema.json"
            node["selection"] <- JsonNode.Parse "{\"kind\":\"channel\",\"value\":\"nightly\"}"

            StateDocuments.readLock (bytes node)
            |> getResult
            |> ignore
        }
        test "repository names are portable tokens rather than arbitrary display text" {
            let node =
                utf8
                    "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/repositories.schema.json\",\"repositories\":[{\"name\":\"bad/name\",\"endpoint\":{\"kind\":\"local-directory\",\"path\":\"/repo\"},\"state\":\"enabled\"}]}"

            StateDocuments.readRepositories node
            |> refuseResult "extension.invalid_document"
        }
    ]

[<Tests>]
let transactions =
    testLabel "PlatformSpecific" <| testList "StateTransactions" [
        test "transaction preserves an identical-byte substituted journal identity" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let catalog = bytes (sample "catalog.schema.json")
            let locked = bytes (sample "lock.schema.json")

            let id =
                (StateDocuments.readLock locked
                 |> getResult)
                    .Entry.Id

            let journal =
                Path.Combine(
                    root,
                    "locks",
                    "extensions",
                    id
                    + ".json.transaction"
                )

            let mutable replacement: byte array = [||]
            let mutable replaced = false

            let action () =
                StateOwnership.withLock
                    root
                    ct
                    (fun directory ->
                        StateTransactions.commit
                            directory
                            id
                            (Some locked)
                            (Some catalog)
                            (Action<int>(fun phase ->
                                if phase = 2 then
                                    replacement <- File.ReadAllBytes journal
                                    let substitute = Path.Combine(root, "replacement-journal")
                                    File.WriteAllBytes(substitute, replacement)
                                    File.Move(substitute, journal, true)
                                    replaced <- true
                            ))
                            ct
                    )

            Expect.throws (fun () -> action ()) "new identity is foreign even with identical bytes"
            Expect.isTrue replaced "the actual replacement completed before the refusal"
            Expect.isTrue (File.Exists journal) "replacement is retained"

            Expect.sequenceEqual
                (File.ReadAllBytes journal)
                replacement
                "identical replacement bytes untouched"
        }
        test "transaction never deletes a substituted foreign journal after committing its catalog" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let catalog = bytes (sample "catalog.schema.json")
            let locked = bytes (sample "lock.schema.json")

            let id =
                (StateDocuments.readLock locked
                 |> getResult)
                    .Entry.Id

            let journal =
                Path.Combine(
                    root,
                    "locks",
                    "extensions",
                    id
                    + ".json.transaction"
                )

            let foreign = "foreign replacement journal"

            let action () =
                StateOwnership.withLock
                    root
                    ct
                    (fun directory ->
                        StateTransactions.commit
                            directory
                            id
                            (Some locked)
                            (Some catalog)
                            (Action<int>(fun phase ->
                                if phase = 2 then
                                    File.WriteAllText(journal, foreign)
                            ))
                            ct
                    )

            Expect.throws (fun () -> action ()) "substitution refuses"
            Expect.equal (File.ReadAllText journal) foreign "foreign marker preserved"
        }
        test "an invalid saved current lock is old state and is never partially replayed" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let directory = Directory.CreateDirectory(Path.Combine(root, "locks", "extensions"))
            let path = Path.Combine(directory.FullName, "org.example.backend.json.transaction")

            let invalidLock =
                utf8
                    "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/lock.schema.json\"}"

            let numbers =
                String.Join(
                    ',',
                    invalidLock
                    |> Array.map (fun value -> value.ToString())
                )

            let original =
                utf8
                    $"{{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/journal.schema.json\",\"nextLock\":[{numbers}],\"nextCatalog\":null}}"

            File.WriteAllBytes(path, original)

            InstalledState.RecoverAsync(root, true, ct).GetAwaiter().GetResult()
            |> refused "extension.old_state"

            Expect.sequenceEqual (File.ReadAllBytes path) original "journal unchanged"

            Expect.isFalse
                (Directory.Exists(Path.Combine(root, "catalog")))
                "no partial catalog effect"
        }
        test "current URL alone does not certify malformed state" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            Directory.CreateDirectory(Path.Combine(root, "catalog"))
            |> ignore

            File.WriteAllText(
                Path.Combine(root, "catalog", "extensions.json"),
                "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/catalog.schema.json\"}"
            )

            InstalledState.CheckCompatibilityAsync(root, ct).GetAwaiter().GetResult()
            |> refused "extension.invalid_document"
        }
        for phase in [ 0; 1; 2 ] do
            testCase
                $"interruption after durable phase {phase} retains recoverable exact bytes"
                (fun () ->
                    use sandbox = TestSandbox.Create()
                    let root = home sandbox
                    let catalog = bytes (sample "catalog.schema.json")
                    let locked = bytes (sample "lock.schema.json")

                    let id =
                        (StateDocuments.readLock locked
                         |> getResult)
                            .Entry.Id

                    Expect.throwsT<IOException>
                        (fun () ->
                            StateOwnership.withLock
                                root
                                ct
                                (fun directory ->
                                    StateTransactions.commit
                                        directory
                                        id
                                        (Some locked)
                                        (Some catalog)
                                        (Action<int>(fun current ->
                                            if current = phase then
                                                raise (
                                                    IOException "injected durable interruption"
                                                )
                                        ))
                                        ct
                                )
                        )
                        "real interruption"

                    let journal =
                        Path.Combine(
                            root,
                            "locks",
                            "extensions",
                            id
                            + ".json.transaction"
                        )

                    Expect.isTrue (File.Exists journal) "intent retained"

                    if phase = 0 then
                        Expect.isFalse
                            (File.Exists(Path.Combine(root, "catalog", "extensions.json")))
                            "no premature catalog effect"

                    InstalledState.RecoverAsync(root, true, ct).GetAwaiter().GetResult()
                    |> get
                    |> ignore

                    Expect.sequenceEqual
                        (File.ReadAllBytes(
                            Path.Combine(
                                root,
                                "locks",
                                "extensions",
                                id
                                + ".json"
                            )
                        ))
                        locked
                        "exact locked args/claim bytes"

                    Expect.sequenceEqual
                        (File.ReadAllBytes(Path.Combine(root, "catalog", "extensions.json")))
                        catalog
                        "exact catalog bytes"

                    Expect.isFalse (File.Exists journal) "removed last"
                )
        testTask "explicit consent replays exact supported journal bytes and removes marker" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let directory = Directory.CreateDirectory(Path.Combine(root, "locks", "extensions"))

            let journal =
                Path.Combine(directory.FullName, "org.example.backend.json.transaction")

            let saved =
                String.Join(
                    ',',
                    emptyCatalog
                    |> Array.map (fun b -> b.ToString())
                )

            File.WriteAllText(
                journal,
                $"{{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/journal.schema.json\",\"nextLock\":null,\"nextCatalog\":[{saved}]}}"
            )

            let! result = InstalledState.RecoverAsync(root, true, CancellationToken.None)

            get result
            |> ignore

            Expect.sequenceEqual
                (File.ReadAllBytes(Path.Combine(root, "catalog", "extensions.json")))
                emptyCatalog
                "exact saved bytes"

            Expect.isFalse (File.Exists journal) "committed journal removed last"
        }
        testTask "recovery without explicit consent changes no journal bytes" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let directory = Directory.CreateDirectory(Path.Combine(root, "locks", "extensions"))

            let journal =
                Path.Combine(directory.FullName, "org.example.backend.json.transaction")

            let content =
                utf8
                    "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/journal.schema.json\",\"nextLock\":null,\"nextCatalog\":null}"

            File.WriteAllBytes(journal, content)
            let! result = InstalledState.RecoverAsync(root, false, CancellationToken.None)
            refused "extension.recovery_required" result
            Expect.sequenceEqual (File.ReadAllBytes journal) content "no replay"
        }
    ]

[<Tests>]
let corpus =
    use document =
        JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "ParentContracts", "identity-cases.json")
            )
        )

    let cases =
        document.RootElement.GetProperty("cases").EnumerateArray()
        |> Seq.map (fun value -> value.Clone())
        |> Seq.toList

    testLabel "PlatformSpecific" <| testList "StateCorpus" [
        for case in cases do
            let input = case.GetProperty "input"
            let mutable operation = Unchecked.defaultof<JsonElement>

            if
                input.TryGetProperty("operation", &operation)
                && operation.GetString() = "read-catalog"
            then
                testCase
                    (nonNull (case.GetProperty("id").GetString()))
                    (fun () ->
                        let bytes = utf8 (input.GetProperty("document").GetRawText())

                        match
                            StateDocuments.readCatalog bytes,
                            case.GetProperty("expected").GetProperty("outcome").GetString()
                        with
                        | Ok _, "accept" -> ()
                        | Error _, "refuse" -> ()
                        | Error error, _ -> failtest (error.ToString())
                        | Ok _, _ -> failtest "Expected parent refusal"
                    )
    ]
