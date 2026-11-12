// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json.Nodes;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps that install a dotnet tool package from the feed of the workspace into a tool folder, run its command,
/// uninstall it, and read the describe reply of an extension tool (a message framed with <c>Content-Length</c>).
/// </summary>
[Binding]
public sealed class ToolSteps(IPackageDriver packages, IScenarioWorkspace workspace, ScenarioRun run, PackageRun state)
{
    private const string Describe =
        """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}""";

    private JsonNode? _reply;

    private WorkspaceDirectory Tools => state.ToolFolder ??= workspace.Directory("tools");

    private WorkspaceFile Command(string name) => Tools.File(OperatingSystem.IsWindows() ? name + ".exe" : name);

    [When("I install the tool {string} from the feed into a tool folder")]
    public async Task InstallTheTool(string id)
    {
        var config = packages.WriteNuGetConfig(workspace.Directory("tool-feed"), includeNuGetOrg: false);
        // A local feed has no list of versions, so the install names the version.
        run.Result = await packages.DotnetAsync(workspace.Work,
            ["tool", "install", id, "--version", state.VersionOf(id), "--tool-path", Tools.Path, "--configfile", config.Path]);
    }

    [When("I uninstall the tool {string} from the tool folder")]
    public async Task UninstallTheTool(string id) =>
        run.Result = await packages.DotnetAsync(workspace.Work, ["tool", "uninstall", id, "--tool-path", Tools.Path]);

    [When("I run the installed command {string} with {string}")]
    public async Task RunTheInstalledCommand(string command, string argument) =>
        run.Result = await packages.RunAsync(Command(command), [argument]);

    [Then("the tool folder has the command {string}")]
    public void TheToolFolderHasTheCommand(string command)
    {
        if (!workspace.Exists(Command(command)))
            throw new CommandAssertionException($"The tool folder has no command {Command(command).Name}.", run.Result);
    }

    [Then("the tool folder has no command {string}")]
    public void TheToolFolderHasNoCommand(string command)
    {
        if (workspace.Exists(Command(command)))
            throw new CommandAssertionException($"The tool folder still has the command {Command(command).Name}.", run.Result);
    }

    /// <summary>Sends one describe request. Standard input closes after the frame, and the guest exits at the end of its input.</summary>
    [When("I send a describe request to the installed command {string}")]
    public async Task SendADescribeRequest(string command)
    {
        run.Result = await packages.RunAsync(Command(command), [], new PackageCommandOptions
        {
            Timeout = TimeSpan.FromMinutes(1),
            StandardInput = $"Content-Length: {Encoding.UTF8.GetByteCount(Describe)}\r\n\r\n{Describe}",
        });
        _reply = null;
    }

    [Then("the describe reply has the extension id {string}")]
    public void TheReplyHasTheExtensionId(string id) => Expect(Text(Claims()["extension"]?["id"]) == $"\"{id}\"", $"the extension id is not \"{id}\"");

    [Then("the describe reply has the claims version {string}")]
    public void TheReplyHasTheClaimsVersion(string version) =>
        Expect(Text(Claims()["claimsVersion"]) == $"\"{version}\"", $"the claims version is not \"{version}\"");

    [Then("the extension version of the describe reply is the version of the package {string}")]
    public void TheExtensionVersionIsTheVersionOfThePackage(string id) =>
        Expect(Text(Claims()["extension"]?["version"]) == $"\"{state.VersionOf(id)}\"", $"the extension version is not the package version {state.VersionOf(id)}");

    /// <summary>The claimed model transport tuples: the JSON form of the Morphir IR, one for each version, in that order.</summary>
    [Then("the describe reply claims the model transport tuples of {string} for the IR versions {string}")]
    public void TheReplyClaimsTheTuples(string operation, string versions)
    {
        var expected = JsonNode.Parse("[" + string.Join(",", versions.Split(',', StringSplitOptions.TrimEntries).Select(version =>
            $$"""{"operation":"{{operation}}","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"{{version}}"}""")) + "]");
        var tuples = Claims()["capabilities"]?["modelTransport"]?["tuples"];
        Expect(JsonNode.DeepEquals(tuples, expected), $"the tuples are {Text(tuples)}, not {expected!.ToJsonString()}");
    }

    private static string Text(JsonNode? node) => node?.ToJsonString() ?? "null";

    private JsonNode Claims()
    {
        _reply ??= ReadFrame(run.Result);
        return _reply["result"] ?? throw new CommandAssertionException($"The describe reply has no result: {_reply.ToJsonString()}", run.Result);
    }

    /// <summary>The body of the one message on standard output, framed as <c>Content-Length: N</c>, an empty line, and N bytes.</summary>
    private static JsonNode ReadFrame(CommandResult result)
    {
        var output = result.StandardOutput;
        const string header = "Content-Length:";
        var separator = output.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (!output.StartsWith(header, StringComparison.OrdinalIgnoreCase) || separator < 0
            || !int.TryParse(output[header.Length..separator].Trim(), out var length))
            throw new CommandAssertionException("Standard output is not one message framed with Content-Length.", result);

        var body = Encoding.UTF8.GetBytes(output[(separator + 4)..]);
        if (body.Length < length) throw new CommandAssertionException($"The message has {body.Length} bytes, not {length}.", result);
        return JsonNode.Parse(Encoding.UTF8.GetString(body, 0, length))
            ?? throw new CommandAssertionException("The message is the JSON null.", result);
    }

    private void Expect(bool condition, string failure)
    {
        if (!condition) throw new CommandAssertionException($"The describe reply: {failure}.", run.Result);
    }
}
