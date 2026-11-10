namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// The library reads its import table from the pinned contract, embedded as a resource (design PIN-1), so the
/// embedded copy must be the pinned file.
/// </summary>
public class ContractTests
{
    [Test]
    public async Task The_embedded_contract_is_the_pinned_file()
    {
        var pinned = MepWasmFixtures.Sha256(MepWasmFixtures.Contract);

        await Assert.That(WasmProfileContract.Pinned.Sha256).IsEqualTo(pinned);
    }

    [Test]
    public async Task The_embedded_contract_has_the_pinned_identity()
    {
        var contract = WasmProfileContract.Pinned;

        await Assert.That(contract.Id).IsEqualTo("morphir/mep-wasm-execution-profiles");
        await Assert.That(contract.Version).IsEqualTo("0.1.0-draft.1");
    }

    [Test]
    public async Task The_notification_advertisement_is_read_from_the_contract()
    {
        // The Extism profile's notificationDelivery object: a string member of the morphir.initialize result.
        var delivery = WasmProfileContract.Pinned.Profile(WasmAbi.ExtismKernel).NotificationDelivery;

        await Assert.That(delivery).IsNotNull();
        await Assert.That(delivery!.Member).IsEquivalentTo(new[] { "capabilities", "notificationDelivery" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(delivery.Delivering).IsEqualTo("call");
        await Assert.That(delivery.Default).IsEqualTo("none");
        await Assert.That(WasmProfileContract.Pinned.Profile(WasmAbi.MorphirBytes).NotificationDelivery).IsNull();
    }

    /// <summary>The 46 functions of <c>wasi_snapshot_preview1.witx</c>, in its order.</summary>
    private static readonly string[] WasiPreview1Functions =
    [
        "args_get", "args_sizes_get", "environ_get", "environ_sizes_get", "clock_res_get", "clock_time_get",
        "fd_advise", "fd_allocate", "fd_close", "fd_datasync", "fd_fdstat_get", "fd_fdstat_set_flags",
        "fd_fdstat_set_rights", "fd_filestat_get", "fd_filestat_set_size", "fd_filestat_set_times", "fd_pread",
        "fd_prestat_get", "fd_prestat_dir_name", "fd_pwrite", "fd_read", "fd_readdir", "fd_renumber", "fd_seek",
        "fd_sync", "fd_tell", "fd_write", "path_create_directory", "path_filestat_get", "path_filestat_set_times",
        "path_link", "path_open", "path_readlink", "path_remove_directory", "path_rename", "path_symlink",
        "path_unlink_file", "poll_oneoff", "proc_exit", "proc_raise", "sched_yield", "random_get", "sock_accept",
        "sock_recv", "sock_send", "sock_shutdown",
    ];

    [Test]
    public async Task The_engine_defines_every_function_of_the_contract_WASI_surface()
    {
        // The classifier takes WASI signatures from the engine's own preview 1 definitions; the contract names the
        // witx definition and its 46 functions instead of a table.
        var surface = WasmProfileContract.Pinned.Profile(WasmAbi.ExtismKernel);
        using var linker = new Wasmtime.Linker(WasmEngine.Shared.Engine);
        using var store = new Wasmtime.Store(WasmEngine.Shared.Engine);
        linker.DefineWasi();

        var missing = WasiPreview1Functions.Where(name => linker.GetFunction(store, "wasi_snapshot_preview1", name) is null).ToList();

        await Assert.That(surface.WasiFunctions).IsEqualTo(46);
        await Assert.That(WasiPreview1Functions.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(surface.WasiFunctions);
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task The_contract_gives_the_memory_rules_the_library_applies()
    {
        // The reader refuses a contract whose memory rules are not these, in this order, so a changed rule set
        // fails the load instead of passing unread.
        using var contract = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var rules = contract.RootElement.GetProperty("memory").EnumerateArray()
            .Select(rule => rule.GetProperty("id").GetString()!)
            .ToList();

        await Assert.That(rules).IsEquivalentTo(WasmProfileContract.MemoryRules, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(WasmProfileContract.MemoryRules).IsEquivalentTo(
            new[] { "at-ceiling", "guest-reported", "observed-refusal", "initial-above-ceiling" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_worker_instruction_range_is_read_from_the_contract()
    {
        // worker.arguments gives --instructions a range; the library reads it so the worker takes no copy of its own.
        using var contract = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var range = contract.RootElement.GetProperty("worker").GetProperty("arguments").EnumerateArray()
            .Single(argument => argument.GetProperty("name").GetString() == "--instructions")
            .GetProperty("range");
        var read = WasmProfileContract.Pinned.WorkerInstructionRange;

        await Assert.That(read).IsEqualTo(new WorkerArgumentRange(range[0].GetUInt64(), range[1].GetUInt64()));
        await Assert.That(read).IsEqualTo(new WorkerArgumentRange(1, 9007199254740991));
        await Assert.That(WasmProfileContract.Pinned.InstructionBudget).IsGreaterThanOrEqualTo(read.Minimum);
        await Assert.That(WasmProfileContract.Pinned.InstructionBudget).IsLessThanOrEqualTo(read.Maximum);
    }

    [Test]
    public async Task The_worker_refusal_codes_are_read_from_the_contract()
    {
        // worker.refusals lists the codes a worker refuses with besides the module refusals; the reader refuses a
        // contract whose list is not the library's, so a code added upstream fails the load instead of passing unread.
        using var contract = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var codes = contract.RootElement.GetProperty("worker").GetProperty("refusals").EnumerateArray()
            .Select(refusal => refusal.GetProperty("code").GetString()!)
            .ToList();

        await Assert.That(WasmProfileContract.Pinned.WorkerRefusalCodes).IsEquivalentTo(codes);
        await Assert.That(WasmProfileContract.Pinned.WorkerRefusalCodes).IsEquivalentTo(WasmWorkerRefusal.Codes);
        await Assert.That(WasmWorkerRefusal.Codes).IsEquivalentTo(
            new[] { "extension.worker_runtime_mismatch", "extension.runtime_unavailable" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_contract_states_the_worker_start_order_the_library_implements()
    {
        // The rule start-order is prose, not data, so the reader holds its text word for word: an upstream change to
        // the order fails the load, and the worker is reviewed against the new text before the pin moves.
        using var contract = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var rule = contract.RootElement.GetProperty("worker").GetProperty("rules").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == "start-order")
            .GetProperty("rule").GetString();

        await Assert.That(rule).IsEqualTo(WasmProfileContract.WorkerStartOrderRule);
        await Assert.That(WasmProfileContract.WorkerStartOrder).IsEquivalentTo(
            new[] { "arguments", "build-identity", "engine", "module" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_contract_says_a_cancelled_call_is_an_outcome_without_a_fault_kind()
    {
        // The hostSession rule cancelled, which the library's GuestFailureKind.Cancelled follows.
        using var contract = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var rules = contract.RootElement.GetProperty("hostSession").GetProperty("rules").EnumerateArray()
            .Select(rule => rule.GetProperty("id").GetString()!)
            .ToList();
        var failure = new GuestFailure(GuestFailureKind.Cancelled, GuestFailureKind.Cancelled, "detail", new MemoryUse(null, 65536, 65536 * 4), 0, TimeSpan.Zero);

        await Assert.That(rules).Contains("cancelled");
        await Assert.That(failure.FaultKind).IsNull();
        await Assert.That(failure.Code).IsNull();
        await Assert.That(failure.Outcome).IsEqualTo("cancelled");
    }
}
