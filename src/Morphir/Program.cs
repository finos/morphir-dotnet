using System.CommandLine;

namespace Morphir;

internal static class Program
{
    private static int Main(string[] args)
    {
        var rootCommand = new RootCommand("Morphir command line");
        Command infoCommand = new("info", "Get information about the workspace/project");
        rootCommand.Subcommands.Add(infoCommand);

        infoCommand.SetAction(parseResult => { Console.WriteLine("Morphir command line info:"); });
        return rootCommand.Parse(args).Invoke();
    }
}
