// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Math.EC.Rfc8032

/// <summary>
/// Signature thresholds for the tool release metadata v1 profile: Ed25519 only, with exact key and signature sizes,
/// counting only keys of the role.
/// </summary>
/// <remarks>
/// SECURITY: this code has not had a security review. It must not be used to authenticate a production release until
/// it has. The primitive is BouncyCastle's RFC 8032 Ed25519, used directly through
/// <c>Ed25519PublicKeyParameters.Verify</c>, never through a factory.
/// </remarks>
module TufSignatures =
    /// <summary>Whether an Ed25519 signature over the message verifies under the public key (RFC 8032, pure Ed25519,
    /// no context). Anything but a 32-byte key and a 64-byte signature is refused before the primitive runs, and a
    /// key that is not a valid point fails.</summary>
    let verifyEd25519 (publicKey: byte array) (message: byte array) (signature: byte array) =
        publicKey.Length = 32
        && signature.Length = 64
        && (try
                Ed25519PublicKeyParameters(publicKey, 0)
                    .Verify(Ed25519.Algorithm.Ed25519, null, message, 0, message.Length, signature, 0)
            with _ ->
                // BouncyCastle refuses a key that is not a valid point with ArgumentException; any other exception
                // fails closed too.
                false)

    let private thresholdOf (root: TufRoot) (role: TufRole) (document: TufDocument<'T>) =
        match root.Roles.TryFind role with
        | None -> false
        | Some keys ->
            let verified =
                document.Signatures
                |> List.filter (fun signature -> List.contains signature.KeyId keys.KeyIds)
                |> List.distinctBy _.KeyId
                |> List.choose (fun signature ->
                    match root.Keys.TryFind signature.KeyId with
                    | Some publicKey when verifyEd25519 publicKey document.Canonical signature.Value ->
                        Some(Convert.ToHexStringLower publicKey)
                    | _ -> None)
                |> List.distinct

            List.length verified >= keys.Threshold

    /// <summary>Whether the role's threshold is met: the number of distinct public keys of the role, named by
    /// distinct key ids, whose signature over the document's canonical <c>signed</c> bytes verifies. A signature by a
    /// key the root does not list, or lists for another role, does not count. Two key ids naming one public key count
    /// once (provisional: the profile does not say).</summary>
    let thresholdMet (root: TufRoot) (role: TufRole) (document: TufDocument<'T>) =
        try
            thresholdOf root role document
        with _ ->
            // Fails closed: a defect never counts a signature.
            false

/// <summary>The trusted metadata a refresh starts from and returns: always a root, and the last trusted timestamp,
/// snapshot and targets once a refresh has completed. A value in and a value out; keeping it durable is the
/// caller's.</summary>
type TufTrustedSet =
    { Root: TufRoot
      Timestamp: TufTimestamp option
      Snapshot: TufSnapshot option
      Targets: TufTargets option }

/// <summary>The bytes a refresh reads, by consistent-snapshot name: <c>N.root.json</c>, <c>timestamp.json</c>,
/// <c>N.snapshot.json</c> and <c>N.targets.json</c>. <c>None</c> means the source has no such file. The refresh
/// applies the download bounds to whatever bytes it is given.</summary>
type MetadataSource =
    { Root: uint64 -> byte array option
      Timestamp: unit -> byte array option
      Snapshot: uint64 -> byte array option
      Targets: uint64 -> byte array option }

/// <summary>How a refresh ended. Every case carries the trust state the caller must keep: the newest root, and each
/// role, accepted so far. TUF persists each accepted root (5.3.8) and each verified role (5.4.5, 5.5.7, 5.6.6), so a
/// refresh that stops, at the root limit or at a failure, still hands back what it accepted, and a revocation of root
/// keys is not lost. A caller cannot read the failure without the case that also holds that state.</summary>
[<RequireQualifiedAccess>]
type RefreshResult =
    /// Every role was verified; the trusted set holds the new root, timestamp, snapshot and targets.
    | Refreshed of trusted: TufTrustedSet
    /// The refresh accepted 32 roots and more follow: the trusted set holds the newest accepted root, and the user is
    /// asked to retry, which continues from it. This is TUF's stop at the limit (5.3.3), handed back the same way as
    /// a failure.
    | RootRotationPaused of trusted: TufTrustedSet
    /// A step was refused. The trusted set is the newest trust state accepted before it, which the caller keeps.
    | Failed of failure: TufFailure * trusted: TufTrustedSet

    /// The trust state to keep, whatever the outcome.
    member this.Trusted =
        match this with
        | Refreshed trusted
        | RootRotationPaused trusted
        | Failed(_, trusted) -> trusted

/// <summary>
/// The update workflow of the tool release metadata v1 profile (TUF 1.0.36 section 5) as a pure function over
/// supplied bytes and one start time: root rotation one version at a time with both thresholds, at most 32 roots per
/// refresh, then timestamp, snapshot and targets with signature, rollback, link, version and expiry checks.
/// </summary>
/// <remarks>
/// SECURITY: this code has not had a security review. It must not be used to authenticate a production release until
/// it has. It does no I/O: no network, no file system and no clock. The caller supplies the bytes, the trusted set and
/// the trusted start time, and keeps the result.
/// </remarks>
module TufClient =
    /// The most roots one refresh accepts before it asks for a retry.
    let maxRootRotations = 32

    /// <summary>The consistent-snapshot name of a metadata file: <c>N.root.json</c>, <c>timestamp.json</c>,
    /// <c>N.snapshot.json</c> or <c>N.targets.json</c>.</summary>
    let metadataName (role: TufRole) (version: uint64) =
        match role with
        | TufRole.Timestamp -> "timestamp.json"
        | other -> $"{version}.{TufMetadata.roleName other}.json"

    /// <summary>The consistent-snapshot name of a target: its SHA-256 before the file name, in the same directory,
    /// always with <c>/</c>.</summary>
    let consistentTargetPath (path: string) (sha256: Sha256) =
        match path.LastIndexOf '/' with
        | -1 -> $"{sha256.Text}.{path}"
        | at -> $"{path.Substring(0, at + 1)}{sha256.Text}.{path.Substring(at + 1)}"

    let private expired (start: DateTimeOffset) (expires: DateTimeOffset) = expires <= start

    /// The keys a role trusts, as public keys, so a rotation that changes them can be seen.
    let private roleKeys (root: TufRoot) (role: TufRole) =
        root.Roles[role].KeyIds |> List.map (fun id -> Convert.ToHexStringLower root.Keys[id]) |> set, root.Roles[role].Threshold

    /// Bytes against the version, length and SHA-256 their parent names, before they are parsed.
    let private linked (role: TufRole) (meta: TufMetaFile) (bytes: byte array) =
        if uint64 bytes.LongLength <> meta.Length || Sha256.ofBytes bytes <> meta.Sha256 then
            Error(TufFailure.LinkMismatch role)
        else
            Ok()

    /// <summary>Asks the caller's source for a file. A source that throws has failed to give it: the role is
    /// unavailable, and the exception does not leave the refresh.</summary>
    let private ask (role: TufRole) (call: unit -> byte array option) : Result<byte array option, TufFailure> =
        try
            Ok(call ())
        with _ ->
            Error(TufFailure.Unavailable role)

    let private fetched (role: TufRole) (call: unit -> byte array option) =
        match ask role call with
        | Ok(Some bytes) -> Ok bytes
        | Ok None -> Error(TufFailure.Unavailable role)
        | Error failure -> Error failure

    /// <summary>Root rotation (5.3): from trusted version N, only N + 1, then one version at a time. Each new root
    /// must have the next version and meet the old root's threshold and its own. After 32 accepted roots, when another
    /// follows, the refresh pauses.</summary>
    let private rotate (trusted: TufRoot) (source: MetadataSource) (accept: TufRoot -> unit) : Result<bool, TufFailure> =
        let rec next (root: TufRoot) (accepted: int) =
            let version = root.Version + 1UL

            match ask TufRole.Root (fun () -> source.Root version) with
            | Error failure -> Error failure
            | Ok None -> Ok false
            | Ok(Some _) when accepted = maxRootRotations -> Ok true
            | Ok(Some bytes) ->
                match TufMetadata.readRoot bytes with
                | Error(TufFailure.MetadataInvalid(_, reason)) -> Error(TufFailure.RootRotationInvalid(version, reason))
                | Error failure -> Error failure
                | Ok read ->
                    if read.Signed.Version <> version then
                        Error(TufFailure.RootRotationInvalid(version, $"{metadataName TufRole.Root version} has version {read.Signed.Version}"))
                    elif not (TufSignatures.thresholdMet root TufRole.Root read) then
                        Error(TufFailure.RootRotationInvalid(version, "the trusted root's threshold is not met"))
                    elif not (TufSignatures.thresholdMet read.Signed TufRole.Root read) then
                        Error(TufFailure.RootRotationInvalid(version, "the new root's own threshold is not met"))
                    else
                        accept read.Signed
                        next read.Signed (accepted + 1)

        next trusted 0

    /// <summary>One refresh from a trusted set, with every expiry checked against <paramref name="start"/>, the one
    /// trusted start time of the refresh. The result always carries the newest trust state accepted, also when a step
    /// is refused.</summary>
    let refresh (start: DateTimeOffset) (trusted: TufTrustedSet) (source: MetadataSource) : RefreshResult =
        let current = ref trusted

        // 5.3.7 to 5.3.9: each accepted root is trusted at once. 5.3.11: when the timestamp or snapshot keys differ
        // from the trusted root's, the trusted timestamp and snapshot are dropped, so a fast-forward attack on them can
        // be recovered from. The trusted targets go with them: TUF checks targets only against the snapshot (5.6.4),
        // so keeping them would make a targets fast-forward permanent (review of #581, T2).
        let accept (root: TufRoot) =
            let rotatedKeys =
                roleKeys root TufRole.Timestamp <> roleKeys trusted.Root TufRole.Timestamp
                || roleKeys root TufRole.Snapshot <> roleKeys trusted.Root TufRole.Snapshot

            current.Value <-
                if rotatedKeys then
                    { current.Value with
                        Root = root
                        Timestamp = None
                        Snapshot = None
                        Targets = None }
                else
                    { current.Value with Root = root }

        let steps () : Result<RefreshResult, TufFailure> =
            result {
                let! paused = rotate trusted.Root source accept

                if paused then
                    return RefreshResult.RootRotationPaused current.Value
                else
                    let root = current.Value.Root

                    // 5.3.10: the newest root must not have expired.
                    if expired start root.Expires then
                        return! Error(TufFailure.Expired TufRole.Root)

                    // 5.4: the timestamp.
                    let! timestampBytes = fetched TufRole.Timestamp source.Timestamp
                    let! timestamp = TufMetadata.readTimestamp timestampBytes

                    if not (TufSignatures.thresholdMet root TufRole.Timestamp timestamp) then
                        return! Error(TufFailure.SignatureInvalid TufRole.Timestamp)

                    match current.Value.Timestamp with
                    | Some old when timestamp.Signed.Version < old.Version || timestamp.Signed.Snapshot.Version < old.Snapshot.Version ->
                        return! Error(TufFailure.Rollback TufRole.Timestamp)
                    | _ -> ()

                    if expired start timestamp.Signed.Expires then
                        return! Error(TufFailure.Expired TufRole.Timestamp)

                    // 5.4.5: the verified timestamp is trusted.
                    current.Value <- { current.Value with Timestamp = Some timestamp.Signed }

                    // 5.5: the snapshot the timestamp names.
                    let snapshotMeta = timestamp.Signed.Snapshot
                    let! snapshotBytes = fetched TufRole.Snapshot (fun () -> source.Snapshot snapshotMeta.Version)

                    if snapshotBytes.Length > TufMetadata.snapshotLimit then
                        return! Error(TufFailure.MetadataTooLarge(TufRole.Snapshot, TufMetadata.snapshotLimit))

                    do! linked TufRole.Snapshot snapshotMeta snapshotBytes
                    let! snapshot = TufMetadata.readSnapshot snapshotBytes

                    if not (TufSignatures.thresholdMet root TufRole.Snapshot snapshot) then
                        return! Error(TufFailure.SignatureInvalid TufRole.Snapshot)

                    if snapshot.Signed.Version <> snapshotMeta.Version then
                        return! Error(TufFailure.LinkMismatch TufRole.Snapshot)

                    match current.Value.Snapshot with
                    | Some old when snapshot.Signed.Targets.Version < old.Targets.Version ->
                        return! Error(TufFailure.Rollback TufRole.Snapshot)
                    | _ -> ()

                    if expired start snapshot.Signed.Expires then
                        return! Error(TufFailure.Expired TufRole.Snapshot)

                    // 5.5.7: the verified snapshot is trusted.
                    current.Value <- { current.Value with Snapshot = Some snapshot.Signed }

                    // 5.6: the targets the snapshot names.
                    let targetsMeta = snapshot.Signed.Targets
                    let! targetsBytes = fetched TufRole.Targets (fun () -> source.Targets targetsMeta.Version)

                    if targetsBytes.Length > TufMetadata.targetsLimit then
                        return! Error(TufFailure.MetadataTooLarge(TufRole.Targets, TufMetadata.targetsLimit))

                    do! linked TufRole.Targets targetsMeta targetsBytes
                    let! targets = TufMetadata.readTargets targetsBytes

                    if not (TufSignatures.thresholdMet root TufRole.Targets targets) then
                        return! Error(TufFailure.SignatureInvalid TufRole.Targets)

                    if targets.Signed.Version <> targetsMeta.Version then
                        return! Error(TufFailure.LinkMismatch TufRole.Targets)

                    match current.Value.Targets with
                    | Some old when targets.Signed.Version < old.Version -> return! Error(TufFailure.Rollback TufRole.Targets)
                    | _ -> ()

                    if expired start targets.Signed.Expires then
                        return! Error(TufFailure.Expired TufRole.Targets)

                    // 5.6.6: the verified targets are trusted.
                    current.Value <- { current.Value with Targets = Some targets.Signed }
                    return RefreshResult.Refreshed current.Value
            }

        // The boundary: an exception inside the workflow, which is a defect, fails the refresh closed and still hands
        // back the trust state accepted before it (review of #581, T1).
        let outcome =
            try
                steps ()
            with error ->
                Error(TufFailure.Internal $"the refresh failed with {error.GetType().Name}, a defect")

        match outcome with
        | Ok outcome -> outcome
        | Error failure -> RefreshResult.Failed(failure, current.Value)
/// What a trust anchor is for.
[<RequireQualifiedAccess>]
type TrustAnchorPurpose =
    /// A fixture or test repository, explicitly configured.
    | TestOnly
    /// A repository whose roots authenticate real releases. No production root exists in this build.
    | Production

/// Why a root was refused as a trust anchor.
[<RequireQualifiedAccess>]
type TrustAnchorRefusal =
    /// The root does not read, or does not meet its own root threshold.
    | Unreadable of failure: TufFailure
    /// A production root that lists a published TEST-ONLY fixture key.
    | TestOnlyKey of keyId: string
    /// A production root whose root threshold is below the two signatures the profile requires of the official
    /// repository.
    | RootThresholdBelowTwo

/// <summary>Loading the first trusted root, delivered out of band. A root must read and meet its own root threshold.
/// For production, its root threshold must be at least two, and it must list none of the published TEST-ONLY fixture
/// keys. No production root ships with this build.</summary>
/// <remarks>SECURITY: this code has not had a security review. It must not be used to authenticate a production
/// release until it has.</remarks>
module TrustAnchor =
    /// The Ed25519 public keys of the TEST-ONLY signed repository of the toolchain contracts. They are published with
    /// their seeds' labels, so anyone can sign with them.
    let testOnlyPublicKeys =
        set
            [ "f9db72b56a2dc2a43424eb6391d3821584c62194d2e539f748bafc9159e865a0"
              "c27cf17ad50b98201df9c5f35993ad472cf1dc45ac2678df17cdb2249900b173"
              "139a7bf84b988c484391e5b513b8ccc357ba531f042f6a6fef04949a9c1fa50c"
              "a0e75c13e50d16e9b8810b56a0364a1786d80ed5e04c2b18345571172801d3f1"
              "c7bbb4edb1d3835582259412da5a09e50393b90ed506afb539a615bfee833317" ]

    /// The trusted set the anchor starts, and the repository identity.
    let private loadOf (purpose: TrustAnchorPurpose) (bytes: byte array) : Result<TufTrustedSet * Sha256, TrustAnchorRefusal> =
        match TufMetadata.readRoot bytes with
        | Error failure -> Error(TrustAnchorRefusal.Unreadable failure)
        | Ok read when not (TufSignatures.thresholdMet read.Signed TufRole.Root read) ->
            Error(TrustAnchorRefusal.Unreadable(TufFailure.SignatureInvalid TufRole.Root))
        | Ok read ->
            let testKey =
                read.Signed.Keys |> Map.tryFindKey (fun _ publicKey -> testOnlyPublicKeys.Contains(Convert.ToHexStringLower publicKey))

            match purpose, testKey with
            | TrustAnchorPurpose.Production, _ when read.Signed.Roles[TufRole.Root].Threshold < 2 -> Error TrustAnchorRefusal.RootThresholdBelowTwo
            | TrustAnchorPurpose.Production, Some keyId -> Error(TrustAnchorRefusal.TestOnlyKey keyId)
            | _ ->
                Ok(
                    { Root = read.Signed
                      Timestamp = None
                      Snapshot = None
                      Targets = None },
                    TufMetadata.repositoryIdentity read
                )

    let load (purpose: TrustAnchorPurpose) (bytes: byte array) : Result<TufTrustedSet * Sha256, TrustAnchorRefusal> =
        try
            loadOf purpose bytes
        with error ->
            Error(TrustAnchorRefusal.Unreadable(TufFailure.Internal $"the anchor failed with {error.GetType().Name}, a defect"))
