// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The TEST-ONLY signed repository of the pinned contract as metadata files: each role is served as the canonical
/// JSON bytes of its envelope, as the contract's fixture description says. Mutations re-sign with the fixture's
/// TEST-ONLY keys, whose seeds are the SHA-256 of their published labels. None of this is key material for any real
/// repository.
/// </summary>
module Morphir.Toolchain.Tests.TufFixture

open System
open System.Text
open Expecto
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Math.EC.Rfc8032
open Morphir.Toolchain

let private repositoryValue =
    lazy
        (match Syntax.parseJson (Fixtures.bytesIn Fixtures.signed "repository.json") with
         | Ok value -> value
         | Error _ -> failwith "repository.json does not parse")

let memberOf (name: string) (value: Value) =
    match value with
    | Value.Object members ->
        match members |> List.tryFind (fst >> (=) name) with
        | Some(_, item) -> item
        | None -> failwithf "no member %s" name
    | _ -> failwithf "not an object at %s" name

let rec at (path: string list) (value: Value) =
    match path with
    | [] -> value
    | head :: rest -> at rest (memberOf head value)

/// The value with the member at a path replaced; a missing last member is added.
let rec setAt (path: string list) (replacement: Value) (value: Value) =
    match path, value with
    | [], _ -> replacement
    | [ head ], Value.Object members when not (members |> List.exists (fst >> (=) head)) ->
        Value.Object(members @ [ head, replacement ])
    | head :: rest, Value.Object members ->
        Value.Object(members |> List.map (fun (name, item) -> if name = head then name, setAt rest replacement item else name, item))
    | _ -> failwithf "cannot set %A" path

/// The value with the member at a path removed.
let rec removeAt (path: string list) (value: Value) =
    match path, value with
    | [ last ], Value.Object members -> Value.Object(members |> List.filter (fst >> (<>) last))
    | head :: rest, Value.Object members ->
        Value.Object(members |> List.map (fun (name, item) -> if name = head then name, removeAt rest item else name, item))
    | _ -> failwithf "cannot remove %A" path

let repository () = repositoryValue.Value

let canonical (value: Value) =
    match Canonical.bytes value with
    | Ok bytes -> bytes
    | Error _ -> failwith "not canonical"

let number (n: uint64) = Value.Number(string n)
let text (s: string) = Value.String s

/// A TEST-ONLY signing key: its key id, public key and seed.
type TestKey =
    { Role: string
      KeyId: string
      Public: string
      Seed: byte array }

let private keyOfLabel role (label: string) =
    let seed = Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes label)
    let publicKey = Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded()
    let publicHex = Convert.ToHexStringLower publicKey

    // A TUF key id is the SHA-256 of the canonical key object.
    let keyObject =
        Value.Object
            [ "keytype", text "ed25519"
              "scheme", text "ed25519"
              "keyval", Value.Object [ "public", text publicHex ] ]

    { Role = role
      KeyId = Convert.ToHexStringLower(Security.Cryptography.SHA256.HashData(canonical keyObject))
      Public = publicHex
      Seed = seed }

/// The fixture's TEST-ONLY keys, in its order, checked against their published key ids and public keys.
let fixtureKeys =
    lazy
        [ match at [ "testOnlyKeys" ] (repository ()) with
          | Value.Array items ->
              for item in items do
                  let field name = match memberOf name item with Value.String s -> s | _ -> failwith name
                  let key = keyOfLabel (field "role") (field "label")

                  if key.KeyId <> field "keyId" || key.Public <> field "public" then
                      failwithf "the seed of %s does not give its published key" (field "label")

                  yield key
          | _ -> failwith "testOnlyKeys" ]

let keysOf (role: string) = fixtureKeys.Value |> List.filter (fun key -> key.Role = role)

/// A new TEST-ONLY key that no fixture publishes.
let newTestKey (name: string) = keyOfLabel "root" $"morphir-dotnet TEST-ONLY tuf-client test key: {name}"

/// The key object a root lists for a key.
let keyValue (key: TestKey) =
    Value.Object
        [ "keytype", text "ed25519"
          "scheme", text "ed25519"
          "keyval", Value.Object [ "public", text key.Public ] ]

let signatureOf (key: TestKey) (signed: Value) =
    let message = canonical signed
    let signature = Array.zeroCreate<byte> 64
    Ed25519PrivateKeyParameters(key.Seed, 0).Sign(Ed25519.Algorithm.Ed25519, null, message, 0, message.Length, signature, 0)
    Value.Object [ "keyid", text key.KeyId; "sig", text (Convert.ToHexStringLower signature) ]

/// An envelope of signed content, signed by the keys given.
let envelope (signed: Value) (signers: TestKey list) =
    Value.Object [ "signatures", Value.Array(signers |> List.map (fun key -> signatureOf key signed)); "signed", signed ]

/// A metadata set of the fixture (metadata, learnedRevocation, recipeRevocation) as role envelopes.
type MetadataSet =
    { Root: Value
      Timestamp: Value
      Snapshot: Value
      Targets: Value
      /// Bytes served for a role (timestamp, snapshot or targets) instead of its canonical envelope: a
      /// replace-role-text mutation serves text no value encodes to, such as a decomposed string under the original
      /// signature.
      Served: Map<string, byte array> }

let metadataSet (name: string) =
    let set = at [ name ] (repository ())

    { Root = at [ "trustedRoot" ] (repository ())
      Timestamp = memberOf "timestamp" set
      Snapshot = memberOf "snapshot" set
      Targets = memberOf "targets" set
      Served = Map.empty }

/// The bytes the set serves for a role: its override, or the canonical bytes of its envelope.
let served (role: string) (set: MetadataSet) =
    match set.Served.TryFind role with
    | Some bytes -> bytes
    | None ->
        match role with
        | "timestamp" -> canonical set.Timestamp
        | "snapshot" -> canonical set.Snapshot
        | "targets" -> canonical set.Targets
        | other -> failwithf "no role %s" other

/// <summary>The replace-role-text mutation: the role's served bytes with one exact UTF-8 text replaced. The text must
/// occur exactly once, so a mutation that does not apply fails its case instead of being ignored.</summary>
let replaceRoleText (role: string) (find: string) (replace: string) (set: MetadataSet) =
    let text = Text.Encoding.UTF8.GetString(served role set)
    let at = text.IndexOf(find, StringComparison.Ordinal)

    if at < 0 || text.IndexOf(find, at + find.Length, StringComparison.Ordinal) >= 0 then
        failwithf "replace-role-text: the %s text to find does not occur exactly once" role

    { set with Served = set.Served.Add(role, Text.Encoding.UTF8.GetBytes(text.Substring(0, at) + replace + text.Substring(at + find.Length))) }

let versionOf (role: Value) =
    match at [ "signed"; "version" ] role with
    | Value.Number spelling -> UInt64.Parse spelling
    | _ -> failwith "version"

/// Re-signs a role's envelope with the fixture's key for that role.
let resign (role: string) (envelopeValue: Value) =
    envelope (memberOf "signed" envelopeValue) (keysOf role)

/// The fixture's relink: the snapshot's entry for targets.json and the timestamp's for snapshot.json are computed
/// again from the served bytes, and both are signed again.
let relink (set: MetadataSet) =
    let entry (role: Value) (bytes: byte array) =
        Value.Object
            [ "version", number (versionOf role)
              "length", number (uint64 bytes.Length)
              "hashes", Value.Object [ "sha256", text (Digest.ofBytes bytes) ] ]

    let snapshot = resign "snapshot" (setAt [ "signed"; "meta"; "targets.json" ] (entry set.Targets (served "targets" set)) set.Snapshot)
    let timestamp = resign "timestamp" (setAt [ "signed"; "meta"; "snapshot.json" ] (entry snapshot (canonical snapshot)) set.Timestamp)

    { set with
        Snapshot = snapshot
        Timestamp = timestamp
        Served = set.Served.Remove("snapshot").Remove("timestamp") }

/// The source a refresh reads: the trusted root as 1.root.json, any further roots, and the set's other roles at the
/// versions their envelopes state.
let sourceOf (set: MetadataSet) (laterRoots: Value list) : MetadataSource =
    let roots = (set.Root :: laterRoots) |> List.map (fun root -> versionOf root, canonical root) |> Map.ofList

    { Root = fun version -> roots.TryFind version
      Timestamp = fun () -> Some(served "timestamp" set)
      Snapshot = fun version -> if version = versionOf set.Snapshot then Some(served "snapshot" set) else None
      Targets = fun version -> if version = versionOf set.Targets then Some(served "targets" set) else None }

/// The fixture's reference time, the trusted start time of every refresh in these tests.
let referenceTime = DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)

let expectOk (what: string) (result: Result<'T, TufFailure>) =
    match result with
    | Ok value -> value
    | Error failure -> failtestf "%s: %A" what failure
