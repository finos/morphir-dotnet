// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

// A child process for the tests of the test support library, so they start a program of the repository and no shell.
// tests/Morphir.TestSupport/build/Morphir.ProcessHelper.targets builds it once with `dotnet build`, into the obj folder
// of Morphir.TestSupport, and copies it into the folder process-helper of each test project's output. The tests start
// it with `dotnet exec`; nothing compiles it while a test runs. It has no package and writes no lock file.
//
// Modes:
//   write-both <n>         writes n times 'a' to stdout and n times 'b' to stderr, then exits 0
//   converse               reads one line, writes "got <line>", then reads the rest and writes "rest <rest, trimmed>"
//   send-ctrl <pid> <0|1>  Windows only: sends CTRL_C_EVENT (0) or CTRL_BREAK_EVENT (1) to the console of <pid>
//                          and writes "sending <UTC ticks>", then "sent"; or "attach-failed <error>" and exits 2, or
//                          "generate-failed <error>" and exits 3

#:property RestorePackagesWithLockFile=false
#:property UseAppHost=false
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

return args switch
{
    ["write-both", var size] => WriteBoth(int.Parse(size, CultureInfo.InvariantCulture)),
    ["converse"] => Converse(),
    ["send-ctrl", var pid, var controlEvent] => OperatingSystem.IsWindows()
        ? ConsoleControl.Send(uint.Parse(pid, CultureInfo.InvariantCulture), uint.Parse(controlEvent, CultureInfo.InvariantCulture))
        : Fail("send-ctrl sends a Windows console control event and runs only on Windows; use kill(pid, SIGINT) elsewhere."),
    _ => Fail($"Unknown process-helper invocation: {string.Join(' ', args)}"),
};

static int WriteBoth(int size)
{
    // Both writes are large, so a caller that reads one stream to its end before the other deadlocks.
    using var stdout = Console.OpenStandardOutput();
    using var stderr = Console.OpenStandardError();
    stdout.Write(Encoding.ASCII.GetBytes(new string('a', size)));
    stdout.Flush();
    stderr.Write(Encoding.ASCII.GetBytes(new string('b', size)));
    stderr.Flush();
    return 0;
}

static int Converse()
{
    var line = Console.In.ReadLine();
    Console.Out.WriteLine("got " + line);
    Console.Out.Flush();
    var rest = Console.In.ReadToEnd();
    Console.Out.WriteLine("rest " + rest.Trim());
    Console.Out.Flush();
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 64;
}

/// <summary>
/// A console control event reaches every process attached to a console, so the test process cannot send one to a
/// child without receiving it too. This helper runs with a console of its own, leaves it, attaches to the console of
/// the target, ignores Ctrl+C itself, and generates the event for every process on that console. A Ctrl+Break also
/// ends the helper, so the time is written just before the event.
/// </summary>
static class ConsoleControl
{
    const int AttachFailed = 2;
    const int GenerateFailed = 3;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

    public static int Send(uint pid, uint controlEvent)
    {
        // Open stdout before the console changes: it is the caller's pipe, which stays valid after FreeConsole.
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        FreeConsole();
        if (!AttachConsole(pid))
        {
            output.WriteLine("attach-failed " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));
            return AttachFailed;
        }

        SetConsoleCtrlHandler(IntPtr.Zero, true);
        output.WriteLine("sending " + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        if (!GenerateConsoleCtrlEvent(controlEvent, 0))
        {
            output.WriteLine("generate-failed " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));
            return GenerateFailed;
        }

        output.WriteLine("sent");
        FreeConsole();
        return 0;
    }
}
