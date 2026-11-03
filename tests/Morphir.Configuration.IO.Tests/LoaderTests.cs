using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO.Tests;

public sealed class LoaderTests
{
    [Test]
    public void ExplicitForbiddenControlsFailBeforeAnyProfileReaderExecutes()
    {
        var explicitValues = ((SourceContent.Available)ConfigurationReader.ReadText("explicit.json", "{profiles:{active:['bad']}}", SourceRole.Explicit).Content).Item.Values;
        var input = new ResolutionInput(FSharpList<SourceInput>.Empty, MapModule.Empty<string, string>(), explicitValues,
            new ProfileOptions(ProfileSelection.NewNames(ListModule.OfSeq(new[] { "bad" })), 32, 4096),
            ProjectScope.Root, FSharpOption<OwningWorkspace>.None, FSharpList<string>.Empty);
        var source = new SourceIdentity("bad.json", DocumentFormat.Json, SourceRole.WorkspaceProfile, 0, FSharpOption<string>.Some("bad"));
        var called = false;
        var result = ConfigurationLoader.LoadAndResolve(input, [new ConfigurationCandidate(source, () =>
        {
            called = true;
            return ConfigurationReader.ReadText(source, "{}");
        })]);
        called.Should().BeFalse();
        result.Diagnostics.ToArray().Should().Contain(item => item.Severity == DiagnosticSeverity.Error);
    }
}
