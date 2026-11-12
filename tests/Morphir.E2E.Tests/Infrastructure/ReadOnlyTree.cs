// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// A directory tree made read-only for the current user, as an installation in a location the user cannot write. On
/// Windows every file gets the read-only attribute and the directory a deny-write rule for the current user that every
/// file and folder below it inherits: no file or folder can be created, written or deleted. On Linux and macOS every
/// write bit is cleared: folders become <c>r-x</c>, files <c>r--</c> or, when they were executable, <c>r-x</c>.
/// Disposing gives every file and folder back what it had, so the tree can be deleted.
/// </summary>
public sealed class ReadOnlyTree : IDisposable
{

    private const UnixFileMode WriteBits = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    private readonly string _root;
    private readonly IReadOnlyList<(string Path, FileAttributes Attributes)> _attributes;
    private readonly IReadOnlyList<(string Path, UnixFileMode Mode)> _modes;
    private readonly object? _denyRule;
    private bool _restored;

    private ReadOnlyTree(string root, IReadOnlyList<(string, FileAttributes)> attributes, IReadOnlyList<(string, UnixFileMode)> modes, object? denyRule)
    {
        _root = root;
        _attributes = attributes;
        _modes = modes;
        _denyRule = denyRule;
    }

    /// <summary>Makes <paramref name="root"/> and everything below it read-only for the current user.</summary>
    public static ReadOnlyTree Apply(string root)
    {
        root = Path.GetFullPath(root);
        if (OperatingSystem.IsWindows())
        {
            var attributes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(file => (file, File.GetAttributes(file))).ToList();
            foreach (var (file, current) in attributes) File.SetAttributes(file, current | FileAttributes.ReadOnly);
            return new ReadOnlyTree(root, attributes, [], DenyWrites(root));
        }

        return new ReadOnlyTree(root, [], ClearWriteBits(root), null);
    }

    /// <summary>Clears every write bit below and of <paramref name="root"/>, and returns each path's mode before.</summary>
    [UnsupportedOSPlatform("windows")]
    private static List<(string, UnixFileMode)> ClearWriteBits(string root)
    {
        // Files first, then folders from the deepest up, so no folder is closed before what is inside it is changed.
        var paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Append(root).OrderByDescending(folder => folder.Length))
            .ToList();
        var modes = new List<(string, UnixFileMode)>();
        foreach (var path in paths)
        {
            var mode = File.GetUnixFileMode(path);
            modes.Add((path, mode));
            File.SetUnixFileMode(path, mode & ~WriteBits);
        }

        return modes;
    }

    [UnsupportedOSPlatform("windows")]
    private static void RestoreModes(IReadOnlyList<(string Path, UnixFileMode Mode)> modes)
    {
        // Folders from the root down, so each one can be reached again before what is inside it.
        foreach (var (path, mode) in Enumerable.Reverse(modes)) Try(() => File.SetUnixFileMode(path, mode));
    }

    /// <summary>
    /// The writes into <paramref name="root"/> that succeeded: a new file in each folder, a new folder in the root, and
    /// opening each file for writing. Empty for a read-only tree. A probe that succeeds is undone; no file is changed.
    /// </summary>
    public static IReadOnlyList<string> WriteProblems(string root)
    {
        var problems = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
        {
            var probe = Path.Combine(folder, $"morphir-write-probe-{Guid.NewGuid():N}");
            if (Try(() => File.WriteAllBytes(probe, [])))
            {
                problems.Add($"a new file can be created in {folder}");
                Try(() => File.Delete(probe));
            }
        }

        var probeFolder = Path.Combine(root, $"morphir-write-probe-{Guid.NewGuid():N}");
        if (Try(() => Directory.CreateDirectory(probeFolder)))
        {
            problems.Add($"a new folder can be created in {root}");
            Try(() => Directory.Delete(probeFolder));
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (Try(() => new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose()))
                problems.Add($"{file} can be opened for writing");
        }

        return problems;
    }

    /// <summary>Every folder (ending in /) and file with its size below <paramref name="root"/>, relative and in ordinal order.</summary>
    public static IReadOnlyList<string> Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(entry => (Relative: Path.GetRelativePath(root, entry).Replace('\\', '/'), Folder: Directory.Exists(entry)))
            .Select(entry => entry.Folder ? entry.Relative + "/" : $"{entry.Relative} {new FileInfo(Path.Combine(root, entry.Relative)).Length}")
            .Order(StringComparer.Ordinal)];

    /// <summary>Gives every file and folder back its attributes, mode and access rules.</summary>
    public void Dispose()
    {
        if (_restored) return;
        _restored = true;
        try
        {
            if (OperatingSystem.IsWindows() && _denyRule is FileSystemAccessRule rule) AllowWrites(_root, rule);
        }
        finally
        {
            foreach (var (file, attributes) in _attributes) Try(() => File.SetAttributes(file, attributes));
            if (!OperatingSystem.IsWindows()) RestoreModes(_modes);
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule DenyWrites(string root)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows user has no security identifier.");
        const FileSystemRights denied = FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.WriteExtendedAttributes
            | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete;
        var rule = new FileSystemAccessRule(user, denied, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Deny);
        var directory = new DirectoryInfo(root);
        var security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);
        return rule;
    }

    [SupportedOSPlatform("windows")]
    private static void AllowWrites(string root, FileSystemAccessRule rule)
    {
        var directory = new DirectoryInfo(root);
        var security = directory.GetAccessControl();
        security.RemoveAccessRuleSpecific(rule);
        directory.SetAccessControl(security);
    }

    private static bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
