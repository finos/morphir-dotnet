// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;

namespace Morphir.Extensions.Wasm.Tests;

public sealed class WasmPlatformTests
{
    [Test]
    public async Task Windows_x64_cannot_create_the_engine_even_without_the_product_probe()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip.Test("The engine refusal is exercised on the Windows x64 lane.");

        Exception? failure = null;
        try { _ = WasmEngine.Shared; }
        catch (Exception error) { failure = error; }
        await Assert.That(failure is PlatformNotSupportedException).IsTrue();
        await Assert.That(failure!.Message).Contains("win_x64_trap_unsupported");
    }
}

public static class WasmPlatformHooks
{
    [BeforeEvery(HookType.Test)]
    public static void RequireAvailableRuntime(TestContext context)
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
            && context.Metadata.TestDetails.Class.ClassType != typeof(WasmPlatformTests)
            && context.Metadata.TestDetails.Class.ClassType != typeof(FixtureProvenanceTests)
            && context.Metadata.TestDetails.Class.ClassType != typeof(ModuleDigestTests)
            && context.Metadata.TestDetails.Class.ClassType != typeof(ContractTests)
            && context.Metadata.TestDetails.Class.ClassType != typeof(WasmtimeCApiPinTests))
            Skip.Test("win_x64_trap_unsupported: guest execution is unavailable; tracked in #609.");
    }
}
