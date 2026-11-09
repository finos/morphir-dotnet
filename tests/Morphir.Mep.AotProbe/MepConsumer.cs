// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using Morphir.Mep;

namespace Morphir.Mep.AotProbe;

/// <summary>The C# consumer shared by unit tests and the standalone JIT/Native AOT probe.</summary>
public static class MepConsumer
{
    /// <summary>Exercises the public protocol API and returns deterministic output.</summary>
    public static async Task<string> RunAsync()
    {
        var output = new StringBuilder();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (JsonSerializer.IsReflectionEnabledByDefault)
            throw new InvalidOperationException("The probe must use generated codecs.");

        if (!ProtocolVersion.TryParse("0.2.0-draft.1", out var version, out var failure))
            throw new InvalidOperationException(failure.ToString());
        output.AppendLine($"version {version.Value}");
        var wireVersion = JsonSerializer.Serialize(version, MepJson.Default.ProtocolVersion);
        var parsedVersion = JsonSerializer.Deserialize(wireVersion, MepJson.Default.ProtocolVersion)
            ?? throw new InvalidOperationException("No standalone protocol version.");
        if (!parsedVersion.Equals(version)) throw new InvalidOperationException("The scalar codec changed the version.");
        output.AppendLine($"wire-version {wireVersion}");

        if (ProtocolVersion.TryParse(null, out var rejected, out var refused))
            throw new InvalidOperationException($"Null was accepted as {rejected.Value}.");
        output.AppendLine($"parse-failure {refused}");

        var parameters = new DescribeParams([version]);
        MepJson.Default.Verify(MepJson.Default.Options);
        var json = JsonSerializer.Serialize(parameters, MepJson.Default.DescribeParams);
        var decoded = JsonSerializer.Deserialize(json, MepJson.Default.DescribeParams)
            ?? throw new InvalidOperationException("No describe params.");
        if (!decoded.ProtocolVersions[0].Equals(version))
            throw new InvalidOperationException("The generated codec changed the protocol version.");
        output.AppendLine($"describe {json}");
        output.AppendLine($"description {parameters}");

        using var stream = new MemoryStream();
        await ContentLengthFraming.WriteAsync(stream, Encoding.UTF8.GetBytes(json), deadline.Token);
        stream.Position = 0;
        var frame = await ContentLengthFraming.ReadAsync(stream, deadline.Token);
        var payload = frame.Match(
            body => Encoding.UTF8.GetString(body),
            () => throw new InvalidOperationException("Unexpected EOF."),
            error => throw new InvalidOperationException(error.ToString()));
        if (payload != json) throw new InvalidOperationException("The frame changed the JSON.");
        output.AppendLine($"frame {frame}");
        output.AppendLine($"payload {payload}");

        var eof = await ContentLengthFraming.ReadAsync(stream, deadline.Token);
        output.AppendLine(eof.Match(
            _ => throw new InvalidOperationException("Unexpected frame."),
            () => "clean-eof",
            error => throw new InvalidOperationException(error.ToString())));

        using var invalid = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: -1\r\n\r\n"));
        var bad = await ContentLengthFraming.ReadAsync(invalid, deadline.Token);
        output.AppendLine(bad.Match(
            _ => throw new InvalidOperationException("Invalid length was accepted."),
            () => throw new InvalidOperationException("Invalid header was EOF."),
            error => $"framing-failure {error}"));

        return output.ToString();
    }
}
