// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Serialization.Metadata;

namespace Morphir.Tooling.Cli;

/// <summary>
/// The machine output of a command that has no events (adopt-streaming-json, JSON-2): in stream mode one
/// <c>completed</c> line, which is also one JSON document, and in document mode the report alone.
/// </summary>
public static class JsonReportOutput
{
    /// <summary>
    /// Writes <paramref name="report"/> to <paramref name="stdout"/> in <paramref name="mode"/>, which must be a JSON
    /// mode, through the source-generated <paramref name="reportInfo"/>.
    /// </summary>
    public static void Write<TReport>(TextWriter stdout, JsonOutputMode mode, TReport report, JsonTypeInfo<TReport> reportInfo) =>
        new JsonOutputWriter<TReport>(stdout, mode, reportInfo).Completed(report);
}
