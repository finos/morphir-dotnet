using System.Text.Json;
using Morphir.Mep;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 3.1 of add-wasm-extension-host on the published Python extension <c>0.6.0</c> (design MEP-1): a whole
/// <see cref="HostSession"/> on MEP <c>0.2.0-draft.1</c> through <see cref="WasmChannel"/>, with the generic guest
/// kit's model-transport offer and its <c>compile-model</c> requests.
/// </summary>
public class PythonGuestSessionTests
{
    private const string NegotiatedVersion = "0.2.0-draft.1";

    private static async Task<WasmChannel> OpenAsync()
    {
        var bytes = await PublishedGuest.BytesAsync();
        var module = WasmEngine.Shared.Load(bytes).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));
        return WasmChannel.Open(module, SessionLimits.Default).Match(
            channel => channel,
            failure => throw new InvalidOperationException(failure.Message));
    }

    private static string Failure(HostSession session) =>
        session.FailureOrNull()?.ToString() ?? "none";

    [Test]
    public async Task A_compile_session_on_the_Python_guest_makes_one_handle_call_per_request()
    {
        using var channel = await OpenAsync();
        var session = new HostSession(channel, GenericGuestKit.Host, GenericGuestKit.Offer());
        var run = session.RunAsync(CancellationToken.None);

        var initialized = await session.InitializeAsync(CancellationToken.None);
        var negotiated = initialized.Match(
            value => value,
            failure => throw new InvalidOperationException($"initialize failed: {failure}; session failure: {Failure(session)}"));
        // morphir.initialize was one call; morphir.initialized, which HostSession always sends, was not delivered.
        var callsAfterInitialize = channel.HandleCalls;

        var compiled = new List<JsonElement>();
        foreach (var irVersion in GenericGuestKit.IrVersions)
        {
            using var parameters = JsonDocument.Parse(GenericGuestKit.CompileModelParams(irVersion));
            var reply = await session.RequestAsync(Method.CompileModel, parameters.RootElement.Clone(), CancellationToken.None);
            compiled.Add(reply.Match(
                value => value,
                error => throw new InvalidOperationException($"compileModel {irVersion} failed: {error}; session failure: {Failure(session)}")));
        }

        var callsAfterCompile = channel.HandleCalls;
        var shutdown = await session.ShutdownAsync(CancellationToken.None);
        var callsAfterShutdown = channel.HandleCalls;
        await session.ExitAsync();
        var ended = await run.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(negotiated.ProtocolVersion.Value).IsEqualTo(NegotiatedVersion);
        await Assert.That(negotiated.Extension.Id).IsEqualTo("morphir-python");
        await Assert.That(negotiated.Extension.Version).IsEqualTo("0.6.0");
        await Assert.That(negotiated.Capabilities.ModelTransportOrNull()).IsNotNull();
        await Assert.That(callsAfterInitialize).IsEqualTo(1);
        await Assert.That(callsAfterCompile).IsEqualTo(1 + GenericGuestKit.IrVersions.Count);
        await Assert.That(shutdown.Match(_ => true, _ => false)).IsTrue();
        await Assert.That(callsAfterShutdown).IsEqualTo(2 + GenericGuestKit.IrVersions.Count);
        // morphir.exit ended the instance without a call: the guest advertised nothing.
        await Assert.That(channel.HandleCalls).IsEqualTo(callsAfterShutdown);
        await Assert.That(channel.DeliversNotifications).IsFalse();
        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.Failure).IsNull();
        await Assert.That(session.FailureOrNull()).IsNull();
        await Assert.That(session.State.Match(() => "created", _ => "initialized", _ => "shut-down", () => "exited")).IsEqualTo("exited");
        await Assert.That(ended.Match(() => "end-of-stream", () => "stopped", failure => $"framing {failure}")).IsEqualTo("stopped");

        for (var index = 0; index < compiled.Count; index++)
        {
            var result = compiled[index];
            var irVersion = GenericGuestKit.IrVersions[index];
            await Assert.That(result.GetProperty("success").GetBoolean()).IsTrue();
            var model = result.GetProperty("model");
            await Assert.That(model.GetProperty("envelopeVersion").GetString()).IsEqualTo("0.1.0-draft.1");
            await Assert.That(model.GetProperty("profile").GetString()).IsEqualTo("morphir-ir");
            await Assert.That(model.GetProperty("irVersion").GetString()).IsEqualTo(irVersion);
            await Assert.That(model.GetProperty("payload").GetProperty("codec").GetString()).IsEqualTo("morphir-json");
            await Assert.That(model.GetProperty("payload").GetProperty("content").GetString()).IsNotEmpty();
        }
    }

    [Test]
    public async Task The_pinned_module_is_the_bundle_pin()
    {
        var bytes = await PublishedGuest.BytesAsync();

        await Assert.That(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))).IsEqualTo(PublishedGuest.Sha256);
        await Assert.That(PublishedGuest.DownloadUrl.Scheme).IsEqualTo("https");
    }
}
