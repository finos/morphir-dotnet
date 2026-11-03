using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

public static partial class ProjectSystem
{
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
