// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>The sample of the pinned crafted corpus that the integration group drives through the worker of each host.</summary>
[Category("PlatformSpecific")]
public class WorkerCorpusTests
{
    [Test]
    public void The_sample_has_every_worker_case_and_every_group()
    {
        var all = WorkerCorpus.CaseIds;
        WorkerCorpus.Sample.Should().OnlyContain(id => all.Contains(id));
        WorkerCorpus.Sample.Should().Contain(all.Where(id => id.StartsWith("worker/", StringComparison.Ordinal)));
        WorkerCorpus.Sample.Select(Group).Distinct().Should().BeEquivalentTo(all.Select(Group).Distinct());
    }

    [Test]
    public void A_module_of_the_corpus_is_a_fixture_of_the_test_project_with_its_pinned_digest()
    {
        var module = WorkerCorpus.ModuleFixture("extism-echo");

        module.Should().StartWith("MepWasm/").And.EndWith(".wasm");
        File.Exists(WorkerCorpus.ModulePath("extism-echo")).Should().BeTrue();
        WorkerCorpus.ModuleSha256("extism-echo").Should().MatchRegex("^[0-9a-f]{64}$");
    }

    private static string Group(string caseId) => caseId[..caseId.IndexOf('/', StringComparison.Ordinal)];
}
