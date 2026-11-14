// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Collections.Generic
open Morphir.Toolchain
open Morphir.Mep

module internal PackagePaths =
    let read code (value: string) =
        let bad () =
            Operations.fail code $"Not a portable package path: {value}"

        if
            value.Length = 0
            || value.Length > 120
            || value
               |> Seq.exists (fun c ->
                   c < ' '
                   || c > '~'
                   || "<>:\"|?*\\".Contains c
               )
        then
            bad ()

        let parts = value.Split '/'

        if parts.Length > 16 then
            bad ()

        for part in parts do
            if
                part.Length = 0
                || part = "."
                || part = ".."
                || part.EndsWith '.'
                || part.EndsWith ' '
            then
                bad ()

            let stem = ((part.Split('.'))[0]).ToUpperInvariant()

            if
                [ "CON"; "PRN"; "AUX"; "NUL"; "CONIN$"; "CONOUT$" ]
                |> List.contains stem
                || (stem.Length = 4
                    && (stem.StartsWith "COM"
                        || stem.StartsWith "LPT")
                    && stem[3]
                       >= '1'
                    && stem[3]
                       <= '9')
            then
                bad ()

        match DistributionIdentifiers.ParseDeclaredPath value with
        | Ok path -> path
        | Error _ ->
            bad ()
            Unchecked.defaultof<TargetPath>

    let result work =
        try
            Ok(work ())
        with InstallationException failure ->
            Error failure

type internal StateEntry = {
    Json: Value
    Id: string
    Version: SemVer
    Runtime: ArtifactRuntime
    Platform: Value option
    Args: string list
    /// The archive or module digest; None for a nupkg package, whose tree digest is its only digest.
    Digest: Sha256Digest option
    Claims: ReleaseClaims
    Index: Value
    Package: Value option
    StorePath: string option
    HostRequirements: Comparator list
}

type internal StateLock = {
    Entry: StateEntry
    Json: Value
    Bytes: byte array
}

type internal StateCatalog = {
    Entries: StateEntry list
    Json: Value
    Bytes: byte array
}

type internal StateJournal = {
    NextLock: StateLock option
    NextCatalog: StateCatalog option
    Bytes: byte array
}

module internal StateDocuments =
    open SharedJson

    let schemaUrl kind =
        $"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/{kind}.schema.json"

    let private stringValue path name items =
        required path name items
        |> text $"{path}.{name}"

    let private nonemptyValue path name items =
        required path name items
        |> nonempty $"{path}.{name}"

    let private field path name items = required path name items

    let private choices path values value =
        let value = text path value

        if not (List.contains value values) then
            invalid path "Unsupported value."

        value

    let private closed path allowed items =
        for name, _ in items do
            if not (Set.contains name allowed) then
                invalid $"{path}.{name}" "Unknown member in a closed document."

    let rec private closedTree path prefix known value =
        match value with
        | Value.Object items ->
            for name, child in items do
                let memberPath =
                    if prefix = "" then
                        name
                    else
                        prefix
                        + "."
                        + name

                if not (Set.contains memberPath known) then
                    invalid $"{path}.{memberPath}" "Unknown member in closed claims."

                closedTree path memberPath known child
        | Value.Array items ->
            for child in items do
                closedTree path prefix known child
        | _ -> ()

    let rec private projection prefix known value =
        match value with
        | Value.Object items ->
            Value.Object [
                for name, child in items do
                    let memberPath =
                        if prefix = "" then
                            name
                        else
                            prefix
                            + "."
                            + name

                    if Set.contains memberPath known then
                        yield name, projection memberPath known child
            ]
        | Value.Array items -> Value.Array(List.map (projection prefix known) items)
        | value -> value

    let private equal code path left right =
        match difference path left right with
        | Some memberPath -> fail code memberPath "Bound values differ."
        | None -> ()

    let private protectRead name bytes action =
        read name bytes
        |> Result.bind (fun value ->
            try
                protect (fun () -> action value)
            with InstallationException error ->
                Error error
        )

    let private portable path value =
        parsed path DistributionIdentifiers.ParseDeclaredPath value
        |> fun path -> path.Text

    let private absolute path value =
        let value = nonempty path value

        if
            value.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || not (
                value.StartsWith('/')
                || (value.Length > 2
                    && Char.IsAsciiLetterUpper value[0]
                    && value[1] = ':'
                    && value[2] = '\\')
                || value.StartsWith("\\\\", StringComparison.Ordinal)
            )
        then
            invalid path "Expected ordinary absolute canonical repository spelling."

        if
            value.Length > 3
            && (value.EndsWith('/')
                || value.EndsWith('\\'))
        then
            invalid path "Canonical root has no trailing separator."

        value

    let requirements path items =
        match optional "requires" items with
        | None -> []
        | Some value ->
            let fields =
                members
                    (path
                     + ".requires")
                    value

            let hosts =
                required
                    (path
                     + ".requires")
                    "host"
                    fields
                |> array (
                    path
                    + ".requires.host"
                )

            if hosts.IsEmpty then
                invalid path "Host requirements must not be empty."

            let critical =
                optional "critical" items
                |> Option.map (
                    strings (
                        path
                        + ".critical"
                    )
                )
                |> Option.defaultValue []

            if not (List.contains "requires.host" critical) then
                invalid path "requires.host must be critical."

            hosts
            |> List.map (
                parsed
                    (path
                     + ".requires.host")
                    Comparator.tryParse
            )

    let private runtime path value =
        match choices path [ "process"; "wasm" ] value with
        | "process" -> ArtifactRuntime.Process
        | _ -> ArtifactRuntime.Wasm

    let private platform path closedContext value =
        let items = members path value

        if closedContext then
            closed path (set [ "os"; "arch" ]) items

        let os = stringValue path "os" items
        let arch = stringValue path "arch" items

        if
            not (Text.RegularExpressions.Regex.IsMatch(os, "^[a-z]([a-z0-9-]*[a-z0-9])?$"))
            || not (Text.RegularExpressions.Regex.IsMatch(arch, "^[a-z0-9_]+$"))
        then
            invalid path "Invalid platform tokens."

        value

    let package path closedContext value =
        let items = members path value

        if closedContext then
            closed path (set [ "format"; "root"; "entryPoint"; "executables"; "treeSha256" ]) items

        let format =
            required path "format" items
            |> choices
                (path
                 + ".format")
                [ "zip"; "tar.gz"; "nupkg" ]

        if format = "nupkg" && Option.isNone (optional "root" items) then
            invalid (path + ".root") "A nupkg package requires root."

        let entry =
            stringValue path "entryPoint" items
            |> PackagePaths.read "package.path"

        optional "root" items
        |> Option.iter (fun value ->
            text
                (path
                 + ".root")
                value
            |> PackagePaths.read "package.path"
            |> ignore
        )

        optional "executables" items
        |> Option.iter (fun value ->
            strings
                (path
                 + ".executables")
                value
            |> distinct (
                path
                + ".executables"
            )
            |> List.iter (fun value ->
                PackagePaths.read "package.path" value
                |> ignore
            )
        )

        required path "treeSha256" items
        |> parsed
            (path
             + ".treeSha256")
            Sha256Digest.Parse
        |> ignore

        entry

    let private provenance path closedContext claimsRequired value =
        SourceDocuments.readProvenance path closedContext claimsRequired absolute value
        |> ignore

        value

    let private packagePaths =
        set [
            "package"
            "package.format"
            "package.root"
            "package.entryPoint"
            "package.executables"
            "package.treeSha256"
        ]

    let private entryPaths =
        set [
            "extensionId"
            "name"
            "version"
            "runtime"
            "platform"
            "platform.os"
            "platform.arch"
            "args"
            "digest"
            "storePath"
            "capabilities"
            "mepVersions"
            "index"
            "index.kind"
            "index.identity"
            "index.repository"
            "index.tag"
            "index.feed"
            "index.id"
            "index.version"
            "index.revision"
            "frontend"
            "backend"
            "executable"
            "claims"
            "claimCheck"
            "probeSource"
            "critical"
            "requires"
            "requires.host"
        ]
        |> Set.union packagePaths
        |> Set.union (
            Claims.knownPaths
            |> Set.map (fun path ->
                "claims."
                + path
            )
        )
        |> Set.union (
            Claims.knownPaths
            |> Set.filter (fun path ->
                path.StartsWith("capabilities.frontend", StringComparison.Ordinal)
                || path.StartsWith("capabilities.backend", StringComparison.Ordinal)
            )
            |> Set.map (fun path -> path.Substring("capabilities.".Length))
        )

    let private entry path isLock value =
        let items = members path value

        let common =
            set [
                "extensionId"
                "name"
                "version"
                "runtime"
                "platform"
                "args"
                "digest"
                "capabilities"
                "mepVersions"
                "index"
                "frontend"
                "backend"
                "executable"
                "package"
            ]

        if isLock then
            closed
                path
                (common
                 |> Set.union (set [ "$schema"; "selection"; "source" ]))
                items

        let id =
            required path "extensionId" items
            |> parsed
                (path
                 + ".extensionId")
                Morphir.Extensions.ExtensionId.Parse

        let version =
            required path "version" items
            |> parsed
                (path
                 + ".version")
                SemVer.tryParse

        let name = nonemptyValue path "name" items

        let runtime =
            required path "runtime" items
            |> runtime (
                path
                + ".runtime"
            )

        let platform =
            optional "platform" items
            |> Option.map (
                platform
                    (path
                     + ".platform")
                    isLock
            )

        let args =
            required path "args" items
            |> strings (
                path
                + ".args"
            )

        // A nupkg package has no archive digest: its tree digest is its only digest.
        let isNupkg =
            match optional "package" items with
            | Some(Value.Object packageItems) -> optional "format" packageItems = Some(Value.String "nupkg")
            | _ -> false

        let digest =
            match isNupkg, optional "digest" items with
            | true, Some _ -> invalid (path + ".digest") "A nupkg package has no archive digest."
            | true, None -> None
            | false, _ ->
                required path "digest" items
                |> parsed
                    (path
                     + ".digest")
                    Sha256Digest.Parse
                |> Some

        let executable =
            required path "executable" items
            |> boolean (
                path
                + ".executable"
            )

        let packageValue = optional "package" items

        match runtime, platform with
        | ArtifactRuntime.Process, None -> invalid path "Process requires platform."
        | ArtifactRuntime.Wasm, Some _ -> invalid path "WASM has no platform."
        | _ -> ()

        if
            runtime = ArtifactRuntime.Wasm
            && (args <> []
                || executable
                || packageValue.IsSome)
        then
            invalid path "WASM requires empty args, executable=false and no package."

        packageValue
        |> Option.iter (
            package
                (path
                 + ".package")
                isLock
            >> ignore
        )

        let index =
            required path "index" items
            |> provenance
                (path
                 + ".index")
                isLock
                isLock

        let claimsValue =
            if isLock then
                required
                    (path
                     + ".index")
                    "claims"
                    (members
                        (path
                         + ".index")
                        index)
            else
                required path "claims" items

        if isLock then
            closedTree
                (path
                 + ".index.claims")
                ""
                Claims.knownPaths
                claimsValue

        let claims =
            Claims.readEmbedded
                (path
                 + ".claims")
                claimsValue

        if
            claims.Extension.Id
            <> id.Value
            || claims.Extension.Version
               <> version.Text
        then
            fail "extension.claims_mismatch" path "Claims identity differs from installed identity."

        let claimItems = members "claims" claims.Projection

        let extension =
            required "claims" "extension" claimItems
            |> members "claims.extension"

        if
            name
            <> stringValue "claims.extension" "name" extension
        then
            fail "extension.claims_mismatch" path "Display name differs from claims."

        equal
            "extension.claims_mismatch"
            (path
             + ".capabilities")
            (required path "capabilities" items)
            (required "claims.extension" "types" extension)

        equal
            "extension.claims_mismatch"
            (path
             + ".mepVersions")
            (required path "mepVersions" items)
            (required "claims" "protocolVersions" claimItems)

        let capabilities =
            required "claims" "capabilities" claimItems
            |> members "claims.capabilities"

        for kind in [ "frontend"; "backend" ] do
            match optional kind capabilities, optional kind items with
            | Some expected, Some actual ->
                if isLock then
                    closedTree
                        path
                        ("capabilities."
                         + kind)
                        Claims.knownPaths
                        actual

                equal
                    "extension.claims_mismatch"
                    (path
                     + "."
                     + kind)
                    expected
                    (projection
                        ("capabilities."
                         + kind)
                        Claims.knownPaths
                        actual)
            | None, None -> ()
            | _ ->
                fail
                    "extension.claims_mismatch"
                    (path
                     + "."
                     + kind)
                    "Capability summary differs from claims."

        let storePath =
            if isLock then
                None
            else
                Some(
                    required path "storePath" items
                    |> portable (
                        path
                        + ".storePath"
                    )
                )

        if not isLock then
            Claims.readCritical path entryPaths items
            |> ignore

            let check =
                required path "claimCheck" items
                |> choices
                    (path
                     + ".claimCheck")
                    [ "probed"; "unchecked" ]

            optional "probeSource" items
            |> Option.iter (fun source ->
                choices
                    (path
                     + ".probeSource")
                    [ "describe"; "session-fallback" ]
                    source
                |> ignore

                if
                    check
                    <> "probed"
                then
                    invalid path "Unchecked claims cannot have a probe source."
            )

            match packageValue, storePath with
            | Some packageValue, Some storePath ->
                let p = members "package" packageValue
                let tree = stringValue "package" "treeSha256" p
                let entry = stringValue "package" "entryPoint" p

                if
                    storePath
                    <> $"store/extensions/packages/sha256/{tree}/tree/{entry}"
                then
                    fail
                        "extension.lock_mismatch"
                        path
                        "Package storePath does not bind the canonical tree and entry point."
            | None, Some storePath when
                not (storePath.StartsWith("store/", StringComparison.Ordinal))
                ->
                invalid path "Artifact store path is outside store."
            | _ -> ()

        {
            Json = value
            Id = id.Value
            Version = version
            Runtime = runtime
            Platform = platform
            Args = args
            Digest = digest
            Claims = claims
            Index = index
            Package = packageValue
            StorePath = storePath
            HostRequirements = requirements path items
        }

    let readCatalog bytes =
        protectRead
            "catalog/extensions.json"
            bytes
            (fun value ->
                let items = members "catalog" value
                schema "catalog" (schemaUrl "catalog") items

                let entries =
                    required "catalog" "extensions" items
                    |> array "catalog.extensions"
                    |> List.mapi (fun index value ->
                        entry $"catalog.extensions[{index + 1}]" false value
                    )

                let mutable previous: string option = None

                for item in entries do
                    match previous with
                    | Some id when id = item.Id ->
                        fail "extension.duplicate_id" "catalog.extensions" $"Duplicate id {id}."
                    | Some id when StringComparer.Ordinal.Compare(id, item.Id) > 0 ->
                        fail
                            "extension.unsorted_catalog"
                            "catalog.extensions"
                            "Catalog ids are out of order."
                    | _ -> ()

                    previous <- Some item.Id

                let known =
                    set [ "$schema"; "extensions"; "critical" ]
                    |> Set.union (
                        entryPaths
                        |> Set.map (fun name ->
                            "extensions."
                            + name
                        )
                    )

                let critical = Claims.readCritical "catalog" known items

                if
                    entries
                    |> List.exists (fun entry -> entry.Package.IsSome)
                    && not (List.contains "extensions.package" critical)
                then
                    fail
                        "package.not_critical"
                        "catalog.critical"
                        "extensions.package must be critical."

                {
                    Entries = entries
                    Json = value
                    Bytes = Array.copy bytes
                }
            )

    let readLock bytes =
        protectRead
            "extension.lock"
            bytes
            (fun value ->
                let items = members "lock" value
                schema "lock" (schemaUrl "lock") items
                let entry = entry "lock" true value

                let selection =
                    required "lock" "selection" items
                    |> members "lock.selection"

                closed "lock.selection" (set [ "kind"; "value" ]) selection

                let kind =
                    required "lock.selection" "kind" selection
                    |> choices "lock.selection.kind" [ "exact"; "channel" ]

                let requested = required "lock.selection" "value" selection

                if kind = "exact" then
                    let selected = parsed "lock.selection.value" SemVer.tryParse requested

                    if
                        selected.Text
                        <> entry.Version.Text
                    then
                        fail
                            "extension.lock_mismatch"
                            "lock.selection"
                            "Exact version differs from lock version."
                else
                    parsed "lock.selection.value" Token.tryParse requested
                    |> ignore

                let source =
                    required "lock" "source" items
                    |> SourceDocuments.readSource "lock.source" true

                let provenance =
                    SourceDocuments.readProvenance "lock.index" true true absolute entry.Index

                SourceDocuments.checkPair "lock" provenance source

                {
                    Entry = entry
                    Json = value
                    Bytes = Array.copy bytes
                }
            )

    let readIndex bytes index =
        protectRead
            "index-record"
            bytes
            (fun value ->
                let items = members "index-record" value
                schema "index-record" (schemaUrl "index-record") items

                let id =
                    required "index-record" "id" items
                    |> parsed "index-record.id" Morphir.Extensions.ExtensionId.Parse

                let version =
                    required "index-record" "version" items
                    |> parsed "index-record.version" SemVer.tryParse

                let name =
                    required "index-record" "name" items
                    |> nonempty "index-record.name"

                required "index-record" "channels" items
                |> strings "index-record.channels"
                |> List.iter (fun channel ->
                    if channel.Length = 0 then
                        invalid "channels" "Empty channel."
                )

                let artifacts =
                    required "index-record" "artifacts" items
                    |> array "index-record.artifacts"

                if List.isEmpty artifacts then
                    invalid "index-record.artifacts" "Expected at least one artifact."

                let known =
                    set [
                        "$schema"
                        "id"
                        "name"
                        "version"
                        "channels"
                        "artifacts"
                        "critical"
                        "requires"
                        "requires.host"
                        "mepVersions"
                        "capabilities"
                        "frontend"
                        "backend"
                    ]
                    |> Set.union (
                        entryPaths
                        |> Set.map (fun name ->
                            "artifacts."
                            + name
                        )
                    )
                    |> Set.union (
                        set [
                            "artifacts.filename"
                            "artifacts.sha256"
                            "artifacts.source"
                            yield! SourceDocuments.sourcePaths "artifacts.source"
                        ]
                    )

                let critical = Claims.readCritical "index-record" known items

                for artifact in artifacts do
                    let fields = members "artifact" artifact
                    let claimValue = required "artifact" "claims" fields
                    let claims = Claims.readEmbedded "artifact.claims" claimValue

                    let extension =
                        required "claims" "extension" (members "claims" claims.Json)
                        |> members "claims.extension"

                    let caps =
                        required "claims" "capabilities" (members "claims" claims.Json)
                        |> members "claims.capabilities"

                    let filename =
                        required "artifact" "filename" fields
                        |> parsed "artifact.filename" DistributionIdentifiers.ParseArtifactFileName

                    let digest =
                        required "artifact" "sha256" fields
                        |> parsed "artifact.sha256" Sha256Digest.Parse

                    let packageValue = optional "package" fields

                    if
                        packageValue.IsSome
                        && not (List.contains "artifacts.package" critical)
                    then
                        fail
                            "package.not_critical"
                            "index-record"
                            "artifacts.package must be critical."

                    let runtime =
                        required "artifact" "runtime" fields
                        |> runtime "artifact.runtime"

                    let lower = filename.Text.ToLowerInvariant()

                    match packageValue with
                    | Some packageValue ->
                        if
                            runtime
                            <> ArtifactRuntime.Process
                        then
                            fail
                                "package.runtime"
                                "artifact"
                                "Only process artifacts have packages."

                        let packageItems = members "package" packageValue
                        let format = stringValue "package" "format" packageItems

                        if
                            not (
                                (format = "zip"
                                 && lower.EndsWith ".zip")
                                || (format = "tar.gz"
                                    && (lower.EndsWith ".tar.gz"
                                        || lower.EndsWith ".tgz"))
                            )
                        then
                            fail
                                "package.archive_format"
                                "artifact"
                                "Package format differs from archive filename."
                    | None when
                        runtime = ArtifactRuntime.Process
                        && [ ".zip"; ".tar.gz"; ".tgz"; ".tar" ]
                           |> List.exists lower.EndsWith
                        ->
                        fail
                            "package.archive_without_package"
                            "artifact"
                            "Process archive requires package metadata."
                    | None -> ()

                    required "artifact" "source" fields
                    |> SourceDocuments.readSource "source" false
                    |> ignore

                    let storePath =
                        match packageValue with
                        | Some packageValue ->
                            let p = members "package" packageValue
                            let tree = stringValue "package" "treeSha256" p
                            let entryPoint = stringValue "package" "entryPoint" p
                            $"store/extensions/packages/sha256/{tree}/tree/{entryPoint}"
                        | None -> $"store/extensions/sha256/{digest.Value}/{filename.Text}"

                    let selected =
                        Value.Object(
                            [
                                "extensionId", Value.String id.Value
                                "name", Value.String name
                                "version", Value.String version.Text
                                "runtime", required "artifact" "runtime" fields
                                "args", required "artifact" "args" fields
                                "digest", Value.String digest.Value
                                "executable", required "artifact" "executable" fields
                                "claims", claimValue
                                "index", index
                                "storePath", Value.String storePath
                                "claimCheck", required "artifact" "claimCheck" fields
                                "capabilities",
                                optional "capabilities" items
                                |> Option.defaultValue (required "extension" "types" extension)
                                "mepVersions",
                                optional "mepVersions" items
                                |> Option.defaultValue (
                                    required
                                        "claims"
                                        "protocolVersions"
                                        (members "claims" claims.Json)
                                )
                            ]
                            @ [
                                for key in [ "platform"; "package"; "probeSource" ] do
                                    match optional key fields with
                                    | Some value -> yield key, value
                                    | None -> ()
                            ]
                            @ [
                                for kind in [ "frontend"; "backend" ] do
                                    match
                                        optional kind items
                                        |> Option.orElseWith (fun () -> optional kind caps)
                                    with
                                    | Some value -> yield kind, value
                                    | None -> ()
                            ]
                        )

                    entry "index-record.artifact" false selected
                    |> ignore

                value
            )

    let bind (entry: StateEntry) (locked: StateLock) =
        protect (fun () ->
            let catalog = members "catalog.entry" entry.Json
            let lockItems = members "lock" locked.Json

            for name in
                [
                    "extensionId"
                    "name"
                    "version"
                    "runtime"
                    "platform"
                    "args"
                    "digest"
                    "capabilities"
                    "mepVersions"
                    "frontend"
                    "backend"
                    "executable"
                    "package"
                ] do
                match optional name catalog, optional name lockItems with
                | Some a, Some b ->
                    let known = if name = "package" then packagePaths else entryPaths
                    equal "extension.lock_mismatch" name (projection name known a) b
                | None, None -> ()
                | _ -> fail "extension.lock_mismatch" name "Member presence differs."

            let lockedIndex =
                members "lock.index" locked.Entry.Index
                |> List.filter (fun (name, _) ->
                    name
                    <> "claims"
                )
                |> Value.Object

            equal
                "extension.lock_mismatch"
                "index"
                (projection "index" entryPaths entry.Index)
                lockedIndex

            if not (Claims.equivalent entry.Claims locked.Entry.Claims) then
                fail "extension.lock_mismatch" "claims" "Locked claims differ."

            ()
        )

    let readRepositories bytes =
        protectRead
            "repositories.json"
            bytes
            (fun value ->
                let items = members "repositories" value
                schema "repositories" (schemaUrl "repositories") items
                closed "repositories" (set [ "$schema"; "repositories" ]) items
                let seen = HashSet<string>(StringComparer.Ordinal)

                for repository in
                    required "repositories" "repositories" items
                    |> array "repositories.repositories" do
                    let fields = members "repository" repository
                    closed "repository" (set [ "name"; "endpoint"; "state" ]) fields
                    let name = nonemptyValue "repository" "name" fields

                    parsed "repository.name" Token.tryParse (Value.String name)
                    |> ignore

                    if not (seen.Add name) then
                        invalid "repository.name" "Duplicate repository name."

                    required "repository" "state" fields
                    |> choices "repository.state" [ "enabled"; "disabled" ]
                    |> ignore

                    let endpoint =
                        required "repository" "endpoint" fields
                        |> members "repository.endpoint"

                    closed "repository.endpoint" (set [ "kind"; "path" ]) endpoint

                    required "repository.endpoint" "kind" endpoint
                    |> choices "repository.endpoint.kind" [ "local-directory" ]
                    |> ignore

                    required "repository.endpoint" "path" endpoint
                    |> absolute "repository.endpoint.path"
                    |> ignore

                value
            )

    let readJournal bytes =
        protectRead
            "journal"
            bytes
            (fun value ->
                let fields = members "journal" value
                schema "journal" (schemaUrl "journal") fields
                closed "journal" (set [ "$schema"; "nextLock"; "nextCatalog" ]) fields

                let saved name (parser: byte array -> Result<'T, InstallationFailure>) =
                    match required "journal" name fields with
                    | Value.Null -> None
                    | value ->
                        let bytes =
                            array
                                ("journal."
                                 + name)
                                value
                            |> List.map (
                                function
                                | Value.Number text ->
                                    match Byte.TryParse text with
                                    | true, value -> value
                                    | _ -> invalid name "Saved byte is outside 0..255."
                                | _ -> invalid name "Saved value must be a byte integer."
                            )
                            |> List.toArray

                        match parser bytes with
                        | Ok value -> Some value
                        | Error failure -> fail "extension.old_state" name failure.Message

                {
                    NextLock = saved "nextLock" readLock
                    NextCatalog = saved "nextCatalog" readCatalog
                    Bytes = Array.copy bytes
                }
            )
