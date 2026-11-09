// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Morphir.Compilation;
using Morphir.Conformance;
using Morphir.Frontend.FSharp;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Task 4.6, the fold property (STRM-1): for every case of the MCK F# corpus, folding the runner's events gives
/// the same outcome and the same diagnostics as the batch path, which composes the same stage results directly.
/// </summary>
public sealed class CompileFoldPropertyTests
{
    private static readonly Lazy<CaseSet> Corpus = new(ReadCorpus);

    private static CaseSet ReadCorpus()
    {
        var root = Path.Combine(RepositoryRoot(), "conformance");
        var artifacts = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => new Morphir.Conformance.Artifact(Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllBytes(file)));
        var options = new CorpusOptions("frontend-fsharp", "1.1", ListModule.OfSeq(["1", "2", "3", "4"]),
            ListModule.OfSeq(["files", "directory", "project file"]));
        var read = Morphir.Conformance.Corpus.read(options, ListModule.OfSeq(artifacts));
        if (read.IsError) throw new InvalidOperationException(string.Join("\n", read.ErrorValue.Select(issue => issue.Message)));
        return read.ResultValue;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "conformance", "suites"))) return directory.FullName;
        throw new InvalidOperationException("The repository root was not found.");
    }

    [Test]
    public async Task FoldingTheEventsEqualsTheBatchPathForEveryCorpusCase()
    {
        var cases = Corpus.Value.Cases.ToArray();
        cases.Should().NotBeEmpty();
        var differences = new List<string>();
        foreach (var conformanceCase in cases)
        {
            var difference = await Compare(conformanceCase);
            if (difference is not null) differences.Add($"{conformanceCase.Id}: {difference}");
        }
        differences.Should().BeEmpty();
    }

    private static async Task<string?> Compare(ConformanceCase conformanceCase)
    {
        var input = conformanceCase.Input;
        using var workspace = new CompileWorkspace();
        foreach (var document in input.Documents) workspace.Write(document.Uri, document.Text);
        workspace.Write("case.proj", "<Project />");
        workspace.Write("morphir.toml", $"""
            [project]
            name = "{input.Package}"
            exposed_modules = [{string.Join(", ", input.ExposedModules.Select(name => $"\"{name}\""))}]

            [frontend]
            language = "fsharp"

            [frontend.fsharp]
            project_file = "case.proj"
            """);
        var uris = input.Documents.Select(document => document.Uri).ToArray();
        if (input.InputKind == "directory") Array.Sort(uris, StringComparer.Ordinal);
        var provider = new FakeProjectFileProvider(uris);
        var recording = new RecordingFrontend(FSharpFrontend.Registration);
        var runner = CompileRunners.Create([recording], providers: [provider]);

        var events = await CompileRunners.Events(runner, new CompileOverrides { IrVersion = input.IrVersion }, workspace.Context());
        var folded = CompileHandler.Fold(events);

        // The batch path: the same frontend outcome and the writer, composed directly.
        if (recording.Outcome is null) return $"the frontend did not run: {Json(folded.Diagnostics)}";
        var writer = CompileRunners.Writers.Resolve(Morphir.IR.Versioning.parse(input.IrVersion).ResultValue);
        writer.TryGetValue(out var resolved).Should().BeTrue();
        var batch = new List<CompileStageDiagnostic>();
        var success = false;
        byte[]? bytes = null;
        recording.Outcome.Match(
            completed =>
            {
                batch.AddRange(completed.Diagnostics.Select(d => new CompileStageDiagnostic(CompileStage.Frontend, d)));
                if (completed.TryGetValue(out var package))
                {
                    var written = resolved!.Writer.Write(resolved.Target, CompileRunner.PublishedLayout, package.Model);
                    batch.AddRange(written.Diagnostics.Select(d => new CompileStageDiagnostic(CompileStage.Write, d)));
                    success = written.TryGetValue(out bytes);
                }
                return 0;
            },
            failed =>
            {
                batch.AddRange(failed.Diagnostics.Select(d => new CompileStageDiagnostic(CompileStage.Frontend, d)));
                return 0;
            });

        if (folded.Success != success) return $"success {folded.Success}, batch {success}";
        var expected = Json(CompileHandler.Order(batch));
        var actual = Json(folded.Diagnostics);
        if (expected != actual) return $"diagnostics differ:\n  fold  {actual}\n  batch {expected}";
        if (success)
        {
            var published = File.ReadAllBytes(((CompileConclusion.Succeeded)folded.Conclusion).Value.Publication.IrPath);
            if (!published.AsSpan().SequenceEqual(bytes)) return "the published IR differs from the batch writer's bytes";
        }
        else if (folded.ExitCode != 1 && recording.Outcome.Match(_ => true, _ => false)) return $"exit code {folded.ExitCode} for a source failure";
        return null;
    }

    private static string Json(IEnumerable<CompileStageDiagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(d => $"{d.Stage.WireValue()} {d.Diagnostic} data={d.Diagnostic.Data?.GetRawText()} related={string.Join(";", d.Diagnostic.Related)}"));

    /// <summary>Delegates to a frontend and keeps its outcome.</summary>
    private sealed class RecordingFrontend(IFrontend inner) : IFrontend
    {
        public FrontendOutcome? Outcome { get; private set; }
        public ComponentId Identity => inner.Identity;
        public FrontendTransport Transport => inner.Transport;
        public FrontendCapability Capability => inner.Capability;

        public async Task<FrontendOutcome> CompileAsync(CompileRequest request, CancellationToken cancellationToken)
        {
            Outcome = await inner.CompileAsync(request, cancellationToken);
            return Outcome;
        }
    }
}
