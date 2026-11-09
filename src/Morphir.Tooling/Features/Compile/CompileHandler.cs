// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Compilation;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Receives each compile event as it occurs, before the fold completes.</summary>
public interface ICompileEventObserver
{
    /// <summary>Called once for each event, in stream order.</summary>
    ValueTask OnEventAsync(CompileEvent compileEvent, CancellationToken ct);
}

/// <summary>The mediator message of one compile.</summary>
public sealed record CompileCommandRequest(CompileOverrides Overrides, CompileContext Context, ICompileEventObserver? Observer = null);

/// <summary>
/// Owns compile orchestration (CLI-1). It consumes the runner's event stream, gives each event to the observer,
/// and folds the stream into one result. The report is computed only from this fold (STRM-1).
/// </summary>
public static class CompileHandler
{
    /// <summary>Runs one compile and folds its events.</summary>
    public static async Task<CompileResult> Handle(CompileCommandRequest request, ICompileRunner runner, CancellationToken ct)
    {
        var events = new List<CompileEvent>();
        await foreach (var compileEvent in runner.RunAsync(request.Overrides, request.Context, ct).WithCancellation(CancellationToken.None))
        {
            events.Add(compileEvent);
            if (request.Observer is { } observer) await observer.OnEventAsync(compileEvent, CancellationToken.None);
        }
        return Fold(events);
    }

    /// <summary>
    /// Folds a complete event stream: the conclusion of its <see cref="CompileEvent.Completed"/> event and every
    /// reported diagnostic, ordered by stage, then document, then position.
    /// </summary>
    public static CompileResult Fold(IEnumerable<CompileEvent> events)
    {
        var diagnostics = new List<CompileStageDiagnostic>();
        CompileEvent.Completed? completed = null;
        foreach (var compileEvent in events)
        {
            if (completed is not null) throw new InvalidOperationException("An event follows the completed event.");
            switch (compileEvent)
            {
                case CompileEvent.DiagnosticsReported reported:
                    diagnostics.AddRange(reported.Diagnostics.Select(diagnostic => new CompileStageDiagnostic(reported.Stage, diagnostic)));
                    break;
                case CompileEvent.Completed last:
                    completed = last;
                    break;
            }
        }
        if (completed is null) throw new InvalidOperationException("The event stream has no completed event.");
        return new CompileResult(completed.Conclusion, completed.Settings, Order(diagnostics));
    }

    /// <summary>
    /// The report order (DIAG-1): by stage in the STRM-2 order, then by document URI (diagnostics without a
    /// document first), then by position. Diagnostics at the same place keep their emission order.
    /// </summary>
    public static ImmutableArray<CompileStageDiagnostic> Order(IEnumerable<CompileStageDiagnostic> diagnostics) =>
        [.. diagnostics
            .Select((diagnostic, index) => (diagnostic, index))
            .OrderBy(item => item.diagnostic.Stage)
            .ThenBy(item => item.diagnostic.Diagnostic.LocationOrNull?.Uri, StringComparer.Ordinal)
            .ThenBy(item => item.diagnostic.Diagnostic.LocationOrNull?.Range.Start.Line ?? -1)
            .ThenBy(item => item.diagnostic.Diagnostic.LocationOrNull?.Range.Start.Character ?? -1)
            .ThenBy(item => item.index)
            .Select(item => item.diagnostic)];
}
