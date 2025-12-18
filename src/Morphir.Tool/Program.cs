// Entry point for the Morphir.Tool dotnet tool package
// Delegates all command processing to the Morphir namespace

namespace Morphir.Tool;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Delegate to the main Morphir.Program
        return Morphir.Program.Main(args);
    }
}
