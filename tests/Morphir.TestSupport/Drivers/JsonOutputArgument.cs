// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The <c>--json</c> argument that a driver puts on a command line (adopt-streaming-json): the stream (a bare
/// <c>--json</c>), the document (<c>--json=document</c>), or a form that a test of the convention gives on purpose,
/// such as a mode that is not one, a mode after a space, or an option that the command does not have. Read the report
/// of either form with <see cref="JsonOutputAssertions"/>.
/// </summary>
/// <remarks>
/// A driver puts <see cref="Words"/> at the place of its <c>--json</c> argument (before the input of a command with a
/// positional input), and <see cref="Trailing"/> at the end of the command line, where the parser cannot read an
/// unknown option as the value of the positional input.
/// </remarks>
public sealed class JsonOutputArgument
{
    private JsonOutputArgument(string description, IReadOnlyList<string> words, IReadOnlyList<string> trailing)
    {
        Description = description;
        Words = words;
        Trailing = trailing;
    }

    /// <summary>The words of the <c>--json</c> argument, in order.</summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>The words that end the command line, after every other argument.</summary>
    public IReadOnlyList<string> Trailing { get; }

    /// <summary>What the argument asks for, for a failure message.</summary>
    public string Description { get; }

    /// <summary>A bare <c>--json</c>: JSON Lines that end with one <c>completed</c> record.</summary>
    public static JsonOutputArgument Stream { get; } = new("the stream", ["--json"], []);

    /// <summary><c>--json=document</c>: the report alone, as one JSON document.</summary>
    public static JsonOutputArgument Document { get; } = new("the document", ["--json=document"], []);

    /// <summary><c>--json=<paramref name="mode"/></c>, also for a mode that the CLI does not know.</summary>
    public static JsonOutputArgument WithMode(string mode) => new($"the mode {mode}", [$"--json={mode}"], []);

    /// <summary><c>--json <paramref name="mode"/></c>: a mode after a space, which the CLI does not take as the mode.</summary>
    public static JsonOutputArgument Separated(string mode) => new($"the mode {mode} after a space", ["--json", mode], []);

    /// <summary><c>--json-lines</c>, the streaming flag that the convention removed, at the end of the command line.</summary>
    public static JsonOutputArgument FormerStreamingFlag { get; } = new("the former streaming flag", [], ["--json-lines"]);

    /// <summary>This argument, and <paramref name="more"/> at the end of the command line: for example an option that the command does not have.</summary>
    public JsonOutputArgument Then(params string[] more) => new($"{Description}, then {string.Join(' ', more)}", Words, [.. Trailing, .. more]);

    public override string ToString() => string.Join(' ', [.. Words, .. Trailing]);
}
