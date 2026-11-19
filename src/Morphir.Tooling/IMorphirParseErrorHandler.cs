using System.CommandLine;

namespace Morphir.Tooling;

/// <summary>Lets a command retain its output and exit-code contract for parser failures.</summary>
public interface IMorphirParseErrorHandler
{
    /// <summary>Renders the parse errors of <paramref name="result"/> and returns the exit code.</summary>
    /// <param name="result">The parse result, which has at least one error.</param>
    /// <param name="messages">
    /// The message of each error of <paramref name="result"/>, in order, as the host renders it: with the hint of
    /// <see cref="Cli.OptionValueRewrite.ErrorMessages"/>, for example <c>--json=document</c>. A handler that shows the
    /// errors shows these, not the messages of <see cref="ParseResult.Errors"/>.
    /// </param>
    int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages);
}

/// <summary>Lets an opted-in command render host initialization failures after validating its arguments.</summary>
internal interface IMorphirHostInitializationErrorHandler
{
    void HandleHostInitializationFailure();
}
