// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.Collections.Generic
open System.IO
open System.Threading

module private RunDirectoryIO =
    let prefix = "morphir-dotnet-run-"

    let rec empty (directory: SafeDirectory) =
        for name in directory.Entries() do
            if
                name
                <> ".owner"
            then
                let path = Path.Combine(directory.Path, name)
                let attributes = File.GetAttributes path

                if attributes.HasFlag FileAttributes.ReparsePoint then
                    Operations.fail "extension.path_escape" $"Refusing run-copy link: {path}"

                if attributes.HasFlag FileAttributes.Directory then
                    do use child = directory.Child(name, false) in empty child
                    directory.DeleteDirectory name
                else
                    directory.DeleteFile name

    let remove (parent: SafeDirectory) name expectedDirectory expectedOwner =
        let directoryIdentity =
            use child = parent.Child(name, false)

            match expectedDirectory with
            | Some identity when
                child.Identity
                <> identity
                ->
                Operations.fail "extension.path_escape" "A run directory changed identity."
            | _ -> ()

            let ownerIdentity =
                use owner = child.File(".owner", false, true)
                let identity = child.FileIdentity owner

                match expectedOwner with
                | Some expected when
                    identity
                    <> expected
                    ->
                    Operations.fail "extension.path_escape" "A run owner changed identity."
                | _ -> ()

                if not (child.HasFileIdentity(".owner", identity)) then
                    Operations.fail "extension.path_escape" "The run owner was replaced."

                empty child
                child.ValidateLocation()
                identity

            if not (child.HasFileIdentity(".owner", ownerIdentity)) then
                Operations.fail "extension.path_escape" "The run owner was replaced during cleanup."

            child.DeleteIfIdentity(".owner", ownerIdentity)
            child.Identity
        // The cross-process mutation lease remains held through owner removal and final directory deletion.
        do
            use current = parent.Child(name, false)

            if
                current.Identity
                <> directoryIdentity
            then
                Operations.fail
                    "extension.path_escape"
                    "The run directory was replaced during cleanup."

        parent.DeleteDirectory name

/// Lifetime of a .NET-owned temporary directory. Dispose only after its guest and children have exited.
[<Sealed>]
type OwnedRunDirectory internal (path: string, owner: FileStream, cleanup: Action) =
    let mutable disposed = false
    /// Native path of this run's private directory.
    member _.Path = path
    /// Stable text without reflection.
    override _.ToString() = path

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                owner.Dispose()

                try
                    cleanup.Invoke()
                with
                | :? IOException
                | :? UnauthorizedAccessException
                | InstallationException _ -> ()

/// Result of a best-effort sweep. Busy or unsafe directories are left for inspection or a later sweep.
[<Sealed>]
type RunCopySweep internal (removed: string array, skipped: string array) =
    /// Directories successfully removed.
    member _.Removed: IReadOnlyList<string> = Array.AsReadOnly removed
    /// Directories left in place.
    member _.Skipped: IReadOnlyList<string> = Array.AsReadOnly skipped

    /// Stable text without reflection.
    override _.ToString() =
        $"Removed {removed.Length} stale run directories; left {skipped.Length}."

/// Owns and sweeps only .NET run directories, without any installed-record or package metadata.
[<AbstractClass; Sealed>]
type RunCopies private () =
    static member private CreateCore
        (
            home: string,
            beforeOwner: Action<string>,
            ct: CancellationToken
        ) =
        Operations.run
            ct
            (fun () ->
                use root = SafeDirectory.Open(home, true)
                use mutation = FileSafety.mutationLease root
                let homeIdentity = root.Identity

                FileSafety.withDirectories
                    root
                    [ "tmp"; "extensions" ]
                    true
                    (fun parent ->
                        let name =
                            RunDirectoryIO.prefix
                            + Guid.NewGuid().ToString("N")

                        use directory = parent.Child(name, true)

                        if not (OperatingSystem.IsWindows()) then
                            File.SetUnixFileMode(
                                directory.Handle,
                                UnixFileMode.UserRead
                                ||| UnixFileMode.UserWrite
                                ||| UnixFileMode.UserExecute
                            )

                        beforeOwner.Invoke directory.Path
                        let owner = directory.File(".owner", false, true)
                        let ownerIdentity = directory.FileIdentity owner
                        let directoryIdentity = directory.Identity

                        new OwnedRunDirectory(
                            directory.Path,
                            owner,
                            Action(fun () ->
                                use root = SafeDirectory.Open(home, false)

                                if
                                    root.Identity
                                    <> homeIdentity
                                then
                                    Operations.fail
                                        "extension.path_escape"
                                        "Morphir Home changed identity."

                                use mutation = FileSafety.mutationLease root

                                FileSafety.withDirectories
                                    root
                                    [ "tmp"; "extensions" ]
                                    false
                                    (fun parent ->
                                        RunDirectoryIO.remove
                                            parent
                                            name
                                            (Some directoryIdentity)
                                            (Some ownerIdentity)
                                    )
                            )
                        )
                    )
            )

    /// Creates a private directory and holds its owner file exclusively until disposal.
    static member CreateAsync(home: string, ct: CancellationToken) =
        RunCopies.CreateCore(home, Action<string>(ignore), ct)

    static member internal CreateWithHookAsync
        (
            home: string,
            hook: Action<string>,
            ct: CancellationToken
        ) =
        RunCopies.CreateCore(home, hook, ct)

    /// Deletes stale directories with the .NET prefix, taking a nonwaiting owner lock and never following reparse points.
    static member SweepAsync(home: string, ct: CancellationToken) =
        Operations.run
            ct
            (fun () ->
                let removed, skipped = ResizeArray<string>(), ResizeArray<string>()
                use root = SafeDirectory.Open(home, true)

                try
                    use mutation = FileSafety.mutationLease root

                    FileSafety.withDirectories
                        root
                        [ "tmp"; "extensions" ]
                        false
                        (fun parent ->
                            for name in
                                parent.Entries()
                                |> Array.sort do
                                ct.ThrowIfCancellationRequested()

                                if
                                    name.StartsWith(
                                        RunDirectoryIO.prefix,
                                        StringComparison.Ordinal
                                    )
                                then
                                    let path = Path.Combine(parent.Path, name)

                                    try
                                        RunDirectoryIO.remove parent name None None
                                        removed.Add path
                                    with
                                    | :? IOException
                                    | :? UnauthorizedAccessException
                                    | InstallationException _ -> skipped.Add path
                        )
                with
                | :? FileNotFoundException -> ()
                | :? IOException -> skipped.Add root.Path

                RunCopySweep(removed.ToArray(), skipped.ToArray())
            )
