namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 2.4 of add-wasm-extension-host: classification from the engine's validated module against the pinned
/// contract table (parent decision CLS-1), checked in-process against the crafted <c>classification/</c> cases.
/// </summary>
public class ClassificationTests
{
    // Counted in spec/mep/mck/wasm-host.cases.json at finos/morphir 48d10706f (MepWasmFixtures.PinnedCommit).
    // Change it only when the pin moves.
    private const int PinnedClassificationCaseCount = 14;

    private const string ClassificationGroup = "classification/";

    /// <summary>The profiles this host declares that it runs.</summary>
    private static IReadOnlySet<string> HostProfiles =>
        WasmHost.Profiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);

    public static IEnumerable<string> ClassificationCaseIds() =>
        CraftedCorpus.Cases
            .Where(@case => @case.CaseId.StartsWith(ClassificationGroup, StringComparison.Ordinal))
            .Select(@case => @case.CaseId);

    [Test]
    public async Task The_corpus_has_every_pinned_classification_case()
    {
        await Assert.That(ClassificationCaseIds().Count()).IsEqualTo(PinnedClassificationCaseCount);
    }

    [Test]
    public async Task This_host_runs_the_Extism_profile_only()
    {
        await Assert.That(HostProfiles).IsEquivalentTo(new[] { "extism-kernel-v1" });
    }

    /// <summary>
    /// Each classification case gives its expected observation in-process. A case that applies to this host is
    /// checked against the expectation the corpus selects for a host that runs only <c>extism-kernel-v1</c>. A
    /// case that applies only to <c>morphir-bytes-v1</c> is <c>not_applicable</c> to this host in the worker
    /// run; here its classification against the contract alone is checked against its expectation, and this
    /// host's admission must then refuse it as the contract says for a profile the host does not run.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(ClassificationCaseIds))]
    public async Task Classification_case_gives_its_expected_observation(string caseId)
    {
        var @case = CraftedCorpus.Case(caseId);
        var bytes = await File.ReadAllBytesAsync(@case.WasmPath);
        await Assert.That(MepWasmFixtures.Sha256(@case.WasmPath)).IsEqualTo(@case.Sha256);

        var admitted = HostOutcome.Admit(bytes, @case.DeclaredAbi);
        if (HostProfiles.Contains(@case.AppliesTo))
        {
            foreach (var alternatives in @case.ExpectationsFor(HostProfiles))
            {
                await CaseExpectation.SatisfyOneAsync(alternatives, expectation => ExpectOutcome(admitted, expectation));
            }

            return;
        }

        var classified = HostOutcome.Classify(bytes, @case.DeclaredAbi);
        var hostRunningTheProfile = HostProfiles.Append(@case.AppliesTo).ToHashSet(StringComparer.Ordinal);
        foreach (var alternatives in @case.ExpectationsFor(hostRunningTheProfile))
        {
            await CaseExpectation.SatisfyOneAsync(alternatives, expectation => ExpectOutcome(classified, expectation));
        }

        if (classified.RefusalCode is not null)
        {
            await Assert.That(admitted).IsEqualTo(classified);
        }
        else
        {
            await Assert.That(admitted.RefusalCode).IsEqualTo("extension.abi_unsupported");
            await Assert.That(admitted.Message!).Contains(classified.Profile!.Id);
        }
    }

    private static async Task ExpectOutcome(HostOutcome outcome, CaseExpectation expectation)
    {
        if (expectation.RefusedCode is { } code)
        {
            await Assert.That(outcome.RefusalCode).IsEqualTo(code);
            foreach (var fragment in expectation.MessageIncludes)
            {
                await Assert.That(outcome.Message!).Contains(fragment);
            }

            return;
        }

        // A started event: the module loads, as the classified profile. What the guest then answers
        // (expectation.GuestRunMembers) needs the guest to run, which is the Extism adapter of task 3.1.
        await Assert.That(outcome.RefusalCode).IsNull();
        if (expectation.StartedAbi is { } abi)
        {
            await Assert.That(outcome.Profile!.Id).IsEqualTo(abi);
        }
    }

    [Test]
    public async Task A_byte_profile_module_with_a_bad_import_is_refused_naming_the_import_and_not_the_profile()
    {
        // Contract rule classification:order: the import check comes before the check of whether this host runs
        // the profile, so an Extism-only host names the import (the parent's scenario "Byte-profile module with a
        // bad import on an Extism-only host").
        var outcome = HostOutcome.Admit(CraftedCorpus.ModuleBytes("bytes-input-byte-i64"), "morphir-bytes-v1");

        await Assert.That(outcome.RefusalCode).IsEqualTo("extension.abi_unsupported");
        await Assert.That(outcome.Message!).Contains("morphir_bytes_v1.input_byte");
        await Assert.That(outcome.Message!).DoesNotContain("this host runs Extism modules");
    }

    [Test]
    public async Task The_classifier_makes_its_checks_in_the_contract_order()
    {
        // The order the classifier and WasmHost.Admit implement, which the contract reader requires the pin to give.
        using var contract = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var order = contract.RootElement.GetProperty("classification").GetProperty("order").EnumerateArray()
            .Select(step => step.GetString()!)
            .ToList();

        await Assert.That(order).IsEquivalentTo(WasmProfileContract.ClassificationOrder, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(WasmProfileContract.Pinned.Id).IsNotNull();
    }

    [Test]
    public async Task An_import_outside_the_contract_table_is_refused_by_name()
    {
        var outcome = HostOutcome.Admit(CraftedCorpus.ModuleBytes("env-open-socket"), "extism-kernel-v1");

        await Assert.That(outcome.RefusalCode).IsEqualTo("extension.abi_unsupported");
        await Assert.That(outcome.Message!).Contains("env.open_socket");
    }

    [Test]
    public async Task A_shared_memory_is_refused_as_an_unsupported_feature_naming_it()
    {
        var load = WasmEngine.Shared.Load(CraftedCorpus.ModuleBytes("shared-memory"));

        var refusal = load.Match<WasmRefusal?>(_ => null, refused => refused, _ => null);
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.Code).IsEqualTo("extension.wasm_feature_unsupported");
        await Assert.That(refusal.Message).Contains("shared memor");
    }

    [Test]
    [Arguments("memory64", "64-bit memor")]
    [Arguments("two-memories", "multiple memories")]
    public async Task A_memory_the_contract_excludes_is_refused_naming_the_feature(string module, string feature)
    {
        var outcome = HostOutcome.Admit(CraftedCorpus.ModuleBytes(module), "extism-kernel-v1");

        await Assert.That(outcome.RefusalCode).IsEqualTo("extension.wasm_feature_unsupported");
        await Assert.That(outcome.Message!).Contains(feature);
    }

    [Test]
    [Arguments("morphir-bytes-v1")]
    [Arguments(null)]
    public async Task A_byte_profile_module_is_refused_naming_the_profile(string? declaredAbi)
    {
        var bytes = CraftedCorpus.ModuleBytes("bytes-echo");

        var classified = HostOutcome.Classify(bytes, declaredAbi);
        var outcome = HostOutcome.Admit(bytes, declaredAbi);

        await Assert.That(classified.Profile).IsEqualTo(WasmAbi.MorphirBytes);
        await Assert.That(outcome.RefusalCode).IsEqualTo("extension.abi_unsupported");
        await Assert.That(outcome.Message!).Contains("morphir-bytes-v1");
        await Assert.That(outcome.Message!).Contains("this host runs Extism modules");
    }

    [Test]
    public async Task A_byte_profile_declaration_is_still_compared_with_the_classified_profile()
    {
        var outcome = HostOutcome.Admit(CraftedCorpus.ModuleBytes("bytes-echo"), "extism-kernel-v1");

        await Assert.That(outcome.RefusalCode).IsEqualTo("extension.abi_mismatch");
        await Assert.That(outcome.Message!).Contains("extism-kernel-v1");
        await Assert.That(outcome.Message!).Contains("morphir-bytes-v1");
    }

    [Test]
    public async Task A_module_with_no_profile_import_and_no_declaration_is_extism()
    {
        // Contract rule classification:default; the corpus has no case for it because a worker requires --abi.
        var outcome = HostOutcome.Admit(CraftedCorpus.ModuleBytes("no-imports"), declaredAbi: null);

        await Assert.That(outcome).IsEqualTo(HostOutcome.Accepted(WasmAbi.ExtismKernel));
    }

    /// <summary>The imports of the published Python extension 0.4.2, as the kernel proof recorded them
    /// (evidence/2.2-kernel-proof.md, "guest imports (8)"). The module is not in the repository.</summary>
    private static readonly (string Name, string Signature)[] PythonImports =
    [
        ("alloc", "(i64) -> (i64)"),
        ("error_set", "(i64) -> ()"),
        ("output_set", "(i64, i64) -> ()"),
        ("input_length", "() -> (i64)"),
        ("input_load_u64", "(i64) -> (i64)"),
        ("input_load_u8", "(i64) -> (i32)"),
        ("store_u64", "(i64, i64) -> ()"),
        ("store_u8", "(i64, i32) -> ()"),
    ];

    [Test]
    public async Task A_module_of_the_Python_shape_is_accepted_as_extism()
    {
        // extism-full-surface imports every function of the contract's table, so every import of the
        // Python 0.4.2 module, with the same signature, and exports handle as () -> i32 as Python does.
        var bytes = CraftedCorpus.ModuleBytes("extism-full-surface");
        var compiled = WasmEngine.Shared.Load(bytes).Match(
            compiled => compiled,
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

        var imports = compiled.Module.Imports
            .OfType<Wasmtime.FunctionImport>()
            .Where(import => import.ModuleName == "extism:host/env")
            .ToDictionary(import => import.Name, Signature, StringComparer.Ordinal);
        foreach (var (name, signature) in PythonImports)
        {
            await Assert.That(imports).ContainsKey(name);
            await Assert.That(imports[name]).IsEqualTo(signature);
        }

        await Assert.That(HostOutcome.Admit(bytes, "extism-kernel-v1")).IsEqualTo(HostOutcome.Accepted(WasmAbi.ExtismKernel));
    }

    /// <summary>
    /// No module that the corpus expects to load is refused by classification: every case that applies to this
    /// host and expects no <c>refused</c> event, in any run or alternative, is admitted as its declared profile.
    /// This covers the modules of the other groups, the WASI imports of the sandbox cases among them.
    /// </summary>
    [Test]
    public async Task Every_module_the_corpus_expects_to_load_is_admitted()
    {
        var loading = CraftedCorpus.Cases
            .Where(@case => HostProfiles.Contains(@case.AppliesTo))
            .Where(@case => @case.AllExpectations().All(expectation => expectation.RefusedCode is null))
            .ToList();
        await Assert.That(loading.Count).IsGreaterThan(0);

        var refused = new List<string>();
        foreach (var @case in loading)
        {
            var outcome = HostOutcome.Admit(await File.ReadAllBytesAsync(@case.WasmPath), @case.DeclaredAbi);
            var expected = @case.DeclaredAbi ?? "extism-kernel-v1";
            if (outcome.RefusalCode is not null || outcome.Profile!.Id != expected)
            {
                refused.Add($"{@case.CaseId} ({@case.Module}): {outcome}");
            }
        }

        await Assert.That(refused).IsEmpty();
    }

    [Test]
    public async Task Every_refusal_code_the_classifier_gives_is_a_contract_refusal_code()
    {
        using var contract = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));
        var codes = contract.RootElement.GetProperty("refusals").EnumerateArray()
            .Select(refusal => refusal.GetProperty("code").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var given = new HashSet<string>(StringComparer.Ordinal);
        foreach (var @case in CraftedCorpus.Cases)
        {
            var bytes = await File.ReadAllBytesAsync(@case.WasmPath);
            foreach (var declared in new[] { @case.DeclaredAbi, null, "extism-kernel-v1", "morphir-bytes-v1" })
            {
                if (HostOutcome.Admit(bytes, declared).RefusalCode is { } code)
                {
                    given.Add(code);
                }
            }
        }

        await Assert.That(given.Count).IsGreaterThan(0);
        await Assert.That(given.Except(codes)).IsEmpty();
    }

    private static string Signature(Wasmtime.FunctionImport import) =>
        $"({Kinds(import.Parameters)}) -> ({Kinds(import.Results)})";

    private static string Kinds(IReadOnlyList<Wasmtime.ValueKind> kinds) => string.Join(", ", kinds.Select(kind => kind switch
    {
        Wasmtime.ValueKind.Int32 => "i32",
        Wasmtime.ValueKind.Int64 => "i64",
        _ => kind.ToString(),
    }));
}
