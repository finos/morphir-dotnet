using System.CommandLine;

namespace Morphir.Tooling;

/// <summary>Lets a command retain its output and exit-code contract for parser failures.</summary>
public interface IMorphirParseErrorHandler
{
    int HandleParseErrors(ParseResult result);
}
