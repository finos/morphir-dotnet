using Morphir.Toolchain.IO;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Checks Unix file kinds before opening potentially blocking special files.</summary>
internal static class UnixInputFile
{
    internal static bool IsRegular(string path) =>
        OperatingSystem.IsWindows() || InputFileKind.IsRegular(path);
}
