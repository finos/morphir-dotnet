namespace Morphir.Tooling;

/// <summary>
/// Represents a scenario-based configuration bundle for MorphirCli.
/// Scenarios group related commands, services, and configuration together.
/// </summary>
public interface IMorphirCliScenario
{
    /// <summary>
    /// Configures the MorphirCli builder with commands and services for this scenario.
    /// </summary>
    /// <param name="builder">The MorphirCli builder to configure</param>
    void Configure(MorphirCliBuilder builder);
}
