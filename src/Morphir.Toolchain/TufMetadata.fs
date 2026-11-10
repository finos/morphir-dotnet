// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Globalization

/// <summary>
/// Why TUF metadata or a refresh was refused. Each case maps to one stable code (<c>TufFailure.code</c>).
/// </summary>
/// <remarks>
/// SECURITY: this code has not had a security review. It must not be used to authenticate a production release until
/// it has.
/// </remarks>
[<RequireQualifiedAccess>]
type TufFailure =
    /// Metadata that does not read under the profile: not JSON, a wrong <c>_type</c>, an unsupported
    /// <c>spec_version</c>, a key that is not Ed25519, and so on. The reason is for logs, never for matching.
    | MetadataInvalid of role: TufRole * reason: string
    /// Metadata larger than the profile's download bound for its role, refused before it is parsed.
    | MetadataTooLarge of role: TufRole * limit: int
    /// The role's threshold of signatures by its own keys is not met.
    | SignatureInvalid of role: TufRole
    /// A new root is not the next version, or does not meet the old or its own root threshold.
    | RootRotationInvalid of version: uint64 * reason: string
    /// The metadata expires at or before the refresh's start time.
    | Expired of role: TufRole
    /// The metadata is older than the trusted metadata of its role.
    | Rollback of role: TufRole
    /// The metadata has the trusted version of its role but other content: two documents under one version.
    | VersionReused of role: TufRole
    /// The metadata is not the version, length or digest that its parent role names.
    | LinkMismatch of role: TufRole
    /// The targets metadata lists a target path that breaks the target path rules.
    | NonPortableTarget of target: string
    /// The source has no file for a role the refresh needs, or failed to give one.
    | Unavailable of role: TufRole
    /// A defect: an exception inside the client, turned into a failure at its boundary so the refresh fails closed.
    /// The reason is for logs, never for matching.
    | Internal of reason: string

/// <summary>The stable code and role of each TUF failure, for a <c>trust-failure</c> diagnostic.</summary>
module TufFailure =
    /// <summary>The stable code. Provisional: metadata that does not read under the profile, or is over its
    /// download bound, is <c>metadata_signature_invalid</c>, because the contract has no code for it; it cannot be
    /// verified as signed metadata of the profile.</summary>
    let code (failure: TufFailure) =
        match failure with
        | TufFailure.MetadataInvalid _
        | TufFailure.MetadataTooLarge _
        | TufFailure.SignatureInvalid _
        | TufFailure.Internal _ -> Code.Trust TrustCode.MetadataSignatureInvalid
        | TufFailure.RootRotationInvalid _ -> Code.Trust TrustCode.MetadataRootRotationInvalid
        | TufFailure.Expired _ -> Code.Trust TrustCode.MetadataExpired
        | TufFailure.Rollback _
        | TufFailure.VersionReused _ -> Code.Trust TrustCode.MetadataRollback
        | TufFailure.LinkMismatch _ -> Code.Trust TrustCode.MetadataLinkMismatch
        | TufFailure.NonPortableTarget _ -> Code.Reader ReaderCode.NonPortablePath
        | TufFailure.Unavailable _ -> Code.Trust TrustCode.RepositoryUnavailable

    /// The metadata role a diagnostic names.
    let role (failure: TufFailure) =
        match failure with
        | TufFailure.MetadataInvalid(role, _)
        | TufFailure.MetadataTooLarge(role, _)
        | TufFailure.SignatureInvalid role
        | TufFailure.Expired role
        | TufFailure.Rollback role
        | TufFailure.VersionReused role
        | TufFailure.LinkMismatch role
        | TufFailure.Unavailable role -> Some role
        | TufFailure.RootRotationInvalid _ -> Some TufRole.Root
        | TufFailure.NonPortableTarget _ -> Some TufRole.Targets
        | TufFailure.Internal _ -> None

/// A signature over a metadata document's canonical <c>signed</c> bytes.
type TufSignature =
    { KeyId: string
      Value: byte array }

/// A metadata document as read: its signed content, the canonical bytes of <c>signed</c> that its signatures cover,
/// and the signatures. Nothing here says the signatures verify.
type TufDocument<'T> =
    { Signed: 'T
      Canonical: byte array
      Signatures: TufSignature list }

/// The keys and threshold of one top-level role.
type TufRoleKeys =
    { KeyIds: string list
      Threshold: int }

/// A root: its version, expiry, Ed25519 keys by key id, and the four top-level roles.
type TufRoot =
    { Version: uint64
      Expires: DateTimeOffset
      /// The 32-byte Ed25519 public key of each key id.
      Keys: Map<string, byte array>
      Roles: Map<TufRole, TufRoleKeys> }

/// The version, length and SHA-256 of a metadata file, as its parent names it.
type TufMetaFile =
    { Version: uint64
      Length: uint64
      Sha256: Sha256 }

type TufTimestamp =
    { Version: uint64
      Expires: DateTimeOffset
      Snapshot: TufMetaFile }

type TufSnapshot =
    { Version: uint64
      Expires: DateTimeOffset
      Targets: TufMetaFile }

type TufTargets =
    { Version: uint64
      Expires: DateTimeOffset
      /// The authenticated length, SHA-256 and custom object of each target, by target path.
      Targets: Map<string, AuthenticatedTarget> }

/// <summary>
/// Strict readers for the four top-level TUF roles of the tool release metadata v1 profile: TUF 1.0 JSON, Ed25519
/// keys, SHA-256 hashes, consistent snapshots and no delegations. A reader checks the document's shape only;
/// signatures, thresholds, versions against trusted metadata and expiry are the update workflow's.
/// </summary>
/// <remarks>
/// SECURITY: this code has not had a security review. It must not be used to authenticate a production release until
/// it has. Readings marked provisional are the stricter reading where the profile is silent; each is listed for the
/// security reviewer.
/// </remarks>
module TufMetadata =
    /// The profile's download bounds, applied before parsing.
    let rootLimit = 64 * 1024
    let timestampLimit = 64 * 1024
    let snapshotLimit = 1024 * 1024
    let targetsLimit = 8 * 1024 * 1024

    let limitOf (role: TufRole) =
        match role with
        | TufRole.Root -> rootLimit
        | TufRole.Timestamp -> timestampLimit
        | TufRole.Snapshot -> snapshotLimit
        | TufRole.Targets -> targetsLimit

    let roleName (role: TufRole) =
        match role with
        | TufRole.Root -> "root"
        | TufRole.Timestamp -> "timestamp"
        | TufRole.Snapshot -> "snapshot"
        | TufRole.Targets -> "targets"

    // The spellings are checked character by character, never with a regular expression: .NET's $ also matches
    // before a final line feed, so "^[0-9a-f]{128}$" accepts 128 digits and a line feed (review of #581, T1).

    /// Exactly <paramref name="length"/> lowercase hexadecimal digits.
    let private isLowerHex (length: int) (s: string) =
        s.Length = length && s |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

    /// TUF's expires: exactly YYYY-MM-DDTHH:MM:SSZ, in UTC, without fractions or an offset.
    let private isExpiresSpelling (s: string) =
        s.Length = 20
        && s
           |> Seq.indexed
           |> Seq.forall (fun (index, c) ->
               match index with
               | 4
               | 7 -> c = '-'
               | 10 -> c = 'T'
               | 13
               | 16 -> c = ':'
               | 19 -> c = 'Z'
               | _ -> c >= '0' && c <= '9')

    type private Reader<'T> = Result<'T, TufFailure>

    let private members (role: TufRole) (what: string) (value: Value) : Reader<(string * Value) list> =
        match value with
        | Value.Object items -> Ok items
        | _ -> Error(TufFailure.MetadataInvalid(role, $"{what} is not an object"))

    let private find (name: string) (items: (string * Value) list) =
        items |> List.tryFind (fst >> (=) name) |> Option.map snd

    let private required (role: TufRole) (name: string) (items: (string * Value) list) : Reader<Value> =
        match find name items with
        | Some value -> Ok value
        | None -> Error(TufFailure.MetadataInvalid(role, $"no {name}"))

    let private text (role: TufRole) (name: string) (value: Value) : Reader<string> =
        match value with
        | Value.String s -> Ok s
        | _ -> Error(TufFailure.MetadataInvalid(role, $"{name} is not a string"))

    /// <summary>The largest integer a reader takes: 2^53 - 1, the largest that every reader language holds exactly
    /// (a JavaScript number; I-JSON, RFC 7493), as the version bound reasons. Provisional: the profile states no bound
    /// (review of #581, T8).</summary>
    let maxInteger = 9007199254740991UL

    /// A plain non-negative integer of at most <c>maxInteger</c>.
    let private integer (role: TufRole) (name: string) (value: Value) : Reader<uint64> =
        match value with
        | Value.Number spelling ->
            match UInt64.TryParse(spelling, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, n when spelling = n.ToString(CultureInfo.InvariantCulture) && n <= maxInteger -> Ok n
            | _ -> Error(TufFailure.MetadataInvalid(role, $"{name} is not a plain integer"))
        | _ -> Error(TufFailure.MetadataInvalid(role, $"{name} is not a number"))

    let private positive role name value =
        integer role name value
        |> Result.bind (fun n -> if n = 0UL then Error(TufFailure.MetadataInvalid(role, $"{name} is 0")) else Ok n)

    let private hexBytes (role: TufRole) (name: string) (digits: int) (value: Value) : Reader<byte array> =
        text role name value
        |> Result.bind (fun s ->
            if isLowerHex digits s then Ok(Convert.FromHexString s)
            else Error(TufFailure.MetadataInvalid(role, $"{name} is not lowercase hexadecimal of the right length")))

    let private sha256 (role: TufRole) (value: Value) : Reader<Sha256> =
        text role "sha256" value
        |> Result.bind (fun s ->
            match Sha256.tryParse s with
            | Ok digest -> Ok digest
            | Error _ -> Error(TufFailure.MetadataInvalid(role, "sha256 is not 64 lowercase hexadecimal digits")))

    /// <summary>The first string or member name inside a value that is not already NFC. The signature input is the
    /// NFC canonical form, so a document with such text would share its input with another (review of #581, T4).</summary>
    let rec private firstNotNfc (value: Value) : string option =
        match value with
        | Value.Object members ->
            members
            |> List.tryPick (fun (name, item) -> if Nfc.isNormalized name then firstNotNfc item else Some $"the member name {name}")
        | Value.Array items -> items |> List.tryPick firstNotNfc
        | Value.String s when not (Nfc.isNormalized s) -> Some "a string"
        | _ -> None

    /// <summary>Refuses a member whose name differs from a known member of the object only in ASCII letter case, such
    /// as <c>Version</c> beside <c>version</c>: unknown members are ignored as TUF says, but one that a consumer matching
    /// names without case would read instead of the real member is refused (review of #581, T4).</summary>
    let private noCaseVariants (role: TufRole) (what: string) (known: string list) (items: (string * Value) list) : Reader<unit> =
        match
            items
            |> List.tryFind (fun (name, _) -> not (List.contains name known) && known |> List.exists (equalsAsciiIgnoreCase name))
        with
        | Some(name, _) -> Error(TufFailure.MetadataInvalid(role, $"{what} has the member {name}, which differs from a known member only in letter case"))
        | None -> Ok()

    let private exactMembers (role: TufRole) (what: string) (allowed: string list) (items: (string * Value) list) : Reader<unit> =
        match items |> List.tryFind (fun (name, _) -> not (List.contains name allowed)) with
        | Some(name, _) -> Error(TufFailure.MetadataInvalid(role, $"{what} has the member {name}, which the profile does not define"))
        | None -> Ok()

    /// <summary>The envelope: exactly <c>signatures</c> and <c>signed</c> (provisional: no other member), each signature
    /// exactly a 64-hex key id and a 128-hex Ed25519 signature, and no key id signing twice (provisional). The canonical
    /// bytes of <c>signed</c> are the signature input.</summary>
    let private envelope (role: TufRole) (bytes: byte array) : Reader<(string * Value) list * byte array * TufSignature list> =
        let limit = limitOf role

        if bytes.Length > limit then
            Error(TufFailure.MetadataTooLarge(role, limit))
        else
            match Syntax.parseJson bytes with
            | Error _ -> Error(TufFailure.MetadataInvalid(role, "not a JSON document"))
            | Ok value ->
                result {
                    let! top = members role "the document" value
                    do! exactMembers role "the envelope" [ "signatures"; "signed" ] top
                    let! signed = required role "signed" top
                    let! signedMembers = members role "signed" signed

                    match firstNotNfc signed with
                    | Some what -> return! Error(TufFailure.MetadataInvalid(role, $"{what} inside signed is not NFC"))
                    | None -> ()

                    let! canonical =
                        Canonical.bytes signed
                        |> Result.mapError (fun _ -> TufFailure.MetadataInvalid(role, "signed has no canonical form"))

                    let! signatureValues =
                        match required role "signatures" top with
                        | Ok(Value.Array items) -> Ok items
                        | Ok _ -> Error(TufFailure.MetadataInvalid(role, "signatures is not an array"))
                        | Error failure -> Error failure

                    let! signatures =
                        signatureValues
                        |> Result.traverse (fun item ->
                            result {
                                let! fields = members role "a signature" item
                                do! exactMembers role "a signature" [ "keyid"; "sig" ] fields
                                let! keyId = required role "keyid" fields |> Result.bind (text role "keyid")

                                if not (isLowerHex 64 keyId) then
                                    return! Error(TufFailure.MetadataInvalid(role, "a signature's keyid is not 64 lowercase hexadecimal digits"))

                                let! value = required role "sig" fields |> Result.bind (hexBytes role "sig" 128)
                                return { KeyId = keyId; Value = value }
                            })

                    if List.length (signatures |> List.distinctBy _.KeyId) <> List.length signatures then
                        return! Error(TufFailure.MetadataInvalid(role, "a key id signs twice"))

                    return signedMembers, canonical, signatures
                }

    /// <summary><c>_type</c>, <c>spec_version</c> (provisional: canonical SemVer 1.0.x, so 1.0.36 and not 1.1.0),
    /// a positive <c>version</c> and the exact <c>expires</c> format.</summary>
    let private common (role: TufRole) (signed: (string * Value) list) : Reader<uint64 * DateTimeOffset> =
        result {
            let own =
                match role with
                | TufRole.Root -> [ "consistent_snapshot"; "keys"; "roles" ]
                | TufRole.Timestamp
                | TufRole.Snapshot -> [ "meta" ]
                | TufRole.Targets -> [ "targets"; "delegations" ]

            do! noCaseVariants role "signed" ([ "_type"; "spec_version"; "version"; "expires" ] @ own) signed

            let! kind = required role "_type" signed |> Result.bind (text role "_type")

            if kind <> roleName role then
                return! Error(TufFailure.MetadataInvalid(role, $"_type is {kind}"))

            let! spec = required role "spec_version" signed |> Result.bind (text role "spec_version")

            match SemVer.tryParse spec with
            | Ok version when version.SemVersion.Major = Numerics.BigInteger.One && version.SemVersion.Minor.IsZero -> ()
            | _ -> return! Error(TufFailure.MetadataInvalid(role, $"spec_version {spec} is not compatible with 1.0"))

            let! version = required role "version" signed |> Result.bind (positive role "version")
            let! expiresText = required role "expires" signed |> Result.bind (text role "expires")

            let! expires =
                match
                    isExpiresSpelling expiresText,
                    DateTimeOffset.TryParseExact(
                        expiresText,
                        "yyyy-MM-dd'T'HH:mm:ss'Z'",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal
                    )
                with
                | true, (true, parsed) -> Ok parsed
                | _ -> Error(TufFailure.MetadataInvalid(role, $"expires {expiresText} is not YYYY-MM-DDTHH:MM:SSZ"))

            return version, expires
        }

    let private document (role: TufRole) (read: (string * Value) list -> Reader<'T>) (bytes: byte array) : Reader<TufDocument<'T>> =
        result {
            let! signed, canonical, signatures = envelope role bytes
            let! content = read signed

            return
                { Signed = content
                  Canonical = canonical
                  Signatures = signatures }
        }

    /// <summary>A key of a root: exactly <c>keytype</c> <c>ed25519</c>, <c>scheme</c> <c>ed25519</c> and a 32-byte
    /// <c>keyval.public</c>. Provisional: the key id must be the SHA-256 of the canonical key object, and a root that
    /// lists any key of another type is refused, not only one a role uses.</summary>
    let private key (keyId: string) (value: Value) : Reader<byte array> =
        let role = TufRole.Root

        result {
            if not (isLowerHex 64 keyId) then
                return! Error(TufFailure.MetadataInvalid(role, "a key id is not 64 lowercase hexadecimal digits"))

            let! fields = members role "a key" value
            do! exactMembers role "a key" [ "keytype"; "scheme"; "keyval" ] fields
            let! keyType = required role "keytype" fields |> Result.bind (text role "keytype")
            let! scheme = required role "scheme" fields |> Result.bind (text role "scheme")

            if keyType <> "ed25519" || scheme <> "ed25519" then
                return! Error(TufFailure.MetadataInvalid(role, $"a key is {keyType} with scheme {scheme}; the profile allows Ed25519 only"))

            let! keyval = required role "keyval" fields |> Result.bind (members role "keyval")
            do! exactMembers role "keyval" [ "public" ] keyval
            let! publicKey = required role "public" keyval |> Result.bind (hexBytes role "public" 64)

            let! canonicalKey =
                Canonical.bytes value |> Result.mapError (fun _ -> TufFailure.MetadataInvalid(role, "a key has no canonical form"))

            if Digest.ofBytes canonicalKey <> keyId then
                return! Error(TufFailure.MetadataInvalid(role, "a key id is not the SHA-256 of its canonical key"))

            return publicKey
        }

    let private topLevelRoles = [ TufRole.Root; TufRole.Timestamp; TufRole.Snapshot; TufRole.Targets ]

    let private rootContent (signed: (string * Value) list) : Reader<TufRoot> =
        let role = TufRole.Root

        result {
            let! version, expires = common role signed

            match find "consistent_snapshot" signed with
            | Some(Value.Boolean true) -> ()
            | _ -> return! Error(TufFailure.MetadataInvalid(role, "consistent_snapshot is not true"))

            let! keyItems = required role "keys" signed |> Result.bind (members role "keys")
            let! keys = keyItems |> Result.traverse (fun (id, value) -> key id value |> Result.map (fun k -> id, k))
            let keys = Map.ofList keys

            let! roleItems = required role "roles" signed |> Result.bind (members role "roles")
            do! exactMembers role "roles" (topLevelRoles |> List.map roleName) roleItems

            let! roles =
                topLevelRoles
                |> Result.traverse (fun top ->
                    result {
                        let! fields = required role (roleName top) roleItems |> Result.bind (members role (roleName top))
                        do! exactMembers role (roleName top) [ "keyids"; "threshold" ] fields

                        let! keyIds =
                            match required role "keyids" fields with
                            | Ok(Value.Array items) -> items |> Result.traverse (text role "keyids")
                            | Ok _ -> Error(TufFailure.MetadataInvalid(role, "keyids is not an array"))
                            | Error failure -> Error failure

                        let! threshold = required role "threshold" fields |> Result.bind (positive role "threshold")

                        if List.length (List.distinct keyIds) <> List.length keyIds then
                            return! Error(TufFailure.MetadataInvalid(role, $"the {roleName top} role names a key twice"))

                        match keyIds |> List.tryFind (keys.ContainsKey >> not) with
                        | Some _ -> return! Error(TufFailure.MetadataInvalid(role, $"the {roleName top} role names a key the root does not list"))
                        | None -> ()

                        if threshold > uint64 (List.length keyIds) then
                            return! Error(TufFailure.MetadataInvalid(role, $"the {roleName top} threshold is above its keys"))

                        return top, { KeyIds = keyIds; Threshold = int threshold }
                    })

            return
                { Version = version
                  Expires = expires
                  Keys = keys
                  Roles = Map.ofList roles }
        }

    /// <summary>A metadata file as its parent names it: a positive version, and (provisional) both a length and a
    /// SHA-256, which TUF makes optional.</summary>
    let private metaFile (role: TufRole) (value: Value) : Reader<TufMetaFile> =
        result {
            let! fields = members role "a meta entry" value
            do! noCaseVariants role "a meta entry" [ "version"; "length"; "hashes" ] fields
            let! version = required role "version" fields |> Result.bind (positive role "version")
            let! length = required role "length" fields |> Result.bind (integer role "length")
            let! hashes = required role "hashes" fields |> Result.bind (members role "hashes")
            do! noCaseVariants role "hashes" [ "sha256" ] hashes
            let! sha = required role "sha256" hashes |> Result.bind (sha256 role)

            return
                { Version = version
                  Length = length
                  Sha256 = sha }
        }

    /// <summary>The <c>meta</c> of a timestamp or snapshot: exactly the one file the profile names (provisional).</summary>
    let private onlyMeta (role: TufRole) (file: string) (signed: (string * Value) list) : Reader<TufMetaFile> =
        result {
            let! meta = required role "meta" signed |> Result.bind (members role "meta")
            do! exactMembers role "meta" [ file ] meta
            return! required role file meta |> Result.bind (metaFile role)
        }

    let private timestampContent (signed: (string * Value) list) : Reader<TufTimestamp> =
        result {
            let! version, expires = common TufRole.Timestamp signed
            let! snapshot = onlyMeta TufRole.Timestamp "snapshot.json" signed
            return { Version = version; Expires = expires; Snapshot = snapshot }
        }

    let private snapshotContent (signed: (string * Value) list) : Reader<TufSnapshot> =
        result {
            let! version, expires = common TufRole.Snapshot signed
            let! targets = onlyMeta TufRole.Snapshot "targets.json" signed
            return { Version = version; Expires = expires; Targets = targets }
        }

    /// <summary>A target: a length, a SHA-256 among its hashes, and an optional <c>custom</c> object whose
    /// <c>morphir</c> member, when present, is an object. Its path must be a portable target path.</summary>
    let private target (path: string) (value: Value) : Reader<string * AuthenticatedTarget> =
        let role = TufRole.Targets

        result {
            match TargetPath.tryParse path with
            | Error _ -> return! Error(TufFailure.NonPortableTarget path)
            | Ok _ -> ()

            let! fields = members role "a target" value
            do! noCaseVariants role "a target" [ "length"; "hashes"; "custom" ] fields
            let! length = required role "length" fields |> Result.bind (integer role "length")
            let! hashes = required role "hashes" fields |> Result.bind (members role "hashes")
            do! noCaseVariants role "hashes" [ "sha256" ] hashes
            let! sha = required role "sha256" hashes |> Result.bind (sha256 role)

            let! custom =
                match find "custom" fields with
                | None -> Ok None
                | Some(Value.Object customMembers as custom) ->
                    match noCaseVariants role "custom" [ "morphir" ] customMembers with
                    | Error failure -> Error failure
                    | Ok() ->
                    match find "morphir" customMembers with
                    | None
                    | Some(Value.Object _) -> Ok(Some custom)
                    | Some _ -> Error(TufFailure.MetadataInvalid(role, "custom.morphir is not an object"))
                | Some _ -> Error(TufFailure.MetadataInvalid(role, "custom is not an object"))

            return
                path,
                { Length = length
                  Sha256 = sha
                  Custom = custom }
        }

    let private targetsContent (signed: (string * Value) list) : Reader<TufTargets> =
        let role = TufRole.Targets

        result {
            let! version, expires = common role signed

            if (find "delegations" signed).IsSome then
                return! Error(TufFailure.MetadataInvalid(role, "delegations are not part of this profile subset"))

            let! items = required role "targets" signed |> Result.bind (members role "targets")
            let! targets = items |> Result.traverse (fun (path, value) -> target path value)

            return
                { Version = version
                  Expires = expires
                  Targets = Map.ofList targets }
        }

    /// <summary>A reader as a total function: an exception inside it, which is a defect, refuses the metadata
    /// instead of leaving the reader (review of #581, T1).</summary>
    let private total (role: TufRole) (read: (string * Value) list -> Reader<'T>) (bytes: byte array) : Reader<TufDocument<'T>> =
        try
            document role read bytes
        with error ->
            Error(TufFailure.MetadataInvalid(role, $"the reader failed with {error.GetType().Name}, a defect"))

    let readRoot (bytes: byte array) = total TufRole.Root rootContent bytes
    let readTimestamp (bytes: byte array) = total TufRole.Timestamp timestampContent bytes
    let readSnapshot (bytes: byte array) = total TufRole.Snapshot snapshotContent bytes
    let readTargets (bytes: byte array) = total TufRole.Targets targetsContent bytes

    /// <summary>The repository identity: the SHA-256 of the canonical JSON of the initial root's <c>signed</c>
    /// object. Signatures do not affect it.</summary>
    let repositoryIdentity (root: TufDocument<TufRoot>) = Sha256.ofBytes root.Canonical
