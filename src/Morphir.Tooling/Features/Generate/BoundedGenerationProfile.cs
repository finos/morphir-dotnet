// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;
using Morphir.Extensions.Wasm;
using Morphir.Mep;

namespace Morphir.Tooling.Features.Generate;

/// <summary>An explicit host admission policy for recorded bounded generation.</summary>
public sealed class BoundedGenerationProfile
{
    private BoundedGenerationProfile(string name) => Name = name;
    /// <summary>The normal recorded-generation policy.</summary>
    public static BoundedGenerationProfile Default { get; } = new("default");
    /// <summary>Provisional admission for the manual Orders scale recipe.</summary>
    public static BoundedGenerationProfile OrdersScale { get; } = new("orders-scale");
    /// <summary>The diagnostic spelling of this policy.</summary>
    public string Name { get; }
    /// <summary>Absence preserves normal limits. Only the exact opt-in spelling selects scale admission.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BoundedGenerationProfile? profile)
    {
        profile = text switch { null => Default, "orders-scale" => OrdersScale, _ => null };
        return profile is not null;
    }
    /// <inheritdoc />
    public override string ToString() => Name;
}

internal static class RecordedGenerationAdmission
{
    // These cumulative allowances are provisional, not a measured resource qualification.
    internal static ModelReadLimits ReaderLimits(BoundedGenerationProfile profile, ModelReadLimits normal) =>
        profile == BoundedGenerationProfile.OrdersScale
            ? normal with { MaxBytes = 32 * 1024, MaxEntries = 160_000 }
            : normal;

    // The exchange can include a delivered document and an emitted chunk together.
    internal static long DocumentByteLimit(ModelReadLimits reader, ModelAccessLimits selected) =>
        Math.Min(reader.MaxBytes, (long)selected.MaxDecodedBytesPerExchange);

    // A zero-byte scale reservation still requires admission. Null means the default policy does not opt in.
    internal static long? RequestIdReservation(BoundedGenerationProfile profile, long selectedHostCacheBytes) =>
        profile == BoundedGenerationProfile.OrdersScale ? selectedHostCacheBytes / 2L : null;

    internal static SessionLimits WasmLimits(BoundedGenerationProfile profile, ModelAccessLimits offer)
    {
        var defaults = SessionLimits.Default;
        var wholeFrameBytes = WasmChannel.FrameBytes(offer.MaxFrameBytes);
        return defaults with
        {
            MemoryCeilingBytes = Math.Min(defaults.MemoryCeilingBytes, offer.MaxGuestStateBytes),
            CallDeadline = TimeSpan.FromMilliseconds(offer.MaxCallMilliseconds),
            RequestFrameBytes = profile == BoundedGenerationProfile.OrdersScale
                ? Math.Min(defaults.RequestFrameBytes, wholeFrameBytes) : defaults.RequestFrameBytes,
            ResponseFrameBytes = profile == BoundedGenerationProfile.OrdersScale
                ? Math.Min(defaults.ResponseFrameBytes, wholeFrameBytes) : defaults.ResponseFrameBytes,
        };
    }
    internal static int WasmFrameBodyBytes()
    {
        var adapter = SessionLimits.Default;
        var frameBytes = Math.Min(adapter.RequestFrameBytes, adapter.ResponseFrameBytes);
        return MaxBodyBytesForWholeFrame(frameBytes);
    }

    internal static int MaxBodyBytesForWholeFrame(long frameBytes)
    {
        var headerBytes = System.Text.Encoding.ASCII.GetByteCount($"Content-Length: {frameBytes}\r\n\r\n");
        var body = Math.Max(0L, frameBytes - headerBytes);
        while (body < frameBytes && WasmChannel.FrameBytes(body + 1) <= frameBytes)
            body++;
        return (int)Math.Min(ContentLengthFraming.MaxBodyBytes, body);
    }

    internal static ModelAccessSupport ClampForWasm(BoundedGenerationProfile profile, ModelAccessSupport offer)
    {
        if (profile == BoundedGenerationProfile.OrdersScale)
            return offer;
        var l = offer.Limits;
        var clampedFrame = Math.Min(l.MaxFrameBytes, WasmFrameBodyBytes());
        if (clampedFrame == l.MaxFrameBytes)
            return offer;
        var clamped = new ModelAccessLimits(clampedFrame, l.MaxEncodedBytesPerExchange, l.MaxDecodedBytesPerExchange,
            l.MaxDocumentsPerBatch, l.MaxDepth, l.MaxNodes, l.MaxDiagnosticsPerExchange, l.MaxOutstandingRequests,
            l.MaxGuestStateBytes, l.MaxHostCacheBytes, l.MaxArtifactChunkBytes, l.MaxArtifactBytes,
            l.MaxTotalArtifactBytes, l.MaxSessionDiskBytes, l.MaxSessionWorkUnits, l.MaxRounds,
            l.MaxCallMilliseconds, l.MaxSessionMilliseconds);
        return new ModelAccessSupport(offer.Tuples, clamped);
    }

    internal static ModelAccessLimits Offer(BoundedGenerationProfile profile, ModelReadLimits limits, bool wasm)
    {
        if (profile == BoundedGenerationProfile.OrdersScale)
            return new(64 * 1024, 64 * 1024, 64 * 1024, 1, limits.MaxDepth, limits.MaxNodes,
                64, 1, 1024 * 1024, 64 * 1024 * 1024, 32 * 1024,
                2L * 1024 * 1024, 3L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024,
                100_000_000L, 200_000, 30_000, 7_200_000);

        var frameBodyBytes = wasm ? WasmFrameBodyBytes() : ContentLengthFraming.MaxBodyBytes;
        return new(frameBodyBytes, 4 * 1024 * 1024, (int)Math.Min(limits.MaxBytes, 3 * 1024 * 1024),
            64, limits.MaxDepth, limits.MaxNodes, 64, 64, 64 * 1024 * 1024, 16 * 1024 * 1024, 1024 * 1024,
            4L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024,
            1_000_000, 10_000, 30_000, 120_000);
    }
}
