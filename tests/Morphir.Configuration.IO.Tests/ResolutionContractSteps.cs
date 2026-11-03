using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO.Tests;

[Binding]
public sealed class ResolutionContractSteps
{
    private readonly List<SourceInput> _sources = [];
    private FSharpMap<string, string> _environment = MapModule.Empty<string, string>();
    private ProfileOptions _profiles = DocumentDefaults.profileOptions;
    private DocumentResolution? _result;
    private bool _called;

    private static FSharpList<T> List<T>(IEnumerable<T> items) => ListModule.OfSeq(items);
    private static ConfigValue At(ConfigValue tree, params string[] path) => ConfigTree.tryFind(List(path), tree).Value;
    private ResolutionInput Input => new(List(_sources), _environment, DocumentDefaults.emptyValue,
        _profiles, ProjectScope.Root, FSharpOption<OwningWorkspace>.None, FSharpList<string>.Empty);

    [Given("lower and higher sources supply different arrays")]
    public void Arrays()
    {
        foreach (var role in new[] { SourceRole.Defaults, SourceRole.System, SourceRole.Global,
                     SourceRole.RootPrimary, SourceRole.Selected, SourceRole.MemberPrimary, SourceRole.UserOverride })
            _sources.Add(ConfigurationReader.ReadText($"{role}.json5", $"{{custom:{{winner:'{role}',items:[1,2,3]}}}}", role));
        _sources.Add(ConfigurationReader.ReadText("last-user.toml", "[custom]\nitems = [9]\n", SourceRole.UserOverride, 1));
        _environment = MapModule.OfSeq([Tuple.Create("MORPHIR_CUSTOM__WINNER", "environment")]);
    }

    [When("the supplied sources are resolved")]
    public void Resolve() => _result = Resolver.resolve(Input);

    [Then("only the higher array and its origin remain")]
    public void ArrayWinner()
    {
        At(_result!.Effective, "custom", "items").Should().Be(ConfigValue.NewArray(List([ConfigValue.NewInteger(9)])));
        _result.Origins[List(["custom", "items"])].Source.Path.Should().Be("last-user.toml");
        At(_result.Effective, "custom", "winner").Should().Be(ConfigValue.NewString("environment"));
    }

    [Given("environment values supply a scalar and its nested setting")]
    public void EnvironmentConflict() => _environment = MapModule.OfSeq([
        Tuple.Create("MORPHIR_IR", "scalar"), Tuple.Create("MORPHIR_IR__STRICT_MODE", "true")]);

    [Then("the shorter environment path wins in either enumeration order")]
    public void EnvironmentWinner()
    {
        At(_result!.Effective, "ir").Should().Be(ConfigValue.NewString("scalar"));
        var reversed = MapModule.OfSeq(_environment.Reverse().Select(item => Tuple.Create(item.Key, item.Value)));
        var second = Resolver.resolve(new ResolutionInput(Input.Sources, reversed, Input.Explicit,
            Input.Profiles, Input.Scope, Input.OwningWorkspace, Input.SuppressWarnings));
        second.Effective.Should().Be(_result.Effective);
    }

    [Given("postgres then metrics are explicitly selected")]
    public void Select() => _profiles = new ProfileOptions(ProfileSelection.NewNames(List(["postgres", "metrics"])), 32, 4096);

    [Given("member postgres and workspace metrics set the same setting")]
    public void ProfileSources()
    {
        _sources.Add(ConfigurationReader.ReadText("postgres.toml", "winner='member postgres'", SourceRole.MemberProfile, profile: "postgres"));
        _sources.Add(ConfigurationReader.ReadText("metrics.toml", "winner='workspace metrics'", SourceRole.WorkspaceProfile, profile: "metrics"));
    }

    [Then("the workspace metrics value wins")]
    public void ProfileWinner() => At(_result!.Effective, "winner").Should().Be(ConfigValue.NewString("workspace metrics"));

    [Given("profile loading is disabled")]
    public void Disabled() => _profiles = new ProfileOptions(ProfileSelection.Disabled, 32, 4096);

    [Given("a supplied inactive profile reader would fail")]
    public void InactiveReader() => _called = false;

    [When("the explicit candidate loader runs")]
    public void RunLoader()
    {
        var source = new SourceIdentity("bad-profile.json", DocumentFormat.Json, SourceRole.WorkspaceProfile, 0,
            FSharpOption<string>.Some("bad"));
        _result = ConfigurationLoader.LoadAndResolve(Input, [new ConfigurationCandidate(source, () =>
        {
            _called = true;
            throw new InvalidOperationException("Inactive reader must not execute");
        })]);
    }

    [Then("the inactive reader has not been called")]
    public void NotCalled()
    {
        _called.Should().BeFalse();
        _result!.ActiveProfiles.ToArray().Should().BeEmpty();
    }
}
