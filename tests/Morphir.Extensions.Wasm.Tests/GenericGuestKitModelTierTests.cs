using Morphir.Conformance.GuestKit;
using Morphir.Mep;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 3.5 of add-wasm-extension-host: the generic guest kit's model tier, pinned under
/// <c>tests/Fixtures/MepGuestKit/</c>, run in-process through <see cref="HostSession"/> and <see cref="WasmChannel"/>
/// by the kit runner of <c>Morphir.Conformance</c>. The host speaks MEP <c>0.2.0-draft.1</c> only (design MEP-1), so
/// the kit's legacy tier is not run and a run is never a complete run of the kit.
/// </summary>
public class GenericGuestKitModelTierTests
{
    static readonly PeerInfo Host = new(PeerKind.Cli, "morphir-dotnet-tests", "0.0.0");

    static GuestKit Kit() =>
        GuestKit.TryParse(File.ReadAllBytes(MepGuestKitFixtures.KitFile), out var kit, out var failure)
            ? kit
            : throw new InvalidOperationException($"The pinned kit does not load: {failure}");

    static ExtismModule Admit(byte[] bytes) =>
        WasmEngine.Shared.Load(bytes).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));


    /// <summary>Opens one <see cref="WasmChannel"/> for each session the runner asks for, so each session is a new
    /// guest instance, as each tier of the kit is a new guest process.</summary>
    sealed class Sessions(ExtismModule module) : IDisposable
    {
        readonly List<(KitSessionRequest Request, WasmChannel Channel)> opened = [];

        public IReadOnlyList<(KitSessionRequest Request, WasmChannel Channel)> Opened => opened;

        public Task<HostSession> OpenAsync(KitSessionRequest request, CancellationToken cancellation)
        {
            var channel = WasmChannel.Open(module, SessionLimits.Default).Match(
                opened => opened,
                failure => throw new InvalidOperationException(failure.Message));
            opened.Add((request, channel));
            return Task.FromResult(new HostSession(channel, Host, request.OfferOrNull()));
        }

        public void Dispose()
        {
            foreach (var (_, channel) in opened)
            {
                channel.Dispose();
            }
        }
    }

    static int Requests(ModelTierRun run, KitTier tier) =>
        run.Steps.Where(step => step.Tier.Equals(tier)).Sum(step => step.Requests);

    [Test]
    public async Task The_model_tier_on_the_Python_guest_gives_the_kits_IR_for_each_claimed_IR_version()
    {
        using var sessions = new Sessions(Admit(await PublishedGuest.BytesAsync()));

        var run = await ModelTierRunner.RunAsync(Kit(), sessions.OpenAsync, CancellationToken.None);
        Console.WriteLine(run.Report());

        await Assert.That(run.Guest).IsEqualTo("morphir-python 0.6.0");
        await Assert.That(run.Language).IsEqualTo("python");
        await Assert.That(run.ModelTierOnly).IsTrue();
        await Assert.That(run.ModelIrVersions).IsEquivalentTo(new[] { "3.0.0", "4.0.0" });

        foreach (var suffix in new[] { "vthree", "vfour" })
        {
            await Assert.That(run.RequiredStep($"compile-model-{suffix}").Outcome.WireValue).IsEqualTo("passed");
            await Assert.That(run.RequiredStep($"roundtrip-model-{suffix}").Outcome.WireValue).IsEqualTo("passed");
            await Assert.That(run.RequiredStep($"roundtrip-model-independent-{suffix}").Outcome.WireValue).IsEqualTo("passed");
        }

        var notPassed = run.Steps.Where(step => step.Outcome.WireValue != "passed").Select(step => $"{step.Id}: {step.Outcome}").ToList();
        await Assert.That(notPassed).IsEmpty();

        await Assert.That(run.LegacyTier).IsNotEmpty();
        foreach (var step in run.LegacyTier)
        {
            await Assert.That(step.Outcome.WireValue).IsEqualTo("not-run");
        }

        // Two guest instances: one for describe, one for the model tier. Each request was one handle call, and no
        // notification reached a guest that advertises none.
        await Assert.That(sessions.Opened.Count).IsEqualTo(2);
        var (describeRequest, describeChannel) = sessions.Opened[0];
        var (modelRequest, modelChannel) = sessions.Opened[1];
        await Assert.That(describeRequest.Purpose.Equals(KitSessionPurpose.Describe)).IsTrue();
        await Assert.That(modelRequest.Purpose.Equals(KitSessionPurpose.ModelTier)).IsTrue();
        await Assert.That(describeChannel.HandleCalls).IsEqualTo(Requests(run, KitTier.Describe));
        await Assert.That(modelChannel.HandleCalls).IsEqualTo(Requests(run, KitTier.Model));
        await Assert.That(describeChannel.HandleCalls).IsEqualTo(1);

        foreach (var (_, channel) in sessions.Opened)
        {
            await Assert.That(channel.DeliversNotifications).IsFalse();
            await Assert.That(channel.InstanceEnded).IsTrue();
            await Assert.That(channel.Failure).IsNull();
        }
    }

    /// <summary>
    /// The SDK fixture guest at the pinned commit claims no model transport, so it speaks MEP <c>0.1</c> only: it
    /// refuses even <c>morphir.extension.describe</c> when the host offers <c>0.2.0-draft.1</c> alone, with
    /// <c>-32011</c>. This host does not offer <c>0.1</c> (design MEP-1), so the kit plans nothing for it here and the
    /// guest is recorded as not run. Running its model tier needs a build of the fixture that claims
    /// <c>0.2.0-draft.1</c> with JSON <c>compileModel</c> and <c>generateModel</c> tuples.
    /// </summary>
    [Test]
    public async Task The_SDK_fixture_guest_speaks_MEP_0_1_only_so_no_model_tier_runs()
    {
        using var sessions = new Sessions(Admit(await SdkFixtureGuest.BytesAsync()));

        var run = await ModelTierRunner.RunAsync(Kit(), sessions.OpenAsync, CancellationToken.None);
        Console.WriteLine(run.Report());
        var describe = run.RequiredStep("describe");
        Console.WriteLine($"describe response: {describe.Response}");

        await Assert.That(describe.Outcome.ToString()).StartsWith("failed: expected a result, got RPC error -32011");
        await Assert.That(describe.Response).Contains("\"extensionVersions\":[\"0.1\"]");
        await Assert.That(run.Language).IsNull();
        await Assert.That(run.ModelIrVersions).IsEmpty();
        await Assert.That(run.ModelTierReason).IsNotNull();
        await Assert.That(run.Steps.Select(step => step.Id).ToList()).IsEquivalentTo(new[] { "describe" });
        await Assert.That(run.LegacyTier).IsEmpty();
        await Assert.That(sessions.Opened.Count).IsEqualTo(1);

        var (_, channel) = sessions.Opened[0];
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
        await Assert.That(channel.DeliversNotifications).IsFalse();
        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.Failure).IsNull();
    }

    [Test]
    public async Task The_pinned_SDK_fixture_module_is_the_build_receipts_digest()
    {
        var bytes = await SdkFixtureGuest.BytesAsync();

        await Assert.That(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))).IsEqualTo(SdkFixtureGuest.Sha256);
        await Assert.That(SdkFixtureGuest.DownloadUrl.Scheme).IsEqualTo("https");
    }
}

/// <summary>The pinned generic guest kit under <c>tests/Fixtures/MepGuestKit/</c>, which the build copies beside the
/// test assembly. Its <c>PROVENANCE.md</c> records the origin; <c>Morphir.Conformance.Tests</c> checks the digests.</summary>
internal static class MepGuestKitFixtures
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "MepGuestKit");

    public static string KitFile => Path.Combine(Root, "guest-generic-json.json");
}
