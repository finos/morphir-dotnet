using System.Runtime.InteropServices;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Checks Unix file kinds before opening potentially blocking special files.</summary>
internal static class UnixInputFile
{
    internal static bool IsRegular(string path)
    {
        if (OperatingSystem.IsWindows()) return true; // FileAttributes handles Windows devices and links.
        if (LStat(path, out var status) != 0)
            throw new IOException("The input entry could not be inspected.");
        return (status.Mode & 0xF000) == 0x8000;
    }

    // System.Native normalizes the platform-specific libc stat layouts. This matches the
    // .NET 10 runtime ABI, including its trailing fields; do not substitute a libc struct.
    // https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/Common/src/Interop/Unix/System.Native/Interop.Stat.cs
    [StructLayout(LayoutKind.Sequential)]
    private struct FileStatus
    {
        internal int Flags;
        internal int Mode;
        internal uint Uid;
        internal uint Gid;
        internal long Size;
        internal long ATime;
        internal long ATimeNsec;
        internal long MTime;
        internal long MTimeNsec;
        internal long CTime;
        internal long CTimeNsec;
        internal long BirthTime;
        internal long BirthTimeNsec;
        internal long Dev;
        internal long RDev;
        internal long Ino;
        internal uint UserFlags;
    }

    [DllImport("System.Native", EntryPoint = "SystemNative_LStat", SetLastError = true)]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out FileStatus status);
}
