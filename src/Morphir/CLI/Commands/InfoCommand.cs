using JasperFx.CommandLine;

namespace Morphir.CLI.Commands;

/// <summary>
/// Input model for info command (no arguments needed)
/// </summary>
public class InfoInput
{
}

/// <summary>
/// Command to get information about the workspace/project
/// </summary>
[Description("Get information about the workspace/project")]
public class InfoCommand : JasperFxCommand<InfoInput>
{
    public override bool Execute(InfoInput input)
    {
        Console.WriteLine("Morphir command line info:");
        return true;
    }
}
