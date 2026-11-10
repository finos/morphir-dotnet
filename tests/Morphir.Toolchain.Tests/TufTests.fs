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
