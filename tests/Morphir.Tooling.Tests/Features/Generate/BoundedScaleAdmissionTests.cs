// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class BoundedScaleAdmissionTests
{
    [Test]
    [Arguments(null, true)]
    [Arguments("orders-scale", true)]
    [Arguments("default", false)]
    [Arguments("Orders-Scale", false)]
    [Arguments("orders-scale ", false)]
    public void ProfileSelectionIsExplicitAndExact(string? text, bool accepted)
    {
        BoundedGenerationProfile.TryParse(text, out var profile).Should().Be(accepted);
        if (accepted) profile.Should().BeSameAs(text is null
            ? BoundedGenerationProfile.Default : BoundedGenerationProfile.OrdersScale);
    }

    [Test]
    public void DefaultReaderAndSessionCeilingsArePreserved()
    {
        var profile = BoundedGenerationProfile.Default;
        RecordedGenerationAdmission.ReaderLimits(profile, ModelReadLimits.Default).Should().BeSameAs(ModelReadLimits.Default);
        var limits = RecordedGenerationAdmission.Offer(profile, ModelReadLimits.Default, wasm: false);
        limits.MaxRounds.Should().Be(10_000);
        limits.MaxSessionMilliseconds.Should().Be(120_000);
        limits.MaxSessionWorkUnits.Should().Be(1_000_000);
        limits.MaxHostCacheBytes.Should().Be(16 * 1024 * 1024);
        RecordedGenerationAdmission.RequestIdReservation(limits.MaxHostCacheBytes).Should().Be(8L * 1024 * 1024);
        var wasm = RecordedGenerationAdmission.WasmLimits(profile, limits);
        wasm.RequestFrameBytes.Should().Be(Morphir.Extensions.Wasm.SessionLimits.Default.RequestFrameBytes);
        wasm.ResponseFrameBytes.Should().Be(Morphir.Extensions.Wasm.SessionLimits.Default.ResponseFrameBytes);
        ModelReadLimits.Default.MaxEntries.Should().Be(10_000);
        ModelReadLimits.Default.MaxBytes.Should().Be(64L * 1024 * 1024);
    }

    [Test]
    public void EveryProfileReservesHalfOfNegotiatedHostCacheForRequestIds()
    {
        RecordedGenerationAdmission.RequestIdReservation(1).Should().Be(0L);
        RecordedGenerationAdmission.RequestIdReservation(4096).Should().Be(2048L);
    }

    [Test]
    public void ScaleOffersBoundedDocumentsFramesAndProvisionalCumulativeAllowances()
    {
        var profile = BoundedGenerationProfile.OrdersScale;
        var reader = RecordedGenerationAdmission.ReaderLimits(profile, ModelReadLimits.Default);
        var limits = RecordedGenerationAdmission.Offer(profile, reader, wasm: true);
        reader.MaxBytes.Should().Be(32768);
        reader.MaxEntries.Should().Be(160_000).And.BeGreaterThan(139_275);
        limits.MaxDecodedBytesPerExchange.Should().Be(65536);
        RecordedGenerationAdmission.DocumentByteLimit(reader, limits).Should().Be(32768);
        limits.MaxFrameBytes.Should().Be(65536);
        limits.MaxEncodedBytesPerExchange.Should().Be(65536);
        limits.MaxDocumentsPerBatch.Should().Be(1);
        limits.MaxOutstandingRequests.Should().Be(1);
        limits.MaxArtifactChunkBytes.Should().Be(32768);
        limits.MaxArtifactBytes.Should().BeGreaterThanOrEqualTo(1_065_025L);
        limits.MaxTotalArtifactBytes.Should().BeGreaterThanOrEqualTo(2_181_171_200L);
        limits.MaxSessionDiskBytes.Should().Be(8L * 1024 * 1024 * 1024);
        limits.MaxRounds.Should().Be(200_000).And.BeGreaterThan(157_825);
        limits.MaxSessionWorkUnits.Should().Be(100_000_000L);
        limits.MaxSessionMilliseconds.Should().Be(7_200_000);
        limits.MaxGuestStateBytes.Should().Be(2 * 1024 * 1024);
        var wasm = RecordedGenerationAdmission.WasmLimits(profile, limits);
        wasm.RequestFrameBytes.Should().Be(65536);
        wasm.ResponseFrameBytes.Should().Be(65536);
        wasm.MemoryCeilingBytes.Should().Be(2 * 1024 * 1024);
        var ids = RecordedGenerationAdmission.RequestIdReservation(limits.MaxHostCacheBytes);
        var discovery = limits.MaxHostCacheBytes / 4L;
        ids.Should().Be(32L * 1024 * 1024);
        (limits.MaxHostCacheBytes - ids - discovery).Should().Be(16L * 1024 * 1024);
        // Scalar accounting only: the maximum recipe ID needs at most seven UTF-16 code units.
        (256L + (128L + 2L * 7) * 157_825L).Should().BeLessThan(ids);
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task ResolverCarriesTheTypedSelectionWithoutIncreasingWholeModelDefaults()
    {
        var result = await new CliGenerationSettingsResolver().ResolveAsync(
            new("record-placeholder", "text", "generated") { BoundedProfile = "orders-scale" },
            new(System.IO.Path.GetTempPath()), default);
        var settings = result.Should().BeOfType<Outcome<GenerationSettings>.Success>().Which.Value;
        settings.BoundedProfile.Should().BeSameAs(BoundedGenerationProfile.OrdersScale);
        settings.Limits.Should().BeSameAs(ModelReadLimits.Default);
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task UnknownProfileIsAnInvocationFailure()
    {
        var result = await new CliGenerationSettingsResolver().ResolveAsync(
            new("record-placeholder", "text", "generated") { BoundedProfile = "orders-scale-extra" },
            new(System.IO.Path.GetTempPath()), default);
        result.Should().BeOfType<Outcome<GenerationSettings>.Failure>().Which.Error.Diagnostics[0].Code
            .Should().Be("generate.invalid_bounded_profile");
    }
    [Test]
    [Category("PlatformSpecific")]
    public async Task ExplicitInputCannotUseScaleOrFallBackToTheWholeModelLoader()
    {
        var loader = new RejectingLoader();
        var result = await GenerateHandler.Handle(new(
                new("model.json", "text", "generated") { BoundedProfile = "orders-scale" },
                new(System.IO.Path.GetTempPath())),
            new CliGenerationSettingsResolver(), loader, new RegisteredBackendResolver([]), new GenerationPublisher(), default);
        result.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Which.Error.Diagnostics[0].Code
            .Should().Be("generate.invalid_bounded_profile");
        loader.Calls.Should().Be(0);
    }

    [Test]
    public async Task RefusingTheNonRecordedRouteReleasesItsInputLease()
    {
        var lease = new ProbeLease();
        var loader = new RejectingLoader();
        var settings = new GenerationSettings(new("not-opened"), RequestedTarget.Parse("text"), BackendOptions.Empty,
            new("not-written"), ModelReadLimits.Default)
        { BoundedProfile = BoundedGenerationProfile.OrdersScale, InputLease = lease };
        var result = await GenerateHandler.Handle(new(new(), new("not-used")), new FixedSettings(settings),
            loader, new RegisteredBackendResolver([]), new GenerationPublisher(), default);
        result.Should().BeOfType<Outcome<GenerationSuccess>.Failure>();
        lease.Disposed.Should().BeTrue();
        loader.Calls.Should().Be(0);
    }

    private sealed class ProbeLease : ModelInputLease
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) => Disposed = disposing;
    }

    private sealed class FixedSettings(GenerationSettings settings) : IGenerationSettingsResolver
    {
        public ValueTask<Outcome<GenerationSettings>> ResolveAsync(GenerateOverrides input, InvocationContext context, CancellationToken ct) =>
            ValueTask.FromResult<Outcome<GenerationSettings>>(new Outcome<GenerationSettings>.Success(settings));
    }
    [Test]
    public void ProductionDocumentAndChunkShareTheExchangeAllowanceWithoutRaisingTheDocumentCap()
    {
        var profile = BoundedGenerationProfile.OrdersScale;
        var reader = RecordedGenerationAdmission.ReaderLimits(profile, ModelReadLimits.Default);
        var selected = RecordedGenerationAdmission.Offer(profile, reader, wasm: true);
        const int documentBytes = 16_560;
        const int chunkBytes = 16_385;
        (documentBytes + chunkBytes).Should().Be(32_945).And.BeGreaterThan(32_768);
        selected.MaxDecodedBytesPerExchange.Should().BeGreaterThanOrEqualTo(documentBytes + chunkBytes);
        RecordedGenerationAdmission.DocumentByteLimit(reader, selected).Should().Be(32_768);
        var encoded = 4 * ((documentBytes + 2) / 3) + 4 * ((chunkBytes + 2) / 3);
        encoded.Should().Be(43_928).And.BeLessThanOrEqualTo(selected.MaxEncodedBytesPerExchange);
        var defaults = RecordedGenerationAdmission.Offer(BoundedGenerationProfile.Default, ModelReadLimits.Default, wasm: true);
        RecordedGenerationAdmission.DocumentByteLimit(ModelReadLimits.Default, defaults).Should().Be(defaults.MaxDecodedBytesPerExchange);
    }
    [Test]
    [Category("PlatformSpecific")]
    public async Task ProductionOrdersScaleProfileAdmitsExactMaxFrameBytesAndRefusesOneByteOverThroughRealWasmAdapter()
    {
        var profile = BoundedGenerationProfile.OrdersScale;
        var reader = RecordedGenerationAdmission.ReaderLimits(profile, ModelReadLimits.Default);
        var offer = RecordedGenerationAdmission.Offer(profile, reader, wasm: true);
        var wasmLimits = RecordedGenerationAdmission.WasmLimits(profile, offer);

        var moduleBytes = Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes(
            Morphir.TestSupport.Fixtures.FixtureCatalog.WasmHost.ExtismEchoModule);
        var admitted = Morphir.Extensions.Wasm.WasmEngine.Shared.Load(moduleBytes).Match(
            compiled => Morphir.Extensions.Wasm.WasmHost.Admit(compiled, Morphir.Extensions.Wasm.WasmAbi.ExtismKernel).Match(
                static ok => ok,
                static err => throw new InvalidOperationException(err.Message)),
            static err => throw new InvalidOperationException(err.Message),
            static notWasm => throw new InvalidOperationException(notWasm));

        var exactBodyLength = RecordedGenerationAdmission.MaxBodyBytesForWholeFrame(offer.MaxFrameBytes);
        exactBodyLength.Should().Be(65511);

        using (var channel = Morphir.Extensions.Wasm.WasmChannel.Open(admitted, wasmLimits).Match(
            static ok => ok,
            static err => throw new InvalidOperationException(err.Message)))
        {
            var exactBody = new byte[exactBodyLength];
            Array.Fill(exactBody, (byte)'x');
            var reply = await channel.RequestAsync("morphir.shutdown", exactBody, CancellationToken.None);
            reply.Should().NotBeNull();
            reply!.Length.Should().Be(exactBodyLength);
        }

        using (var overChannel = Morphir.Extensions.Wasm.WasmChannel.Open(admitted, wasmLimits).Match(
            static ok => ok,
            static err => throw new InvalidOperationException(err.Message)))
        {
            var oversizedBody = new byte[exactBodyLength + 1];
            Array.Fill(oversizedBody, (byte)'x');
            var act = async () => await overChannel.RequestAsync("morphir.shutdown", oversizedBody, CancellationToken.None);
            var thrown = (await act.Should().ThrowAsync<Morphir.Extensions.Wasm.WasmChannelException>()).Which;
            thrown.Failure.Should().BeOfType<Morphir.Extensions.Wasm.WasmChannelFailure.RequestOverLimit>();
        }
    }

    private sealed class RejectingLoader : IModelLoader
    {
        public int Calls { get; private set; }
        public ValueTask<Outcome<LoadedModel>> LoadAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("Scale admission must not load the whole model.");
        }
    }
}
