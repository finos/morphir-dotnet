// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>extension list</c> and of its report. The extension commands get their driver interface with the
/// other command families (task 7.2 of the change consolidate-black-box-tests); until then the steps give the
/// arguments of the report type to <see cref="ICliDriver"/>.
/// </summary>
[Binding]
public sealed class ExtensionListSteps(DriverSet drivers, ScenarioRun run, OtherHostRun other)
{
    private ExtensionListReport Report => ExtensionListReport.Of(run.Result);

    [When("I list the extensions as JSON")]
    public async Task ListTheExtensionsAsJson() => run.Result = await drivers.Cli.RunAsync(ExtensionListReport.Arguments);

    [When("I list the extensions as text")]
    public async Task ListTheExtensionsAsText() => run.Result = await drivers.Cli.RunAsync(["extension", "list"]);

    [When("I list the extensions as JSON on the other host")]
    public async Task ListTheExtensionsAsJsonOnTheOtherHost() =>
        other.Result = await drivers.CliOf(drivers.OtherHost).RunAsync(ExtensionListReport.Arguments);

    [Then("the extension list names the host of the run")]
    public void TheListNamesTheHost() => Expect(Report.Host == ExtensionListReport.HostName(drivers.Host),
        $"The extension list names the host {Report.Host}, not {ExtensionListReport.HostName(drivers.Host)}.");

    [Then("the extension list has the ids {string}")]
    public void TheListHasTheIds(string ids) => Expect(Report.Extensions.Select(entry => entry.Id).SequenceEqual(ids.Split(", ")),
        $"The extension list has the ids {string.Join(", ", Report.Extensions.Select(entry => entry.Id))}, not {ids}.");

    [Then("each listed extension except {string} is available in the host")]
    public void EachExtensionIsAvailable(string except)
    {
        var unavailable = Report.Extensions.Where(entry => entry.Id != except && !entry.Available).Select(entry => entry.Id).ToArray();
        Expect(unavailable.Length == 0, $"These extensions are not available: {string.Join(", ", unavailable)}.");
    }

    /// <summary>
    /// The in-process F# frontend is in the managed composition only: the managed host and the tool host have it; the
    /// native <c>morphir</c>, also published as Native AOT or as a single file, lists it as unavailable with the reason.
    /// </summary>
    [Then("the in-process F# frontend is available exactly when the host has it")]
    public void TheInProcessFrontendIsAvailableExactlyWhenTheHostHasIt()
    {
        var entry = Report.Extension("morphir-dotnet.fsharp");
        var expected = drivers.Capabilities.Contains(DriverCapability.InProcessFSharpFrontend);
        Expect(entry.Available == expected, $"morphir-dotnet.fsharp is {(entry.Available ? "" : "not ")}available on the {drivers.Host.Name()} host.");
        if (expected) Expect(!entry.HasReason, $"morphir-dotnet.fsharp is available and has the reason '{entry.Reason}'.");
        else Expect(entry.Reason?.Contains("morphir-fsharp-frontend", StringComparison.Ordinal) == true,
            $"The reason of morphir-dotnet.fsharp does not name morphir-fsharp-frontend: '{entry.Reason}'.");
    }

    [Then("the extension list names the deployment of the target")]
    public void TheListNamesTheDeploymentOfTheTarget()
    {
        var (deployment, execution) = ExtensionListReport.ExpectedDeploymentOf(drivers.Target);
        Expect((deployment is null ? !Report.HasDeployment : Report.Deployment == deployment) && Report.Execution == execution,
            $"The extension list has the deployment {Report.Deployment ?? "(none)"} and the execution {Report.Execution}, not {deployment ?? "(none)"} and {execution}.");
    }

    [Then("the extension {string} has the types {string}")]
    public void TheExtensionHasTheTypes(string id, string types) =>
        Expect(Report.Extension(id).Types.SequenceEqual(types.Split(", ")), $"{id} has the types {string.Join(", ", Report.Extension(id).Types)}, not {types}.");

    [Then("the extension {string} has the delivery {string} and the hosts {string}")]
    public void TheExtensionHasTheDeliveryAndTheHosts(string id, string delivery, string hosts)
    {
        var entry = Report.Extension(id);
        Expect(entry.Delivery == delivery && entry.Hosts.SequenceEqual(hosts.Split(", ")),
            $"{id} has the delivery {entry.Delivery} and the hosts {string.Join(", ", entry.Hosts)}, not {delivery} and {hosts}.");
    }

    [Then("the extension {string} claims the validator IR versions {string}")]
    public void TheExtensionClaimsTheValidatorIrVersions(string id, string versions)
    {
        var claimed = Report.Extension(id).ValidatorIrVersions;
        Expect(claimed is { } list && list.SequenceEqual(versions.Split(", ")),
            $"{id} claims the validator IR versions {(claimed is { } some ? string.Join(", ", some) : "(none)")}, not {versions}.");
    }

    [Then("the extensions that are available in both hosts are equal in both lists")]
    public void TheSharedExtensionsAreEqual()
    {
        other.Result.ShouldExitWith(0);
        var mine = Report;
        var theirs = ExtensionListReport.Of(other.Result);
        Expect(mine.Host != theirs.Host, $"Both lists name the host {mine.Host}.");
        // The entries available in both lists; an entry that one host lacks differs in available and reason.
        var available = theirs.Extensions.Where(entry => entry.Available).ToDictionary(entry => entry.Id, entry => entry.RawJson);
        var shared = mine.Extensions.Where(entry => entry.Available && available.ContainsKey(entry.Id)).ToArray();
        Expect(shared.Length > 0, "No extension is available in both hosts.");
        var different = shared.Where(entry => entry.RawJson != available[entry.Id]).Select(entry => entry.Id).ToArray();
        Expect(different.Length == 0, $"These entries differ between the hosts: {string.Join(", ", different)}.\n{other.Result.Describe()}");
    }

    private void Expect(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
