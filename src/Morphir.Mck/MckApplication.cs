using System.CommandLine;

namespace Morphir.Mck;

/// <summary>Structured usage errors owned by the development command adapter.</summary>
public interface IMckParseErrorHandler
{
    int HandleParseErrors(ParseResult parsed);
}

/// <summary>The dedicated development MCK command boundary.</summary>
public static class MckApplication
{
    /// <summary>Runs a development command with separate result and diagnostic streams.</summary>
    public static Task<int> RunAsync(string[] arguments, TextWriter stdout, TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var root = new RootCommand("[experimental, internal] Development Morphir Compatibility Kit");
        root.Subcommands.Add(MckCommandProvider.Create(stdout.WriteLine, stderr));
        var normalized = arguments.Length > 0 && arguments[0] == "mck" ? arguments : ["mck", .. arguments];
        var parsed = root.Parse(normalized);
        if (parsed.Errors.Count > 0)
        {
            if (parsed.CommandResult.Command is IMckParseErrorHandler handler)
                return Task.FromResult(handler.HandleParseErrors(parsed));
            stderr.WriteLine(string.Join(" ", parsed.Errors.Select(error => error.Message)));
            return Task.FromResult(2);
        }
        return parsed.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr }, cancellationToken);
    }
}
