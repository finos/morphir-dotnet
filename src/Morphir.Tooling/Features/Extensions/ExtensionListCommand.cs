using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling.Cli;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// <c>morphir extension list</c>: the extensions that this host knows, as text, as the one <c>completed</c> record of
/// <c>--json</c>, or as one JSON document with <c>--json=document</c>.
/// </summary>
public sealed class ExtensionListCommand : Command, IMorphirParseErrorHandler
{
    /// <summary>The code of a usage error.</summary>
    public const string InvalidArgumentsCode = "extension.invalid_arguments";

    private readonly IServiceProvider _services;
    // --platform and --local select installed extensions, which arrive with PRD step 3. Built-ins always appear.
    private readonly Option<string?> _platform = new("--platform") { Description = "Platform RID for installed extensions (defaults to current)" };
    private readonly Option<bool> _local = new("--local") { Description = "List local (project) installed extensions only; built-ins always appear" };
    private readonly Option<string> _json = JsonOutputOption.Create();

    public ExtensionListCommand(IServiceProvider services) : base("list", "List the extensions that this host knows")
    {
        _services = services;
        Options.Add(_platform);
        Options.Add(_local);
        Options.Add(_json);
        SetAction(result => Execute(services.GetRequiredService<ExtensionRegistry>(), JsonOutputOption.ModeOf(result),
            ConsoleOutputHelper.Stdout, Console.Error, TerminalProfile.ForStdout()));
    }

    /// <inheritdoc />
    public int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages) =>
        Usage(_services.GetRequiredService<HostIdentity>(), messages, JsonOutputOption.ScanTokens([.. result.Tokens.Select(token => token.Value)]), ConsoleOutputHelper.Stdout, Console.Error);

    /// <summary>
    /// Writes a usage error: in a JSON mode the report of <see cref="ExtensionListReport.Usage"/>, else each message on
    /// <paramref name="stderr"/>. Returns 2.
    /// </summary>
    public static int Usage(HostIdentity host, IReadOnlyList<string> messages, JsonOutputMode mode, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (mode != JsonOutputMode.None)
            JsonReportOutput.Write(stdout, mode, ExtensionListReport.Usage(host, InvalidArgumentsCode, messages), ExtensionListReport.JsonInfo);
        else
            foreach (var message in messages) stderr.WriteLine($"Error: {InvalidArgumentsCode}: {message}");
        return 2;
    }

    /// <summary>
    /// Writes the list to <paramref name="stdout"/>, in the JSON output of <paramref name="mode"/> or as a table, and
    /// diagnostics to <paramref name="stderr"/>; returns the exit code.
    /// </summary>
    public static int Execute(ExtensionRegistry registry, JsonOutputMode mode, TextWriter stdout, TextWriter stderr, TerminalProfile terminal)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        if (!registry.TryRead(out var catalog, out var error))
        {
            if (mode != JsonOutputMode.None)
                JsonReportOutput.Write(stdout, mode, ExtensionListReport.Failure(registry.Host, error), ExtensionListReport.JsonInfo);
            else
                foreach (var diagnostic in error.Diagnostics)
                    stderr.WriteLine($"Error: {diagnostic.Code.WireValue}: {diagnostic.Message}");
            return 2;
        }

        var report = ExtensionListReport.From(catalog, () => registry.WasmRuntime.Availability);
        if (mode != JsonOutputMode.None)
            JsonReportOutput.Write(stdout, mode, report, ExtensionListReport.JsonInfo);
        else
            stdout.WriteLine(TerminalTable.Render(
                ["ID", "NAME", "VERSION", "TYPES", "DELIVERY", "HOSTS"],
                report.Extensions.Select(entry => (IReadOnlyList<string>)
                    [entry.Id, entry.Name, entry.Version, entry.CapabilitySummary, entry.Delivery, string.Join(", ", entry.Hosts)]),
                terminal));
        return 0;
    }
}
