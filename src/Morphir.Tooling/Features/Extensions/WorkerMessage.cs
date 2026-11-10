// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The worker's own message checks (contract rule <c>worker:message-checks</c>): a request or a response must be UTF-8
/// text holding one JSON object. Anything else is fault <c>failure</c>. The worker does not check the JSON-RPC version
/// or ids, which are host-session checks; it looks inside a request only for its <c>id</c>, to tell a request from a
/// notification, and its <c>method</c> (design WRK-2).
/// </summary>
/// <param name="IsRequest">Whether the message has an <c>id</c> member: a request, which gets one frame.</param>
/// <param name="Method">The <c>method</c> member when it is a string, else empty.</param>
public readonly record struct WorkerMessage(bool IsRequest, string Method)
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>No nesting limit that a real message could reach: the reader keeps its depth in a bit stack, not on the
    /// call stack, and IR documents nest deeply.</summary>
    static readonly JsonReaderOptions Options = new() { MaxDepth = 1_000_000 };

    /// <summary>Reads a request or notification, or says why it is not UTF-8 text holding one JSON object.</summary>
    public static bool TryRead(ReadOnlySpan<byte> body, out WorkerMessage message, [NotNullWhen(false)] out string? problem)
    {
        message = default;
        if (!IsOneObject(body, out problem, out var isRequest, out var method)) return false;
        message = new WorkerMessage(isRequest, method);
        return true;
    }

    /// <summary>Whether <paramref name="body"/> is UTF-8 text holding one JSON object, or why not.</summary>
    public static bool IsOneObject(ReadOnlySpan<byte> body, [NotNullWhen(false)] out string? problem) =>
        IsOneObject(body, out problem, out _, out _);

    static bool IsOneObject(ReadOnlySpan<byte> body, [NotNullWhen(false)] out string? problem, out bool hasId, out string method)
    {
        hasId = false;
        method = string.Empty;
        try
        {
            StrictUtf8.GetCharCount(body);
        }
        catch (DecoderFallbackException)
        {
            problem = "it is not UTF-8 text";
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(body, Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                problem = "it is not one JSON object";
                return false;
            }

            while (reader.Read())
            {
                if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName) continue;
                var isId = reader.ValueTextEquals("id"u8);
                var isMethod = reader.ValueTextEquals("method"u8);
                if (!reader.Read()) break;
                if (isId) hasId = true;
                if (isMethod && reader.TokenType == JsonTokenType.String) method = reader.GetString() ?? string.Empty;
                reader.Skip();
            }

            // Anything after the object other than white space is a second value, which the reader refuses here.
            if (reader.TokenType != JsonTokenType.EndObject || reader.CurrentDepth != 0 || reader.Read())
            {
                problem = "it is not one JSON object";
                return false;
            }
        }
        catch (JsonException error)
        {
            problem = $"it is not one JSON object ({error.Message})";
            return false;
        }

        problem = null;
        return true;
    }
}
