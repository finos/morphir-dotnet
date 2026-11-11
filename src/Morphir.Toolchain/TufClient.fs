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

/// <summary>What a trust anchor is for, carried in every trusted set it starts.</summary>
[<RequireQualifiedAccess>]
type TrustAnchorPurpose =
    /// A fixture or test repository, explicitly configured.
    | TestOnly
    /// <summary>The repository whose identity the caller names, to authenticate real releases. A root is never
    /// production on the strength of the TEST-ONLY deny list and the threshold alone: it must be the repository named.
    /// Where a production identity comes from (pinned in the binary, or configured) is the maintainer's decision; no
    /// production identity ships with this build.</summary>
    | Production of repository: Sha256

/// Why a root was refused as a trust anchor.
[<RequireQualifiedAccess>]
type TrustAnchorRefusal =
    /// The root does not read, or does not meet its own root threshold.
    | Unreadable of failure: TufFailure
    /// A production root that is not the repository the caller named.
    | OtherRepository of actual: Sha256
    /// A production root that lists a published TEST-ONLY fixture key.
    | TestOnlyKey of keyId: string
    /// A production root whose root threshold is below the two signatures the profile requires of the official
    /// repository.
    | RootThresholdBelowTwo

/// <summary>The rules a root must meet for its purpose, at the anchor and at every rotation. For production: a root
/// threshold of at least two, and none of the published TEST-ONLY fixture keys.</summary>
/// <remarks>What the deny list does: it refuses the five public keys of this repository's TEST-ONLY signed fixture,
/// wherever a production root lists them. What it does not do: it does not refuse any other published test seed (this
/// repository has several), and it says nothing about which repository is meant. That is why production also needs
/// the repository identity the caller names.</remarks>
module TrustRules =
    /// The Ed25519 public keys of the TEST-ONLY signed repository of the toolchain contracts. They are published with
    /// their seeds' labels, so anyone can sign with them.
    let testOnlyPublicKeys =
        set
            [ "f9db72b56a2dc2a43424eb6391d3821584c62194d2e539f748bafc9159e865a0"
              "c27cf17ad50b98201df9c5f35993ad472cf1dc45ac2678df17cdb2249900b173"
              "139a7bf84b988c484391e5b513b8ccc357ba531f042f6a6fef04949a9c1fa50c"
              "a0e75c13e50d16e9b8810b56a0364a1786d80ed5e04c2b18345571172801d3f1"
              "c7bbb4edb1d3835582259412da5a09e50393b90ed506afb539a615bfee833317" ]

    /// Why a root breaks its purpose's rules, or None. The repository identity is checked at the anchor only.
    let refusal (purpose: TrustAnchorPurpose) (root: TufRoot) : TrustAnchorRefusal option =
        match purpose with
        | TrustAnchorPurpose.TestOnly -> None
        | TrustAnchorPurpose.Production _ ->
            if root.Roles[TufRole.Root].Threshold < 2 then
                Some TrustAnchorRefusal.RootThresholdBelowTwo
            else
                root.Keys
                |> Map.tryFindKey (fun _ publicKey -> testOnlyPublicKeys.Contains(Convert.ToHexStringLower publicKey))
                |> Option.map TrustAnchorRefusal.TestOnlyKey

/// <summary>The trusted metadata a refresh starts from and returns: always a root and the anchor's purpose, and the
/// last trusted timestamp, snapshot and targets once a refresh has verified them. A value in and a value out; keeping
/// it durable is the caller's.</summary>
type TufTrustedSet =
    { Purpose: TrustAnchorPurpose
      Root: TufRoot
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
    /// <summary>The timestamp is the trusted one, so the update cycle ends without an error (TUF 5.4.3.1). The trusted
    /// set holds any root accepted before it; its timestamp, snapshot and targets are unchanged and unexpired.</summary>
    | NothingNew of trusted: TufTrustedSet
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
        | NothingNew trusted
        | RootRotationPaused trusted
        | Failed(_, trusted) -> trusted

/// Defensive snapshots of the only mutable members of parsed trust state: public-key byte arrays.
module internal TufTrustedSnapshot =
    let copy (trusted: TufTrustedSet) =
        { trusted with
            Root =
                { trusted.Root with
                    Keys = trusted.Root.Keys |> Map.map (fun _ key -> if obj.ReferenceEquals(key, null) then key else Array.copy key) } }

/// One explicit input request. Bounds and links come from the existing metadata profile and accepted parent.
type internal TufMetadataRequest =
    { Role: TufRole
      Version: uint64 option
      MaxBytes: int
      Link: TufMetaFile option }

/// Exact accepted evidence, isolated from both the source and the checkpoint consumer.
/// Internal only: no persistence representation or production authority is defined here.
type internal TufAcceptedCheckpoint internal (role, version, envelope: byte array, accepted: TufTrustedSet) =
    let evidence = Array.copy envelope
    let state = TufTrustedSnapshot.copy accepted
    member _.Role: TufRole = role
    member _.Version: uint64 = version
    member _.EnvelopeBytes = Array.copy evidence
    member _.Accepted = TufTrustedSnapshot.copy state

[<RequireQualifiedAccess>]
type internal TufAcceptanceStep =
    | Request of request: TufMetadataRequest * supply: (Result<byte array option, TufFailure> -> TufAcceptanceStep)
    | Accepted of checkpoint: TufAcceptedCheckpoint * acknowledge: (unit -> TufAcceptanceStep)
    | Complete of result: RefreshResult
    | Refused of failure: TufFailure * accepted: TufTrustedSet

/// The single pure acceptance path, shared by synchronous and awaited drivers.
/// SECURITY: unused and unqualified for production. No I/O or durability is performed by this core.
module internal TufAcceptance =
    let maxRootRotations = 32

    let metadataName (role: TufRole) (version: uint64) =
        match role with
        | TufRole.Timestamp -> "timestamp.json"
        | other -> $"{version}.{TufMetadata.roleName other}.json"

    let private expired (start: DateTimeOffset) (expires: DateTimeOffset) = expires <= start

    let private roleKeys (root: TufRoot) (role: TufRole) =
        root.Roles[role].KeyIds |> List.map (fun id -> Convert.ToHexStringLower root.Keys[id]) |> set, root.Roles[role].Threshold

    let private linked (role: TufRole) (meta: TufMetaFile) (bytes: byte array) =
        if uint64 bytes.LongLength <> meta.Length || Sha256.ofBytes bytes <> meta.Sha256 then
            Error(TufFailure.LinkMismatch role)
        else
            Ok()

    // A caller may retain a continuation and invoke it again after observing a terminal branch.
    // Terminal state must not expose the mutable key buffers still owned by that continuation.
    let private refused (failure, current) =
        TufAcceptanceStep.Refused(failure, TufTrustedSnapshot.copy current)

    let private complete (result: RefreshResult) =
        let snapshot = TufTrustedSnapshot.copy result.Trusted
        let detached =
            match result with
            | RefreshResult.Refreshed _ -> RefreshResult.Refreshed snapshot
            | RefreshResult.NothingNew _ -> RefreshResult.NothingNew snapshot
            | RefreshResult.RootRotationPaused _ -> RefreshResult.RootRotationPaused snapshot
            | RefreshResult.Failed(failure, _) -> RefreshResult.Failed(failure, snapshot)
        TufAcceptanceStep.Complete detached
    let private protect current action =
        try action ()
        with error -> refused (TufFailure.Internal $"the refresh failed with {error.GetType().Name}, a defect", current)

    // A refused oversized input is never copied. Null stays on the existing reader/checker's refusal path.
    // Accepted evidence is frozen before parsing, so it is the exact envelope the checker read.
    let private freeze role (bytes: byte array) =
        if obj.ReferenceEquals(bytes, null) || bytes.Length > TufMetadata.limitOf role then bytes else Array.copy bytes

    let private request current role version link consume =
        TufAcceptanceStep.Request(
            { Role = role; Version = version; MaxBytes = TufMetadata.limitOf role; Link = link },
            fun response -> protect current (fun () -> consume response))

    let private required current role version link consume =
        request current role version link (function
            | Error failure -> refused (failure, current)
            | Ok None -> refused (TufFailure.Unavailable role, current)
            | Ok(Some bytes) -> consume (freeze role bytes))

    let private accepted role version bytes current next =
        let checkpoint = TufAcceptedCheckpoint(role, version, bytes, current)
        TufAcceptanceStep.Accepted(checkpoint, fun () -> protect current (fun () -> next current))

    let private checkedStep current check next =
        match check () with
        | Error failure -> refused (failure, current)
        | Ok value -> next value

    let start (start: DateTimeOffset) (trusted: TufTrustedSet) : TufAcceptanceStep =
        protect trusted (fun () ->
            // Continuations own their trust state. Neither the input caller nor a checkpoint consumer can mutate keys.
            let initial = TufTrustedSnapshot.copy trusted

            let targets (current: TufTrustedSet) (snapshot: TufSnapshot) =
                let meta = snapshot.Targets
                required current TufRole.Targets (Some meta.Version) (Some meta) (fun bytes ->
                    checkedStep current (fun () -> result {
                        if bytes.Length > TufMetadata.targetsLimit then
                            return! Error(TufFailure.MetadataTooLarge(TufRole.Targets, TufMetadata.targetsLimit))
                        do! linked TufRole.Targets meta bytes
                        let! read = TufMetadata.readTargets bytes
                        if not (TufSignatures.thresholdMet current.Root TufRole.Targets read) then
                            return! Error(TufFailure.SignatureInvalid TufRole.Targets)
                        if read.Signed.Version <> meta.Version then
                            return! Error(TufFailure.LinkMismatch TufRole.Targets)
                        match current.Targets with
                        | Some old when read.Signed.Version < old.Version -> return! Error(TufFailure.Rollback TufRole.Targets)
                        | _ -> ()
                        if expired start read.Signed.Expires then
                            return! Error(TufFailure.Expired TufRole.Targets)
                        return read.Signed
                    }) (fun read ->
                        // 5.6.6: no completion until this accepted evidence is acknowledged.
                        accepted TufRole.Targets read.Version bytes { current with Targets = Some read }
                            (RefreshResult.Refreshed >> complete)))

            let snapshot (current: TufTrustedSet) (timestamp: TufTimestamp) =
                let meta = timestamp.Snapshot
                required current TufRole.Snapshot (Some meta.Version) (Some meta) (fun bytes ->
                    checkedStep current (fun () -> result {
                        if bytes.Length > TufMetadata.snapshotLimit then
                            return! Error(TufFailure.MetadataTooLarge(TufRole.Snapshot, TufMetadata.snapshotLimit))
                        do! linked TufRole.Snapshot meta bytes
                        let! read = TufMetadata.readSnapshot bytes
                        if not (TufSignatures.thresholdMet current.Root TufRole.Snapshot read) then
                            return! Error(TufFailure.SignatureInvalid TufRole.Snapshot)
                        if read.Signed.Version <> meta.Version then
                            return! Error(TufFailure.LinkMismatch TufRole.Snapshot)
                        match current.Snapshot with
                        | Some old when read.Signed.Targets.Version < old.Targets.Version ->
                            return! Error(TufFailure.Rollback TufRole.Snapshot)
                        | _ -> ()
                        if expired start read.Signed.Expires then
                            return! Error(TufFailure.Expired TufRole.Snapshot)
                        return read.Signed
                    }) (fun read ->
                        // 5.5.7: snapshot evidence is acknowledged before requesting targets.
                        accepted TufRole.Snapshot read.Version bytes { current with Snapshot = Some read }
                            (fun acknowledged -> targets acknowledged read)))

            let timestamp (current: TufTrustedSet) =
                required current TufRole.Timestamp None None (fun bytes ->
                    checkedStep current (fun () -> result {
                        let! read = TufMetadata.readTimestamp bytes
                        if not (TufSignatures.thresholdMet current.Root TufRole.Timestamp read) then
                            return! Error(TufFailure.SignatureInvalid TufRole.Timestamp)
                        match current.Timestamp with
                        | Some old when read.Signed.Version < old.Version || read.Signed.Snapshot.Version < old.Snapshot.Version ->
                            return! Error(TufFailure.Rollback TufRole.Timestamp)
                        | _ -> ()
                        let sameVersion =
                            match current.Timestamp with
                            | Some old when read.Signed.Version = old.Version -> Some old
                            | _ -> None
                        match sameVersion with
                        | Some old when read.Signed <> old -> return! Error(TufFailure.VersionReused TufRole.Timestamp)
                        | _ -> ()
                        if expired start read.Signed.Expires then
                            return! Error(TufFailure.Expired TufRole.Timestamp)
                        return read.Signed, sameVersion
                    }) (fun (read, sameVersion) ->
                        // 5.4.5: even a same-version accepted timestamp is acknowledged before the cycle ends.
                        accepted TufRole.Timestamp read.Version bytes { current with Timestamp = Some read } (fun acknowledged ->
                            let unchanged =
                                match sameVersion, acknowledged.Snapshot, acknowledged.Targets with
                                | Some _, Some snapshot, Some targets ->
                                    snapshot.Version = read.Snapshot.Version && targets.Version = snapshot.Targets.Version
                                | _ -> false
                            if unchanged then
                                let snapshot, targets = Option.get acknowledged.Snapshot, Option.get acknowledged.Targets
                                if expired start snapshot.Expires then
                                    refused (TufFailure.Expired TufRole.Snapshot, acknowledged)
                                elif expired start targets.Expires then
                                    refused (TufFailure.Expired TufRole.Targets, acknowledged)
                                else complete(RefreshResult.NothingNew acknowledged)
                            else snapshot acknowledged read)))

            let afterRoots (current: TufTrustedSet) =
                if expired start current.Root.Expires then refused (TufFailure.Expired TufRole.Root, current)
                else timestamp current

            let rec rotate (current: TufTrustedSet) count =
                if current.Root.Version >= TufMetadata.maxInteger then afterRoots current else
                let version = current.Root.Version + 1UL
                request current TufRole.Root (Some version) None (function
                    | Error failure -> refused (failure, current)
                    | Ok None -> afterRoots current
                    // Preserve 5.3.3: detect presence of the 33rd root without parsing or copying it.
                    | Ok(Some _) when count = maxRootRotations -> complete(RefreshResult.RootRotationPaused current)
                    | Ok(Some supplied) ->
                        let bytes = freeze TufRole.Root supplied
                        checkedStep current (fun () ->
                            match TufMetadata.readRoot bytes with
                            | Error(TufFailure.MetadataInvalid(_, reason)) -> Error(TufFailure.RootRotationInvalid(version, reason))
                            | Error(TufFailure.MetadataTooLarge(_, limit)) ->
                                Error(TufFailure.RootRotationInvalid(version, $"{metadataName TufRole.Root version} is larger than {limit} bytes"))
                            | Error failure -> Error(TufFailure.RootRotationInvalid(version, $"%A{failure}"))
                            | Ok read ->
                                // 5.3.4/5.3.5: old and new thresholds precede the version check.
                                if not (TufSignatures.thresholdMet current.Root TufRole.Root read) then
                                    Error(TufFailure.RootRotationInvalid(version, "the trusted root's threshold is not met"))
                                elif not (TufSignatures.thresholdMet read.Signed TufRole.Root read) then
                                    Error(TufFailure.RootRotationInvalid(version, "the new root's own threshold is not met"))
                                elif read.Signed.Version <> version then
                                    Error(TufFailure.RootRotationInvalid(version, $"{metadataName TufRole.Root version} has version {read.Signed.Version}"))
                                else
                                    match TrustRules.refusal initial.Purpose read.Signed with
                                    | Some refusal -> Error(TufFailure.RootRotationInvalid(version, $"the new root breaks the anchor's purpose: %A{refusal}"))
                                    | None -> Ok read.Signed)
                            (fun root ->
                                // Preserve comparison with the refresh's initial root and irreversible role invalidation.
                                let rotatedKeys =
                                    roleKeys root TufRole.Timestamp <> roleKeys initial.Root TufRole.Timestamp
                                    || roleKeys root TufRole.Snapshot <> roleKeys initial.Root TufRole.Snapshot
                                let next =
                                    if rotatedKeys then { current with Root = root; Timestamp = None; Snapshot = None; Targets = None }
                                    else { current with Root = root }
                                accepted TufRole.Root root.Version bytes next (fun acknowledged -> rotate acknowledged (count + 1))))
            rotate initial 0)

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
    let maxRootRotations = TufAcceptance.maxRootRotations

    /// <summary>The consistent-snapshot name of a metadata file: <c>N.root.json</c>, <c>timestamp.json</c>,
    /// <c>N.snapshot.json</c> or <c>N.targets.json</c>.</summary>
    let metadataName role version = TufAcceptance.metadataName role version

    /// <summary>The consistent-snapshot name of a target: its SHA-256 before the file name, in the same directory,
    /// always with <c>/</c>.</summary>
    let consistentTargetPath (path: string) (sha256: Sha256) =
        match path.LastIndexOf '/' with
        | -1 -> $"{sha256.Text}.{path}"
        | at -> $"{path.Substring(0, at + 1)}{sha256.Text}.{path.Substring(at + 1)}"

    /// <summary>Compatibility driver of the same pure acceptance core. Checkpoints are acknowledged immediately;
    /// this synchronous API does not promise durability. It retains the newest accepted state on every failure.</summary>
    let refresh (start: DateTimeOffset) (trusted: TufTrustedSet) (source: MetadataSource) : RefreshResult =
        let rec drive step =
            match step with
            | TufAcceptanceStep.Request(request, supply) ->
                let response =
                    try
                        match request.Role, request.Version with
                        | TufRole.Root, Some version -> Ok(source.Root version)
                        | TufRole.Timestamp, None -> Ok(source.Timestamp())
                        | TufRole.Snapshot, Some version -> Ok(source.Snapshot version)
                        | TufRole.Targets, Some version -> Ok(source.Targets version)
                        | _ -> Error(TufFailure.Internal "an invalid metadata request, a defect")
                    with _ -> Error(TufFailure.Unavailable request.Role)
                drive (supply response)
            | TufAcceptanceStep.Accepted(_, acknowledge) -> drive (acknowledge ())
            | TufAcceptanceStep.Complete result -> result
            | TufAcceptanceStep.Refused(failure, accepted) -> RefreshResult.Failed(failure, accepted)
        drive (TufAcceptance.start start trusted)
/// <summary>Loading the first trusted root, delivered out of band. A root must read and meet its own root threshold.
/// For production, it must be the repository the caller names, and meet <c>TrustRules</c>: a root threshold of at
/// least two and none of the published TEST-ONLY fixture keys. Every root the refresh later accepts must meet the same
/// rules. No production identity ships with this build.</summary>
/// <remarks>SECURITY: this code has not had a security review. It must not be used to authenticate a production
/// release until it has.</remarks>
module TrustAnchor =
    /// The Ed25519 public keys of the TEST-ONLY signed repository of the toolchain contracts. They are published with
    /// their seeds' labels, so anyone can sign with them.
    let testOnlyPublicKeys = TrustRules.testOnlyPublicKeys

    /// The trusted set the anchor starts, and the repository identity.
    let private loadOf (purpose: TrustAnchorPurpose) (bytes: byte array) : Result<TufTrustedSet * Sha256, TrustAnchorRefusal> =
        match TufMetadata.readRoot bytes with
        | Error failure -> Error(TrustAnchorRefusal.Unreadable failure)
        | Ok read when not (TufSignatures.thresholdMet read.Signed TufRole.Root read) ->
            Error(TrustAnchorRefusal.Unreadable(TufFailure.SignatureInvalid TufRole.Root))
        | Ok read ->
            let identity = TufMetadata.repositoryIdentity read

            match purpose with
            | TrustAnchorPurpose.Production named when identity <> named -> Error(TrustAnchorRefusal.OtherRepository identity)
            | _ ->
            match TrustRules.refusal purpose read.Signed with
            | Some refusal -> Error refusal
            | None ->
                Ok(
                    { Purpose = purpose
                      Root = read.Signed
                      Timestamp = None
                      Snapshot = None
                      Targets = None },
                    identity
                )

    let load (purpose: TrustAnchorPurpose) (bytes: byte array) : Result<TufTrustedSet * Sha256, TrustAnchorRefusal> =
        try
            loadOf purpose bytes
        with error ->
            Error(TrustAnchorRefusal.Unreadable(TufFailure.Internal $"the anchor failed with {error.GetType().Name}, a defect"))
