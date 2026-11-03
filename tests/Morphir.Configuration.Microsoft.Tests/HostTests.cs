// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.Microsoft.Tests;

public sealed class HostTests
{
    [Test]
    public void RegistrationIsExplicitAndLoadsOnlyTheSuppliedTree()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[project]\nname='Sample'\n[custom]\nvalue=7\n",
            ["/repo/morphir.user.toml"] = "[custom]\nvalue=8\n"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo");
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["host:setting"] = "retained" });
        using (var before = (ConfigurationRoot)builder.Build()) before["custom:value"].Should().BeNull();
        builder.AddMorphirConfiguration(tree, discovery.Discovery, options: new ProjectLoadOptions { Isolated = true });
        var observed = tree.ObservedPaths.Count;
        using var configuration = (ConfigurationRoot)builder.Build();
        configuration.Reload();
        tree.ObservedPaths.Count.Should().Be(observed);
        configuration["custom:value"].Should().Be("7");
        configuration["host:setting"].Should().Be("retained");
        tree.ObservedPaths.Should().NotContain("/repo/morphir.user.toml");
        configuration.GetMorphirConfiguration().Sources.ToArray().Should().Contain(s => s.Source.Path == "/repo/morphir.toml");
    }

    [Test]
    public void CollisionRegistrationDoesNotChangeTheExistingBuilder()
    {
        var canonical = MicrosoftConfigurationSteps.Resolve("{custom:{Foo:1,foo:2}}");
        var builder = new ConfigurationBuilder();
        var action = () => builder.AddMorphirConfiguration(canonical);
        action.Should().Throw<MorphirConfigurationException>().Which.Diagnostics.Should().Contain(d => d.Code == "config.microsoft.collision");
        builder.Sources.ToArray().Should().BeEmpty();
        ConfigTree.tryFind(global::Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "custom", "Foo" }), canonical.Effective).Value.Should().Be(ConfigValue.NewInteger(1));
    }

    [Test]
    public void LiteralSeparatorWithoutACompetitorIsRejectedRatherThanReinterpreted()
    {
        var projection = MorphirConfigurationProjection.Create(MicrosoftConfigurationSteps.Resolve("{custom:{'a:b':1}}"));
        projection.IsSuccess.Should().BeFalse();
        projection.Diagnostics.Should().Contain(d => d.Code == "config.microsoft.collision" && d.Paths.Contains("custom.\"a:b\""));
    }

    [Test]
    public void ParentCaseCollisionIsRejectedEvenWhenLeafNamesDiffer()
    {
        var projection = MorphirConfigurationProjection.Create(MicrosoftConfigurationSteps.Resolve("{custom:{Foo:{a:1},foo:{b:2}}}"));
        projection.Diagnostics.Should().Contain(d => d.Paths.Contains("custom.Foo") && d.Paths.Contains("custom.foo"));
    }

    [Test]
    public void InvalidCanonicalResolutionCannotBeRegisteredAsHealthyConfiguration()
    {
        var canonical = MicrosoftConfigurationSteps.Resolve("{ir:{format_version:'wrong'}}");
        canonical.Diagnostics.ToArray().Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
        var projection = MorphirConfigurationProjection.Create(canonical);
        projection.IsSuccess.Should().BeFalse();
        projection.Diagnostics.Should().Contain(d => d.Code == "config.microsoft.invalid_resolution");
    }

    [Test]
    public void SecretReferencesAreOpaqueAndNeverInvoked()
    {
        var canonical = MicrosoftConfigurationSteps.Resolve("{custom:{credential:{command:['must-not-run','--secret']}}}");
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddMorphirConfiguration(canonical).Build();
        configuration["custom:credential"].Should().Be("[REDACTED]");
        configuration.GetSection("custom:credential").GetChildren().Should().BeEmpty();
        ConfigTree.tryFind(global::Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "custom", "credential" }), configuration.GetMorphirConfiguration().Effective)
            .Value.Should().BeOfType<ConfigValue.SecretReference>();
    }

    [Test]
    public void PublishedProviderIsReadOnly()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddMorphirConfiguration(MicrosoftConfigurationSteps.Resolve("{custom:{value:7}}")).Build();
        var action = () => configuration["custom:value"] = "changed";
        action.Should().Throw<NotSupportedException>();
        configuration["custom:value"].Should().Be("7");
    }

    [Test]
    public void ScalarProjectionIsInvariantAndRetainsNestedAndNullValues()
    {
        var canonical = MicrosoftConfigurationSteps.Resolve("{custom:{number:1.25,big:9223372036854775808,nested:[{enabled:true},{empty:{}}],empty:{},nulls:[null,'kept']}}");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddMorphirConfiguration(canonical).Build();
            configuration["custom:number"].Should().Be("1.25");
            configuration["custom:big"].Should().Be("9223372036854775808");
            configuration["custom:nested:0:enabled"].Should().Be("true");
            configuration["custom:nulls:0"].Should().BeNull();
            configuration["custom:nulls:1"].Should().Be("kept");
            var projection = MorphirConfigurationProjection.Create(canonical);
            projection.Values.Should().ContainKey("custom:empty");
            projection.Values.Should().ContainKey("custom:nested:1:empty");
            projection.Values.Should().ContainKey("custom:nulls:0");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public void TemporalProjectionKeepsItsCanonicalTextAndType()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.toml"] = "[custom]\nwhen=1979-05-27\n" });
        var discovery = ProjectSystem.Discover(tree, "/repo");
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddMorphirConfiguration(tree, discovery.Discovery,
            options: new ProjectLoadOptions { Isolated = true }).Build();
        configuration["custom:when"].Should().Be("1979-05-27");
        ConfigTree.tryFind(global::Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "custom", "when" }), configuration.GetMorphirConfiguration().Effective)
            .Value.Should().BeOfType<ConfigValue.Temporal>();
    }

    [Test]
    public void StandardViewUsesSharedSensitiveValueRedaction()
    {
        var canonical = MicrosoftConfigurationSteps.Resolve("{custom:{password:'never-display'}}");
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddMorphirConfiguration(canonical).Build();
        configuration["custom:password"].Should().Be("[REDACTED]");
        ConfigTree.tryFind(global::Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "custom", "password" }), configuration.GetMorphirConfiguration().Effective)
            .Value.Should().Be(ConfigValue.NewString("never-display"));
    }

    [Test]
    public void CanonicalAccessRequiresExplicitRegistration()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Build();
        var action = () => configuration.GetMorphirConfiguration();
        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    [Arguments("['winner']", "winner", 1)]
    [Arguments("[]", null, 0)]
    public void ResolvedArraysMaskEarlierHostIndexes(string targets, string? first, int count)
    {
        var canonical = MicrosoftConfigurationSteps.Resolve("{custom:{targets:" + targets + "}}");
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["custom:targets"] = "old scalar",
                ["custom:targets:0"] = "old zero",
                ["custom:targets:1"] = "stale",
                ["custom:targets:99"] = "stale",
                ["host:unrelated"] = "retained"
            }).AddMorphirConfiguration(canonical).Build();
        configuration.GetSection("custom:targets").GetChildren().Should().HaveCount(count);
        configuration["custom:targets:0"].Should().Be(first);
        configuration["custom:targets:1"].Should().BeNull();
        configuration["custom:targets:99"].Should().BeNull();
        configuration.GetSection("custom").Get<CodegenOptions>()!.Targets.Should().HaveCount(count);
        configuration["host:unrelated"].Should().Be("retained");
    }

    [Test]
    public void ArrayObjectsAndRedactedContainersHideEarlierHostDescendants()
    {
        var canonical = MicrosoftConfigurationSteps.Resolve("{custom:{targets:[{name:'winner'}],secret:{command:['must-not-run']}}}");
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["custom:targets:0:leak"] = "stale",
                ["custom:secret:command:0"] = "must-not-display"
            }).AddMorphirConfiguration(canonical).Build();
        configuration.GetSection("custom:targets:0").GetChildren().Select(s => s.Key).Should().Equal("name");
        configuration["custom:targets:0:leak"].Should().BeNull();
        configuration.GetSection("custom:secret").GetChildren().Should().BeEmpty();
        configuration["custom:secret:command:0"].Should().BeNull();
    }

    [Test]
    [Arguments("{custom:{'':1}}", "custom.\"\"")]
    [Arguments("{'':{custom:1}}", "\"\"")]
    public void EmptySegmentsAreRejectedRatherThanAliasedWithDotNetSections(string text, string path)
    {
        var projection = MorphirConfigurationProjection.Create(MicrosoftConfigurationSteps.Resolve(text));
        projection.IsSuccess.Should().BeFalse();
        projection.Diagnostics.Should().Contain(d => d.Code == "config.microsoft.collision" && d.Paths.Contains(path));
    }

}
