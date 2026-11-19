// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Serialization;
using Morphir.MiddleEnd;

namespace Morphir.Tooling.Features.Transform;

/// <summary>A source range of the user's code, one-based, with an exclusive end, as the model has it.</summary>
public sealed record PassSourceRange(int StartLine, int StartColumn, int EndLine, int EndColumn);

/// <summary>One application of a rule or of an upgrade step: the node before, pinned to the input of the pipeline, and the replacement.</summary>
public sealed record PassApplicationReport(string Rule, string Before, string After,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PassSourceRange? Origin);

/// <summary>A read of another module by a pass.</summary>
public sealed record PassLookupReport(string Pass, string From, string Module,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value);

/// <summary>What <c>--json</c> reports about the passes: which ones ran, the application list and the reads of other modules.</summary>
public sealed record PassesReport(string[] Ran, PassApplicationReport[] Applications, PassLookupReport[] Lookups)
{
    internal static PassesReport From(PassReport report) =>
        new([.. report.Passes],
            [.. report.Applications.Select(application => new PassApplicationReport(
                application.Rule.Value,
                NodeAddressModule.ToUri(application.Before),
                NodeAddressModule.ToUri(application.After),
                application.OriginOrNull is { } origin
                    ? new PassSourceRange(origin.StartLine, origin.StartColumn, origin.EndLine, origin.EndColumn)
                    : null))],
            [.. report.Lookups.Select(lookup => new PassLookupReport(
                lookup.Pass,
                lookup.Read.FromText,
                lookup.Read.ModuleText,
                lookup.Read.ValueTextOrNull))]);
}
