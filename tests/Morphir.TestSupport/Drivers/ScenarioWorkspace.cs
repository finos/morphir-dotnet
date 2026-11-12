// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;

namespace Morphir.TestSupport.Drivers;

/// <summary>A file of a workspace. Only a workspace makes one, so a step has no path of the machine.</summary>
public sealed record WorkspaceFile
{
    internal WorkspaceFile(string path) => Path = path;

    /// <summary>The full path, for a command line and for a message.</summary>
    public string Path { get; }

    /// <summary>The name of the file with its extension.</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    public override string ToString() => Path;
}

/// <summary>A directory of a workspace.</summary>
public sealed record WorkspaceDirectory
{
    internal WorkspaceDirectory(string path) => Path = path;

    /// <summary>The full path, for a command line and for a message.</summary>
    public string Path { get; }

    /// <summary>A file below this directory.</summary>
    public WorkspaceFile File(params string[] segments) => new(ScenarioWorkspace.Below(Path, segments));

    /// <summary>A directory below this directory.</summary>
    public WorkspaceDirectory Directory(params string[] segments) => new(ScenarioWorkspace.Below(Path, segments));

    public override string ToString() => Path;
}

/// <summary>The state of the files of a work area at one moment. See <see cref="IScenarioWorkspace.Mark"/>.</summary>
public sealed class WorkspaceMark
{
    internal WorkspaceMark(IReadOnlyDictionary<string, (long Length, DateTime LastWriteUtc)> files) => Files = files;

    internal IReadOnlyDictionary<string, (long Length, DateTime LastWriteUtc)> Files { get; }
}

/// <summary>
/// The contained folder of one test: a work area, a home, machine configuration locations and a temporary
/// directory, on paths that no other test gets. A step or a test uses it for each file that it makes or reads, and
/// a driver uses it to contain each product process.
/// </summary>
public interface IScenarioWorkspace : IAsyncDisposable
{
    /// <summary>The folder that holds all the others. Its path has no link.</summary>
    string Root { get; }

    /// <summary>Where the test puts its files. A product process starts here.</summary>
    WorkspaceDirectory Work { get; }

    /// <summary>The home directory of a product process.</summary>
    WorkspaceDirectory Home { get; }

    /// <summary>The global Morphir home. It is not made: each test decides if it is there.</summary>
    WorkspaceDirectory MorphirHome { get; }

    /// <summary>The temporary directory of a product process.</summary>
    WorkspaceDirectory Temp { get; }

    /// <summary>A file below the work area. A path that leaves the work area is refused.</summary>
    WorkspaceFile File(params string[] segments);

    /// <summary>A directory below the work area. A path that leaves the work area is refused.</summary>
    WorkspaceDirectory Directory(params string[] segments);

    /// <summary>Copies a fixture file of the test project into the work area, with the same relative path or with <paramref name="destination"/>.</summary>
    WorkspaceFile CopyFixture(string fixture, string? destination = null);

    /// <summary>Copies a fixture directory of the test project, with its files, into the work area.</summary>
    WorkspaceDirectory CopyFixtureDirectory(string fixture, string? destination = null);

    bool Exists(WorkspaceFile file);

    bool Exists(WorkspaceDirectory directory);

    string ReadText(WorkspaceFile file);

    byte[] ReadBytes(WorkspaceFile file);

    /// <summary>Writes the file, and makes its directory when it is not there.</summary>
    void WriteText(WorkspaceFile file, string text);

    /// <summary>The files below <paramref name="directory"/>, as relative paths with <c>/</c>, in ordinal order.</summary>
    IReadOnlyList<string> FilesIn(WorkspaceDirectory directory);

    /// <summary>Writes the file, and makes its directory when it is not there.</summary>
    void WriteBytes(WorkspaceFile file, byte[] bytes);

    /// <summary>Makes the directory and its parents when they are not there.</summary>
    void CreateDirectory(WorkspaceDirectory directory);

    /// <summary>Makes <paramref name="link"/> a symbolic link to the directory <paramref name="target"/>.</summary>
    void CreateDirectoryLink(WorkspaceDirectory link, WorkspaceDirectory target);

    DateTime LastWriteTimeUtc(WorkspaceFile file);

    void SetLastWriteTimeUtc(WorkspaceFile file, DateTime time);

    /// <summary>Records the files of the work area, for <see cref="FilesWrittenSince"/>.</summary>
    WorkspaceMark Mark();

    /// <summary>
    /// The files of the work area that are new or changed since <paramref name="mark"/>, as paths relative to the
    /// work area with <c>/</c>, in ordinal order.
    /// </summary>
    IReadOnlyList<string> FilesWrittenSince(WorkspaceMark mark);

    /// <summary>
    /// Contains a product process: its home, its machine locations and its temporary directory are those of the
    /// workspace, the variables of the developer for the product are removed, and it starts in the work area.
    /// </summary>
    void Contain(ProcessStartInfo info);

    /// <summary>Keeps the folder when the workspace is disposed, as evidence of a failed test.</summary>
    void Keep();
}

/// <summary>The workspace on a <see cref="TestSandbox"/>.</summary>
public sealed class ScenarioWorkspace : IScenarioWorkspace
{
    private readonly TestSandbox _sandbox;
    private readonly string _fixtures;
    private bool _keep;

    private ScenarioWorkspace(TestSandbox sandbox, string fixtures)
    {
        _sandbox = sandbox;
        _fixtures = fixtures;
        Work = new WorkspaceDirectory(sandbox.Work);
        Home = new WorkspaceDirectory(sandbox.Home);
        MorphirHome = new WorkspaceDirectory(sandbox.MorphirHome);
        Temp = new WorkspaceDirectory(System.IO.Directory.CreateDirectory(sandbox.PathOf("tmp")).FullName);
    }

    /// <summary>
    /// Makes a workspace below the temporary directory of the test process, which has no link in its path
    /// (<see cref="PhysicalTempDirectory"/>). <paramref name="fixtures"/> is the fixture folder of the test project.
    /// </summary>
    public static ScenarioWorkspace Create(string fixtures)
    {
        // A short name: the product makes sockets and pipes below its temporary directory, and their paths have a limit.
        var sandbox = TestSandbox.Create("mbb-");
        var physical = PhysicalTempDirectory.Resolve(sandbox.Root);
        if (!string.Equals(physical, sandbox.Root, PathComparison))
        {
            sandbox.Dispose();
            throw new TestSessionException(
                $"The workspace {sandbox.Root} has a link in its path (the physical path is {physical}). Set TMPDIR to a directory with no link.");
        }

        return new ScenarioWorkspace(sandbox, fixtures);
    }

    public string Root => _sandbox.Root;

    public WorkspaceDirectory Work { get; }

    public WorkspaceDirectory Home { get; }

    public WorkspaceDirectory MorphirHome { get; }

    public WorkspaceDirectory Temp { get; }

    public WorkspaceFile File(params string[] segments) => Work.File(segments);

    public WorkspaceDirectory Directory(params string[] segments) => Work.Directory(segments);

    public WorkspaceFile CopyFixture(string fixture, string? destination = null)
    {
        var source = Below(_fixtures, [fixture]);
        if (!System.IO.File.Exists(source))
            throw new TestSessionException($"The fixture file '{fixture}' is not in the fixture folder {_fixtures}.");

        var file = File(destination ?? fixture);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        System.IO.File.Copy(source, file.Path, overwrite: true);
        return file;
    }

    public WorkspaceDirectory CopyFixtureDirectory(string fixture, string? destination = null)
    {
        var source = Below(_fixtures, [fixture]);
        if (!System.IO.Directory.Exists(source))
            throw new TestSessionException($"The fixture directory '{fixture}' is not in the fixture folder {_fixtures}.");

        var directory = Directory(destination ?? fixture);
        foreach (var path in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(directory.Path, Path.GetRelativePath(source, path));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.Copy(path, target, overwrite: true);
        }

        return directory;
    }

    public bool Exists(WorkspaceFile file) => System.IO.File.Exists(file.Path);

    public bool Exists(WorkspaceDirectory directory) => System.IO.Directory.Exists(directory.Path);

    public string ReadText(WorkspaceFile file) => System.IO.File.ReadAllText(file.Path);

    public byte[] ReadBytes(WorkspaceFile file) => System.IO.File.ReadAllBytes(file.Path);

    public void WriteText(WorkspaceFile file, string text)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        System.IO.File.WriteAllText(file.Path, text);
    }

    public IReadOnlyList<string> FilesIn(WorkspaceDirectory directory) =>
        [.. Snapshot(directory.Path).Keys.Order(StringComparer.Ordinal)];

    public void WriteBytes(WorkspaceFile file, byte[] bytes)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        System.IO.File.WriteAllBytes(file.Path, bytes);
    }

    public void CreateDirectory(WorkspaceDirectory directory) => System.IO.Directory.CreateDirectory(directory.Path);

    public void CreateDirectoryLink(WorkspaceDirectory link, WorkspaceDirectory target)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(link.Path)!);
        System.IO.Directory.CreateSymbolicLink(link.Path, target.Path);
    }

    public DateTime LastWriteTimeUtc(WorkspaceFile file) => System.IO.File.GetLastWriteTimeUtc(file.Path);

    public void SetLastWriteTimeUtc(WorkspaceFile file, DateTime time) => System.IO.File.SetLastWriteTimeUtc(file.Path, time);

    public WorkspaceMark Mark() => new(Snapshot(Work.Path));

    public IReadOnlyList<string> FilesWrittenSince(WorkspaceMark mark) =>
    [
        .. Snapshot(Work.Path)
            .Where(file => !mark.Files.TryGetValue(file.Key, out var before) || before != file.Value)
            .Select(file => file.Key)
            .Order(StringComparer.Ordinal),
    ];

    public void Contain(ProcessStartInfo info)
    {
        _sandbox.Apply(info);
        var temporary = Temp.Path + Path.DirectorySeparatorChar;
        info.Environment["TMPDIR"] = temporary;
        info.Environment["TEMP"] = temporary;
        info.Environment["TMP"] = temporary;
        info.WorkingDirectory = Work.Path;
    }

    public void Keep() => _keep = true;

    public async ValueTask DisposeAsync()
    {
        if (!_keep) await _sandbox.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>The path of <paramref name="segments"/> below <paramref name="parent"/>, or an error when it leaves the parent.</summary>
    internal static string Below(string parent, string[] segments)
    {
        var path = Path.GetFullPath(Path.Combine([parent, .. segments]));
        var prefix = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, PathComparison))
            throw new ArgumentException($"The path '{Path.Combine(segments)}' is outside {parent}.", nameof(segments));
        return path;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static Dictionary<string, (long Length, DateTime LastWriteUtc)> Snapshot(string directory)
    {
        var files = new Dictionary<string, (long, DateTime)>(StringComparer.Ordinal);
        if (!System.IO.Directory.Exists(directory)) return files;

        foreach (var path in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            files[Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/')] = (info.Length, info.LastWriteTimeUtc);
        }

        return files;
    }
}
