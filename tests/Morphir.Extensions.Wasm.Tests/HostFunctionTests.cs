using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Morphir.TestSupport;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 3.3 of add-wasm-extension-host: the host-function policy of the pinned contract and the Morphir functions
/// with their limits. <c>http_request</c> opens no connection and records its method and URL, the variable store
/// and the IR cache enforce their limits and belong to one session, and the private workspace lives exactly as long
/// as its session.
/// </summary>
public class HostFunctionTests
{
    private static SessionStore Open(ExtismModule module, string workspaceRoot) =>
        SessionStore.Open(module, SessionLimits.Default, workspaceRoot).Match(
            opened => opened,
            failure => throw new InvalidOperationException(failure.Message));

    private static WasmChannel OpenChannel(ExtismModule module, string workspaceRoot) =>
        WasmChannel.Open(module, SessionLimits.Default, workspaceRoot).Match(
            opened => opened,
            failure => throw new InvalidOperationException(failure.Message));

    private static string Reply(HandleOutcome outcome) => outcome.Match(
        completed => Encoding.UTF8.GetString(completed.Result.Output),
        failed => throw new InvalidOperationException($"The call failed: {failed.Message}"),
        notRun => throw new InvalidOperationException($"The call did not run: {notRun.Message}"));

    private static GuestFailure Failed(HandleOutcome outcome) => outcome.Match(
        completed => throw new InvalidOperationException($"The call completed with {Encoding.UTF8.GetString(completed.Result.Output)}"),
        failed => failed,
        notRun => throw new InvalidOperationException($"The call did not run: {notRun.Message}"));

    private static HandleOutcome Call(SessionStore store, string step) => store.CallHandle(CraftedGuest.Input(step));

    [Test]
    public async Task Http_request_fails_the_session_and_records_the_method_and_the_URL()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-http-");
        using var store = Open(CraftedGuest.Admit("https://example.org/kit"), sandbox.Work);

        var failure = Failed(Call(store, CraftedGuest.Step.Http));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.IncompleteSession);
        await Assert.That(failure.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(failure.Detail).Contains("http_request");
        await Assert.That(store.RefusedHttpRequests.Count).IsEqualTo(1);
        await Assert.That(store.RefusedHttpRequests[0]).IsEqualTo(new RefusedHttpRequest("POST", "https://example.org/kit"));
        var output = store.GuestOutput.Outputs.Single();
        await Assert.That(output.Source).IsEqualTo(GuestOutputSource.RefusedHttp);
        await Assert.That(output.Text).Contains("POST");
        await Assert.That(output.Text).Contains("https://example.org/kit");
        // The guest never got past http_request: its "done" is not the output.
        await Assert.That(Call(store, CraftedGuest.Step.VarGet).Match(_ => "ran", _ => "failed", _ => "not run")).IsEqualTo("not run");
    }

    [Test]
    public async Task Http_request_makes_no_connection_attempt()
    {
        // A listener on the loopback interface that the guest names. If the host connected, the listener would have a
        // pending connection; nothing leaves the machine either way.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"http://127.0.0.1:{port}/kit";
        using var sandbox = TestSandbox.Create("morphir-wasm-http-");
        using var store = Open(CraftedGuest.Admit(url), sandbox.Work);

        var failure = Failed(Call(store, CraftedGuest.Step.Http));
        var accepted = listener.AcceptTcpClientAsync();
        var connected = await Task.WhenAny(accepted, Task.Delay(TimeSpan.FromMilliseconds(500))) == accepted;
        listener.Stop();

        await Assert.That(connected).IsFalse();
        await Assert.That(failure.FaultKind).IsEqualTo("failure");
        await Assert.That(store.RefusedHttpRequests.Single().Url).IsEqualTo(url);
    }

    [Test]
    public async Task The_library_has_no_network_API_to_reach()
    {
        // The callbacks are in this assembly, and it references no networking assembly, so no host function can open a
        // socket or an HTTP connection.
        var references = typeof(SessionStore).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        await Assert.That(references.Where(name => name.StartsWith("System.Net", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task Http_status_code_and_headers_give_0_and_handle_0()
    {
        var @case = CraftedCorpus.Case("sandbox/http-no-request");
        using var sandbox = TestSandbox.Create("morphir-wasm-http-");
        using var store = Open(Admit(@case.Module), sandbox.Work);

        var reply = Reply(store.CallHandle(CraftedCorpus.FrameBody("initialize")));

        await Assert.That(reply).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"tt"}""");
    }

    [Test]
    public async Task The_variable_store_holds_exactly_its_limit_of_keys_and_values()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-var-");
        using var store = Open(CraftedGuest.Admit(), sandbox.Work);

        // One key byte and 1,048,575 value bytes: exactly 1 MiB.
        await Assert.That(Reply(Call(store, CraftedGuest.Step.VarSetAtLimit))).IsEqualTo("ok");
        await Assert.That(Reply(Call(store, CraftedGuest.Step.VarGet))).IsEqualTo("present");
        // Replacing the value of a key counts its new size only.
        await Assert.That(Reply(Call(store, CraftedGuest.Step.VarSetAtLimit))).IsEqualTo("ok");
        // Value handle 0 deletes the key and frees its bytes.
        await Assert.That(Reply(Call(store, CraftedGuest.Step.VarDelete))).IsEqualTo("ok");
        await Assert.That(Reply(Call(store, CraftedGuest.Step.VarGet))).IsEqualTo("missing");
        await Assert.That(Reply(Call(store, CraftedGuest.Step.VarSetAtLimit))).IsEqualTo("ok");
        // The store is full: one more key of one byte is two bytes over.
        var failure = Failed(Call(store, CraftedGuest.Step.VarSetOneByte));

        await Assert.That(failure.FaultKind).IsEqualTo("failure");
        await Assert.That(failure.Detail).Contains("var_set");
        await Assert.That(failure.Detail).Contains("1048576");
    }

    [Test]
    public async Task A_variable_one_byte_over_the_limit_fails_the_call()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-var-");
        using var store = Open(CraftedGuest.Admit(), sandbox.Work);

        var failure = Failed(Call(store, CraftedGuest.Step.VarSetOverLimit));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.IncompleteSession);
        await Assert.That(failure.Detail).Contains("1048577");
    }

    [Test]
    public async Task The_IR_cache_holds_exactly_its_byte_limit()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-cache-");
        using var store = Open(CraftedGuest.Admit(), sandbox.Work);

        // One key byte and 16,777,215 value bytes: exactly 16 MiB.
        await Assert.That(Reply(Call(store, CraftedGuest.Step.CacheAtLimit))).IsEqualTo("ok");
        var failure = Failed(Call(store, CraftedGuest.Step.CacheOneByte));

        await Assert.That(failure.FaultKind).IsEqualTo("failure");
        await Assert.That(failure.Detail).Contains("morphir_cache_ir");
        await Assert.That(failure.Detail).Contains("16777216");
    }

    [Test]
    public async Task The_IR_cache_holds_exactly_its_entry_limit()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-cache-");
        using var store = Open(CraftedGuest.Admit(), sandbox.Work);

        await Assert.That(Reply(Call(store, CraftedGuest.Step.Cache1024))).IsEqualTo("ok");
        // A key that exists is replaced, not added.
        await Assert.That(Reply(Call(store, CraftedGuest.Step.CacheOverwrite))).IsEqualTo("ok");
        var failure = Failed(Call(store, CraftedGuest.Step.CacheAnother));

        await Assert.That(failure.FaultKind).IsEqualTo("failure");
        await Assert.That(failure.Detail).Contains("1024 entries");
    }

    [Test]
    public async Task A_missing_IR_cache_entry_is_the_text_null()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-cache-");
        using var store = Open(CraftedGuest.Admit(), sandbox.Work);

        await Assert.That(Reply(Call(store, CraftedGuest.Step.CacheMissing))).IsEqualTo("null");
    }

    [Test]
    [Arguments("sandbox/var-round-trip", "from-var")]
    [Arguments("sandbox/ir-cache-round-trip", "from-cache")]
    public async Task A_second_session_starts_empty(string caseId, string stored)
    {
        var module = Admit(CraftedCorpus.Case(caseId).Module);
        using var sandbox = TestSandbox.Create("morphir-wasm-state-");
        var initialize = CraftedCorpus.FrameBody("initialize");
        var shutdown = CraftedCorpus.FrameBody("shutdown");

        using var first = Open(module, sandbox.Work);
        var firstReply = Reply(first.CallHandle(initialize));
        var firstAgain = Reply(first.CallHandle(shutdown));
        using var second = Open(module, sandbox.Work);
        var secondReply = Reply(second.CallHandle(initialize));

        await Assert.That(firstReply).Contains("\"stored\"");
        await Assert.That(firstAgain).Contains($"\"{stored}\"");
        // The first session still holds its value; the second never saw it.
        await Assert.That(secondReply).Contains("\"stored\"");
    }

    [Test]
    public async Task Config_get_gives_handle_0()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-config-");
        using var store = Open(Admit("extism-config"), sandbox.Work);

        await Assert.That(Reply(store.CallHandle(CraftedCorpus.FrameBody("initialize")))).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"t"}""");
    }

    [Test]
    public async Task Logging_records_info_and_above_and_every_morphir_log()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-log-");
        using var store = Open(Admit("extism-log"), sandbox.Work);

        var reply = Reply(store.CallHandle(CraftedCorpus.FrameBody("initialize")));
        var outputs = store.GuestOutput.Outputs;

        await Assert.That(reply).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"level-2"}""");
        await Assert.That(string.Join(" | ", outputs.Select(output => output.Text))).IsEqualTo(
            "log info: kit-info--marker | log warn: kit-warn--marker | log error: kit-error-marker | morphir_log trace: kit-morph-marker");
        await Assert.That(string.Join(" ", outputs.Select(output => output.Source))).IsEqualTo("Log Log Log MorphirLog");
    }

    [Test]
    public async Task The_workspace_is_a_private_empty_directory_that_exists_during_the_session_and_is_gone_after_exit()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-workspace-");
        using var channel = OpenChannel(CraftedGuest.Admit(), sandbox.Work);
        await Assert.That(channel.WorkspaceDirectory).IsNull();

        var root = WorkspaceRoot(await channel.RequestAsync("morphir.ping", CraftedGuest.Input(CraftedGuest.Step.Workspace), CancellationToken.None));
        var during = Directory.Exists(root);
        var empty = !Directory.EnumerateFileSystemEntries(root).Any();
        var again = WorkspaceRoot(await channel.RequestAsync("morphir.ping", CraftedGuest.Input(CraftedGuest.Step.Workspace), CancellationToken.None));
        await channel.NotifyAsync("morphir.exit", Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"morphir.exit"}"""), CancellationToken.None);

        await Assert.That(during).IsTrue();
        await Assert.That(empty).IsTrue();
        await Assert.That(again).IsEqualTo(root);
        await Assert.That(channel.WorkspaceDirectory).IsEqualTo(root);
        await Assert.That(Path.GetDirectoryName(root)).IsEqualTo(Path.GetFullPath(sandbox.Work));
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task The_workspace_is_gone_after_a_failure()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-workspace-");
        using var channel = OpenChannel(CraftedGuest.Admit(), sandbox.Work);
        var root = WorkspaceRoot(await channel.RequestAsync("morphir.ping", CraftedGuest.Input(CraftedGuest.Step.Workspace), CancellationToken.None));
        var during = Directory.Exists(root);

        await Assert.That(async () => await channel.RequestAsync("morphir.ping", CraftedGuest.Input(CraftedGuest.Step.Trap), CancellationToken.None))
            .Throws<WasmChannelException>();

        await Assert.That(during).IsTrue();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task The_workspace_is_gone_after_dispose()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-workspace-");
        var store = Open(CraftedGuest.Admit(), sandbox.Work);
        var root = WorkspaceRoot(Encoding.UTF8.GetBytes(Reply(Call(store, CraftedGuest.Step.Workspace))));
        var during = Directory.Exists(root);

        store.Dispose();

        await Assert.That(during).IsTrue();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task Two_sessions_get_two_workspaces()
    {
        using var sandbox = TestSandbox.Create("morphir-wasm-workspace-");
        var module = CraftedGuest.Admit();
        using var first = Open(module, sandbox.Work);
        using var second = Open(module, sandbox.Work);

        var firstRoot = WorkspaceRoot(Encoding.UTF8.GetBytes(Reply(Call(first, CraftedGuest.Step.Workspace))));
        var secondRoot = WorkspaceRoot(Encoding.UTF8.GetBytes(Reply(Call(second, CraftedGuest.Step.Workspace))));

        await Assert.That(firstRoot).IsNotEqualTo(secondRoot);
    }

    [Test]
    public async Task A_relative_workspace_root_is_refused()
    {
        await Assert.That(() => SessionStore.Open(CraftedGuest.Admit(), SessionLimits.Default, "relative/workspaces"))
            .Throws<ArgumentException>();
    }

    private static string WorkspaceRoot(byte[]? info)
    {
        using var document = JsonDocument.Parse(info ?? throw new InvalidOperationException("No reply."));
        if (document.RootElement.GetProperty("output_dir").GetString() != string.Empty)
        {
            throw new InvalidOperationException("output_dir is not empty.");
        }

        return document.RootElement.GetProperty("root").GetString()!;
    }

    private static ExtismModule Admit(string module) =>
        WasmEngine.Shared.Load(CraftedCorpus.ModuleBytes(module)).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));
}
