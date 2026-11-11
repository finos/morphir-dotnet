using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>The extension list and <c>morphir generate</c> must agree on the built-in backends.</summary>
public sealed class ExtensionGenerationAgreementTests
{
    private static readonly HostIdentity Host = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    /// <summary>The disagreements between the registered backends and the entries of a source; empty when they agree.</summary>
    private static List<string> Disagreements(IReadOnlyList<IGenerationBackend> backends, IExtensionSource source)
    {
        var problems = new List<string>();
        var resolver = new RegisteredBackendResolver(backends);
        foreach (var backend in backends)
            if (!source.Entries.Any(entry => entry.Identity.Id.Equals(backend.Descriptor.Id)))
                problems.Add($"backend {backend.Descriptor.Id} has no extension entry");
        foreach (var entry in source.Entries)
        {
            var claim = entry.BackendClaimOrNull;
            if (claim is null)
                continue;
            foreach (var target in claim.Targets)
            {
                var declared = backends.Where(backend => backend.Descriptor.Target.Equals(target))
                    .SelectMany(backend => backend.Descriptor.Capabilities.SelectMany(capability =>
                        capability.Versions.Select(version => new ModelDescriptor(version, capability.Family, capability.Kind))))
                    .ToArray();
                if (!declared.Any(model => resolver.Resolve(new RequestedTarget.Valid(target), model) is Outcome<IGenerationBackend>.Success))
                    problems.Add($"target {target} of {entry.Identity.Id} does not resolve for any declared model");
            }
        }
        return problems;
    }

    [Test]
    public void TheRegisteredBackendsAndTheirEntriesAgree()
    {
        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().Build();
        var backends = app.Services.GetServices<IGenerationBackend>().ToArray();
        backends.Should().HaveCount(2);
        Disagreements(backends, new BackendExtensionSource(backends, Host)).Should().BeEmpty();
    }

    [Test]
    public void TheCheckFailsWhenTheSourceDoesNotSeeABackend()
    {
        IGenerationBackend[] backends = [new CSharpGenerationBackend(), new FSharpGenerationBackend()];
        var partial = new BackendExtensionSource([backends[0]], Host);
        Disagreements(backends, partial).Should().ContainSingle().Which.Should().Contain("morphir.fsharp");
    }
}
