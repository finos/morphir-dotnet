// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using Morphir.Toolchain.IO;

namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// Joins the existing process-lifetime kill-on-close job. The shared I/O library
/// owns the native job primitives; the host retains its original call contract.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class KillOnCloseJob
{
    internal static bool TryJoin([NotNullWhen(false)] out string? reason)
    {
        try
        {
            WindowsProcessLifetime.JoinCurrentProcess();
            reason = null;
            return true;
        }
        catch (Win32Exception error)
        {
            reason = error.Message;
            return false;
        }
    }
}
