// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The private workspace of one session: the empty directory that <c>morphir_get_workspace_info</c> names as
/// <c>root</c> (contract policy <c>workspaceInfo.root</c>, <c>private-empty-directory</c>). It is created the first
/// time the guest asks for it, below the session's workspace root, with a name no other session has, and readable only
/// by the user on Unix. It is removed when the session ends, however it ends. The guest cannot reach it: the WASI
/// sandbox has no preopened directory, so the name is all it gets.
/// </summary>
internal sealed class SessionWorkspace
{
    const string Prefix = "morphir-wasm-session-";

    readonly object gate = new();
    readonly string root;
    string? directory;
    bool removed;

    /// <param name="root">The directory below which the workspace is created, or null for the system's temporary
    /// directory.</param>
    public SessionWorkspace(string? root)
    {
        if (root is not null && !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException($"The workspace root must be an absolute path; {root} is not.", nameof(root));
        }

        this.root = Path.GetFullPath(root ?? Path.GetTempPath());
    }

    /// <summary>The workspace directory, or null when the guest has not asked for it.</summary>
    public string? Directory
    {
        get
        {
            lock (gate)
            {
                return directory;
            }
        }
    }

    /// <summary>The workspace directory, created on the first call.</summary>
    /// <exception cref="IOException">The directory could not be created.</exception>
    /// <exception cref="ObjectDisposedException">The session has ended.</exception>
    public string Create()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(removed, this);
            if (directory is not null)
            {
                return directory;
            }

            System.IO.Directory.CreateDirectory(root);
            var path = Path.Combine(root, Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
            if (OperatingSystem.IsWindows())
            {
                System.IO.Directory.CreateDirectory(path);
            }
            else
            {
                System.IO.Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            directory = path;
            return path;
        }
    }

    /// <summary>Removes the workspace, once. A directory that cannot be removed is left; the session still ends.</summary>
    public void Remove()
    {
        string? path;
        lock (gate)
        {
            if (removed)
            {
                return;
            }

            removed = true;
            path = directory;
        }

        if (path is null)
        {
            return;
        }

        try
        {
            System.IO.Directory.Delete(path, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Nothing in the guest can write there, so the directory is empty and this is rare. The library writes
            // nothing to the process's streams; a later cleanup of the root finds the directory by its prefix.
        }
    }
}
