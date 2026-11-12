// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Extensions;
using static Morphir.Tooling.Tests.Features.Extensions.WorkerHarness;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// <c>morphir extension worker</c> at the service level (task 4.1 of <c>add-wasm-extension-host</c>): the start-up
/// order, each event's members, the message checks, the relay through the WASM channel, the frame limits, the exit
/// statuses and interruption. The modules are the pinned crafted corpus and this repository's two worker guests.
/// Stdout is read strictly as Content-Length frames in every test.
/// </summary>
[Category("PlatformSpecific")]
public sealed class ExtensionWorkerTests
{
    [TUnit.Core.Before(HookType.Test)]
    public void RequireRuntime()
    {
        if (WasmRuntime.Process.Platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: guest worker execution is unavailable; #609.");
    }

    private static readonly string[] Envelope = ["profile", "contractVersion", "command", "sequence", "event"];

    // --- Help and usage -------------------------------------------------------------------------------------------

    [Test]
    public async Task Help_with_json_writes_one_command_result_with_the_build_identity_on_stdout()
    {
        var run = await RunAsync(["--help", "--json"], []);

        run.ExitCode.Should().Be(0);
        run.StderrLines.Should().BeEmpty();
        using var result = JsonDocument.Parse(run.Stdout);
        var root = result.RootElement;
        root.GetProperty("profile").GetString().Should().Be("morphir/command-result");
        root.GetProperty("command").GetString().Should().Be("extension worker");
        root.GetProperty("state").GetString().Should().Be("succeeded");
        root.GetProperty("exitCode").GetInt32().Should().Be(0);
        root.GetProperty("result").GetProperty("buildIdentity").GetString().Should().Be(Identity);
        root.GetProperty("result").GetProperty("help").GetString().Should().Contain("[experimental, internal]");
        Encoding.UTF8.GetString(run.Stdout).Should().EndWith("}\n").And.Subject.Count(c => c == '\n').Should().Be(1);
    }

    [Test]
    public async Task Help_without_json_writes_the_help_text_on_stdout()
    {
        var run = await RunAsync(["--help"], []);

        run.ExitCode.Should().Be(0);
        Encoding.UTF8.GetString(run.Stdout).Should().Be(WorkerInvocation.HelpText);
        run.StderrLines.Should().BeEmpty();
    }

    [Test]
    public async Task A_usage_error_with_json_is_one_command_result_failure_on_stderr_and_nothing_on_stdout()
    {
        var run = await RunAsync(["--json", "--module", Corpus("extism-echo")], Frame(Initialize));

        run.ExitCode.Should().Be(2);
        run.Stdout.Should().BeEmpty();
        run.StderrLines.Should().ContainSingle();
        using var result = JsonDocument.Parse(run.StderrLines[0]);
        result.RootElement.GetProperty("profile").GetString().Should().Be("morphir/command-result");
        result.RootElement.GetProperty("state").GetString().Should().Be("failed");
        result.RootElement.GetProperty("exitCode").GetInt32().Should().Be(2);
        result.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("usage");
        result.RootElement.GetProperty("error").GetProperty("message").GetString().Should().Contain("--abi");
        run.Events.Should().BeEmpty("a usage error serves nothing, so it reports no started, refused or fault event");
    }

    [Test]
    public async Task A_usage_error_without_json_is_one_text_line_on_stderr()
    {
        var run = await RunAsync(["--abi", "extism-kernel-v1", "--surprise"], []);

        run.ExitCode.Should().Be(2);
        run.Stdout.Should().BeEmpty();
        run.StderrLines.Should().ContainSingle().Which.Should().StartWith("morphir extension worker: ").And.Contain("--surprise");
    }

    [Test]
    public async Task A_worker_started_without_a_build_identity_exits_with_2_and_serves_nothing()
    {
        // Parent requirement "Worker build identity", scenario "No identity given".
        var run = await RunAsync(["--abi", "extism-kernel-v1", "--module", Corpus("extism-echo"), "--json"], Frame(Initialize));

        run.ExitCode.Should().Be(2, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.Events.Should().BeEmpty("a worker without a build identity loads no module and serves nothing");
        run.StderrLines.Should().ContainSingle().Which.Should().Contain("--build-identity");
    }

    [Test]
    [Arguments("0")]
    [Arguments("65537")]
    [Arguments("many")]
    public async Task A_memory_pages_value_the_worker_cannot_use_is_a_usage_error_before_the_module_is_read(string pages)
    {
        // The contract makes it exit status 2; the MoonBit worker at 89f8924 reads the module and exits with 1.
        var run = await RunAsync([.. Arguments(Corpus("extism-echo")), "--memory-pages", pages], Frame(Initialize));

        run.ExitCode.Should().Be(2, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.Events.Should().BeEmpty("a usage error serves nothing, so it reports no started, refused or fault event");
        run.StderrLines.Should().ContainSingle().Which.Should().Contain("--memory-pages");
    }

    // --- Start-up order -------------------------------------------------------------------------------------------

    [Test]
    public async Task A_stale_build_identity_is_refused_before_the_module_is_read()
    {
        using var sandbox = TestSandbox.Create();
        var missing = Path.Combine(sandbox.Work, "does-not-exist.wasm");
        var stale = Identity[..^1] + (Identity[^1] == '0' ? '1' : '0');

        var run = await RunAsync(["--abi", "extism-kernel-v1", "--module", missing, "--build-identity", stale, "--json"], Frame(Initialize));

        run.ExitCode.Should().Be(3, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("refused", "exited");
        var refused = run.Single("refused");
        refused.Text("error.code").Should().Be("extension.worker_runtime_mismatch");
        refused.Text("error.message").Should().Contain(Identity).And.Contain(stale);
        refused.Text("error.message").Should().NotContain("does-not-exist", "the module path was never looked at");
        run.Single("exited").Json.GetProperty("exitCode").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task A_module_file_that_does_not_exist_is_a_fault_failure_before_started()
    {
        using var sandbox = TestSandbox.Create();
        var missing = Path.Combine(sandbox.Work, "missing.wasm");

        var run = await RunAsync(Arguments(missing), Frame(Initialize));

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("fault", "exited");
        run.Single("fault").Text("fault.kind").Should().Be("failure");
        run.Single("fault").Text("fault.message").Should().Contain(missing).And.Contain("no file");
    }

    [Test]
    public async Task A_text_file_is_not_a_WASM_module_and_is_a_fault_failure_before_started()
    {
        using var sandbox = TestSandbox.Create();
        var text = Path.Combine(sandbox.Work, "guest.wasm");
        File.WriteAllText(text, "not a module\n");

        var run = await RunAsync(Arguments(text), Frame(Initialize));

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("fault", "exited");
        run.Single("fault").Text("fault.kind").Should().Be("failure");
        run.Single("fault").Text("fault.message").Should().Contain(text).And.Contain("not a WASM module").And.Contain(Sha256(text));
    }

    [Test]
    public async Task A_directory_at_the_module_path_is_a_fault_failure_before_started()
    {
        using var sandbox = TestSandbox.Create();

        var run = await RunAsync(Arguments(sandbox.Work), Frame(Initialize));

        run.ExitCode.Should().Be(1, run.Describe());
        run.EventNames.Should().Equal("fault", "exited");
        run.Single("fault").Text("fault.kind").Should().Be("failure");
    }

    [Test]
    public async Task A_refused_module_gives_one_refused_event_no_stdout_and_exit_3()
    {
        var run = await RunAsync(Arguments(Corpus("env-open-socket")), Frame(Initialize));

        run.ExitCode.Should().Be(3, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("refused", "exited");
        var refused = run.Single("refused");
        refused.Text("error.code").Should().Be("extension.abi_unsupported");
        refused.Text("error.message").Should().Contain("env.open_socket").And.Contain(Sha256(Corpus("env-open-socket")));
    }

    [Test]
    public async Task A_byte_profile_module_is_refused_naming_its_profile()
    {
        var run = await RunAsync(["--abi", "morphir-bytes-v1", "--module", Corpus("bytes-echo"), "--build-identity", Identity, "--json"], Frame(Initialize));

        run.ExitCode.Should().Be(3, run.Describe());
        run.Single("refused").Text("error.code").Should().Be("extension.abi_unsupported");
        run.Single("refused").Text("error.message").Should().Contain("morphir-bytes-v1");
    }

    [Test]
    public async Task A_declared_abi_that_differs_is_refused_with_abi_mismatch()
    {
        var run = await RunAsync(["--abi", "morphir-bytes-v1", "--module", Corpus("extism-echo"), "--build-identity", Identity, "--json"], Frame(Initialize));

        run.ExitCode.Should().Be(3, run.Describe());
        run.Single("refused").Text("error.code").Should().Be("extension.abi_mismatch");
    }

    [Test]
    public async Task An_initial_memory_above_the_ceiling_is_a_memory_limit_fault_before_started()
    {
        var run = await RunAsync(Arguments(Corpus("initial-memory-4097")), Frame(Initialize));

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("fault", "exited");
        run.Single("fault").Text("fault.kind").Should().Be("memory-limit");
        run.Single("fault").Text("fault.message").Should().Contain("ceiling");
    }

    // --- The relay ------------------------------------------------------------------------------------------------

    [Test]
    public async Task An_echo_session_answers_each_request_with_one_frame_and_exits_0_at_exit()
    {
        var run = await RunAsync(Arguments(Corpus("extism-echo")), Initialize, Initialized, Shutdown, Exit);

        run.ExitCode.Should().Be(0, run.Describe());
        run.FrameTexts.Should().Equal(Initialize, Shutdown);
        run.EventNames.Should().Equal("started", "exited");
        var started = run.Single("started");
        started.Text("abi").Should().Be("extism-kernel-v1");
        started.Text("moduleSha256").Should().Be(Sha256(Corpus("extism-echo")));
        started.Text("buildIdentity").Should().Be(Identity);
        run.Single("exited").Json.GetProperty("exitCode").GetInt32().Should().Be(0);
    }

    [Test]
    public async Task The_started_event_has_exactly_the_contract_members_and_every_bound_enforced()
    {
        var run = await RunAsync(Arguments(Corpus("extism-echo")), Initialize, Exit);

        var started = run.Single("started");
        started.Members.Should().Equal([.. Envelope, "abi", "moduleSha256", "buildIdentity", "bounds"]);
        var bounds = started.Json.GetProperty("bounds");
        bounds.EnumerateObject().Select(member => member.Name).Should().Equal("memoryCeiling", "instructionBudget", "callDeadline");
        foreach (var bound in bounds.EnumerateObject())
        {
            bound.Value.GetRawText().Should().Be("""{"enforced":true}""", bound.Name);
        }
    }

    [Test]
    public async Task Every_event_is_a_numbered_command_event_of_the_worker_command()
    {
        var run = await RunAsync(Arguments(Corpus("extism-log")), Initialize, Exit);

        run.Events.Should().NotBeEmpty();
        run.StderrLines.Should().HaveCount(run.Events.Count, "with --json every stderr line is an event");
        var sequence = 0;
        foreach (var e in run.Events)
        {
            e.Json.GetProperty("profile").GetString().Should().Be("morphir/command-event");
            e.Json.GetProperty("contractVersion").GetString().Should().Be("0.1.0");
            e.Json.GetProperty("command").GetString().Should().Be("extension worker");
            e.Json.GetProperty("sequence").GetInt32().Should().Be(++sequence);
        }

        foreach (var output in run.Events.Where(e => e.Name == "output"))
        {
            output.Members.Should().Equal([.. Envelope, "output"]);
        }

        run.Single("exited").Members.Should().Equal([.. Envelope, "exitCode"]);
    }

    [Test]
    public async Task A_fault_event_has_the_kind_and_the_message_and_nothing_else()
    {
        var run = await RunAsync(Arguments(Corpus("extism-trap")), Initialize);

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("started", "fault", "exited");
        var fault = run.Single("fault");
        fault.Members.Should().Equal([.. Envelope, "fault"]);
        fault.Json.GetProperty("fault").EnumerateObject().Select(member => member.Name).Should().Equal("kind", "message");
        fault.Text("fault.kind").Should().Be("trap");
        fault.Text("fault.message").Should().Contain("the guest has no memory of its own").And.Contain("kernel memory")
            .And.Contain("ceiling", "every failure states the memory sizes");
    }

    [Test]
    public async Task A_refused_event_has_the_code_and_the_message_and_nothing_else()
    {
        var run = await RunAsync(Arguments(Corpus("extism-no-handle")), Frame(Initialize));

        var refused = run.Single("refused");
        refused.Members.Should().Equal([.. Envelope, "error"]);
        refused.Json.GetProperty("error").EnumerateObject().Select(member => member.Name).Should().Equal("code", "message");
        refused.Text("error.message").Should().Contain("does not export handle as () -> i32");
    }

    [Test]
    public async Task A_notification_gets_no_frame_and_a_guest_that_advertises_nothing_gets_no_call()
    {
        var run = await RunAsync(Arguments(Corpus("extism-count-calls")), Initialize, Initialized, Shutdown, Exit);

        run.ExitCode.Should().Be(0, run.Describe());
        run.Frames.Should().HaveCount(2, "one frame for each request and none for a notification");
        run.FrameTexts[1].Should().Be("""{"jsonrpc":"2.0","id":2,"result":2}""", "the guest was called once for each request only");
    }

    [Test]
    public async Task A_guest_that_advertises_call_delivery_gets_each_notification_as_a_call()
    {
        var run = await RunAsync(Arguments(Corpus("extism-notify-accept")), Initialize, Initialized, Shutdown, Exit);

        run.ExitCode.Should().Be(0, run.Describe());
        run.FrameTexts.Should().HaveCount(2);
        using var second = JsonDocument.Parse(run.FrameTexts[1]);
        second.RootElement.GetProperty("result").GetInt32().Should().Be(3, "the guest counted initialize, initialized and shutdown");
    }

    [Test]
    public async Task Output_for_a_delivered_notification_is_a_fault_failure()
    {
        var run = await RunAsync(Arguments(Corpus("extism-notify-answer")), Initialize, Initialized, Exit);

        run.ExitCode.Should().Be(1, run.Describe());
        run.Frames.Should().HaveCount(1, "the answer to the notification never reaches stdout");
        run.EventNames.Should().Equal("started", "fault", "exited");
        run.Single("fault").Text("fault.kind").Should().Be("failure");
    }

    [Test]
    public async Task Stdin_that_ends_before_the_instance_ends_is_a_fault_failure()
    {
        var run = await RunAsync(Arguments(Corpus("extism-echo")), Initialize);

        run.ExitCode.Should().Be(1, run.Describe());
        run.FrameTexts.Should().Equal(Initialize);
        run.EventNames.Should().Equal("started", "fault", "exited");
        run.Single("fault").Text("fault.kind").Should().Be("failure");
        run.Single("fault").Text("fault.message").Should().Contain("stdin");
    }

    [Test]
    public async Task A_frame_without_a_Content_Length_header_is_a_fault_failure()
    {
        var run = await RunAsync(Arguments(Corpus("extism-echo")), Encoding.ASCII.GetBytes("Content-Type: x\r\n\r\n{}"));

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.Single("fault").Text("fault.kind").Should().Be("failure");
    }

    // --- Message checks -------------------------------------------------------------------------------------------

    [Test]
    [Arguments("42")]
    [Arguments("[1,2]")]
    [Arguments("\"text\"")]
    [Arguments("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\"} {}")]
    [Arguments("{\"jsonrpc\":\"2.0\",\"id\":1")]
    [Arguments("")]
    public async Task A_request_that_is_not_one_JSON_object_is_a_fault_failure_and_the_guest_is_not_called(string body)
    {
        // extism-trap traps on every call, so a call would give the kind trap.
        var run = await RunAsync(Arguments(Corpus("extism-trap")), Frame(Encoding.UTF8.GetBytes(body)));

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("started", "fault", "exited");
        run.Single("fault").Text("fault.kind").Should().Be("failure");
    }

    [Test]
    public async Task A_request_that_is_not_UTF_8_is_a_fault_failure_and_the_guest_is_not_called()
    {
        byte[] body = [.. "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\""u8, 0xff, 0xfe, .. "\"}"u8];

        var run = await RunAsync(Arguments(Corpus("extism-trap")), Frame(body));

        run.ExitCode.Should().Be(1, run.Describe());
        run.Single("fault").Text("fault.kind").Should().Be("failure");
        run.Single("fault").Text("fault.message").Should().Contain("UTF-8");
    }

    [Test]
    public async Task A_response_that_is_not_a_JSON_object_is_a_fault_failure_and_nothing_reaches_stdout()
    {
        var run = await RunAsync(Arguments(Corpus("extism-not-object")), Initialize);

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.Single("fault").Text("fault.kind").Should().Be("failure");
    }

    [Test]
    public async Task The_worker_does_not_check_the_JSON_RPC_version_or_the_id()
    {
        const string odd = """{"jsonrpc":"1.0","id":"x","method":"anything"}""";

        var run = await RunAsync(Arguments(Corpus("extism-echo")), odd, Exit);

        run.ExitCode.Should().Be(0, run.Describe());
        run.FrameTexts.Should().Equal(odd);
    }

    // --- Frame limits ---------------------------------------------------------------------------------------------

    [Test]
    public async Task A_request_frame_at_request_bytes_is_passed_and_one_byte_over_is_a_fault_failure_without_a_call()
    {
        var frameBytes = Frame(Initialize).Length;

        var at = await RunAsync(Arguments(Corpus("extism-echo"), "--request-bytes", $"{frameBytes}"), Initialize, Exit);
        var over = await RunAsync(Arguments(Corpus("extism-trap"), "--request-bytes", $"{frameBytes - 1}"), Initialize);

        at.ExitCode.Should().Be(0, at.Describe());
        at.FrameTexts.Should().Equal(Initialize);
        over.ExitCode.Should().Be(1, over.Describe());
        over.Stdout.Should().BeEmpty();
        over.Single("fault").Text("fault.kind").Should().Be("failure", "the guest traps when called, so it was not called");
        over.Single("fault").Text("fault.message").Should().Contain($"{frameBytes}");
    }

    [Test]
    public async Task A_response_frame_over_response_bytes_is_a_fault_failure_and_nothing_reaches_stdout()
    {
        var run = await RunAsync(Arguments(Corpus("extism-echo"), "--response-bytes", "32"), Initialize);

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.Single("fault").Text("fault.kind").Should().Be("failure");
    }

    // --- Bounds ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task A_small_instruction_budget_ends_a_spinning_guest_with_budget_exhausted()
    {
        var run = await RunAsync(Arguments(Corpus("extism-spin"), "--instructions", "1000000"), Initialize);

        run.ExitCode.Should().Be(1, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.Single("fault").Text("fault.kind").Should().Be("budget-exhausted");
    }

    [Test]
    public async Task A_short_call_deadline_ends_a_spinning_guest_with_call_deadline()
    {
        var run = await RunAsync(Arguments(Corpus("extism-spin"), "--call-ms", "200", "--instructions", "9007199254740991"), Initialize);

        run.ExitCode.Should().Be(1, run.Describe());
        run.Single("fault").Text("fault.kind").Should().Be("call-deadline");
        run.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    // --- Guest output ---------------------------------------------------------------------------------------------

    [Test]
    public async Task Guest_logs_and_prints_become_output_events_and_never_reach_stdout()
    {
        var logged = await RunAsync(Arguments(Corpus("extism-log")), Initialize, Exit);
        var printed = await RunAsync(Arguments(Corpus("extism-fd-write")), Initialize, Exit);

        logged.ExitCode.Should().Be(0, logged.Describe());
        logged.FrameTexts.Should().Equal("""{"jsonrpc":"2.0","id":1,"result":"level-2"}""");
        logged.GuestOutput.Should().ContainAll("kit-info--marker", "kit-warn--marker", "kit-error-marker", "kit-morph-marker");
        logged.GuestOutput.Should().NotContain("kit-trace-marker").And.NotContain("kit-debug-marker");
        logged.GuestOutput.IndexOf("kit-info--marker", StringComparison.Ordinal)
            .Should().BeLessThan(logged.GuestOutput.IndexOf("kit-error-marker", StringComparison.Ordinal));
        logged.EventNames.First().Should().Be("started");
        logged.EventNames.Last().Should().Be("exited");

        printed.ExitCode.Should().Be(0, printed.Describe());
        printed.FrameTexts.Should().Equal("""{"jsonrpc":"2.0","id":1,"result":"printed"}""");
        printed.GuestOutput.Should().Contain("stdout: kit-stdout-marker").And.Contain("stderr: kit-stderr-marker");
        Encoding.UTF8.GetString(printed.Stdout).Should().NotContain("kit-stdout-marker");
    }

    [Test]
    public async Task A_guest_that_writes_20_MiB_in_one_call_gives_bounded_output_events_that_state_what_was_dropped()
    {
        var run = await RunAsync(Arguments(TestGuest("flood-output")), Initialize, Exit);

        run.ExitCode.Should().Be(0, run.Describe());
        run.FrameTexts.Should().Equal("""{"jsonrpc":"2.0","id":1,"result":"flooded"}""");
        var outputs = run.Events.Where(e => e.Name == "output").ToArray();
        var dropped = outputs.Where(e => e.Json.TryGetProperty("droppedBytes", out _)).Should().ContainSingle().Subject;
        dropped.Members.Should().Equal([.. Envelope, "output", "droppedBytes"]);
        dropped.Text("output").Should().Contain("bytes of guest output were dropped");
        var shown = outputs.Where(e => e != dropped).Sum(e => Encoding.UTF8.GetByteCount(e.Text("output")!));
        var droppedBytes = dropped.Json.GetProperty("droppedBytes").GetInt64();
        shown.Should().BeLessThanOrEqualTo(65536);
        droppedBytes.Should().BeGreaterThan(20L * 1024 * 1024 - 2 * 65536);
        var stderrBytes = run.StderrLines.Sum(line => Encoding.UTF8.GetByteCount(line) + 1);
        stderrBytes.Should().BeLessThan(256 * 1024, "the report of one call holds at most the 64 KiB tail and its framing");
        foreach (var output in outputs) Encoding.UTF8.GetByteCount(output.Json.GetRawText()).Should().BeLessThan(4096 + 512);
        outputs.Last().Should().Be(dropped, "the dropped count follows the kept outputs of the call");
        run.EventNames.ToList().IndexOf("output").Should().BeGreaterThan(run.EventNames.ToList().IndexOf("started"));
    }

    [Test]
    public async Task A_message_in_an_event_is_cut_to_4_KiB_of_JSON_text_and_ends_with_an_ellipsis()
    {
        WorkerReport.Cut(new string('a', 5000)).Should().HaveLength(4093 + 1).And.EndWith("…");
        WorkerReport.Cut(new string('a', 4096)).Should().Be(new string('a', 4096));
        WorkerReport.Cut(new string('"', 3000)).Should().EndWith("…").And.HaveLength(2046 + 1, "a quote takes two bytes of JSON text");
        await Task.CompletedTask;
    }

    // --- Text report ----------------------------------------------------------------------------------------------

    [Test]
    public async Task Without_json_the_report_is_text_lines_on_stderr_and_stdout_still_holds_only_frames()
    {
        var run = await RunAsync(["--abi", "extism-kernel-v1", "--module", Corpus("extism-log"), "--build-identity", Identity], Initialize, Exit);

        run.ExitCode.Should().Be(0, run.Describe());
        run.Frames.Should().HaveCount(1);
        run.Events.Should().BeEmpty();
        run.StderrLines.Should().OnlyContain(line => line.StartsWith("morphir extension worker: ", StringComparison.Ordinal));
        run.StderrLines.First().Should().Contain("serving extism-kernel-v1 module " + Sha256(Corpus("extism-log"))).And.NotContain("wasmtime", "no worker event carries the engine identity, in either form");
        run.StderrLines.Last().Should().Be("morphir extension worker: exit 0");
    }

    // --- Interruption and the workspace ---------------------------------------------------------------------------

    [Test]
    public async Task An_interruption_stops_a_spinning_guest_within_one_second_with_130_and_no_fault()
    {
        using var sandbox = TestSandbox.Create();
        var (input, writer) = OpenInput(Initialize);
        using var _ = writer;
        using var interrupt = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var interruptedAt = TimeSpan.Zero;
        var running = RunAsync(Arguments(TestGuest("workspace-then-spin"), "--instructions", "9007199254740991", "--call-ms", "600000"),
            input, sandbox.Work, interrupt.Token);

        // Wait until the guest has asked for its workspace, so the call is inside handle.
        while (!Directory.EnumerateDirectories(sandbox.Work).Any() && clock.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(20);
        Directory.EnumerateDirectories(sandbox.Work).Should().ContainSingle("the guest created its private workspace below the root");
        await Task.Delay(200);
        interruptedAt = clock.Elapsed;
        await interrupt.CancelAsync();
        var run = await running;
        var stopped = clock.Elapsed - interruptedAt;

        run.ExitCode.Should().Be(130, run.Describe());
        stopped.Should().BeLessThan(TimeSpan.FromSeconds(1));
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal("started", "exited");
        run.Single("exited").Json.GetProperty("exitCode").GetInt32().Should().Be(130);
        Directory.EnumerateFileSystemEntries(sandbox.Work).Should().BeEmpty("the session's private directory is removed when the worker exits");
    }

    [Test]
    public async Task An_interruption_while_the_worker_waits_for_a_frame_exits_with_130()
    {
        var (input, writer) = OpenInput(Initialize);
        using var _ = writer;
        using var interrupt = new CancellationTokenSource();
        var stdout = new MemoryStream();
        var running = RunAsync(Arguments(Corpus("extism-echo")), input, null, interrupt.Token, stdout);
        var clock = Stopwatch.StartNew();
        while (stdout.Length == 0 && clock.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(20);
        await Task.Delay(100);
        await interrupt.CancelAsync();

        var run = await running.WaitAsync(TimeSpan.FromSeconds(5));

        run.ExitCode.Should().Be(130, run.Describe());
        run.FrameTexts.Should().Equal(Initialize);
        run.EventNames.Should().Equal("started", "exited");
    }

    [Test]
    public async Task The_private_workspace_is_removed_when_the_guest_served_until_exit()
    {
        using var sandbox = TestSandbox.Create();

        var run = await RunAsync(Arguments(Corpus("extism-workspace-info")), [.. Frame(Initialize), .. Frame(Exit)], sandbox.Work);

        run.ExitCode.Should().Be(0, run.Describe());
        run.FrameTexts.Should().ContainSingle().Which.Should().Contain(sandbox.Work.Replace("\\", "\\\\"));
        Directory.EnumerateFileSystemEntries(sandbox.Work).Should().BeEmpty();
    }

    [Test]
    public async Task The_private_workspace_is_removed_after_a_fault()
    {
        using var sandbox = TestSandbox.Create();

        var run = await RunAsync(Arguments(TestGuest("workspace-then-spin"), "--instructions", "1000000"), Frame(Initialize), sandbox.Work);

        run.ExitCode.Should().Be(1, run.Describe());
        run.Single("fault").Text("fault.kind").Should().Be("budget-exhausted");
        Directory.EnumerateFileSystemEntries(sandbox.Work).Should().BeEmpty();
    }
}
