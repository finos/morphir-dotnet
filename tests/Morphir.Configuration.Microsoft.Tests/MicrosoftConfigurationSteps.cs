// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.Microsoft.Tests;

[Binding]
public sealed class MicrosoftConfigurationSteps
{
    private DocumentResolution _canonical = null!;
    private IConfigurationRoot _configuration = null!;
    private MorphirConfigurationProjection _projection = null!;

    internal static DocumentResolution Resolve(string lower, string higher = "{}") => Resolver.resolve(new ResolutionInput(
        ListModule.OfSeq(new[] {
            ConfigurationReader.ReadText("lower.json", lower, SourceRole.RootPrimary),
            ConfigurationReader.ReadText("higher.json", higher, SourceRole.UserOverride)
        }), MapModule.Empty<string, string>(), DocumentDefaults.emptyValue,
        DocumentDefaults.profileOptions, ProjectScope.Root, FSharpOption<OwningWorkspace>.None, FSharpList<string>.Empty));

    [AfterScenario]
    public void DisposeConfiguration() => (_configuration as IDisposable)?.Dispose();

    [Given("a canonical lower array replaced by one higher target")]
    public void ReplacedArray() => _canonical = Resolve("{codegen:{targets:['a','b','c']}}", "{codegen:{targets:['winner']}}");

    [Given("a canonical empty targets array and an absent missing array")]
    public void EmptyArray() => _canonical = Resolve("{codegen:{targets:['a','b','c']}}", "{codegen:{targets:[]}}");

    [Given("distinct canonical integer and string values")]
    public void Types() => _canonical = Resolve("{custom:{integer:4,text:'4',nested:{enabled:true}}}");

    [Given("canonical values with a (.*) collision")]
    public void Collision(string kind) => _canonical = Resolve(kind switch
    {
        "case" => "{custom:{Foo:1,foo:2}}",
        "separator" => "{custom:{'a:b':1,a:{b:2}}}",
        "prefix" => "{custom:{'a:b':1,a:2}}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });

    [When("the host opts into Morphir configuration")]
    public void Register() => _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["host:unrelated"] = "preserved" })
        .AddMorphirConfiguration(_canonical).Build();

    [When("the host tries to project Morphir configuration")]
    public void Project() => _projection = MorphirConfigurationProjection.Create(_canonical);

    [Then("standard configuration and generated options contain only the winning target")]
    public void ArrayWinner()
    {
        _configuration["codegen:targets:0"].Should().Be("winner");
        _configuration["codegen:targets:1"].Should().BeNull();
        _configuration.GetSection("codegen").Get<CodegenOptions>()!.Targets.Should().Equal("winner");
    }

    [Then("the unrelated host setting is preserved")]
    public void Unrelated() => _configuration["host:unrelated"].Should().Be("preserved");

    [Then("generated options have an empty targets array and an absent missing array")]
    public void EmptyBound()
    {
        var options = _configuration.GetSection("codegen").Get<CodegenOptions>()!;
        options.Targets.Should().NotBeNull().And.BeEmpty();
        options.Missing.Should().BeNull();
        _configuration.GetMorphirConfiguration().Should().BeSameAs(_canonical);
    }

    [Then("flattened strings agree while canonical types and origins remain distinct")]
    public void Metadata()
    {
        _configuration["custom:integer"].Should().Be("4");
        _configuration["custom:text"].Should().Be("4");
        _configuration["custom:nested:enabled"].Should().Be("true");
        var canonical = _configuration.GetMorphirConfiguration();
        canonical.Should().BeSameAs(_canonical);
        ConfigTree.tryFind(ListModule.OfSeq(new[] { "custom", "integer" }), canonical.Effective).Value.Should().BeOfType<ConfigValue.Integer>();
        ConfigTree.tryFind(ListModule.OfSeq(new[] { "custom", "text" }), canonical.Effective).Value.Should().BeOfType<ConfigValue.String>();
        canonical.Origins[ListModule.OfSeq(new[] { "custom", "integer" })].Source.Path.Should().Be("lower.json");
    }

    [Then("projection fails with original paths and retains the canonical result")]
    public void Rejected()
    {
        _projection.IsSuccess.Should().BeFalse();
        _projection.Canonical.Should().BeSameAs(_canonical);
        _projection.Diagnostics.Should().Contain(d => d.Code == "config.microsoft.collision" && d.Paths.Count >= 2);
        _projection.Values.Should().BeEmpty();
        var action = () => new ConfigurationBuilder().AddMorphirConfiguration(_canonical);
        action.Should().Throw<MorphirConfigurationException>().Which.Canonical.Should().BeSameAs(_canonical);
    }
}

public sealed class CodegenOptions
{
    public string[]? Targets { get; set; }
    public string[]? Missing { get; set; }
}
