using System.Globalization;
using System.Text;
using System.Text.Json;
using Morphir.Mep;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 3.2 of add-wasm-extension-host (design NTFY-1): negotiated notifications. A guest whose
/// <c>morphir.initialize</c> result gives <c>capabilities.notificationDelivery</c> the value <c>call</c> receives each
/// later notification as one <c>handle</c> call; output for one fails the session. Any other value, or none, means no
/// delivery, and <c>morphir.exit</c> then ends the instance without a call. The crafted corpus cases run in
/// <see cref="ChannelCorpusTests"/>; these are the named rules.
/// </summary>
public class NotificationTests
{
    private const string Initialize = "morphir.initialize";
    private const string Initialized = "morphir.initialized";
    private const string Shutdown = "morphir.shutdown";
    private const string Exit = "morphir.exit";

    private static readonly PeerInfo Host = new(PeerKind.Cli, "morphir-dotnet-tests", "0.0.0");

    private static ExtismModule Admit(string module) =>
        WasmEngine.Shared.Load(CraftedCorpus.ModuleBytes(module)).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

    private static WasmChannel Open(ExtismModule module) =>
        WasmChannel.Open(module, SessionLimits.Default).Match(
            channel => channel,
            failure => throw new InvalidOperationException($"The channel did not open: {failure.Message}"));

    private static WasmChannel Open(string module) => Open(Admit(module));

    private static byte[] Request(long id, string method) =>
        Encoding.UTF8.GetBytes($$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"{{{method}}}","params":{}}""");

    private static byte[] Notification(string method) =>
        Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","method":"{{method}}"}""");

    /// <summary>The <c>result</c> of a reply, as an integer: the notify guests answer with their call count.</summary>
    private static int Result(byte[]? reply)
    {
        using var document = JsonDocument.Parse(reply ?? throw new InvalidOperationException("No reply."));
        return document.RootElement.GetProperty("result").GetInt32();
    }

    [Test]
    public async Task A_guest_that_advertises_call_receives_each_notification_as_one_handle_call()
    {
        // extism-notify-accept answers its second request with the number of handle calls so far.
        using var channel = Open("extism-notify-accept");

        await channel.RequestAsync(Initialize, Request(1, Initialize), CancellationToken.None);
        var delivers = channel.DeliversNotifications;
        var notified = await channel.NotifyAsync(Initialized, Notification(Initialized), CancellationToken.None);
        var callsAfterNotification = channel.HandleCalls;
        var shutdown = await channel.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None);

        await Assert.That(delivers).IsTrue();
        await Assert.That(notified).IsNull();
        await Assert.That(callsAfterNotification).IsEqualTo(2);
        await Assert.That(Result(shutdown)).IsEqualTo(3);
        await Assert.That(channel.Failure).IsNull();
    }

    [Test]
    public async Task A_notification_before_the_advertisement_is_withheld()
    {
        using var channel = Open("extism-notify-accept");

        var early = await channel.NotifyAsync(Initialized, Notification(Initialized), CancellationToken.None);
        var callsBefore = channel.HandleCalls;
        await channel.RequestAsync(Initialize, Request(1, Initialize), CancellationToken.None);

        await Assert.That(early).IsNull();
        await Assert.That(callsBefore).IsEqualTo(0);
        await Assert.That(channel.HandleCalls).IsEqualTo(1);
        await Assert.That(channel.DeliversNotifications).IsTrue();
    }

    [Test]
    public async Task A_notification_that_returns_output_fails_the_session()
    {
        using var channel = Open("extism-notify-answer");
        await channel.RequestAsync(Initialize, Request(1, Initialize), CancellationToken.None);

        var answer = await channel.NotifyAsync(Initialized, Notification(Initialized), CancellationToken.None);

        // The channel returns the output so that the host session fails the session for it.
        await Assert.That(answer).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(answer!)).IsEqualTo("""{"jsonrpc":"2.0","id":1,"result":"answer"}""");
        await Assert.That(channel.Failure).IsTypeOf<WasmChannelFailure.AnsweredNotification>();
        await Assert.That(channel.Failure!.Code).IsEqualTo("extension.incomplete_session");
        await Assert.That(channel.Failure.FaultKind).IsEqualTo("failure");
        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.HandleCalls).IsEqualTo(2);
        await Assert.That(async () => await channel.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None))
            .Throws<WasmChannelException>();
        await Assert.That(channel.HandleCalls).IsEqualTo(2);
    }

    [Test]
    public async Task Exit_to_a_guest_that_takes_notifications_is_one_call_then_the_instance_ends()
    {
        using var channel = Open("extism-notify-accept");
        await channel.RequestAsync(Initialize, Request(1, Initialize), CancellationToken.None);

        var reply = await channel.NotifyAsync(Exit, Notification(Exit), CancellationToken.None);

        await Assert.That(reply).IsNull();
        await Assert.That(channel.HandleCalls).IsEqualTo(2);
        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.Failure).IsNull();
        await Assert.That(async () => await channel.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None))
            .Throws<ObjectDisposedException>();
        await Assert.That(channel.HandleCalls).IsEqualTo(2);
    }

    [Test]
    public async Task A_guest_that_advertises_a_value_the_contract_does_not_list_receives_no_notification()
    {
        // extism-notify-unlisted advertises "unlisted", which the contract reads as none.
        using var channel = Open("extism-notify-unlisted");
        await channel.RequestAsync(Initialize, Request(1, Initialize), CancellationToken.None);

        var notified = await channel.NotifyAsync(Initialized, Notification(Initialized), CancellationToken.None);
        var shutdown = await channel.RequestAsync(Shutdown, Request(2, Shutdown), CancellationToken.None);
        var exited = await channel.NotifyAsync(Exit, Notification(Exit), CancellationToken.None);

        await Assert.That(channel.DeliversNotifications).IsFalse();
        await Assert.That(notified).IsNull();
        await Assert.That(exited).IsNull();
        await Assert.That(Result(shutdown)).IsEqualTo(2);
        // Exit ended the instance without a call.
        await Assert.That(channel.HandleCalls).IsEqualTo(2);
        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.Failure).IsNull();
    }

    [Test]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"notificationDelivery":"call"}}}""", "call")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"notificationDelivery":"none"}}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"notificationDelivery":"poll"}}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"notificationDelivery":"unlisted"}}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"notificationDelivery":"CALL"}}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"notificationDelivery":true}}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{}}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":"call"}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":{"notificationDelivery":"call"}}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"result":"call"}""", "none")]
    [Arguments("""{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"no"}}""", "none")]
    [Arguments("""[{"result":{"capabilities":{"notificationDelivery":"call"}}}]""", "none")]
    [Arguments("""not json""", "none")]
    [Arguments("", "none")]
    public async Task The_advertisement_is_the_string_member_of_the_initialize_result(string reply, string delivery)
    {
        var advertisement = WasmProfileContract.Pinned.Profile(WasmAbi.ExtismKernel).NotificationDelivery!;

        await Assert.That(advertisement.ReadFrom(Encoding.UTF8.GetBytes(reply))).IsEqualTo(delivery);
    }

    /// <summary>
    /// A guest for a whole <see cref="HostSession"/> that asks for <c>call</c> delivery. Its first request, which
    /// <see cref="HostSession"/> makes <c>morphir.initialize</c> with id 0 (it numbers its requests from 0), is answered
    /// with a complete initialize result. It counts every <c>handle</c> call; a message without <c>"id"</c> is a notification, which gives no
    /// output, or, when <paramref name="answersNotifications"/>, an answer. Its second request, id 1, is answered with
    /// the number of calls so far. It follows <c>extism-notify-accept</c>, with an initialize result that the host
    /// session accepts.
    /// </summary>
    private static ExtismModule SessionGuest(bool answersNotifications)
    {
        const string result =
            """{"jsonrpc":"2.0","id":0,"result":{"protocolVersion":"0.2.0-draft.1","extension":{"id":"crafted-notify","name":"Crafted notify","version":"0.0.0","types":[]},"capabilities":{"notificationDelivery":"call"}}}""";
        const string count = """{"jsonrpc":"2.0","id":1,"result":0}""";
        var length = Encoding.UTF8.GetByteCount(result).ToString(CultureInfo.InvariantCulture);
        var notification = answersNotifications
            ? $"(call $output_set (call $block (i32.const 16) (i32.const {length})) (i64.const {length})) (return (i32.const 0))"
            : "(return (i32.const 0))";
        return CraftedGuest.AdmitWat($$"""
            (module
              (import "extism:host/env" "input_length" (func $input_length (result i64)))
              (import "extism:host/env" "input_load_u8" (func $input_load_u8 (param i64) (result i32)))
              (import "extism:host/env" "alloc" (func $alloc (param i64) (result i64)))
              (import "extism:host/env" "store_u8" (func $store_u8 (param i64 i32)))
              (import "extism:host/env" "output_set" (func $output_set (param i64 i64)))
              (memory 1)
              (global $calls (mut i32) (i32.const 0))
              (global $requests (mut i32) (i32.const 0))
              (data (i32.const 0) "\22id\22")
              (data (i32.const 16) "{{Escape(result)}}")
              (data (i32.const 1024) "{{Escape(count)}}")
              (func $block (param $ptr i32) (param $len i32) (result i64)
                (local $h i64) (local $i i32)
                (local.set $h (call $alloc (i64.extend_i32_u (local.get $len))))
                (block $copied
                  (loop $copy
                    (br_if $copied (i32.ge_u (local.get $i) (local.get $len)))
                    (call $store_u8
                      (i64.add (local.get $h) (i64.extend_i32_u (local.get $i)))
                      (i32.load8_u (i32.add (local.get $ptr) (local.get $i))))
                    (local.set $i (i32.add (local.get $i) (i32.const 1)))
                    (br $copy)))
                (local.get $h))
              (func $is_request (result i32)
                (local $len i64) (local $i i64) (local $j i32)
                (local.set $len (call $input_length))
                (loop $start
                  (if (i64.gt_u (i64.add (local.get $i) (i64.const 4)) (local.get $len))
                    (then (return (i32.const 0))))
                  (local.set $j (i32.const 0))
                  (block $differs
                    (loop $compare
                      (if (i32.eq (local.get $j) (i32.const 4)) (then (return (i32.const 1))))
                      (br_if $differs
                        (i32.ne
                          (call $input_load_u8 (i64.add (local.get $i) (i64.extend_i32_u (local.get $j))))
                          (i32.load8_u (local.get $j))))
                      (local.set $j (i32.add (local.get $j) (i32.const 1)))
                      (br $compare)))
                  (local.set $i (i64.add (local.get $i) (i64.const 1)))
                  (br $start))
                (i32.const 0))
              (func (export "handle") (result i32)
                (global.set $calls (i32.add (global.get $calls) (i32.const 1)))
                (if (i32.eqz (call $is_request)) (then {{notification}}))
                (global.set $requests (i32.add (global.get $requests) (i32.const 1)))
                (if (i32.eq (global.get $requests) (i32.const 1))
                  (then
                    (call $output_set (call $block (i32.const 16) (i32.const {{length}})) (i64.const {{length}}))
                    (return (i32.const 0))))
                (i32.store8 (i32.const 1057) (i32.add (i32.const 48) (global.get $calls)))
                (call $output_set (call $block (i32.const 1024) (i32.const 35)) (i64.const 35))
                (i32.const 0)))
            """);
    }

    private static string Escape(string text) => text.Replace("\"", "\\22", StringComparison.Ordinal);

    [Test]
    public async Task A_host_session_delivers_initialized_and_exit_to_a_guest_that_takes_notifications()
    {
        using var channel = Open(SessionGuest(answersNotifications: false));
        var session = new HostSession(channel, Host);
        var run = session.RunAsync(CancellationToken.None);

        var initialized = await session.InitializeAsync(CancellationToken.None);
        // morphir.initialize, then morphir.initialized, which HostSession always sends, as a second call.
        var callsAfterInitialize = channel.HandleCalls;
        var shutdown = await session.ShutdownAsync(CancellationToken.None);
        var callsAfterShutdown = channel.HandleCalls;
        await session.ExitAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(initialized.Match(_ => "ok", failure => failure.ToString())).IsEqualTo("ok");
        await Assert.That(channel.DeliversNotifications).IsTrue();
        await Assert.That(callsAfterInitialize).IsEqualTo(2);
        // The guest answers shutdown with its call count: 3 means morphir.initialized reached it.
        await Assert.That(shutdown.Match(value => value.GetInt32(), _ => -1)).IsEqualTo(3);
        await Assert.That(callsAfterShutdown).IsEqualTo(3);
        // morphir.exit was one more call, after which the instance ended.
        await Assert.That(channel.HandleCalls).IsEqualTo(4);
        await Assert.That(channel.InstanceEnded).IsTrue();
        await Assert.That(channel.Failure).IsNull();
        await Assert.That(session.FailureOrNull()).IsNull();
    }

    [Test]
    public async Task A_host_session_fails_when_the_guest_answers_morphir_initialized()
    {
        using var channel = Open(SessionGuest(answersNotifications: true));
        var session = new HostSession(channel, Host);

        var initialized = await session.InitializeAsync(CancellationToken.None);

        await Assert.That(initialized.Match(_ => false, _ => true)).IsTrue();
        await Assert.That(session.FailureOrNull()?.Match(
                noReply: _ => "",
                replyForAnotherRequest: (_, _, _) => "",
                notAResponse: (_, _) => "",
                invalidReply: (_, _) => "",
                replyTooLarge: (_, _) => "",
                answeredNotification: method => method,
                cancelled: _ => "",
                channelFailed: (_, _) => ""))
            .IsEqualTo(Initialized);
        await Assert.That(channel.Failure).IsTypeOf<WasmChannelFailure.AnsweredNotification>();
        await Assert.That(channel.HandleCalls).IsEqualTo(2);
    }
}
