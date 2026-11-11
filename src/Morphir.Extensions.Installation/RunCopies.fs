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

    let remove (parent: SafeDirectory) name =
        do
            use child = parent.Child(name, false)
            use owner = child.File(".owner", false, true)
            empty child
        // No valid owner adopts an existing name; ownership always starts with a fresh random directory.
        do use child = parent.Child(name, false) in child.DeleteFile ".owner"
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
    /// Creates a private directory and holds its owner file exclusively until disposal.
    static member CreateAsync(home: string, ct: CancellationToken) =
        Operations.run
            ct
            (fun () ->
                use root = SafeDirectory.Open(home, true)

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

                        let owner = directory.File(".owner", false, true)

                        new OwnedRunDirectory(
                            directory.Path,
                            owner,
                            Action(fun () ->
                                use root = SafeDirectory.Open(home, false)

                                FileSafety.withDirectories
                                    root
                                    [ "tmp"; "extensions" ]
                                    false
                                    (fun parent -> RunDirectoryIO.remove parent name)
                            )
                        )
                    )
            )

    /// Deletes stale directories with the .NET prefix, taking a nonwaiting owner lock and never following reparse points.
    static member SweepAsync(home: string, ct: CancellationToken) =
        Operations.run
            ct
            (fun () ->
                let removed, skipped = ResizeArray<string>(), ResizeArray<string>()
                use root = SafeDirectory.Open(home, true)

                try
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
                                        RunDirectoryIO.remove parent name
                                        removed.Add path
                                    with
                                    | :? IOException
                                    | :? UnauthorizedAccessException
                                    | InstallationException _ -> skipped.Add path
                        )
                with :? FileNotFoundException ->
                    ()

                RunCopySweep(removed.ToArray(), skipped.ToArray())
            )
