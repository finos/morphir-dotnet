// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The TUF client subset of the tool release metadata v1 profile, against the pinned TEST-ONLY signed repository:
/// the strict metadata readers. The client has had no security review.
/// </summary>
module Morphir.Toolchain.Tests.TufTests

open System
open System.Text
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.TufFixture

let private root () = metadataSet("metadata").Root

let private invalid (role: TufRole) (result: Result<'T, TufFailure>) =
    match result with
    | Error(TufFailure.MetadataInvalid(actual, _)) -> Expect.equal actual role "the role"
    | other -> failtestf "expected the %A metadata to be refused as invalid, got %A" role other

/// Root bytes with a member of signed replaced.
let private rootWith (path: string list) (value: Value) =
    canonical (setAt ("signed" :: path) value (root ()))

let private firstKeyId () =
    match at [ "signed"; "roles"; "root"; "keyids" ] (root ()) with
    | Value.Array(Value.String id :: _) -> id
    | _ -> failwith "keyids"

[<Tests>]
let readerTests =
    testList "TUF metadata readers" [
        testCase "the fixture's four roles read" (fun () ->
            let set = metadataSet "metadata"
            let read = TufMetadata.readRoot (canonical set.Root) |> expectOk "root"
            Expect.equal read.Signed.Version 1UL "root version"
            Expect.equal (read.Signed.Roles[TufRole.Root].Threshold) 2 "two of the root keys"

            let timestamp = TufMetadata.readTimestamp (canonical set.Timestamp) |> expectOk "timestamp"
            Expect.equal timestamp.Signed.Snapshot.Version 1UL "the snapshot it names"

            let snapshot = TufMetadata.readSnapshot (canonical set.Snapshot) |> expectOk "snapshot"
            Expect.equal snapshot.Signed.Targets.Version 1UL "the targets it names"

            let targets = TufMetadata.readTargets (canonical set.Targets) |> expectOk "targets"
            let recipe = targets.Signed.Targets["toolchains/recipes/fsharp/1.0.0.json"]
            let bytes = Encoding.UTF8.GetBytes(match at [ "targetFiles"; "toolchains/recipes/fsharp/1.0.0.json" ] (repository ()) with Value.String s -> s | _ -> "")
            Expect.equal recipe.Length (uint64 bytes.Length) "the recipe's length"
            Expect.equal recipe.Sha256 (Sha256.ofBytes bytes) "the recipe's digest"
            Expect.isSome recipe.Custom "custom.morphir")

        testCase "repository identity is the SHA-256 of the canonical initial root's signed object" (fun () ->
            let read = TufMetadata.readRoot (canonical (root ())) |> expectOk "root"
            let expected = match at [ "repositoryId" ] (repository ()) with Value.String s -> s | _ -> ""
            Expect.equal (TufMetadata.repositoryIdentity read).Text expected "repositoryId")

        testList "a root outside the profile is refused" [
            let otherKey = keyValue (newTestKey "other")
            let otherId = (newTestKey "other").KeyId

            for name, bytes in
                [ "spec_version 2.0.0", (fun () -> rootWith [ "spec_version" ] (text "2.0.0"))
                  "spec_version 1.1.0 (provisional: only 1.0.x)", (fun () -> rootWith [ "spec_version" ] (text "1.1.0"))
                  "spec_version 1.0, not SemVer", (fun () -> rootWith [ "spec_version" ] (text "1.0"))
                  "consistent_snapshot false", (fun () -> rootWith [ "consistent_snapshot" ] (Value.Boolean false))
                  "no consistent_snapshot", (fun () -> canonical (removeAt [ "signed"; "consistent_snapshot" ] (root ())))
                  "_type timestamp", (fun () -> rootWith [ "_type" ] (text "timestamp"))
                  "version 0", (fun () -> rootWith [ "version" ] (number 0UL))
                  "a version with a fraction", (fun () -> JsonText.toBytes (setAt [ "signed"; "version" ] (Value.Number "1.5") (root ())))
                  "expires with fractional seconds", (fun () -> rootWith [ "expires" ] (text "2999-01-01T00:00:00.000Z"))
                  "expires with an offset", (fun () -> rootWith [ "expires" ] (text "2999-01-01T00:00:00+00:00"))
                  "expires with a space", (fun () -> rootWith [ "expires" ] (text "2999-01-01 00:00:00Z"))
                  "a key of another type (provisional: any non-Ed25519 key)",
                  (fun () ->
                      rootWith
                          [ "keys"; otherId ]
                          (Value.Object [ "keytype", text "rsa"; "scheme", text "rsassa-pss-sha256"; "keyval", Value.Object [ "public", text "00" ] ]))
                  "an Ed25519 key with another scheme", (fun () -> rootWith [ "keys"; otherId ] (setAt [ "scheme" ] (text "ed25519-ph") otherKey))
                  "a key id that is not the key's SHA-256 (provisional)", (fun () -> rootWith [ "keys"; String.replicate 64 "a" ] otherKey)
                  "a public key of 31 bytes", (fun () -> rootWith [ "keys"; otherId ] (setAt [ "keyval"; "public" ] (text (String.replicate 62 "a")) otherKey))
                  "threshold 0", (fun () -> rootWith [ "roles"; "timestamp"; "threshold" ] (number 0UL))
                  "a threshold above the role's keys (provisional)", (fun () -> rootWith [ "roles"; "root"; "threshold" ] (number 3UL))
                  "a role the profile does not define (provisional)", (fun () -> rootWith [ "roles"; "mirrors" ] (at [ "signed"; "roles"; "timestamp" ] (root ())))
                  "a missing role", (fun () -> canonical (removeAt [ "signed"; "roles"; "snapshot" ] (root ())))
                  "a role naming a key the root does not list", (fun () -> rootWith [ "roles"; "timestamp"; "keyids" ] (Value.Array [ text otherId ]))
                  "a role naming a key twice", (fun () -> rootWith [ "roles"; "root"; "keyids" ] (Value.Array [ text (firstKeyId ()); text (firstKeyId ()) ])) ] ->
                testCase name (fun () -> invalid TufRole.Root (TufMetadata.readRoot (bytes ())))
        ]

        testList "envelopes outside the profile are refused" [
            let signatures () = at [ "signatures" ] (root ())

            let firstSignature () =
                match signatures () with
                | Value.Array(first :: _) -> first
                | _ -> failwith "signatures"

            for name, bytes in
                [ "an unknown envelope member (provisional)", (fun () -> canonical (setAt [ "extra" ] (number 1UL) (root ())))
                  "a signature in upper-case hex", (fun () ->
                      let upper = match memberOf "sig" (firstSignature ()) with Value.String s -> s.ToUpperInvariant() | _ -> ""
                      canonical (setAt [ "signatures" ] (Value.Array [ setAt [ "sig" ] (text upper) (firstSignature ()) ]) (root ())))
                  "a signature of 63 bytes", (fun () ->
                      canonical (setAt [ "signatures" ] (Value.Array [ setAt [ "sig" ] (text (String.replicate 126 "a")) (firstSignature ()) ]) (root ())))
                  "the same key id signing twice (provisional)", (fun () ->
                      canonical (setAt [ "signatures" ] (Value.Array [ firstSignature (); firstSignature () ]) (root ())))
                  "not JSON", (fun () -> Encoding.UTF8.GetBytes "{")
                  "a repeated member", (fun () -> Encoding.UTF8.GetBytes """{"signed":{},"signed":{},"signatures":[]}""") ] ->
                testCase name (fun () -> invalid TufRole.Root (TufMetadata.readRoot (bytes ())))
        ]

        testList "timestamp, snapshot and targets outside the profile are refused" [
            let set () = metadataSet "metadata"
            let with' (role: Value) (path: string list) (value: Value) = canonical (setAt ("signed" :: path) value role)

            yield testCase "a timestamp naming another file besides snapshot.json (provisional)" (fun () ->
                let ts = (set ()).Timestamp
                invalid TufRole.Timestamp (TufMetadata.readTimestamp (with' ts [ "meta"; "targets.json" ] (at [ "signed"; "meta"; "snapshot.json" ] ts))))

            yield testCase "a timestamp whose snapshot.json has no sha256 (provisional)" (fun () ->
                let ts = (set ()).Timestamp
                invalid TufRole.Timestamp (TufMetadata.readTimestamp (canonical (removeAt [ "signed"; "meta"; "snapshot.json"; "hashes" ] ts))))

            yield testCase "a snapshot whose targets.json has no length (provisional)" (fun () ->
                let snapshot = (set ()).Snapshot
                invalid TufRole.Snapshot (TufMetadata.readSnapshot (canonical (removeAt [ "signed"; "meta"; "targets.json"; "length" ] snapshot))))

            yield testCase "targets with delegations (provisional: no delegations in this profile subset)" (fun () ->
                let targets = (set ()).Targets
                invalid TufRole.Targets (TufMetadata.readTargets (with' targets [ "delegations" ] (Value.Object [ "keys", Value.Object []; "roles", Value.Array [] ]))))

            yield testCase "a target without a SHA-256" (fun () ->
                let targets = (set ()).Targets
                invalid TufRole.Targets (TufMetadata.readTargets (with' targets [ "targets"; "toolchains/recipes/fsharp/1.0.0.json"; "hashes" ] (Value.Object [ "sha512", text (String.replicate 128 "a") ]))))

            yield testCase "a target listed under a non-portable path is non_portable_path" (fun () ->
                let targets = (set ()).Targets
                let entry = at [ "signed"; "targets"; "toolchains/recipes/fsharp/1.0.0.json" ] targets

                match TufMetadata.readTargets (with' targets [ "targets"; "toolchains/recipes/../fsharp/1.0.0.json" ] entry) with
                | Error(TufFailure.NonPortableTarget path) -> Expect.equal path "toolchains/recipes/../fsharp/1.0.0.json" "the path"
                | other -> failtestf "expected NonPortableTarget, got %A" other)
        ]

        testCase "metadata over its download bound is refused before it is parsed" (fun () ->
            for read, role, limit in
                [ TufMetadata.readRoot >> Result.map ignore, TufRole.Root, 64 * 1024
                  TufMetadata.readTimestamp >> Result.map ignore, TufRole.Timestamp, 64 * 1024
                  TufMetadata.readSnapshot >> Result.map ignore, TufRole.Snapshot, 1024 * 1024
                  TufMetadata.readTargets >> Result.map ignore, TufRole.Targets, 8 * 1024 * 1024 ] do
                // Not JSON at all: only the size is looked at.
                Expect.equal (read (Array.create (limit + 1) (byte ' '))) (Error(TufFailure.MetadataTooLarge(role, limit))) $"{role}")
    ]

[<Tests>]
let signatureTests =
    testList "TUF signatures" [
        testCase "Ed25519 verifies RFC 8032 test vector 1 and refuses a changed message or signature" (fun () ->
            let publicKey = Convert.FromHexString "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"

            let signature =
                Convert.FromHexString
                    "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"

            Expect.isTrue (TufSignatures.verifyEd25519 publicKey [||] signature) "the empty message"
            Expect.isFalse (TufSignatures.verifyEd25519 publicKey [| 0uy |] signature) "another message"
            let changed = Array.copy signature
            changed[0] <- changed[0] ^^^ 1uy
            Expect.isFalse (TufSignatures.verifyEd25519 publicKey [||] changed) "another signature"
            Expect.isFalse (TufSignatures.verifyEd25519 publicKey[..30] [||] signature) "a 31-byte key"
            Expect.isFalse (TufSignatures.verifyEd25519 publicKey [||] signature[..62]) "a 63-byte signature"
            Expect.isFalse (TufSignatures.verifyEd25519 (Array.create 32 0xFFuy) [||] signature) "a key that is not a point")

        testCase "each fixture role meets its threshold with its own keys only" (fun () ->
            let set = metadataSet "metadata"
            let trusted = (TufMetadata.readRoot (canonical set.Root) |> expectOk "root").Signed
            Expect.isTrue (TufSignatures.thresholdMet trusted TufRole.Root (TufMetadata.readRoot (canonical set.Root) |> expectOk "root")) "root"
            Expect.isTrue (TufSignatures.thresholdMet trusted TufRole.Timestamp (TufMetadata.readTimestamp (canonical set.Timestamp) |> expectOk "timestamp")) "timestamp"
            Expect.isTrue (TufSignatures.thresholdMet trusted TufRole.Snapshot (TufMetadata.readSnapshot (canonical set.Snapshot) |> expectOk "snapshot")) "snapshot"
            Expect.isTrue (TufSignatures.thresholdMet trusted TufRole.Targets (TufMetadata.readTargets (canonical set.Targets) |> expectOk "targets")) "targets"

            // The timestamp signed by the snapshot key: a valid signature, by a key of another role.
            let bySnapshotKey = envelope (memberOf "signed" set.Timestamp) (keysOf "snapshot")
            Expect.isFalse (TufSignatures.thresholdMet trusted TufRole.Timestamp (TufMetadata.readTimestamp (canonical bySnapshotKey) |> expectOk "timestamp")) "another role's key")

        testCase "a signature over other content, or by an unknown key, does not count" (fun () ->
            let set = metadataSet "metadata"
            let trusted = (TufMetadata.readRoot (canonical set.Root) |> expectOk "root").Signed
            let changed = setAt [ "signed"; "expires" ] (text "2998-01-01T00:00:00Z") set.Timestamp
            Expect.isFalse (TufSignatures.thresholdMet trusted TufRole.Timestamp (TufMetadata.readTimestamp (canonical changed) |> expectOk "timestamp")) "changed content"

            // An extra signature by a key the root does not list is ignored; the timestamp key's still counts.
            let signed = memberOf "signed" set.Timestamp
            let extra = Value.Object [ "signatures", Value.Array [ signatureOf (newTestKey "unknown") signed; signatureOf (keysOf "timestamp").Head signed ]; "signed", signed ]
            Expect.isTrue (TufSignatures.thresholdMet trusted TufRole.Timestamp (TufMetadata.readTimestamp (canonical extra) |> expectOk "timestamp")) "ignored")

        testCase "two key ids naming one public key count once" (fun () ->
            // The readers refuse such a root, because a key id is the SHA-256 of its key; the verifier still counts
            // distinct public keys, as well as distinct key ids.
            let set = metadataSet "metadata"
            let trusted = (TufMetadata.readRoot (canonical set.Root) |> expectOk "root").Signed
            let timestampKey = (keysOf "timestamp").Head
            let publicKey = trusted.Keys[timestampKey.KeyId]
            let alias = String.replicate 64 "e"

            let doubled =
                { trusted with
                    Keys = trusted.Keys.Add(alias, publicKey)
                    Roles = trusted.Roles.Add(TufRole.Timestamp, { KeyIds = [ timestampKey.KeyId; alias ]; Threshold = 2 }) }

            let signed = memberOf "signed" set.Timestamp
            let aliasSignature = setAt [ "keyid" ] (text alias) (signatureOf timestampKey signed)
            let twice = Value.Object [ "signatures", Value.Array [ signatureOf timestampKey signed; aliasSignature ]; "signed", signed ]
            Expect.isFalse (TufSignatures.thresholdMet doubled TufRole.Timestamp (TufMetadata.readTimestamp (canonical twice) |> expectOk "timestamp")) "one key, two ids")
    ]
/// A root of the given version, with root keys and threshold, signed by the signers.
let private rootVersion (version: uint64) (rootKeys: TestKey list) (threshold: int) (signers: TestKey list) =
    let base' = memberOf "signed" (metadataSet "metadata").Root

    let keys =
        match memberOf "keys" base' with
        | Value.Object existing -> Value.Object(existing @ [ for key in rootKeys do if not (existing |> List.exists (fst >> (=) key.KeyId)) then yield key.KeyId, keyValue key ])
        | _ -> failwith "keys"

    let signed =
        base'
        |> setAt [ "version" ] (number version)
        |> setAt [ "keys" ] keys
        |> setAt [ "roles"; "root" ] (Value.Object [ "keyids", Value.Array(rootKeys |> List.map (fun key -> text key.KeyId)); "threshold", number (uint64 threshold) ])

    envelope signed signers

let private anchor () =
    match TrustAnchor.load TrustAnchorPurpose.TestOnly (canonical (metadataSet "metadata").Root) with
    | Ok(trusted, _) -> trusted
    | Error refusal -> failtestf "the fixture root does not load as a TEST-ONLY anchor: %A" refusal

let private refresh (trusted: TufTrustedSet) (source: MetadataSource) = TufClient.refresh referenceTime trusted source

let private refreshed (result: Result<RefreshOutcome, TufFailure>) =
    match result with
    | Ok(RefreshOutcome.Refreshed trusted) -> trusted
    | other -> failtestf "expected a completed refresh, got %A" other

let private refused (result: Result<RefreshOutcome, TufFailure>) =
    match result with
    | Error failure -> failure
    | other -> failtestf "expected a refusal, got %A" other

let private rotationRefused (result: Result<RefreshOutcome, TufFailure>) =
    match refused result with
    | TufFailure.RootRotationInvalid _ -> ()
    | other -> failtestf "expected RootRotationInvalid, got %A" other

[<Tests>]
let workflowTests =
    let rootKeys () = keysOf "root"
    let newRootKeys () = [ newTestKey "root-a"; newTestKey "root-b" ]

    testList "TUF refresh" [
        testCase "each fixture metadata set refreshes from the trusted root at the reference time" (fun () ->
            for name in [ "metadata"; "learnedRevocation"; "recipeRevocation" ] do
                let trusted = refreshed (refresh (anchor ()) (sourceOf (metadataSet name) []))
                let targets = (Option.get trusted.Targets).Targets
                let listed = match at [ name; "targets"; "signed"; "targets" ] (repository ()) with Value.Object items -> items.Length | _ -> 0
                Expect.equal targets.Count listed $"{name}: every target"
                Expect.equal trusted.Root.Version 1UL $"{name}: no new root")

        testCase "a root rotation that meets both thresholds is trusted" (fun () ->
            let v2 = rootVersion 2UL (rootKeys ()) 2 (rootKeys ())
            let trusted = refreshed (refresh (anchor ()) (sourceOf (metadataSet "metadata") [ v2 ]))
            Expect.equal trusted.Root.Version 2UL "root v2")

        testCase "a rotation signed by the new threshold only is refused" (fun () ->
            rotationRefused (refresh (anchor ()) (sourceOf (metadataSet "metadata") [ rootVersion 2UL (newRootKeys ()) 2 (newRootKeys ()) ])))

        testCase "a rotation signed by the old threshold only is refused" (fun () ->
            rotationRefused (refresh (anchor ()) (sourceOf (metadataSet "metadata") [ rootVersion 2UL (newRootKeys ()) 2 (rootKeys ()) ])))

        testCase "2.root.json whose signed version is 3 is refused" (fun () ->
            let v3 = rootVersion 3UL (rootKeys ()) 2 (rootKeys ())
            let set = metadataSet "metadata"
            let bytes = canonical v3

            let source =
                { sourceOf set [] with
                    Root = fun version -> if version = 1UL then Some(canonical set.Root) elif version = 2UL then Some bytes else None }

            rotationRefused (refresh (anchor ()) source))

        testCase "a refresh stops after 32 accepted roots and asks for a retry, which continues" (fun () ->
            let roots = [ for version in 2UL .. 34UL -> rootVersion version (rootKeys ()) 2 (rootKeys ()) ]
            let source = sourceOf (metadataSet "metadata") roots

            match refresh (anchor ()) source with
            | Ok(RefreshOutcome.RootRotationPaused trusted) ->
                Expect.equal trusted.Root.Version 33UL "32 accepted roots"
                let again = refreshed (refresh trusted source)
                Expect.equal again.Root.Version 34UL "the retry continues"
            | other -> failtestf "expected a pause after 32 roots, got %A" other

            // Exactly 32 newer roots complete in one refresh.
            let exactly = refreshed (refresh (anchor ()) (sourceOf (metadataSet "metadata") (List.truncate 32 roots)))
            Expect.equal exactly.Root.Version 33UL "no pause when nothing follows")

        testCase "a timestamp older than the trusted one is a rollback" (fun () ->
            let trusted = refreshed (refresh (anchor ()) (sourceOf (metadataSet "learnedRevocation") []))
            Expect.equal (refused (refresh trusted (sourceOf (metadataSet "metadata") []))) (TufFailure.Rollback TufRole.Timestamp) "timestamp v1 after v2")

        testCase "a snapshot naming an older targets version is a rollback" (fun () ->
            let trusted = refreshed (refresh (anchor ()) (sourceOf (metadataSet "learnedRevocation") []))
            let older = metadataSet "metadata"
            let newer = metadataSet "learnedRevocation"

            // Timestamp and snapshot at version 3, linking the version 1 targets.
            let set =
                relink
                    { older with
                        Snapshot = setAt [ "signed"; "version" ] (number 3UL) newer.Snapshot
                        Timestamp = setAt [ "signed"; "version" ] (number 3UL) newer.Timestamp }

            Expect.equal (refused (refresh trusted (sourceOf set []))) (TufFailure.Rollback TufRole.Snapshot) "targets v1 after v2")

        testCase "a snapshot that is not the one the timestamp names is a link mismatch" (fun () ->
            let set = metadataSet "metadata"
            let other = (metadataSet "learnedRevocation").Snapshot

            // The version 2 snapshot served as 1.snapshot.json.
            let source = { sourceOf set [] with Snapshot = fun version -> if version = 1UL then Some(canonical other) else None }
            Expect.equal (refused (refresh (anchor ()) source)) (TufFailure.LinkMismatch TufRole.Snapshot) "mix and match")

        testCase "every expiry is checked against the one start time" (fun () ->
            let expired = { metadataSet "metadata" with Timestamp = at [ "rejectionMetadata"; "expiredTimestamp" ] (repository ()) }
            Expect.equal (refused (refresh (anchor ()) (sourceOf expired []))) (TufFailure.Expired TufRole.Timestamp) "expired in 2020"

            // A timestamp that expires exactly at the start time is expired; one second later it is not.
            let set = metadataSet "metadata"
            let at' (time: string) = { set with Timestamp = resign "timestamp" (setAt [ "signed"; "expires" ] (text time) set.Timestamp) }
            Expect.equal (refused (refresh (anchor ()) (sourceOf (at' "2030-01-01T00:00:00Z") []))) (TufFailure.Expired TufRole.Timestamp) "at the start"
            refreshed (refresh (anchor ()) (sourceOf (at' "2030-01-01T00:00:01Z") [])) |> ignore)

        testCase "an expired newest root is refused" (fun () ->
            let v2 = resign "root" (setAt [ "signed"; "expires" ] (text "2029-01-01T00:00:00Z") (rootVersion 2UL (rootKeys ()) 2 (rootKeys ())))
            // resign uses the role's fixture keys: both root keys sign v2.
            Expect.equal (refused (refresh (anchor ()) (sourceOf (metadataSet "metadata") [ v2 ]))) (TufFailure.Expired TufRole.Root) "root v2 expired")

        testCase "a file the refresh needs that the source does not have is unavailable" (fun () ->
            let source = { sourceOf (metadataSet "metadata") [] with Timestamp = fun () -> None }
            Expect.equal (refused (refresh (anchor ()) source)) (TufFailure.Unavailable TufRole.Timestamp) "no timestamp")

        testCase "consistent-snapshot names use / and the digest before the file name" (fun () ->
            match Sha256.tryParse (String.replicate 64 "a") with
            | Ok digest ->
                Expect.equal (TufClient.consistentTargetPath "artifacts/desktop/1.0.0/windows-x86_64.zip" digest) ("artifacts/desktop/1.0.0/" + String.replicate 64 "a" + ".windows-x86_64.zip") "a target"
                Expect.equal (TufClient.consistentTargetPath "top.json" digest) (String.replicate 64 "a" + ".top.json") "a top-level target"
                Expect.equal (TufClient.metadataName TufRole.Snapshot 7UL) "7.snapshot.json" "a snapshot"
                Expect.equal (TufClient.metadataName TufRole.Timestamp 7UL) "timestamp.json" "the timestamp"
            | Error _ -> failtest "digest")
    ]

[<Tests>]
let anchorTests =
    testList "TUF trust anchors" [
        testCase "the fixture root loads as a TEST-ONLY anchor with the fixture's repository identity" (fun () ->
            match TrustAnchor.load TrustAnchorPurpose.TestOnly (canonical (metadataSet "metadata").Root) with
            | Ok(_, identity) ->
                Expect.equal identity.Text (match at [ "repositoryId" ] (repository ()) with Value.String s -> s | _ -> "") "repositoryId"
            | Error refusal -> failtestf "refused: %A" refusal)

        testCase "a fixture key cannot be loaded as a production root" (fun () ->
            match TrustAnchor.load TrustAnchorPurpose.Production (canonical (metadataSet "metadata").Root) with
            | Error(TrustAnchorRefusal.TestOnlyKey _) -> ()
            | other -> failtestf "expected the TEST-ONLY key to be refused, got %A" other)

        testCase "a production root needs a root threshold of at least two" (fun () ->
            let only = newTestKey "production-shaped"
            // A root whose only root key is not a fixture key, with threshold 1.
            let root = envelope (memberOf "signed" (rootVersion 1UL [ only ] 1 [])) [ only ]

            match TrustAnchor.load TrustAnchorPurpose.Production (canonical root) with
            | Error TrustAnchorRefusal.RootThresholdBelowTwo -> ()
            | other -> failtestf "expected RootThresholdBelowTwo, got %A" other)

        testCase "an anchor must meet its own root threshold" (fun () ->
            let unsigned = Value.Object [ "signatures", Value.Array []; "signed", memberOf "signed" (metadataSet "metadata").Root ]

            match TrustAnchor.load TrustAnchorPurpose.TestOnly (canonical unsigned) with
            | Error(TrustAnchorRefusal.Unreadable(TufFailure.SignatureInvalid TufRole.Root)) -> ()
            | other -> failtestf "expected the unsigned root to be refused, got %A" other)
    ]
[<Tests>]
let failureCodeTests =
    testCase "each TUF failure maps to its stable code" (fun () ->
        for failure, code in
            [ TufFailure.SignatureInvalid TufRole.Targets, "metadata_signature_invalid"
              TufFailure.RootRotationInvalid(2UL, "reason"), "metadata_root_rotation_invalid"
              TufFailure.Expired TufRole.Timestamp, "metadata_expired"
              TufFailure.Rollback TufRole.Snapshot, "metadata_rollback"
              TufFailure.LinkMismatch TufRole.Targets, "metadata_link_mismatch"
              TufFailure.NonPortableTarget "a/../b", "non_portable_path"
              TufFailure.Unavailable TufRole.Timestamp, "repository_unavailable"
              // Provisional: the contract has no code for metadata that does not read under the profile.
              TufFailure.MetadataInvalid(TufRole.Root, "reason"), "metadata_signature_invalid"
              TufFailure.MetadataTooLarge(TufRole.Root, 65536), "metadata_signature_invalid" ] do
            Expect.equal (Code.name (TufFailure.code failure)) code $"%A{failure}"

        Expect.equal (TufFailure.role (TufFailure.Expired TufRole.Snapshot)) (Some TufRole.Snapshot) "the role a diagnostic names"
        Expect.equal (TufFailure.role (TufFailure.NonPortableTarget "x")) (Some TufRole.Targets) "a target path is targets metadata's"
        Expect.equal (TufFailure.role (TufFailure.RootRotationInvalid(2UL, "reason"))) (Some TufRole.Root) "rotation is the root's")
[<Tests>]
let spikeProfileTests =
    // The 16 profile cases of the TUF client evaluation (spike/tuf-client-evaluation, results/conformance-win-arm64.md),
    // on the pinned signed repository's keys. Three need a transport or durable state, which this slice does not have;
    // the report lists them.
    let rootKeys () = keysOf "root"
    let newRootKeys () = [ newTestKey "root-a"; newTestKey "root-b" ]
    let padded (value: Value) = Array.append (canonical value) (Array.create (70 * 1024) (byte ' '))

    testList "TUF profile cases from the client evaluation" [
        testCase "1 rotation v1 to v2 meeting both thresholds" (fun () ->
            Expect.equal (refreshed (refresh (anchor ()) (sourceOf (metadataSet "metadata") [ rootVersion 2UL (rootKeys ()) 2 (rootKeys ()) ]))).Root.Version 2UL "v2")

        testCase "2 rotation signed by a new threshold only" (fun () ->
            rotationRefused (refresh (anchor ()) (sourceOf (metadataSet "metadata") [ rootVersion 2UL (newRootKeys ()) 2 (newRootKeys ()) ])))

        testCase "3 rotation signed by the old threshold only" (fun () ->
            rotationRefused (refresh (anchor ()) (sourceOf (metadataSet "metadata") [ rootVersion 2UL (newRootKeys ()) 2 (rootKeys ()) ])))

        testCase "4 2.root.json whose signed version is 3" (fun () ->
            let set = metadataSet "metadata"
            let v3 = canonical (rootVersion 3UL (rootKeys ()) 2 (rootKeys ()))
            rotationRefused (refresh (anchor ()) { sourceOf set [] with Root = fun v -> if v = 1UL then Some(canonical set.Root) elif v = 2UL then Some v3 else None }))

        testCase "5 33 newer roots stop after 32 and ask to retry" (fun () ->
            match refresh (anchor ()) (sourceOf (metadataSet "metadata") [ for v in 2UL .. 34UL -> rootVersion v (rootKeys ()) 2 (rootKeys ()) ]) with
            | Ok(RefreshOutcome.RootRotationPaused trusted) -> Expect.equal trusted.Root.Version 33UL "paused at v33"
            | other -> failtestf "expected a pause, got %A" other)

        testCase "6 timestamp v1 served after a persisted timestamp v2" (fun () ->
            let trusted = refreshed (refresh (anchor ()) (sourceOf (metadataSet "learnedRevocation") []))
            Expect.equal (refused (refresh trusted (sourceOf (metadataSet "metadata") []))) (TufFailure.Rollback TufRole.Timestamp) "rollback")

        testCase "7 snapshot v2 served as 1.snapshot.json" (fun () ->
            let other = canonical (metadataSet "learnedRevocation").Snapshot
            let source = { sourceOf (metadataSet "metadata") [] with Snapshot = fun v -> if v = 1UL then Some other else None }
            Expect.equal (refused (refresh (anchor ()) source)) (TufFailure.LinkMismatch TufRole.Snapshot) "mix and match")

        testCase "8 and 9 a timestamp of 70 KiB is refused before parsing" (fun () ->
            // The spike's case 9 ran the same input through a bounding transport; here the client bounds it.
            let set = metadataSet "metadata"
            let source = { sourceOf set [] with Timestamp = fun () -> Some(padded set.Timestamp) }
            Expect.equal (refused (refresh (anchor ()) source)) (TufFailure.MetadataTooLarge(TufRole.Timestamp, 64 * 1024)) "the bound")

        testCase "10 a root v2 of 70 KiB is refused before parsing" (fun () ->
            let set = metadataSet "metadata"
            let v2 = padded (rootVersion 2UL (rootKeys ()) 2 (rootKeys ()))
            let source = { sourceOf set [] with Root = fun v -> if v = 1UL then Some(canonical set.Root) elif v = 2UL then Some v2 else None }
            Expect.equal (refused (refresh (anchor ()) source)) (TufFailure.MetadataTooLarge(TufRole.Root, 64 * 1024)) "the bound")

        testCase "11 a timestamp with spec_version 2.0.0 is refused" (fun () ->
            let set = metadataSet "metadata"
            let timestamp = resign "timestamp" (setAt [ "signed"; "spec_version" ] (text "2.0.0") set.Timestamp)

            match refused (refresh (anchor ()) (sourceOf { set with Timestamp = timestamp } [])) with
            | TufFailure.MetadataInvalid(TufRole.Timestamp, _) -> ()
            | other -> failtestf "expected the timestamp to be refused, got %A" other)

        testCase "12 a trusted root with consistent_snapshot false is refused" (fun () ->
            let root = resign "root" (setAt [ "signed"; "consistent_snapshot" ] (Value.Boolean false) (metadataSet "metadata").Root)

            match TrustAnchor.load TrustAnchorPurpose.TestOnly (canonical root) with
            | Error(TrustAnchorRefusal.Unreadable(TufFailure.MetadataInvalid(TufRole.Root, _))) -> ()
            | other -> failtestf "expected the root to be refused, got %A" other)

        testCase "16 the consistent-snapshot path of a target uses / on every system" (fun () ->
            let trusted = refreshed (refresh (anchor ()) (sourceOf (metadataSet "metadata") []))
            let path = "artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip"
            let target = (Option.get trusted.Targets).Targets[path]
            Expect.equal (TufClient.consistentTargetPath path target.Sha256) $"artifacts/morphir-dotnet-native-aot/1.2.3/{target.Sha256.Text}.win-x64.zip" "the path")
    ]