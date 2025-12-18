namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// Redirects Console.Out to stderr to prevent logging contamination of stdout.
/// Provides explicit methods for writing command output to stdout.
/// </summary>
public static class ConsoleOutputHelper
{
    private static TextWriter? _originalStdout;
    private static bool _isRedirected;

    /// <summary>
    /// Redirect Console.Out to stderr. Call at application startup before any logging.
    /// </summary>
    public static void RedirectConsoleToStderr()
    {
        if (_isRedirected) return;
        _originalStdout = Console.Out;
        Console.SetOut(Console.Error);
        _isRedirected = true;
    }

    /// <summary>
    /// Write directly to stdout (for command output like JSON results).
    /// </summary>
    public static void WriteToStdout(string text)
    {
        var writer = _originalStdout ?? Console.Out;
        writer.Write(text);
    }

    /// <summary>
    /// Write line directly to stdout.
    /// </summary>
    public static void WriteLineToStdout(string text)
    {
        var writer = _originalStdout ?? Console.Out;
        writer.WriteLine(text);
    }
}

