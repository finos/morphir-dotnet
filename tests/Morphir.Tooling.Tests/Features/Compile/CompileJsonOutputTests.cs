// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using Morphir.Compilation;
using Morphir.Tooling.Cli;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// The exact bytes of compile's JSON output (adopt-streaming-json, task 3.1; decision JSON-2): the report alone with
/// <c>--json=document</c>, and the <c>stageStarted</c>, <c>diagnostics</c>, <c>stageCompleted</c> and
/// <c>completed</c> lines with <c>--json</c>. The expected text is written here from the record shape of
/// <c>docs/content/docs/cli/compile.md</c>: this is product output, not a contract of the parent repository.
/// </summary>
public sealed class CompileJsonOutputTests
{
    private const string Message = "PackageReference 'Newtonsoft.Json' is ignored.";

    private static readonly Diagnostic Warning = Diagnostic.Create(DiagnosticSeverity.Warning,
        DiagnosticCode.Create("msbuild.ignored_package_reference"), Message,
        Location.Create("Acme.CarRental.fsproj", Morphir.Compilation.Range.Create(Position.Create(0, 0), Position.Create(0, 7))));

    /// <summary>A successful compile of one document with one project-file warning.</summary>
    private static readonly CompileReport Report = new(true, 0, "fsharp", "morphir-dotnet.fsharp (in-process)", "4",
        new CompileReportSources("src/Acme.CarRental.fsproj", "src", ["Rates.fs"]), ["Rates"], ".morphir/out/compile.dest/morphir-ir.json", [],
        [CompileReportDiagnostic.From(Warning)], null,
        new Dictionary<string, CompileReportOrigin>(StringComparer.Ordinal) { ["language"] = new("cli", "--language", null, null) });

    private const string Diagnostics =
        "[{\"severity\":\"warning\",\"code\":\"msbuild.ignored_package_reference\",\"message\":\"PackageReference 'Newtonsoft.Json' is ignored.\","
        + "\"location\":{\"uri\":\"Acme.CarRental.fsproj\",\"range\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":0,\"character\":7}}}}]";

    private const string CompactReport =
        "{\"success\":true,\"exitCode\":0,\"language\":\"fsharp\",\"frontend\":\"morphir-dotnet.fsharp (in-process)\",\"irVersion\":\"4\","
        + "\"sources\":{\"projectFile\":\"src/Acme.CarRental.fsproj\",\"root\":\"src\",\"documents\":[\"Rates.fs\"]},\"modules\":[\"Rates\"],"
        + "\"output\":\".morphir/out/compile.dest/morphir-ir.json\",\"installed\":[],\"diagnostics\":" + Diagnostics + ",\"failureKind\":null,"
        + "\"origins\":{\"language\":{\"kind\":\"cli\",\"flag\":\"--language\"}}}";

    /// <summary>The indented report, with <c>\n</c> line ends; the serializer ends its lines with the platform's.</summary>
    private const string IndentedReport = """
        {
          "success": true,
          "exitCode": 0,
          "language": "fsharp",
          "frontend": "morphir-dotnet.fsharp (in-process)",
          "irVersion": "4",
          "sources": {
            "projectFile": "src/Acme.CarRental.fsproj",
            "root": "src",
            "documents": [
              "Rates.fs"
            ]
          },
          "modules": [
            "Rates"
          ],
          "output": ".morphir/out/compile.dest/morphir-ir.json",
          "installed": [],
          "diagnostics": [
            {
              "severity": "warning",
              "code": "msbuild.ignored_package_reference",
              "message": "PackageReference 'Newtonsoft.Json' is ignored.",
              "location": {
                "uri": "Acme.CarRental.fsproj",
                "range": {
                  "start": {
                    "line": 0,
                    "character": 0
                  },
                  "end": {
                    "line": 0,
                    "character": 7
                  }
                }
              }
            }
          ],
          "failureKind": null,
          "origins": {
            "language": {
              "kind": "cli",
              "flag": "--language"
            }
          }
        }
        """;

    private static byte[] Written(JsonOutputMode mode, Func<JsonLinesWriter, JsonOutputWriter<CompileReport>, Task> write)
    {
        using var stdout = new MemoryStream();
        var output = JsonLinesWriter.Output(stdout, mode);
        write(new JsonLinesWriter(output), output).GetAwaiter().GetResult();
        return stdout.ToArray();
    }

    [Test]
    public void TheDocumentIsTheIndentedReportAndOneLineEnd()
    {
        var bytes = Written(JsonOutputMode.Document, (records, output) =>
        {
            output.Completed(Report);
            return Task.CompletedTask;
        });

        Encoding.UTF8.GetString(bytes).Should().Be(IndentedReport.ReplaceLineEndings(Environment.NewLine) + "\n");
        bytes[0].Should().Be((byte)'{', "stdout starts with the report, not a byte order mark");
    }

    [Test]
    public async Task TheDocumentModeWritesNoEvent()
    {
        using var stdout = new MemoryStream();
        var records = new JsonLinesWriter(JsonLinesWriter.Output(stdout, JsonOutputMode.Document));

        await records.WriteEventAsync(new CompileEvent.StageStarted(CompileStage.Sources), CancellationToken.None);
        await records.WriteEventAsync(new CompileEvent.DiagnosticsReported(CompileStage.Sources, "Acme.CarRental.fsproj", [Warning]), CancellationToken.None);

        stdout.Length.Should().Be(0);
    }

    [Test]
    public void EachStreamRecordIsOneCompactLine()
    {
        var bytes = Written(JsonOutputMode.Stream, async (records, output) =>
        {
            await records.WriteEventAsync(new CompileEvent.StageStarted(CompileStage.Sources), CancellationToken.None);
            await records.WriteEventAsync(new CompileEvent.DiagnosticsReported(CompileStage.Sources, "Acme.CarRental.fsproj", [Warning]), CancellationToken.None);
            await records.WriteEventAsync(new CompileEvent.StageCompleted(CompileStage.Sources, true), CancellationToken.None);
            await records.WriteEventAsync(new CompileEvent.Completed(new CompileConclusion.Failed(new CompileFailure(CompileFailureKind.Source, "unused")), null),
                CancellationToken.None);
            output.Completed(Report);
        });

        Encoding.UTF8.GetString(bytes).Split('\n').Should().Equal(
            "{\"event\":\"stageStarted\",\"stage\":\"sources\"}",
            "{\"event\":\"diagnostics\",\"stage\":\"sources\",\"document\":\"Acme.CarRental.fsproj\",\"diagnostics\":" + Diagnostics + "}",
            "{\"event\":\"stageCompleted\",\"stage\":\"sources\",\"passed\":true}",
            "{\"event\":\"completed\",\"report\":" + CompactReport + "}",
            "");
    }

    [Test]
    public void ADiagnosticsRecordWithoutADocumentHasNoDocumentMember()
    {
        var bytes = Written(JsonOutputMode.Stream, (records, _) =>
            records.WriteEventAsync(new CompileEvent.DiagnosticsReported(CompileStage.Settings, null, ImmutableArray<Diagnostic>.Empty), CancellationToken.None).AsTask());

        Encoding.UTF8.GetString(bytes).Should().Be("{\"event\":\"diagnostics\",\"stage\":\"settings\",\"diagnostics\":[]}\n");
    }
}
