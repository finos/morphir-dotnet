using System.CommandLine;
using System.CommandLine.Parsing;

namespace Morphir.Tooling.Cli;

/// <summary>The machine output that <c>--json[=stream|document]</c> selects.</summary>
public enum JsonOutputMode
{
    /// <summary>No <c>--json</c>: the command writes its human output.</summary>
    None,

    /// <summary>JSON Lines, ending with one <c>completed</c> record that holds the report. A bare <c>--json</c>.</summary>
    Stream,

    /// <summary>The report alone, as one JSON document: <c>--json=document</c>.</summary>
    Document,
}

/// <summary>
/// The shared <c>--json[=stream|document]</c> option (adopt-streaming-json, decisions JSON-1 and JSON-3). A mode is
/// given only with <c>=</c>: <see cref="OptionValueRewrite"/> rewrites a bare <c>--json</c> to <c>--json=stream</c>
/// before parsing, so the option never takes the next argument.
/// </summary>
public static class JsonOutputOption
{
    /// <summary>The option's name.</summary>
    public const string Name = "--json";

    private const string StreamMode = "stream";
    private const string DocumentMode = "document";

    /// <summary>The modes, in the order that an unknown-mode error lists them.</summary>
    public static IReadOnlyList<string> Modes { get; } = [StreamMode, DocumentMode];

    /// <summary>
    /// A new <c>--json</c> option for one command: zero or one value, one of <see cref="Modes"/>, with the bare
    /// default <c>stream</c>.
    /// </summary>
    public static Option<string> Create() =>
        OptionValueRewrite.CreateOption(Name, StreamMode, Modes,
            "Output JSON: --json streams JSON Lines that end with the report; --json=document writes the report alone");

    /// <summary>
    /// The mode of a parse result: <see cref="JsonOutputMode.None"/> when the parsed command has no
    /// <c>--json</c> option of this kind or it was not given. A value that is not a mode (a parse error) gives
    /// <see cref="JsonOutputMode.Stream"/>, the bare default.
    /// </summary>
    public static JsonOutputMode ModeOf(ParseResult result)
    {
        for (SymbolResult? symbol = result.CommandResult; symbol is not null; symbol = symbol.Parent)
        {
            if (symbol is not CommandResult command) continue;
            foreach (var option in command.Command.Options)
            {
                if (option.Name != Name || !OptionValueRewrite.HasBareDefault(option)) continue;
                if (result.GetResult(option) is not { Implicit: false } given) continue;
                return given.Tokens.Count == 0 ? JsonOutputMode.Stream : ModeOfValue(given.Tokens[^1].Value);
            }
        }
        return JsonOutputMode.None;
    }

    /// <summary>
    /// The mode that a command line asks for, for a renderer of parse errors, which has no valid parse result.
    /// Takes either the arguments (after <see cref="OptionValueRewrite"/>) or the token values of a parse result,
    /// which splits <c>--json=document</c> into <c>--json</c> and <c>document</c>. Scanning stops at <c>--</c>; the
    /// last <c>--json</c> wins; a value that is not a mode gives <see cref="JsonOutputMode.Stream"/>.
    /// </summary>
    public static JsonOutputMode ScanTokens(IReadOnlyList<string> args)
    {
        var mode = JsonOutputMode.None;
        for (var i = 0; i < args.Count && args[i] != "--"; i++)
        {
            var arg = args[i];
            if (arg == Name)
                mode = i + 1 < args.Count && Modes.Contains(args[i + 1]) ? ModeOfValue(args[i + 1]) : JsonOutputMode.Stream;
            else if (arg.Length > Name.Length && arg.StartsWith(Name, StringComparison.Ordinal) && arg[Name.Length] is '=' or ':')
                mode = ModeOfValue(arg[(Name.Length + 1)..]);
        }
        return mode;
    }

    private static JsonOutputMode ModeOfValue(string value) =>
        value == DocumentMode ? JsonOutputMode.Document : JsonOutputMode.Stream;
}
