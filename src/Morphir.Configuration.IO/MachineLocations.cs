using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

public static partial class ProjectSystem
{
    /// <summary>Plan separate filesystem roots for captured machine locations without reading them.</summary>
    public static IReadOnlyList<string> MachineBoundaries(MachineLocationInput machine, string startDirectory)
        => ProjectSources.machineCandidates(ResolveMachineLocations(machine, startDirectory), false)
            .Select(source => MachineRoot(source.Path)).Distinct(StringComparer.Ordinal).ToArray();

    private static string MachineRoot(string path)
    {
        path = ProjectPath.normalize(path);
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = path[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            // Every generated candidate includes its directory and filename.
            return "//" + parts[0] + "/" + parts[1];
        }
        else if (path.StartsWith('/')) return "/";
        else if (ProjectPath.isAbsolute(path)) return path[..3];
        throw new ArgumentException("Machine locations must use absolute paths.", nameof(path));
    }

    internal static MachineLocationInput ResolveMachineLocations(MachineLocationInput machine, string startDirectory)
    {
        var environment = machine.Environment;
        if (environment.TryGetValue("MORPHIR_HOME", out var home) && !string.IsNullOrEmpty(home) && !ProjectPath.isAbsolute(home))
            environment = environment.Add("MORPHIR_HOME", ProjectPath.join(startDirectory, home));
        return new(machine.Platform, machine.HomeDirectory, machine.RoamingAppData, machine.ProgramData, environment);
    }

    /// <summary>Capture native directory conventions at the edge. Passing an environment replaces ambient values.</summary>
    public static MachineLocationInput CaptureMachineLocations(IReadOnlyDictionary<string, string>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(p => p.Key is string && p.Value is string)
            .ToDictionary(p => (string)p.Key, p => (string)p.Value!, StringComparer.Ordinal);
        string? Get(string key) => environment.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        static FSharpOption<string> Option(string? value) => string.IsNullOrEmpty(value) ? null! : FSharpOption<string>.Some(ProjectPath.normalize(value));
        var platform = OperatingSystem.IsWindows() ? MachinePlatform.Windows : OperatingSystem.IsMacOS() ? MachinePlatform.MacOS : MachinePlatform.Linux;
        var home = Get(platform == MachinePlatform.Windows ? "USERPROFILE" : "HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new(platform, ProjectPath.normalize(home),
            Option(Get("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            Option(Get("PROGRAMDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
            MapModule.OfSeq(environment.Select(p => Tuple.Create(p.Key, p.Value))));
    }
}
