using System.CommandLine;

namespace Morphir;

internal static class Program
{
    private static int Main(string[] args)
    {
        Option<FileInfo> currentDirectoryOption = new("-C")
        {
            Description = "Override the current directory",
            Required = false
        };
        var rootCommand = new RootCommand("Morphir command line");
        Command infoCommand = new("info", "Get information about the workspace/project");
        rootCommand.Subcommands.Add(infoCommand);
        Command runCommand = new("run", "Run the Morphir compiler");
        runCommand.Options.Add(currentDirectoryOption);
        rootCommand.Subcommands.Add(runCommand);

        infoCommand.SetAction(parseResult => { Console.WriteLine("Morphir command line info:"); });
        runCommand.SetAction(parseResult =>
        {
            var currentDirOverride = parseResult.GetValue(currentDirectoryOption);
            if (currentDirOverride != null)
            {
                Console.WriteLine($"Current directory override: {currentDirOverride.FullName}");
                Console.WriteLine($"Current directory override exists: {currentDirOverride.Exists}");
                
            }
            Console.WriteLine("Morphir command line run:"); 
            
        });
        return rootCommand.Parse(args).Invoke();
    }
}
