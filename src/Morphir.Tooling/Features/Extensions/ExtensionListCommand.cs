using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Extensions;

/// <summary><c>morphir extension list</c>: the extensions that this host knows, as text or one JSON document.</summary>
public sealed class ExtensionListCommand : Command
{
    // --platform and --local select installed extensions, which arrive with PRD step 3. Built-ins always appear.
    private readonly Option<string?> _platform = new("--platform") { Description = "Platform RID for installed extensions (defaults to current)" };
    private readonly Option<bool> _local = new("--local") { Description = "List local (project) installed extensions only; built-ins always appear" };
    private readonly Option<bool> _json = new("--json") { Description = "Output one JSON document" };

    public ExtensionListCommand(IServiceProvider services) : base("list", "List the extensions that this host knows")
    {
        Options.Add(_platform);
        Options.Add(_local);
        Options.Add(_json);
        SetAction(result => Execute(services.GetRequiredService<ExtensionRegistry>(), result.GetValue(_json),
            ConsoleOutputHelper.WriteLineToStdout, Console.Error, TerminalProfile.ForStdout()));
    }

    /// <summary>Writes the list to <paramref name="stdout"/> and diagnostics to <paramref name="stderr"/>; returns the exit code.</summary>
    public static int Execute(ExtensionRegistry registry, bool json, Action<string> stdout, TextWriter stderr, TerminalProfile terminal)
    {
        if (!registry.TryRead(out var catalog, out var error))
        {
            if (json)
                stdout(ExtensionListReport.Failure(registry.Host, error).ToJson());
            else
                foreach (var diagnostic in error.Diagnostics)
                    stderr.WriteLine($"Error: {diagnostic.Code.WireValue}: {diagnostic.Message}");
            return 2;
        }

        var report = ExtensionListReport.From(catalog, registry.WasmRuntime);
        stdout(json ? report.ToJson() : TerminalTable.Render(
            ["ID", "NAME", "VERSION", "TYPES", "DELIVERY", "HOSTS"],
            report.Extensions.Select(entry => (IReadOnlyList<string>)
                [entry.Id, entry.Name, entry.Version, entry.CapabilitySummary, entry.Delivery, string.Join(", ", entry.Hosts)]),
            terminal));
        return 0;
    }
}
