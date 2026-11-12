using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Validate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// Task 2.3: <c>extension list --json</c> names the running offering beside the host, states its prerequisites, and
/// states for each entry the offerings that provide it and why it is unavailable here.
/// </summary>
public sealed class OfferingReportTests
{
    private static readonly ReleaseVersion Version = ReleaseVersion.Create("0.4.0");

    private static NonEmptySet<Deployment> Set(params Deployment[] deployments) => NonEmptySet<Deployment>.Create(deployments[0], deployments[1..]);

    /// <summary>The entry points of this repository and the offerings each one is published as.</summary>
    private static readonly NonEmptySet<Deployment> AotEntryPoint = Set(Deployment.NativeAot);
    private static readonly NonEmptySet<Deployment> ManagedEntryPoint = Set(Deployment.SelfContained, Deployment.FrameworkDependent);
    private static readonly NonEmptySet<Deployment> ToolEntryPoint = Set(Deployment.DotnetTool);

    private static HostIdentity Built(Deployment deployment) =>
        deployment.Equals(Deployment.NativeAot) ? HostIdentity.Create(MorphirHost.Native, Version, AotEntryPoint, BuildProfile.Of(deployment, Platform.WinArm64), ExecutionMode.NativeAot)
        : deployment.Equals(Deployment.DotnetTool) ? HostIdentity.Create(MorphirHost.DotnetTool, Version, ToolEntryPoint, BuildProfile.Portable(deployment), ExecutionMode.Jit)
        : HostIdentity.Create(MorphirHost.Native, Version, ManagedEntryPoint, BuildProfile.Of(deployment, Platform.WinArm64), ExecutionMode.Jit);

    /// <summary>The composition of every host: the generation backends, the built-in frontends and the analyzers.</summary>
    private static ExtensionRegistry Registry(HostIdentity host) =>
        new([
            new BackendExtensionSource([new FSharpGenerationBackend(), new CSharpGenerationBackend()], host),
            new FrontendExtensionSource(host),
            new AnalyzerExtensionSource(new AnalyzerRegistry(host)),
        ], host, WasmRuntime.Process);

    private static JsonDocument List(HostIdentity host)
    {
        var stdout = new List<string>();
        ExtensionListCommand.Execute(Registry(host), true, stdout.Add, new StringWriter(), TerminalProfile.Plain).Should().Be(0);
        return JsonDocument.Parse(string.Join('\n', stdout));
    }

    private static JsonElement Entry(JsonDocument document, string id) =>
        document.RootElement.GetProperty("extensions").EnumerateArray().Single(entry => entry.GetProperty("id").GetString() == id);

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    [Test]
    [Arguments("native-aot", "morphir", "native-aot", "win-arm64")]
    [Arguments("self-contained", "morphir", "jit", "win-arm64")]
    [Arguments("framework-dependent", "morphir", "jit", "win-arm64")]
    [Arguments("dotnet-tool", "dotnet-morphir", "jit", null)]
    public void EachOfferingNamesItsDeploymentBesideTheHost(string deployment, string host, string execution, string? platform)
    {
        using var document = List(Built(Deployment.All.Single(candidate => candidate.WireValue == deployment)));
        var root = document.RootElement;
        root.GetProperty("host").GetString().Should().Be(host, "the host stays the command name");
        root.GetProperty("deployment").GetString().Should().Be(deployment);
        root.GetProperty("execution").GetString().Should().Be(execution);
        if (platform is null)
            root.TryGetProperty("platform", out _).Should().BeFalse("the tool package is portable");
        else
            root.GetProperty("platform").GetString().Should().Be(platform);
    }

    [Test]
    public void StandaloneOfferingsStateNoRuntimePrerequisite()
    {
        foreach (var deployment in new[] { Deployment.NativeAot, Deployment.SelfContained })
        {
            using var document = List(Built(deployment));
            var prerequisites = document.RootElement.GetProperty("prerequisites");
            prerequisites.GetProperty("runtime").GetArrayLength().Should().Be(0, deployment.WireValue);
            prerequisites.GetProperty("sdk").GetArrayLength().Should().Be(0, deployment.WireValue);
            prerequisites.GetProperty("native").GetArrayLength().Should().Be(0, deployment.WireValue);
        }
    }

    [Test]
    public void TheFrameworkDependentOfferingStatesTheRuntimeOfItsRuntimeConfiguration()
    {
        using var document = List(Built(Deployment.FrameworkDependent));
        var prerequisites = document.RootElement.GetProperty("prerequisites");
        var runtime = prerequisites.GetProperty("runtime").EnumerateArray().Single();
        runtime.GetProperty("kind").GetString().Should().Be("runtime");
        runtime.GetProperty("name").GetString().Should().Be("Microsoft.NETCore.App");
        Strings(runtime.GetProperty("versions")).Should().Equal(">=11.0.0", "<12.0.0");
        prerequisites.GetProperty("sdk").GetArrayLength().Should().Be(0, "running needs no SDK");
    }

    [Test]
    public void TheToolStatesTheRuntimeToRunAndTheSdkToInstall()
    {
        using var document = List(Built(Deployment.DotnetTool));
        var prerequisites = document.RootElement.GetProperty("prerequisites");
        prerequisites.GetProperty("runtime").EnumerateArray().Single().GetProperty("name").GetString().Should().Be("Microsoft.NETCore.App");
        var sdk = prerequisites.GetProperty("sdk").EnumerateArray().Single();
        sdk.GetProperty("kind").GetString().Should().Be("sdk");
        sdk.GetProperty("name").GetString().Should().Be("Microsoft.NET.Sdk");
        sdk.GetProperty("phase").GetString().Should().Be("install", "the SDK is an installation prerequisite, not a runtime one");
        Strings(sdk.GetProperty("versions")).Should().Equal(">=11.0.100");
    }

    [Test]
    public void ABuildNoNamedProfileProducedStatesNoDeployment()
    {
        // A development build, a test host, or the legacy single-file publish: never one of the four names.
        var host = HostIdentity.Create(MorphirHost.Native, Version, AotEntryPoint, BuildProfile.NoProfile, ExecutionMode.Jit);
        using var document = List(host);
        var root = document.RootElement;
        root.GetProperty("host").GetString().Should().Be("morphir");
        root.TryGetProperty("deployment", out _).Should().BeFalse();
        root.TryGetProperty("platform", out _).Should().BeFalse();
        root.TryGetProperty("prerequisites", out _).Should().BeFalse("prerequisites belong to an offering");
        root.GetProperty("execution").GetString().Should().Be("jit");
    }

    [Test]
    public void EveryEntryStatesTheOfferingsThatProvideItAndItsOwnPrerequisites()
    {
        using var document = List(Built(Deployment.SelfContained));
        foreach (var id in new[] { "morphir.csharp", "morphir.fsharp", "morphir.type-validator" })
            Strings(Entry(document, id).GetProperty("deployments")).Should().Equal("native-aot", "self-contained", "framework-dependent", "dotnet-tool");
        Strings(Entry(document, "morphir-dotnet.fsharp").GetProperty("deployments")).Should().Equal("self-contained", "framework-dependent", "dotnet-tool");

        foreach (var entry in document.RootElement.GetProperty("extensions").EnumerateArray())
        {
            var prerequisites = entry.GetProperty("prerequisites");
            foreach (var kind in new[] { "runtime", "sdk", "native" })
                prerequisites.GetProperty(kind).GetArrayLength().Should().Be(0, "a built-in needs nothing beyond its offering");
        }
    }

    [Test]
    public void TheNativeAotOfferingListsTheFSharpFrontendAsUnavailableAndSaysWhy()
    {
        using var document = List(Built(Deployment.NativeAot));
        var frontend = Entry(document, "morphir-dotnet.fsharp");
        Strings(frontend.GetProperty("types")).Should().Equal("frontend");
        frontend.GetProperty("delivery").GetString().Should().Be("built-in");
        frontend.GetProperty("available").GetBoolean().Should().BeFalse();
        frontend.GetProperty("reason").GetString().Should().Contain("morphir-fsharp-frontend").And.Contain("--extension");
    }

    [Test]
    [Arguments("self-contained")]
    [Arguments("framework-dependent")]
    [Arguments("dotnet-tool")]
    public void TheManagedOfferingsHaveTheFSharpFrontend(string deployment)
    {
        using var document = List(Built(Deployment.All.Single(candidate => candidate.WireValue == deployment)));
        var frontend = Entry(document, "morphir-dotnet.fsharp");
        frontend.GetProperty("available").GetBoolean().Should().BeTrue();
        frontend.TryGetProperty("reason", out _).Should().BeFalse();
        var language = frontend.GetProperty("capabilities").GetProperty("frontend").GetProperty("languages").EnumerateArray().Single();
        language.GetProperty("id").GetString().Should().Be("fsharp");
        Strings(language.GetProperty("fileExtensions")).Should().Contain(".fs");
        Strings(frontend.GetProperty("capabilities").GetProperty("frontend").GetProperty("irVersions")).Should().NotBeEmpty();
    }

    [Test]
    public void EveryOfferingReportsTheSameSharedEntriesApartFromItsIdentity()
    {
        var documents = Deployment.All.Select(deployment => (deployment, List(Built(deployment)))).ToList();
        try
        {
            foreach (var (first, firstDocument) in documents)
                foreach (var (second, secondDocument) in documents)
                {
                    var available = firstDocument.RootElement.GetProperty("extensions").EnumerateArray()
                        .Where(entry => entry.GetProperty("available").GetBoolean())
                        .Select(entry => entry.GetProperty("id").GetString()!)
                        .Intersect(secondDocument.RootElement.GetProperty("extensions").EnumerateArray()
                            .Where(entry => entry.GetProperty("available").GetBoolean())
                            .Select(entry => entry.GetProperty("id").GetString()!))
                        .ToList();
                    available.Should().NotBeEmpty();
                    foreach (var id in available)
                        Entry(firstDocument, id).GetRawText().Should().Be(Entry(secondDocument, id).GetRawText(), $"{id} in {first} and {second}");
                }
        }
        finally
        {
            foreach (var (_, document) in documents) document.Dispose();
        }
    }
}
