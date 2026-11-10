// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// A Windows job object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> that this process joins. Every process it starts
/// afterwards inherits the job. Only this process holds the job's handle, and the handle stays open for the life of
/// the process, so when this process ends, even by <c>TerminateProcess</c>, Windows ends every process in the job.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class KillOnCloseJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private static readonly Lock Gate = new();
    private static nint _job;

    /// <summary>Joins the job, once per process, or says why it could not.</summary>
    internal static bool TryJoin([NotNullWhen(false)] out string? reason)
    {
        lock (Gate)
        {
            reason = null;
            if (_job != 0) return true;

            var job = CreateJobObjectW(0, 0);
            if (job == 0)
            {
                reason = $"CreateJobObject failed with error {Marshal.GetLastPInvokeError()}";
                return false;
            }

            var limits = new ExtendedLimitInformation { Basic = new BasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose } };
            if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, (uint)Unsafe.SizeOf<ExtendedLimitInformation>()) == 0)
            {
                reason = $"SetInformationJobObject failed with error {Marshal.GetLastPInvokeError()}";
                CloseHandle(job);
                return false;
            }

            // The current-process pseudo handle.
            if (AssignProcessToJobObject(job, -1) == 0)
            {
                reason = $"AssignProcessToJobObject failed with error {Marshal.GetLastPInvokeError()}";
                CloseHandle(job);
                return false;
            }

            _job = job;
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern nint CreateJobObjectW(nint attributes, nint name);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int SetInformationJobObject(nint job, int informationClass, ref ExtendedLimitInformation information, uint length);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int CloseHandle(nint handle);
}
