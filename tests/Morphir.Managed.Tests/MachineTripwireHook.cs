// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.TestSupport;

namespace Morphir.Managed.Tests;

/// <summary>Fails the run if any test in this assembly touched the real machine's Morphir locations.</summary>
public static class MachineTripwireHook
{
    private static MachineTripwire? _tripwire;

    [Before(HookType.Assembly)]
    public static void Capture() => _tripwire = MachineTripwire.Capture();

    [After(HookType.Assembly)]
    public static void Verify() => _tripwire?.AssertUnchanged();
}
