using System.Diagnostics;
using System.Text;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 2.4 of add-wasm-extension-host (parent decision ENG-1): one engine for the process with fuel and epoch
/// interruption, compiled modules reused in memory by digest, and one store per session.
/// </summary>
public class EngineTests
{
    private static CompiledModule Compile(byte[] bytes) =>
        WasmEngine.Shared.Load(bytes).Match(
            compiled => compiled,
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

    private static ExtismModule AdmitExtism(string module) =>
        WasmHost.Admit(Compile(CraftedCorpus.ModuleBytes(module)), WasmAbi.ExtismKernel).Match(
            admitted => admitted,
            refused => throw new InvalidOperationException(refused.Message));

    private static SessionStore Open(ExtismModule module, SessionLimits limits) =>
        SessionStore.Open(module, limits).Match(
            session => session,
            failure => throw new InvalidOperationException(failure.Message));

    private static HandleCallResult Completed(HandleOutcome outcome) =>
        outcome.Match(
            completed => completed.Result,
            failed => throw new InvalidOperationException(failed.Message),
            notRun => throw new InvalidOperationException(notRun.Message));

    private static GuestFailure Failed(HandleOutcome outcome) =>
        outcome.Match(
            completed => throw new InvalidOperationException($"The call completed with return code {completed.Result.ReturnCode}."),
            failed => failed,
            notRun => throw new InvalidOperationException(notRun.Message));

    [Test]
    public async Task The_shared_engine_is_one_instance()
    {
        await Assert.That(ReferenceEquals(WasmEngine.Shared, WasmEngine.Shared)).IsTrue();
        await Assert.That(ReferenceEquals(WasmEngine.Shared.Engine, WasmEngine.Shared.Engine)).IsTrue();
    }

    [Test]
    public async Task The_same_bytes_load_as_the_same_compiled_module()
    {
        var bytes = CraftedCorpus.ModuleBytes("extism-echo");
        var copy = bytes.ToArray();

        var first = Compile(bytes);
        var second = Compile(copy);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(ReferenceEquals(first.Module, second.Module)).IsTrue();
    }

    [Test]
    public async Task Different_bytes_load_as_different_compiled_modules()
    {
        var echo = Compile(CraftedCorpus.ModuleBytes("extism-echo"));
        var trap = Compile(CraftedCorpus.ModuleBytes("extism-trap"));

        await Assert.That(ReferenceEquals(echo, trap)).IsFalse();
        await Assert.That(ReferenceEquals(echo.Module, trap.Module)).IsFalse();
        await Assert.That(echo.Digest).IsNotEqualTo(trap.Digest);
    }

    [Test]
    public async Task A_compiled_module_is_keyed_by_the_SHA256_of_its_bytes()
    {
        var @case = CraftedCorpus.Case("extism/echo");

        var compiled = Compile(await File.ReadAllBytesAsync(@case.WasmPath));

        await Assert.That(compiled.Digest.ToString()).IsEqualTo(@case.Sha256);
        await Assert.That(compiled.ByteLength).IsEqualTo(new FileInfo(@case.WasmPath).Length);
    }

    [Test]
    public async Task Bytes_that_are_not_WASM_are_reported_as_such()
    {
        var load = WasmEngine.Shared.Load(Encoding.UTF8.GetBytes("this is a text file\n"));

        var reason = load.Match<string?>(_ => null, _ => null, notWasm => notWasm);
        await Assert.That(reason).IsNotNull();
    }

    [Test]
    public async Task Default_session_limits_come_from_the_contract()
    {
        var limits = SessionLimits.Default;

        await Assert.That(limits.MemoryCeilingBytes).IsEqualTo(268_435_456L);
        await Assert.That(limits.InstructionBudget).IsEqualTo(5_000_000_000UL);
        await Assert.That(limits.CallDeadline).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task A_second_session_starts_with_nothing_from_the_first()
    {
        var module = AdmitExtism("extism-echo");

        using var first = Open(module, SessionLimits.Default);
        var firstBlock = first.Alloc(4096);
        var secondBlock = first.Alloc(4096);
        first.KernelMemory.GetSpan(firstBlock, 4)[0] = 0x2a;
        var firstReply = Completed(first.CallHandle(Encoding.UTF8.GetBytes("""{"first":1}""")));

        using var second = Open(module, SessionLimits.Default);
        var fresh = second.Alloc(4096);

        await Assert.That(secondBlock).IsNotEqualTo(firstBlock);
        await Assert.That(fresh).IsEqualTo(firstBlock);
        await Assert.That(second.KernelMemory.GetSpan(fresh, 4)[0]).IsEqualTo((byte)0);
        await Assert.That(ReferenceEquals(first.KernelMemory, second.KernelMemory)).IsFalse();

        var secondReply = Completed(second.CallHandle(Encoding.UTF8.GetBytes("""{"second":2}""")));
        await Assert.That(Encoding.UTF8.GetString(firstReply.Output)).IsEqualTo("""{"first":1}""");
        await Assert.That(Encoding.UTF8.GetString(secondReply.Output)).IsEqualTo("""{"second":2}""");
    }

    [Test]
    public async Task Sessions_of_one_module_share_the_compiled_module_and_not_the_store()
    {
        var module = AdmitExtism("extism-echo");

        using var first = Open(module, SessionLimits.Default);
        using var second = Open(module, SessionLimits.Default);

        await Assert.That(ReferenceEquals(first.Module, second.Module)).IsTrue();
        await Assert.That(ReferenceEquals(first.Guest, second.Guest)).IsFalse();
    }

    [Test]
    public async Task The_engine_meters_fuel_against_the_session_budget()
    {
        using var session = Open(AdmitExtism("extism-spin"),
            SessionLimits.Default with { InstructionBudget = 100_000 });

        var failure = Failed(session.CallHandle([]));

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.BudgetExhausted);
    }

    [Test]
    public async Task The_engine_interrupts_a_call_at_the_session_deadline()
    {
        using var session = Open(AdmitExtism("extism-spin"),
            SessionLimits.Default with { InstructionBudget = ulong.MaxValue, CallDeadline = TimeSpan.FromMilliseconds(200) });

        var clock = Stopwatch.StartNew();
        var failure = Failed(session.CallHandle([]));
        clock.Stop();

        await Assert.That(failure.Kind).IsEqualTo(GuestFailureKind.CallDeadline);
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Each_call_gets_the_full_budget_again()
    {
        using var session = Open(AdmitExtism("extism-echo"),
            SessionLimits.Default with { InstructionBudget = 2_000_000 });

        for (var call = 0; call < 5; call++)
        {
            var reply = Completed(session.CallHandle(Encoding.UTF8.GetBytes(new string('x', 10_000))));
            await Assert.That(reply.ReturnCode).IsEqualTo(0);
        }
    }
}
