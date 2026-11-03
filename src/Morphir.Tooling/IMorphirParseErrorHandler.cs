using System.CommandLine;

namespace Morphir.Tooling;

/// <summary>Lets a command retain its output and exit-code contract for parser failures.</summary>
public interface IMorphirParseErrorHandler
{
    int HandleParseErrors(ParseResult result);
}

/// <summary>Lets an opted-in command render host initialization failures after validating its arguments.</summary>
internal interface IMorphirHostInitializationErrorHandler
{
    void HandleHostInitializationFailure();
}
