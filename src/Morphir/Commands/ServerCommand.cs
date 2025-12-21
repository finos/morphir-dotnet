using System.CommandLine;

namespace Morphir;

public static partial class Program
{
    /// <summary>
    /// Creates the 'server' command for launching the Morphir server with web UI.
    /// </summary>
    /// <returns>Configured server command</returns>
    public static Command CreateServerCommand()
    {
        var serverCommand = new Command("server", "Launch the Morphir server");

        var portOption = new Option<int?>("--port")
        {
            Description = "Port number to listen on (default: 5000)"
        };
        serverCommand.Options.Add(portOption);

        var urlsOption = new Option<string?>("--urls")
        {
            Description = "URLs to bind to (e.g., http://localhost:5000)"
        };
        serverCommand.Options.Add(urlsOption);

        var environmentOption = new Option<string?>("--environment")
        {
            Description = "Environment name (Development, Production, etc.)"
        };
        serverCommand.Options.Add(environmentOption);

        serverCommand.SetAction(async parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            var urls = parseResult.GetValue(urlsOption);
            var environment = parseResult.GetValue(environmentOption);

            // Build URLs argument
            var urlsArg = urls ?? (port.HasValue ? $"http://localhost:{port}" : "http://localhost:5000");

            // Set environment if provided
            if (!string.IsNullOrEmpty(environment))
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
            }

            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Starting Morphir server on {urlsArg}...");
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Press Ctrl+C to stop the server.");
            Tooling.Infrastructure.ConsoleOutputHelper.WriteLineToStdout("");

            // Find the backend project directory
            var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var backendProjectPath = Path.Combine(solutionRoot, "src", "Morphir.Server", "Morphir.Server.csproj");

            if (!File.Exists(backendProjectPath))
            {
                Console.Error.WriteLine($"Error: Server project not found at {backendProjectPath}");
                return 1;
            }

            // Use dotnet run to launch the backend
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{backendProjectPath}\" --urls {urlsArg}",
                    WorkingDirectory = solutionRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode;
        });

        return serverCommand;
    }
}
