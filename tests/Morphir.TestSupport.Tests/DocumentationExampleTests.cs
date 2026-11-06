// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.TestSupport.Tests;

/// <summary>Keeps the snippet in docs/content/docs/contributing/qa/test-isolation.md compiling and working.</summary>
public class DocumentationExampleTests
{
    [Test]
    public async Task TestIsolationGuideExample()
    {
        // Begin snippet: docs/content/docs/contributing/qa/test-isolation.md
        await using var sandbox = TestSandbox.Create();
        var project = Directory.CreateDirectory(sandbox.PathOf("work", "app")).FullName;

        var info = ChildProcess.StartInfo("dotnet", "--version");
        info.WorkingDirectory = project;
        sandbox.Apply(info);

        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(60));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        // End snippet
    }
}
