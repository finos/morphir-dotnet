// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text.Json;

// A stand-in for morphir-mck. Environment variables steer it, so that every argument is free for pass-through tests:
//   MCK_STUB_STDIN=1      read standard input to the end and report it
//   MCK_STUB_EXIT=<n>     exit with code n (default 0)
//   MCK_STUB_PIDS=<file>  start a grandchild, write "<own pid> <grandchild pid>" to the file, then wait to be killed
//   MCK_STUB_ROLE=grandchild  wait to be killed (the grandchild itself)
// Standard output is one JSON object with the arguments, the working directory and the standard input, written as
// ASCII (non-ASCII text is escaped), so the console code page cannot change it.

var limit = TimeSpan.FromMinutes(2);

if (Environment.GetEnvironmentVariable("MCK_STUB_ROLE") == "grandchild")
{
    Thread.Sleep(limit);
    return 0;
}

var input = Environment.GetEnvironmentVariable("MCK_STUB_STDIN") == "1" ? Console.In.ReadToEnd() : null;

using (var stdout = Console.OpenStandardOutput())
using (var json = new Utf8JsonWriter(stdout))
{
    json.WriteStartObject();
    json.WriteStartArray("args");
    foreach (var argument in args) json.WriteStringValue(argument);
    json.WriteEndArray();
    json.WriteString("cwd", Environment.CurrentDirectory);
    // The folder this stub runs from, so a test can tell which installed copy ran.
    json.WriteString("base", AppContext.BaseDirectory);
    if (input is null) json.WriteNull("stdin");
    else json.WriteString("stdin", input);
    json.WriteEndObject();
    json.Flush();
    stdout.Write("\n"u8);
}

Console.Error.WriteLine("morphir-mck stub: standard error");

if (Environment.GetEnvironmentVariable("MCK_STUB_PIDS") is { Length: > 0 } pids)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    start.Environment["MCK_STUB_ROLE"] = "grandchild";
    start.Environment.Remove("MCK_STUB_PIDS");
    using var grandchild = Process.Start(start)!;
    File.WriteAllText(pids + ".tmp", $"{Environment.ProcessId} {grandchild.Id}");
    File.Move(pids + ".tmp", pids);
    Thread.Sleep(limit);
}

return int.TryParse(Environment.GetEnvironmentVariable("MCK_STUB_EXIT"), out var code) ? code : 0;
