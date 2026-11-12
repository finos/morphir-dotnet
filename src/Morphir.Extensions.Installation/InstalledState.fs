// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain

module internal StateOwnership =
    let unwrap result =
        match result with
        | Ok value -> value
        | Error failure -> raise (InstallationException failure)

    let bytes (root: SafeDirectory) path =
        use file = FileSafety.openRelative root (Path.Combine(root.Path, path))
        use output = new MemoryStream()
        file.CopyTo output
        output.ToArray()

    let checkDocument (root: SafeDirectory) path kind =
        use file = FileSafety.openRelative root (Path.Combine(root.Path, path))

        use bytes = new MemoryStream()
        file.CopyTo bytes

        match SharedJson.read path (bytes.ToArray()) with
        | Error _ -> Operations.fail "extension.old_state" $"Malformed state: {path}"
        | Ok value ->
            let result =
                SharedJson.protect (fun () ->
                    let items = SharedJson.members path value

                    SharedJson.schema
                        path
                        $"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/{kind}.schema.json"
                        items
                )

            match result with
            | Ok _ -> ()
            | Error _ ->
                Operations.fail "extension.old_state" $"Old state must not be recovered: {path}"

    let detectUnderLock (root: SafeDirectory) (ct: CancellationToken) =
        let mutable journal = None

        if
            root.Entries()
            |> Array.contains "catalog"
        then
            use catalog = root.Child("catalog", false)

            if
                catalog.Entries()
                |> Array.contains "extensions.json"
            then
                checkDocument root "catalog/extensions.json" "catalog"

        if root.Entries() |> Array.contains "locks" then
            use locks = root.Child("locks", false)

            if
                locks.Entries()
                |> Array.contains "extensions"
            then
                use directory = locks.Child("extensions", false)

                let journals =
                    directory.Entries()
                    |> Array.filter (fun name ->
                        not (name.StartsWith '.')
                        && name.EndsWith(".json.transaction", StringComparison.Ordinal)
                    )

                if journals.Length > 1 then
                    Operations.fail "extension.old_state" "Multiple journals must not be replayed."

                for name in directory.Entries() do
                    ct.ThrowIfCancellationRequested()

                    if name.StartsWith '.' then
                        ()
                    elif name.EndsWith(".json.transaction", StringComparison.Ordinal) then
                        checkDocument root $"locks/extensions/{name}" "journal"
                    elif name.EndsWith(".json", StringComparison.Ordinal) then
                        checkDocument root $"locks/extensions/{name}" "lock"

                if journals.Length = 1 then
                    let name = journals[0]

                    let id =
                        name.Substring(
                            0,
                            name.Length
                            - ".json.transaction".Length
                        )

                    match Morphir.Extensions.ExtensionId.Parse id with
                    | Error _ ->
                        Operations.fail
                            "extension.old_state"
                            "Journal filename is not a full extension id."
                    | Ok _ -> ()

                    let saved =
                        match StateDocuments.readJournal (bytes root $"locks/extensions/{name}") with
                        | Ok value -> value
                        | Error failure -> Operations.fail "extension.old_state" failure.Message

                    match saved.NextLock, saved.NextCatalog with
                    | Some locked, Some catalog ->
                        if
                            locked.Entry.Id
                            <> id
                        then
                            Operations.fail
                                "extension.old_state"
                                "Saved lock does not match journal filename."

                        match
                            catalog.Entries
                            |> List.tryFind (fun entry -> entry.Id = id)
                        with
                        | Some entry ->
                            match StateDocuments.bind entry locked with
                            | Ok _ -> ()
                            | Error failure -> Operations.fail "extension.old_state" failure.Message
                        | None ->
                            Operations.fail
                                "extension.old_state"
                                "Saved lock is absent from the saved catalog."
                    | Some _, None ->
                        Operations.fail "extension.old_state" "Saved lock requires a saved catalog."
                    | _ -> ()

                    journal <- Some(name, saved)

        journal

    let checkUnderLock root ct =
        if (detectUnderLock root ct).IsSome then
            Operations.fail
                "extension.recovery_required"
                "A validated journal requires explicit recovery consent before mutation."

    let checkRepositoryDocuments (root: SafeDirectory) =
        if
            root.Entries()
            |> Array.contains "config"
        then
            use config = root.Child("config", false)

            if
                config.Entries()
                |> Array.contains "extensions"
            then
                use extensions = config.Child("extensions", false)

                if
                    extensions.Entries()
                    |> Array.contains "repositories.json"
                then
                    checkDocument root "config/extensions/repositories.json" "repositories"

                    StateDocuments.readRepositories (
                        bytes root "config/extensions/repositories.json"
                    )
                    |> unwrap
                    |> ignore

    let checkRepositories (root: SafeDirectory) =
        use locks = root.Child("locks", true)
        use lease = locks.NamedLease("extensions.repositories.lock", true, true)
        checkRepositoryDocuments root

    let checkRepositoriesAsync (root: SafeDirectory) (ct: CancellationToken) = task {
        use locks = root.Child("locks", true)
        use! lease = locks.NamedLeaseAsync("extensions.repositories.lock", true, true, ct)
        ct.ThrowIfCancellationRequested()
        checkRepositoryDocuments root
    }

    let readCurrent (root: SafeDirectory) =
        let catalog =
            if
                root.Entries()
                |> Array.contains "catalog"
            then
                use directory = root.Child("catalog", false)

                if
                    directory.Entries()
                    |> Array.contains "extensions.json"
                then
                    StateDocuments.readCatalog (bytes root "catalog/extensions.json")
                    |> unwrap
                    |> Some
                else
                    None
            else
                None

        let locks = ResizeArray<StateLock>()
        if root.Entries() |> Array.contains "locks" then
            use lockRoot = root.Child("locks", false)

            if
                lockRoot.Entries()
                |> Array.contains "extensions"
            then
                use directory = lockRoot.Child("extensions", false)

                for name in directory.Entries() do
                    if
                        not (name.StartsWith '.')
                        && name.EndsWith(".json", StringComparison.Ordinal)
                    then
                        let locked =
                            StateDocuments.readLock (bytes root $"locks/extensions/{name}")
                            |> unwrap

                        if
                            name
                            <> locked.Entry.Id
                               + ".json"
                        then
                            Operations.fail
                                "extension.lock_mismatch"
                                "Lock filename differs from extensionId."

                        match
                            catalog
                            |> Option.bind (fun catalog ->
                                catalog.Entries
                                |> List.tryFind (fun entry -> entry.Id = locked.Entry.Id)
                            )
                        with
                        | Some entry ->
                            StateDocuments.bind entry locked
                            |> unwrap
                        | None ->
                            Operations.fail
                                "extension.lock_mismatch"
                                "Lock has no matching catalog entry."

                        locks.Add locked

        catalog, List.ofSeq locks

    let withLock home (ct: CancellationToken) work =
        ct.ThrowIfCancellationRequested()
        use root = SafeDirectory.Open(home, true)
        checkRepositories root
        use locks = root.Child("locks", true)
        use lease = locks.NamedLease("extensions.state.lock", true, true)
        ct.ThrowIfCancellationRequested()
        checkUnderLock root ct
        work root

    let withLockAsync home (ct: CancellationToken) work = task {
        ct.ThrowIfCancellationRequested()
        use root = SafeDirectory.Open(home, true)
        do! checkRepositoriesAsync root ct
        use locks = root.Child("locks", true)
        use! lease = locks.NamedLeaseAsync("extensions.state.lock", true, true, ct)
        ct.ThrowIfCancellationRequested()
        checkUnderLock root ct
        return work root
    }

module internal StateTransactions =
    let private replace (directory: SafeDirectory) name saved =
        match saved with
        | Some bytes -> directory.ReplaceStateFile(name, bytes)
        | None ->
            if
                directory.Entries()
                |> Array.contains name
            then
                use file = directory.File(name, false, false)
                let identity = directory.FileIdentity file
                file.Dispose()
                directory.DeleteIfIdentity(name, identity)

    let replay
        (root: SafeDirectory)
        (name: string)
        (saved: StateJournal)
        (afterPhase: Action<int>)
        (ct: CancellationToken)
        =
        use lockRoot = root.Child("locks", true)
        use locks = lockRoot.Child("extensions", true)
        use catalog = root.Child("catalog", true)

        let id =
            name.Substring(
                0,
                name.Length
                - ".transaction".Length
            )

        let validateJournal () =
            use current = locks.File(name, false, false)
            let identity = locks.FileIdentity current

            if
                current.Length
                <> int64 saved.Bytes.Length
            then
                Operations.fail
                    "extension.old_state"
                    "Transaction journal changed; foreign bytes retained."

            let digest, _ =
                Copying.hashCopy
                    current
                    Stream.Null
                    (int64 saved.Bytes.Length)
                    "extension.old_state"
                    ct

            if
                digest
                <> Digest.ofBytes saved.Bytes
            then
                Operations.fail
                    "extension.old_state"
                    "Transaction journal changed; foreign bytes retained."

            identity

        ct.ThrowIfCancellationRequested()

        let admittedIdentity = validateJournal ()

        let validateOwnedJournal () =
            if
                validateJournal ()
                <> admittedIdentity
            then
                Operations.fail
                    "extension.old_state"
                    "Transaction journal identity changed; foreign file retained."

        replace
            locks
            id
            (saved.NextLock
             |> Option.map (fun value -> value.Bytes))

        afterPhase.Invoke 1
        ct.ThrowIfCancellationRequested()
        validateOwnedJournal ()

        replace
            catalog
            "extensions.json"
            (saved.NextCatalog
             |> Option.map (fun value -> value.Bytes))

        afterPhase.Invoke 2
        ct.ThrowIfCancellationRequested()
        validateOwnedJournal ()
        locks.DeleteIfIdentity(name, admittedIdentity)

        if
            locks.Entries()
            |> Array.contains name
        then
            Operations.fail
                "extension.old_state"
                "A replaced journal was retained instead of removed."

        afterPhase.Invoke 3

    let commit
        (root: SafeDirectory)
        id
        nextLock
        nextCatalog
        (afterPhase: Action<int>)
        (ct: CancellationToken)
        =
        StateOwnership.checkUnderLock root ct

        let encode =
            function
            | None -> Value.Null
            | Some bytes ->
                Value.Array[for b: byte in bytes ->
                                Value.Number(b.ToString(Globalization.CultureInfo.InvariantCulture))]

        let journalBytes =
            JsonText.toBytes (
                Value.Object["$schema", Value.String(StateDocuments.schemaUrl "journal")
                             "nextLock", encode nextLock
                             "nextCatalog", encode nextCatalog]
            )

        let saved =
            StateDocuments.readJournal journalBytes
            |> StateOwnership.unwrap

        match saved.NextLock, saved.NextCatalog with
        | Some locked, Some catalog ->
            if
                locked.Entry.Id
                <> id
            then
                Operations.fail "extension.lock_mismatch" "Transaction id differs from lock id."

            match
                catalog.Entries
                |> List.tryFind (fun entry -> entry.Id = id)
            with
            | Some entry ->
                StateDocuments.bind entry locked
                |> StateOwnership.unwrap
            | None ->
                Operations.fail
                    "extension.lock_mismatch"
                    "Transaction lock has no selected catalog entry."
        | Some _, None ->
            Operations.fail "extension.lock_mismatch" "Transaction lock requires catalog."
        | _ -> ()

        ct.ThrowIfCancellationRequested()
        use lockRoot = root.Child("locks", true)
        use locks = lockRoot.Child("extensions", true)

        let name =
            id
            + ".json.transaction"

        locks.ReplaceStateFile(name, journalBytes)
        afterPhase.Invoke 0
        replay root name saved afterPhase ct

/// Observed recovery of one supported journal; no old format was converted.
[<Sealed>]
type StateRecovery internal (recovered: bool) =
    member _.Recovered = recovered

    override _.ToString() =
        if recovered then
            "Extension state journal recovered."
        else
            "No extension state journal."

module private LocalRepositories =
    let name text =
        match Token.tryParse text with
        | Ok parsed -> parsed.Text
        | Error _ -> Operations.fail "extension.invalid_repository" "Repository must be a stable registered name, never a directory."

    let document (root: SafeDirectory) =
        StateOwnership.checkRepositoryDocuments root
        if root.Entries() |> Array.contains "config" then
            use config = root.Child("config",false)
            if config.Entries() |> Array.contains "extensions" then
                use extensions = config.Child("extensions",false)
                if extensions.Entries() |> Array.contains "repositories.json" then
                    let value = StateDocuments.readRepositories(StateOwnership.bytes root "config/extensions/repositories.json") |> StateOwnership.unwrap
                    SharedJson.members "repositories" value |> SharedJson.required "repositories" "repositories" |> SharedJson.array "repositories"
                else []
            else []
        else []

    let endpoint name records =
        records |> List.tryFind(fun record -> SharedJson.members "repository" record |> SharedJson.required "repository" "name" |> SharedJson.text "name" = name)
        |> Option.map(fun record ->
            let fields = SharedJson.members "repository" record
            let state = SharedJson.required "repository" "state" fields |> SharedJson.text "state"
            if state <> "enabled" then Operations.fail "extension.repository_disabled" "The registered repository is disabled."
            fields |> SharedJson.required "repository" "endpoint" |> SharedJson.members "endpoint"
            |> SharedJson.required "endpoint" "path" |> SharedJson.text "path")

    let inspect root ct =
        StateOwnership.checkUnderLock root ct
        StateOwnership.readCurrent root |> ignore
        document root

    let planned (root: SafeDirectory option) name path ct =
        let existing = root |> Option.map(fun root -> inspect root ct) |> Option.defaultValue []
        match endpoint name existing with
        | Some current when not (String.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath current),path,if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal)) ->
            Operations.fail "extension.repository_conflict" "A stable repository name already binds a different directory."
        | Some _ -> None
        | None ->
            let record = Value.Object ["name",Value.String name;"endpoint",Value.Object["kind",Value.String "local-directory";"path",Value.String path];"state",Value.String "enabled"]
            let records = record::existing |> List.sortBy(fun value -> SharedJson.members "repository" value |> SharedJson.required "repository" "name" |> SharedJson.text "name")
            let value = Value.Object ["$schema",Value.String(StateDocuments.schemaUrl "repositories");"repositories",Value.Array records]
            let bytes = JsonText.toBytes value
            StateDocuments.readRepositories bytes |> StateOwnership.unwrap |> ignore
            Some bytes

/// Compatibility inspection only. This does not replay journals or certify current documents.
[<AbstractClass; Sealed>]
type InstalledState private () =
    static member internal RegisterLocalRepositoryAsync(home: string, name: string, directory: string, dryRun: bool, ct: CancellationToken) : Task<InstallationResult<string>> =
        Operations.runAsync ct (fun () -> task {
            let name = LocalRepositories.name name
            if not(Path.IsPathFullyQualified home) || not(Path.IsPathFullyQualified directory) then
                Operations.fail "extension.invalid_repository" "Home and the local repository directory must be absolute."
            use source = SafeDirectory.Open(directory,false)
            let path = Path.TrimEndingDirectorySeparator source.Path
            if dryRun then
                if Directory.Exists home then
                    use root = SafeDirectory.Open(home,false)
                    LocalRepositories.planned (Some root) name path ct |> ignore
                else LocalRepositories.planned None name path ct |> ignore
            else
                use root = SafeDirectory.Open(home,true)
                use locks = root.Child("locks",true)
                use! repositoryLease = locks.NamedLeaseAsync("extensions.repositories.lock",true,true,ct)
                use! stateLease = locks.NamedLeaseAsync("extensions.state.lock",true,true,ct)
                match LocalRepositories.planned (Some root) name path ct with
                | None -> ()
                | Some bytes ->
                    ct.ThrowIfCancellationRequested()
                    source.ValidateLocation()
                    FileSafety.withDirectories root ["config";"extensions"] true (fun config -> config.ReplaceStateFile("repositories.json",bytes))
            return path
        })

    static member internal ResolveLocalRepositoryAsync(home: string, name: string, ct: CancellationToken) : Task<InstallationResult<string>> =
        Operations.runAsync ct (fun () -> task {
            let name = LocalRepositories.name name
            use root = SafeDirectory.Open(home,false)
            let records = LocalRepositories.inspect root ct
            match LocalRepositories.endpoint name records with
            | None -> return Operations.fail "extension.repository_not_found" "No enabled registered local repository has this name."
            | Some directory ->
                use source = SafeDirectory.Open(directory,false)
                return source.Path |> Path.TrimEndingDirectorySeparator
        })
    /// Detects old shared-family state before any recovery or state mutation is attempted.
    static member CheckCompatibilityAsync(home: string, ct: CancellationToken) =
        Operations.runAsync
            ct
            (fun () ->
                StateOwnership.withLockAsync
                    home
                    ct
                    (fun root ->
                        StateOwnership.readCurrent root
                        |> ignore

                        true
                    )
            )

    /// Replays a fully validated current journal only with explicit consent. Old state always refuses.
    static member RecoverAsync(home: string, consent: bool, ct: CancellationToken) =
        Operations.runAsync
            ct
            (fun () -> task {
                use root = SafeDirectory.Open(home, true)
                do! StateOwnership.checkRepositoriesAsync root ct
                use locks = root.Child("locks", true)
                use! lease = locks.NamedLeaseAsync("extensions.state.lock", true, true, ct)

                match StateOwnership.detectUnderLock root ct with
                | None -> return StateRecovery false
                | Some(name, saved) ->
                    if not consent then
                        Operations.fail
                            "extension.recovery_required"
                            "Recovery requires explicit consent."

                    StateTransactions.replay root name saved (Action<int>(ignore)) ct
                    return StateRecovery true
            })
