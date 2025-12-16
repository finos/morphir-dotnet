using FluentAssertions;
using Reqnroll;

namespace Morphir.E2E.Tests.Features;

[Binding]
public class ExecutableBasicCommandsSteps
{
    [Then("the output should match the semantic version pattern")]
    public void ThenTheOutputShouldMatchTheSemanticVersionPattern()
    {
        SharedSteps.ExecutionResult.Should().NotBeNull("command should have been executed");
        var output = SharedSteps.ExecutionResult!.CombinedOutput.Trim();
            // Semantic version pattern: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
        var semverPattern = @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-(0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(\.(0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*)?(\+[0-9a-zA-Z-]+(\.[0-9a-zA-Z-]+)*)?$";
        output.Should().MatchRegex(semverPattern,
            $"output '{output}' should match semantic version pattern");
    }
}

