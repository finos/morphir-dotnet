// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;

namespace Morphir.TestSupport.Drivers;

/// <summary>The runtime configuration of a host with the dynamic-code feature switch off (<see cref="CliRunOptions.WithoutDynamicCode"/>).</summary>
internal static class DynamicCode
{
    /// <summary>The name of the runtime switch that is false under Native AOT.</summary>
    public const string Switch = "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported";

    /// <summary>
    /// Writes a copy of the runtime configuration of <paramref name="assembly"/> with <see cref="Switch"/> false to
    /// the temporary directory of the workspace, and returns its path. The build output is not changed.
    /// </summary>
    public static string RuntimeConfigurationWithoutDynamicCode(string assembly, IScenarioWorkspace workspace)
    {
        var source = Path.ChangeExtension(assembly, ".runtimeconfig.json");
        if (!File.Exists(source)) throw new TestSessionException($"The host {assembly} has no runtime configuration {source}.");

        var configuration = JsonNode.Parse(File.ReadAllText(source))!;
        var options = configuration["runtimeOptions"]!.AsObject();
        if (options["configProperties"] is not JsonObject properties) options["configProperties"] = properties = new JsonObject();
        properties[Switch] = false;
        var target = workspace.Temp.File(Path.GetFileNameWithoutExtension(assembly) + ".no-dynamic-code.runtimeconfig.json");
        workspace.WriteText(target, configuration.ToJsonString());
        return target.Path;
    }
}
