using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace Morphir.Tooling.Cli;

/// <summary>
/// Option values only with an equals sign (adopt-streaming-json, decision JSON-3). An option with an optional value,
/// such as <c>--json[=stream|document]</c>, would take the next argument as its value in System.CommandLine, so
/// <c>--json model.json</c> would lose the command's input. The CLI instead rewrites a bare flag to
/// <c>--flag=&lt;default&gt;</c> before parsing; the option then always carries its value inline.
/// </summary>
/// <remarks>
/// <para>The rewrite runs after response-file expansion and never after <c>--</c>. System.CommandLine expands
/// response files inside its tokenizer, after which no step sees the arguments, so <see cref="Parse"/> expands them
/// first with the configuration's own <see cref="ParserConfiguration.ResponseFileTokenReplacer"/>, in the
/// tokenizer's order: in place, nested, and not after <c>--</c>. A response file that cannot be read is left as its
/// <c>@</c> argument, and the parser reports it as before.</para>
/// <para>The rewrite is opt-in per command: <see cref="Parse"/> rewrites only the flags of options made by
/// <see cref="CreateOption"/> (as <see cref="JsonOutputOption.Create"/> is) on the command that the arguments
/// name, so a command with a boolean <c>--json</c> keeps its meaning.</para>
/// </remarks>
public static class OptionValueRewrite
{
    private const string DoubleDash = "--";

    /// <summary>
    /// Rewrites each argument that is exactly a flag of <paramref name="bareDefaults"/> to
    /// <c>flag=&lt;default&gt;</c>, up to the first <c>--</c>. Response files must already be expanded.
    /// </summary>
    /// <param name="args">The arguments, after response-file expansion.</param>
    /// <param name="bareDefaults">Each flag with the value that a bare flag means, for example
    /// <c>{"--json": "stream"}</c>.</param>
    public static string[] Apply(string[] args, IReadOnlyDictionary<string, string> bareDefaults)
    {
        if (bareDefaults.Count == 0) return args;
        var rewritten = new string[args.Length];
        var afterDoubleDash = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            afterDoubleDash |= arg == DoubleDash;
            rewritten[i] = !afterDoubleDash && bareDefaults.TryGetValue(arg, out var value) ? $"{arg}={value}" : arg;
        }
        return rewritten;
    }

    /// <summary>
    /// The hint for an unrecognized argument that names one of <paramref name="modes"/>, as when a user writes
    /// <c>--json document</c> on a command without positional arguments: <c>--json=document</c>. Null for any
    /// other argument.
    /// </summary>
    public static string? HintFor(string unrecognizedToken, string flag, IReadOnlyCollection<string> modes) =>
        modes.Contains(unrecognizedToken, StringComparer.Ordinal) ? $"{flag}={unrecognizedToken}" : null;

    /// <summary>
    /// A new option with an optional value from <paramref name="values"/>, given only as
    /// <c>name=&lt;value&gt;</c>; a bare flag means <paramref name="bareDefault"/>.
    /// </summary>
    public static Option<string> CreateOption(string name, string bareDefault, IReadOnlyList<string> values, string description)
    {
        var option = new OptionalValueOption(name, bareDefault, values)
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = description,
            // The help label: ShowValuesAfterAnEqualsSignInHelp turns "--json <stream|document>" into "--json[=stream|document]".
            HelpName = string.Join('|', values),
        };
        option.AcceptOnlyFromAmong([.. values]);
        return option;
    }

    /// <summary>
    /// The messages of the errors of <paramref name="result"/>, for a command that renders its own parse errors. An
    /// unrecognized argument that names a value of an option made by <see cref="CreateOption"/>, and that directly
    /// follows that option given bare, gets the hint of <see cref="HintFor"/>: <c>--json document</c> on a command
    /// without positional arguments says to use <c>--json=document</c>.
    /// </summary>
    /// <remarks>
    /// The host computes these once for every command (<c>MorphirCliApp</c>) and gives them to the command's
    /// <see cref="IMorphirParseErrorHandler"/>; System.CommandLine shows the errors of a command without one, followed by
    /// <see cref="ErrorHints"/>.
    /// </remarks>
    public static IReadOnlyList<string> ErrorMessages(ParseResult result) =>
        [.. result.Errors.Zip(ErrorHints(result), (error, hint) => hint is null ? error.Message : $"{error.Message} {hint}")];

    /// <summary>
    /// The messages of <see cref="ErrorMessages"/> that quote no value that a user gave, for a command whose report
    /// repeats no raw input (configuration and migration, whose values can be secrets): an error of an option made by
    /// <see cref="CreateOption"/>, such as an unknown mode, which lists the modes; an unrecognized argument that
    /// names a mode, with its hint; and an unrecognized argument that is an option name, such as <c>--json-lines</c>.
    /// </summary>
    public static IReadOnlyList<string> RedactedErrorMessages(ParseResult result)
    {
        var messages = ErrorMessages(result);
        var hints = ErrorHints(result);
        var optionNames = result.UnmatchedTokens.Where(IsOptionName).Select(token => $"'{token}'").ToArray();
        return [.. result.Errors.Select((error, index) => (error, index))
            .Where(item => hints[item.index] is not null || OfAnOptionWithABareDefault(item.error.SymbolResult)
                || optionNames.Any(name => item.error.Message.Contains(name, StringComparison.Ordinal)))
            .Select(item => messages[item.index])];
    }

    /// <summary>True for a token that is only an option name, with no value: <c>--json-lines</c>, not <c>--key=value</c>.</summary>
    private static bool IsOptionName(string token) =>
        token.Length > 2 && token.StartsWith(DoubleDash, StringComparison.Ordinal) && char.IsAsciiLetter(token[2])
        && token.Skip(3).All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    /// <summary>True when <paramref name="symbol"/> is, or belongs to, the result of an option made by <see cref="CreateOption"/>.</summary>
    private static bool OfAnOptionWithABareDefault(SymbolResult? symbol)
    {
        for (; symbol is not null; symbol = symbol.Parent)
            if (symbol is OptionResult option) return option.Option is OptionalValueOption;
        return false;
    }

    /// <summary>
    /// The hint of each error of <paramref name="result"/>, in order, or null for an error without one: for example
    /// <c>Give a value with an equals sign: --json=document.</c> for the unrecognized argument of <c>--json document</c>.
    /// </summary>
    public static IReadOnlyList<string?> ErrorHints(ParseResult result)
    {
        var hints = Hints(result);
        return [.. result.Errors.Select(error =>
            hints.FirstOrDefault(hint => error.Message.Contains($"'{hint.Token}'", StringComparison.Ordinal)) is { Hint: { } hint }
                ? $"Give a value with an equals sign: {hint}."
                : null)];
    }

    /// <summary>
    /// Each unrecognized token that names a value of an option made by <see cref="CreateOption"/> and comes right
    /// after that option and the value of its rewrite, with its hint.
    /// </summary>
    private static List<(string Token, string Hint)> Hints(ParseResult result)
    {
        var hints = new List<(string, string)>();
        if (result.UnmatchedTokens.Count == 0) return hints;
        var options = OptionsWithBareDefaults(result.CommandResult);
        var tokens = result.Tokens;
        for (var i = 2; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type != TokenType.Argument || !result.UnmatchedTokens.Contains(token.Value)) continue;
            if (tokens[i - 2].Type != TokenType.Option || !options.TryGetValue(tokens[i - 2].Value, out var option)) continue;
            if (tokens[i - 1].Value != option.BareDefault) continue;
            if (HintFor(token.Value, option.Name, option.Values) is { } hint) hints.Add((token.Value, hint));
        }
        return hints;
    }

    /// <summary>
    /// Makes the help of <paramref name="root"/> and its subcommands show each option made by
    /// <see cref="CreateOption"/> as <c>--json[=stream|document]</c>, the only way to give it a value, instead of
    /// System.CommandLine's <c>--json &lt;stream|document&gt;</c>. Both labels have the same length, so the columns
    /// of the help stay aligned.
    /// </summary>
    public static void ShowValuesAfterAnEqualsSignInHelp(Command root)
    {
        foreach (var help in root.Options.OfType<HelpOption>())
            if (help.Action is HelpAction action)
                help.Action = new EqualsSignHelpAction(action);
    }

    /// <summary>The options of <paramref name="command"/> that <see cref="Parse"/> rewrites, by name and alias.</summary>
    private static Dictionary<string, OptionalValueOption> OptionsWithBareDefaults(CommandResult command)
    {
        var options = new Dictionary<string, OptionalValueOption>(StringComparer.Ordinal);
        var own = true;
        for (SymbolResult? symbol = command; symbol is not null; symbol = symbol.Parent)
        {
            if (symbol is not CommandResult result) continue;
            foreach (var option in result.Command.Options)
            {
                if (option is not OptionalValueOption optional || !(own || option.Recursive)) continue;
                options.TryAdd(option.Name, optional);
                foreach (var alias in option.Aliases) options.TryAdd(alias, optional);
            }
            own = false;
        }
        return options;
    }

    /// <summary>
    /// Parses <paramref name="args"/> with <paramref name="root"/>: expands response files, rewrites the bare flags
    /// of the command that the arguments name, then parses. The entry point of every host parses through this.
    /// </summary>
    public static ParseResult Parse(Command root, IReadOnlyList<string> args, ParserConfiguration? configuration = null)
    {
        configuration ??= new ParserConfiguration();
        var expanded = ExpandResponseFiles(args, configuration.ResponseFileTokenReplacer);
        // The first parse finds the command. An optional value never consumes a subcommand's name, because these
        // options are on the command they belong to, so the command it finds is the one that the rewrite targets.
        var first = root.Parse(expanded, configuration);
        var bareDefaults = BareDefaultsOf(first.CommandResult);
        if (bareDefaults.Count == 0) return first;
        var rewritten = Apply(expanded, bareDefaults);
        return rewritten.SequenceEqual(expanded) ? first : root.Parse(rewritten, configuration);
    }

    /// <summary>Whether <paramref name="option"/> was made by <see cref="CreateOption"/>.</summary>
    internal static bool HasBareDefault(Option option) => option is OptionalValueOption;

    /// <summary>
    /// The bare defaults of the parsed command: its own options made by <see cref="CreateOption"/> and the
    /// recursive ones of its ancestors, each by its name and aliases.
    /// </summary>
    private static Dictionary<string, string> BareDefaultsOf(CommandResult command) =>
        OptionsWithBareDefaults(command).ToDictionary(item => item.Key, item => item.Value.BareDefault, StringComparer.Ordinal);

    /// <summary>
    /// Expands response files as System.CommandLine's tokenizer does: each <c>@name</c> argument before <c>--</c> is
    /// replaced in place by the tokens of its file, which are expanded in turn. An argument whose file cannot be read
    /// is kept, for the parser to report.
    /// </summary>
    private static string[] ExpandResponseFiles(IReadOnlyList<string> args, TryReplaceToken? replacer)
    {
        if (replacer is null) return [.. args];
        var expanded = new List<string>(args.Count);
        var pending = new Stack<string>(args.Reverse());
        var afterDoubleDash = false;
        while (pending.TryPop(out var arg))
        {
            afterDoubleDash |= arg == DoubleDash;
            if (!afterDoubleDash && arg.Length > 1 && arg[0] == '@'
                && replacer(arg[1..], out var replacement, out _) && replacement is not null)
            {
                for (var i = replacement.Count - 1; i >= 0; i--) pending.Push(replacement[i]);
                continue;
            }
            expanded.Add(arg);
        }
        return [.. expanded];
    }

    /// <summary>An option whose bare flag <see cref="Parse"/> rewrites to <c>name=<see cref="BareDefault"/></c>.</summary>
    private sealed class OptionalValueOption(string name, string bareDefault, IReadOnlyList<string> values) : Option<string>(name)
    {
        public string BareDefault { get; } = bareDefault;

        public IReadOnlyList<string> Values { get; } = values;
    }

    /// <summary>
    /// The help action of System.CommandLine, with each <c>name &lt;values&gt;</c> label of an option made by
    /// <see cref="CreateOption"/> on the command of the help written as <c>name[=values]</c>.
    /// </summary>
    private sealed class EqualsSignHelpAction(HelpAction inner) : SynchronousCommandLineAction
    {
        public override bool ClearsParseErrors => inner.ClearsParseErrors;

        public override int Invoke(ParseResult parseResult)
        {
            var configuration = parseResult.InvocationConfiguration;
            var output = configuration.Output;
            using var buffer = new StringWriter();
            configuration.Output = buffer;
            int exitCode;
            try
            {
                exitCode = inner.Invoke(parseResult);
            }
            finally
            {
                configuration.Output = output;
            }

            var text = buffer.ToString();
            foreach (var option in OptionsWithBareDefaults(parseResult.CommandResult).Values.Distinct())
                text = text.Replace($"{option.Name} <{option.HelpName}>", $"{option.Name}[={option.HelpName}]", StringComparison.Ordinal);
            output.Write(text);
            return exitCode;
        }
    }
}
