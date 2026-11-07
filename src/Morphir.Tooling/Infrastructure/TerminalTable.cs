using Spectre.Console;
using Spectre.Console.Rendering;

namespace Morphir.Tooling.Infrastructure;

/// <summary>How a table is drawn: with or without ANSI styling, and how wide.</summary>
/// <param name="Ansi">True to style with ANSI escape codes (a terminal that is not redirected, and no <c>NO_COLOR</c>).</param>
/// <param name="Width">The width in columns that the table may use.</param>
public sealed record TerminalProfile(bool Ansi, int Width)
{
    /// <summary>Plain text at a fixed width: for redirected output and tests, so the text does not depend on the terminal.</summary>
    public static TerminalProfile Plain { get; } = new(false, 160);

    /// <summary>The profile of the process's real stdout.</summary>
    public static TerminalProfile ForStdout()
    {
        if (Console.IsOutputRedirected)
            return Plain;
        int width;
        try { width = Console.WindowWidth; }
        catch (IOException) { width = Plain.Width; }
        return new(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")), Math.Max(width, 40));
    }
}

/// <summary>
/// Renders tabular CLI output as a TUI-styled table. List commands use it for their text output; <c>--json</c> is the
/// stable format for scripts.
/// </summary>
public static class TerminalTable
{
    /// <summary>The table as text, without a trailing line break.</summary>
    public static string Render(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, TerminalProfile profile)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = profile.Ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = profile.Ansi ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = profile.Width;

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        foreach (var header in headers)
            table.AddColumn(new TableColumn(new Text(header, new Style(decoration: Decoration.Bold))));
        // Text, not Markup: cell values are data and must never be read as markup.
        foreach (var row in rows)
            table.AddRow(row.Select(cell => (IRenderable)new Text(cell)).ToArray());

        console.Write(table);
        return writer.ToString().TrimEnd('\r', '\n');
    }
}
