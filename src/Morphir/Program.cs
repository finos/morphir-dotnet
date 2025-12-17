using System.Text.Json;
using System.Text.Json.Serialization;
using JasperFx.CommandLine;

namespace Morphir;

// JSON source generator context for AOT compilation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Tooling.Features.VerifyIR.VerifyIRResult))]
internal partial class MorphirJsonContext : JsonSerializerContext
{
}

internal static class Program
{
    private static int Main(string[] args)
    {
        // Set console encoding to UTF-8 to support Unicode characters (✓, ✗)
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Handle --version flag explicitly to output only the version string
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            Console.WriteLine(VersionInfo.Version);
            return 0;
        }

        // Execute JasperFx commands
        // This will automatically discover commands from the Morphir.CLI.Commands namespace
        var executor = CommandExecutor.For(factory =>
        {
            // Register all commands from this assembly
            factory.RegisterCommands(typeof(Program).Assembly);
        });

        return executor.Execute(args);
    }
}
