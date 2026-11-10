// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.TestSupport;

namespace Morphir.E2E.Tests;

/// <summary>
/// Fails the run if any scenario touched the real machine's Morphir locations. The E2E scenarios run Morphir
/// executables, built or packaged, against the file system, so AGENTS.md asks for this hook.
/// </summary>
/// <remarks>Qualified attributes: Reqnroll also defines Before and After.</remarks>
public static class MachineTripwireHook
{
    private static MachineTripwire? _tripwire;

    [TUnit.Core.Before(TUnit.Core.HookType.Assembly)]
    public static void Capture() => _tripwire = MachineTripwire.Capture();

    [TUnit.Core.After(TUnit.Core.HookType.Assembly)]
    public static void Verify() => _tripwire?.AssertUnchanged();
}
