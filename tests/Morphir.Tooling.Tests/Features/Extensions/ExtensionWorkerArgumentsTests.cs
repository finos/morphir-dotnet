// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions.Wasm;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// The arguments of <c>morphir extension worker</c> (task 4.1 of <c>add-wasm-extension-host</c>): every argument
/// the pinned contract's <c>worker.arguments</c> lists, with its default, and the usage errors that exit with 2.
/// </summary>
[Category("PlatformSpecific")]
public sealed class ExtensionWorkerArgumentsTests
{
    private static JsonElement ContractWorker()
    {
        using var contract = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(WorkerHarness.Fixtures, "wasm-execution-profiles.json")));
        return contract.RootElement.GetProperty("worker").Clone();
    }

    private static JsonElement ContractArgument(string name) =>
        ContractWorker().GetProperty("arguments").EnumerateArray().Single(argument => argument.GetProperty("name").GetString() == name);

    private static readonly string Module = Path.Combine(Path.GetTempPath(), "no-such-dir", "guest.wasm");

    private static List<string> Required(string abi = "extism-kernel-v1") =>
        ["--abi", abi, "--module", Module, "--build-identity", WorkerHarness.Identity];

    private static WorkerOptions Serve(IReadOnlyList<string> arguments) => WorkerInvocation.Parse(arguments) switch
    {
        WorkerInvocation.Serve serve => serve.Options,
        var other => throw new InvalidOperationException($"expected Serve, got {other}"),
    };

    private static string Invalid(IReadOnlyList<string> arguments) => WorkerInvocation.Parse(arguments) switch
    {
        WorkerInvocation.Invalid invalid => invalid.Message,
        var other => throw new InvalidOperationException($"expected Invalid, got {other}"),
    };

    [Test]
    public void The_worker_takes_exactly_the_arguments_the_contract_lists()
    {
        var names = ContractWorker().GetProperty("arguments").EnumerateArray().Select(argument => argument.GetProperty("name").GetString()!);
        WorkerInvocation.ArgumentNames.Should().Equal(names);
    }

    [Test]
    public void The_contract_names_the_command()
    {
        ContractWorker().GetProperty("command").GetString().Should().Be(WorkerInvocation.Command);
    }

    [Test]
    public void An_optional_argument_left_out_takes_the_contract_default()
    {
        var options = Serve(Required());

        options.RequestBytes.Should().Be(ContractArgument("--request-bytes").GetProperty("default").GetInt64());
        options.ResponseBytes.Should().Be(ContractArgument("--response-bytes").GetProperty("default").GetInt64());
        options.CallDeadline.TotalMilliseconds.Should().Be(ContractArgument("--call-ms").GetProperty("default").GetProperty("extism-kernel-v1").GetInt64());
        options.Instructions.Should().Be(ContractArgument("--instructions").GetProperty("default").GetUInt64());
        options.MemoryPages.Should().Be(ContractArgument("--memory-pages").GetProperty("default").GetInt32());
        options.Json.Should().BeFalse();
        options.Abi.Should().Be(WasmAbi.ExtismKernel);
        options.ModulePath.Should().Be(Module);
        options.BuildIdentity.Should().Be(WorkerHarness.Identity);
    }

    [Test]
    public void The_call_deadline_default_follows_the_declared_profile()
    {
        Serve(Required("morphir-bytes-v1")).CallDeadline.TotalMilliseconds
            .Should().Be(ContractArgument("--call-ms").GetProperty("default").GetProperty("morphir-bytes-v1").GetInt64());
    }

    [Test]
    public void The_defaults_are_the_session_limits_of_the_contract()
    {
        var limits = Serve(Required()).Limits;
        var contract = SessionLimits.Default;

        limits.MemoryCeilingBytes.Should().Be(contract.MemoryCeilingBytes);
        limits.InstructionBudget.Should().Be(contract.InstructionBudget);
        limits.CallDeadline.Should().Be(contract.CallDeadline);
        limits.RequestFrameBytes.Should().Be(contract.RequestFrameBytes);
        limits.ResponseFrameBytes.Should().Be(contract.ResponseFrameBytes);
    }

    [Test]
    public void Every_bound_argument_sets_the_session_limit_it_names()
    {
        var options = Serve([.. Required(), "--request-bytes", "64", "--response-bytes", "32", "--call-ms", "1500",
            "--instructions", "1000000", "--memory-pages", "17", "--json"]);

        options.Json.Should().BeTrue();
        options.Limits.RequestFrameBytes.Should().Be(64);
        options.Limits.ResponseFrameBytes.Should().Be(32);
        options.Limits.CallDeadline.Should().Be(TimeSpan.FromMilliseconds(1500));
        options.Limits.InstructionBudget.Should().Be(1_000_000UL);
        options.Limits.MemoryCeilingBytes.Should().Be(17L * 65536);
    }

    [Test]
    public void An_argument_may_give_its_value_after_an_equals_sign_and_the_arguments_come_in_any_order()
    {
        var options = Serve(["--json", $"--build-identity={WorkerHarness.Identity}", "--instructions=7", "--module", Module, "--abi=extism-kernel-v1"]);

        options.Instructions.Should().Be(7UL);
        options.Json.Should().BeTrue();
    }

    [Test]
    public void The_largest_instruction_budget_is_accepted()
    {
        Serve([.. Required(), "--instructions", "9007199254740991"]).Instructions.Should().Be(9007199254740991UL);
    }

    [Test]
    public void The_instruction_budget_accepts_exactly_the_range_the_contract_states()
    {
        var range = ContractArgument("--instructions").GetProperty("range");
        var (minimum, maximum) = (range[0].GetUInt64(), range[1].GetUInt64());
        string Text(ulong value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Serve([.. Required(), "--instructions", Text(minimum)]).Instructions.Should().Be(minimum);
        Serve([.. Required(), "--instructions", Text(maximum)]).Instructions.Should().Be(maximum);
        Invalid([.. Required(), "--instructions", Text(minimum - 1)]).Should().Contain($"from {Text(minimum)} to {Text(maximum)}");
        Invalid([.. Required(), "--instructions", Text(maximum + 1)]).Should().Contain($"from {Text(minimum)} to {Text(maximum)}");
        WorkerInvocation.HelpText.Should().Contain($"from {Text(minimum)} to").And.Contain(Text(maximum));
    }

    [Test]
    public void Only_the_instruction_budget_has_a_range_in_the_contract()
    {
        // The other ranges (frame limits, --call-ms, --memory-pages) are this host's own until the contract states them.
        ContractWorker().GetProperty("arguments").EnumerateArray()
            .Where(argument => argument.TryGetProperty("range", out _))
            .Select(argument => argument.GetProperty("name").GetString())
            .Should().Equal("--instructions");
    }

    [Test]
    [Arguments("--abi")]
    [Arguments("--module")]
    [Arguments("--build-identity")]
    public void A_required_argument_left_out_is_a_usage_error_that_names_it(string name)
    {
        var arguments = Required();
        var at = arguments.IndexOf(name);
        arguments.RemoveRange(at, 2);

        Invalid(arguments).Should().Contain(name);
    }

    [Test]
    public void An_argument_the_worker_does_not_know_is_a_usage_error()
    {
        Invalid([.. Required(), "--verbose"]).Should().Contain("--verbose");
    }

    [Test]
    public void A_positional_argument_is_a_usage_error()
    {
        Invalid([.. Required(), "extra"]).Should().Contain("extra");
    }

    [Test]
    public void An_argument_given_twice_is_a_usage_error()
    {
        Invalid([.. Required(), "--call-ms", "10", "--call-ms", "20"]).Should().Contain("--call-ms");
    }

    [Test]
    public void An_option_without_its_value_is_a_usage_error()
    {
        Invalid([.. Required(), "--call-ms"]).Should().Contain("--call-ms");
    }

    [Test]
    public void An_abi_that_is_not_a_profile_of_the_contract_is_a_usage_error()
    {
        Invalid(["--abi", "wasi-p2", "--module", Module, "--build-identity", WorkerHarness.Identity]).Should().Contain("--abi");
    }

    [Test]
    [Arguments("abc")]
    [Arguments("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [Arguments("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdefg")]
    public void A_build_identity_that_is_not_64_hexadecimal_digits_is_a_usage_error(string identity)
    {
        Invalid(["--abi", "extism-kernel-v1", "--module", Module, "--build-identity", identity]).Should().Contain("--build-identity");
    }

    [Test]
    public void An_upper_case_build_identity_is_read_as_the_same_digits()
    {
        Serve(["--abi", "extism-kernel-v1", "--module", Module, "--build-identity", WorkerHarness.Identity.ToUpperInvariant()])
            .BuildIdentity.Should().Be(WorkerHarness.Identity);
    }

    [Test]
    [Arguments("--request-bytes", "0")]
    [Arguments("--request-bytes", "-1")]
    [Arguments("--response-bytes", "x")]
    [Arguments("--call-ms", "0")]
    [Arguments("--call-ms", "2147483648")]
    [Arguments("--instructions", "0")]
    [Arguments("--instructions", "-5")]
    [Arguments("--instructions", "9007199254740992")]
    [Arguments("--instructions", "1e6")]
    [Arguments("--memory-pages", "0")]
    [Arguments("--memory-pages", "65537")]
    public void A_bound_that_is_not_in_its_range_is_a_usage_error(string name, string value)
    {
        Invalid([.. Required(), name, value]).Should().Contain(name);
    }

    [Test]
    public void Help_is_shown_whatever_else_is_missing()
    {
        WorkerInvocation.Parse(["--help"]).Should().Be(new WorkerInvocation.Help(Json: false));
        WorkerInvocation.Parse(["--help", "--json"]).Should().Be(new WorkerInvocation.Help(Json: true));
        WorkerInvocation.Parse(["-h", "--abi", "extism-kernel-v1"]).Should().Be(new WorkerInvocation.Help(Json: false));
    }

    [Test]
    public void A_usage_error_remembers_whether_json_was_asked_for()
    {
        WorkerInvocation.Parse(["--json"]).Should().BeOfType<WorkerInvocation.Invalid>().Which.Json.Should().BeTrue();
        WorkerInvocation.Parse(["--abi"]).Should().BeOfType<WorkerInvocation.Invalid>().Which.Json.Should().BeFalse();
    }

    [Test]
    public void The_help_says_the_command_is_internal_and_experimental_and_lists_every_argument()
    {
        var help = WorkerInvocation.HelpText;

        help.Should().Contain("[experimental, internal]");
        help.Should().Contain("Product commands do not use it");
        foreach (var name in WorkerInvocation.ArgumentNames) help.Should().Contain(name);
        help.Should().Contain("6291456").And.Contain("16777216").And.Contain("5000000000").And.Contain("4096").And.Contain("30000");
        help.Should().Contain("130");
    }

    [Test]
    [Arguments("--request-bytes")]
    [Arguments("--response-bytes")]
    public void A_frame_limit_cannot_exceed_what_the_framing_carries(string name)
    {
        // The stdio framing refuses a body over its own limit, so a larger frame limit could not be honoured:
        // a reply between the two would pass the worker's check and then fail at the write.
        var largest = WorkerInvocation.MaxFrameBytes;
        largest.Should().Be((ulong)Morphir.Mep.ContentLengthFraming.MaxBodyBytes + 28UL);

        Serve([.. Required(), name, largest.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        Invalid([.. Required(), name, (largest + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)]).Should().Contain(name);
    }
    [Test]
    public void The_worker_mode_is_selected_by_its_first_two_arguments_only()
    {
        ExtensionWorkerProcess.IsInvocation(["extension", "worker"]).Should().BeTrue();
        ExtensionWorkerProcess.IsInvocation(["extension", "worker", "--help"]).Should().BeTrue();
        ExtensionWorkerProcess.IsInvocation(["extension", "list"]).Should().BeFalse();
        ExtensionWorkerProcess.IsInvocation(["worker", "extension"]).Should().BeFalse();
        ExtensionWorkerProcess.IsInvocation(["--json", "extension", "worker"]).Should().BeFalse();
        ExtensionWorkerProcess.IsInvocation([]).Should().BeFalse();
    }
}
