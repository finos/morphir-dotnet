// A throwaway .NET tool for task 5.4 of add-wasm-extension-host. `--report` prints one JSON object that says
// which tool package was installed (its RID and its files) and whether the Wasmtime engine can be created.
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length == 0 || args[0] != "--report")
{
    Console.WriteLine(Version());
    return 0;
}

var baseDirectory = AppContext.BaseDirectory;
string[] engineNames = ["wasmtime.dll", "libwasmtime.so", "libwasmtime.dylib"];
var engineFiles = Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories)
    .Where(file => engineNames.Contains(Path.GetFileName(file)))
    .Select(file => new JsonObject
    {
        ["path"] = Path.GetRelativePath(baseDirectory, file).Replace('\\', '/'),
        ["bytes"] = new FileInfo(file).Length,
    })
    .ToArray<JsonNode?>();

var engine = new JsonObject();
try
{
    using var created = new Wasmtime.Engine();
    engine["created"] = true;
}
catch (Exception exception)
{
    engine["created"] = false;
    engine["exception"] = exception.GetType().FullName;
    engine["message"] = exception.Message.Split('\n')[0].Trim();
}

// A framework-dependent app runs on the shared runtime, which lives outside the app's own directory.
var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
var report = new JsonObject
{
    ["version"] = Version(),
    ["runtimeIdentifier"] = RuntimeInformation.RuntimeIdentifier,
    ["os"] = RuntimeInformation.OSDescription,
    ["framework"] = RuntimeInformation.FrameworkDescription,
    ["frameworkDependent"] = !Path.GetFullPath(runtimeDirectory).StartsWith(Path.GetFullPath(baseDirectory), StringComparison.Ordinal),
    ["baseDirectory"] = baseDirectory,
    ["engineFiles"] = new JsonArray(engineFiles),
    ["engine"] = engine,
};
Console.WriteLine(report.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
return 0;

static string Version() =>
    typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
