// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// A scripted fake of `<host> extension worker`. The command line is
//
//   [--script <file>] [--log <file>] extension worker <worker arguments>
//   --sleep                           (a child that would outlive its parent; it sleeps until stopped)
//
// The script is a JSON object:
//
//   {
//     "buildIdentity": "<64 hexadecimal digits>",   // what `--help --json` reports
//     "helpExit": 0,                                // optional: the exit status of --help
//     "modules": { "<module file name without extension>" | "*": [ <action>, ... ] }
//   }
//
// Each action is an object with one member:
//
//   {"stderr": "<line>"}      a line on stderr; {moduleSha256}, {buildIdentity} and {abi} are replaced
//   {"event": {...}}          a morphir/command-event line on stderr with these members after its envelope
//   {"started": {...}}        a started event; a bound given false is declared not enforced
//   {"stdout": "<text>"}      raw UTF-8 bytes on stdout
//   {"frame": <json>}         a Content-Length framed message on stdout, the value written compactly
//   {"read": <n>}             reads n frames from stdin
//   {"echo": <n>}             reads n frames and writes each back as a frame
//   {"sleepMs": <n>}          sleeps
//   {"spawn": "<pid file>"}   starts a child (`--sleep`) and writes "<own pid> <child pid>" to the file
//   {"hang": true}            sleeps until stopped
//   {"exit": <n>}             exits with this status
//
// With no exit action the fake exits with 0 after the last action. --log appends one JSON line with the
// command line of each invocation.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Morphir.Conformance.FakeWorker;

internal static class Program
{
    private static int _sequence;

    private static int Main(string[] args)
    {
        if (args is ["--sleep"])
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        var position = 0;
        string? scriptPath = null;
        string? logPath = null;
        while (position < args.Length && args[position].StartsWith("--", StringComparison.Ordinal))
        {
            switch (args[position])
            {
                case "--script": scriptPath = args[position + 1]; break;
                case "--log": logPath = args[position + 1]; break;
                default: return Usage($"unknown option {args[position]}");
            }
            position += 2;
        }

        var rest = args[position..];
        if (logPath is not null)
            File.AppendAllText(logPath, new JsonArray([.. rest.Select(arg => (JsonNode?)JsonValue.Create(arg))]).ToJsonString() + "\n");
        if (rest is not ["extension", "worker", ..]) return Usage("expected: extension worker <arguments>");
        var worker = rest[2..];
        var script = scriptPath is null ? new JsonObject() : JsonNode.Parse(File.ReadAllText(scriptPath))!.AsObject();

        if (worker.Contains("--help"))
        {
            var help = new JsonObject
            {
                ["profile"] = "morphir/command-result",
                ["contractVersion"] = "0.1.0",
                ["command"] = "extension worker",
                ["success"] = true,
                ["result"] = new JsonObject
                {
                    ["help"] = "fake worker",
                    ["buildIdentity"] = script["buildIdentity"]?.GetValue<string>() ?? new string('a', 64),
                },
            };
            WriteStdout(Encoding.UTF8.GetBytes(help.ToJsonString() + "\n"));
            return script["helpExit"]?.GetValue<int>() ?? 0;
        }

        var options = Options(worker);
        var module = options.GetValueOrDefault("--module");
        var name = module is null ? "*" : Path.GetFileNameWithoutExtension(module);
        var modules = script["modules"]?.AsObject() ?? new JsonObject();
        var actions = modules[name] ?? modules["*"];
        if (actions is null) return 0;

        var replacements = new Dictionary<string, string>
        {
            ["{moduleSha256}"] = module is not null && File.Exists(module)
                ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(module)))
                : "",
            ["{buildIdentity}"] = options.GetValueOrDefault("--build-identity") ?? "",
            ["{abi}"] = options.GetValueOrDefault("--abi") ?? "",
        };
        var stdin = Console.OpenStandardInput();

        foreach (var action in actions.AsArray())
        {
            var (kind, value) = action!.AsObject().Single();
            switch (kind)
            {
                case "stderr":
                    WriteStderr(Replace(value!.GetValue<string>(), replacements));
                    break;
                case "event":
                    WriteEvent(value!.AsObject(), replacements);
                    break;
                case "started":
                    WriteEvent(Started(value!.AsObject()), replacements);
                    break;
                case "stdout":
                    WriteStdout(Encoding.UTF8.GetBytes(Replace(value!.GetValue<string>(), replacements)));
                    break;
                case "frame":
                    WriteStdout(Frame(Encoding.UTF8.GetBytes(value!.ToJsonString())));
                    break;
                case "read":
                    for (var index = 0; index < value!.GetValue<int>(); index++)
                        if (ReadFrame(stdin) is null) return 1;
                    break;
                case "echo":
                    for (var index = 0; index < value!.GetValue<int>(); index++)
                    {
                        var body = ReadFrame(stdin);
                        if (body is null) return 1;
                        WriteStdout(Frame(body));
                    }
                    break;
                case "sleepMs":
                    Thread.Sleep(value!.GetValue<int>());
                    break;
                case "spawn":
                    Spawn(value!.GetValue<string>());
                    break;
                case "hang":
                    Thread.Sleep(Timeout.Infinite);
                    break;
                case "exit":
                    return value!.GetValue<int>();
                default:
                    return Usage($"unknown action {kind}");
            }
        }
        return 0;
    }

    private static int Usage(string message)
    {
        WriteStderr($"fake worker: {message}");
        return 2;
    }

    private static Dictionary<string, string> Options(string[] arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < arguments.Length; index++)
            if (arguments[index].StartsWith("--", StringComparison.Ordinal) && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                options[arguments[index]] = arguments[index + 1];
        return options;
    }

    private static string Replace(string text, Dictionary<string, string> replacements) =>
        replacements.Aggregate(text, (current, pair) => current.Replace(pair.Key, pair.Value, StringComparison.Ordinal));

    private static JsonObject Started(JsonObject declared)
    {
        static JsonObject Bound(JsonObject declared, string name, string reason) =>
            declared[name]?.GetValue<bool>() == false
                ? new JsonObject { ["enforced"] = false, ["reason"] = reason }
                : new JsonObject { ["enforced"] = true };

        return new JsonObject
        {
            ["event"] = "started",
            ["abi"] = "{abi}",
            ["moduleSha256"] = "{moduleSha256}",
            ["buildIdentity"] = "{buildIdentity}",
            ["bounds"] = new JsonObject
            {
                ["memoryCeiling"] = Bound(declared, "memoryCeiling", "no-memory-ceiling"),
                ["instructionBudget"] = Bound(declared, "instructionBudget", "no-instruction-metering"),
                ["callDeadline"] = Bound(declared, "callDeadline", "no-call-interruption"),
            },
        };
    }

    private static void WriteEvent(JsonObject members, Dictionary<string, string> replacements)
    {
        var line = new JsonObject
        {
            ["profile"] = "morphir/command-event",
            ["contractVersion"] = "0.1.0",
            ["command"] = "extension worker",
            ["sequence"] = ++_sequence,
        };
        foreach (var (key, value) in members) line[key] = value?.DeepClone();
        WriteStderr(Replace(line.ToJsonString(), replacements));
    }

    private static byte[] Frame(byte[] body) =>
        [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];

    private static byte[]? ReadFrame(Stream input)
    {
        var header = new List<byte>();
        while (!(header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n'))
        {
            var next = input.ReadByte();
            if (next < 0) return null;
            header.Add((byte)next);
        }

        var text = Encoding.ASCII.GetString([.. header]);
        var length = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(parts => int.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture))
            .Single();
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = input.Read(body, read, length - read);
            if (count == 0) return null;
            read += count;
        }
        return body;
    }

    private static void Spawn(string pidFile)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--sleep");
        var child = Process.Start(start)!;
        File.WriteAllText(pidFile, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Environment.ProcessId} {child.Id}"));
    }

    private static void WriteStdout(byte[] bytes)
    {
        var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes);
        stdout.Flush();
    }

    private static void WriteStderr(string line)
    {
        var stderr = Console.OpenStandardError();
        stderr.Write(Encoding.UTF8.GetBytes(line + "\n"));
        stderr.Flush();
    }
}
