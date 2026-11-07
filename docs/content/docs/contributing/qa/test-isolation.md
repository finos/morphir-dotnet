---
title: "Test Isolation"
linkTitle: "Test Isolation"
description: "Keep tests hermetic: sandboxes, child processes, cleanup and the machine tripwire"
weight: 5
---

Test runners run tests in parallel by default. A test that reads your home directory, leaves a child process running, or shares temp paths passes on one machine and fails on another. Use the shared helpers in `tests/Morphir.TestSupport` to keep each test inside its own sandbox.

## Run Morphir in a sandbox

`TestSandbox` creates a temporary tree with its own home folder, machine configuration folders and work area. `Apply` confines a child process to that tree:

- It points `HOME`, `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `PROGRAMDATA`, `XDG_CONFIG_HOME` and `XDG_CACHE_HOME` into the sandbox.
- It sets `MORPHIR_HOME`, `MORPHIR_DISCOVERY_BOUNDARY` (the sandbox root) and `MORPHIR_ISOLATED=1`, and removes every other inherited `MORPHIR_*` variable.
- It keeps `NUGET_PACKAGES` pointing at your real package cache, so `dotnet` children don't download packages again.
- It disables MSBuild node reuse, so no build node outlives the test and keeps its directory locked.

```csharp
await using var sandbox = TestSandbox.Create();
var project = Directory.CreateDirectory(sandbox.PathOf("work", "app")).FullName;

var info = ChildProcess.StartInfo("dotnet", "--version");
info.WorkingDirectory = project;
sandbox.Apply(info);

var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(60));
result.ExitCode.Should().Be(0, result.CombinedOutput);
```

To run the Morphir CLI the same way, start `dotnet` with the path to `morphir.dll` and the command's arguments.

`SandboxOptions` changes the defaults:

| Option | Default | Use |
|--------|---------|-----|
| `Isolated` | `true` | Set to `false` for a test that exercises machine or personal configuration. Write those files inside the sandbox, for example under `sandbox.MorphirHome`. |
| `RedirectHome` | `true` | Set to `false` only for a child that needs the real SDK state. Morphir stays contained either way. |
| `Boundary` | sandbox root | Bound discovery more tightly, for example to one project. |

`sandbox.MorphirHome` is not created for you; each test decides whether a global Morphir home exists. Code that takes an explicit environment instead of reading the process uses `sandbox.EnvironmentSnapshot(...)`.

Never call `Environment.SetEnvironmentVariable` or `Directory.SetCurrentDirectory` in a test. Both change the whole test process, and other tests are running at the same time.

## Start child processes with `ChildProcess`

`ChildProcess.RunAsync` reads stdout and stderr concurrently, so a chatty child can't deadlock the test. When a child times out or the test is cancelled, it kills the child's whole process tree and waits for it to exit before returning. A process that is still dying would otherwise keep its working directory locked on Windows.

Every call goes through a shared gate that limits how many children run at once. The timeout starts only once the call holds a slot, so time spent waiting doesn't eat into a test's budget. The default limit is half the processor count, and never fewer than two. Set `MORPHIR_TEST_MAX_PROCESSES` to change it, for example to `1` to run children one at a time while you investigate a timeout.

## Clean up with `TolerantDirectory`

Use `TolerantDirectory.TryDelete` (in `Dispose`) or `TryDeleteAsync` to remove a test's directories. It retries with backoff and logs instead of throwing, so a file that stays locked briefly can't fail a test whose assertions passed. `TestSandbox` disposes itself this way.

## The machine tripwire

`MachineTripwire` snapshots the real machine's Morphir locations before a test run and checks them afterwards:

- `~/.morphir`, and `MORPHIR_HOME` if it is set.
- The platform configuration and cache directories.
- The system configuration directory.

If a test created, modified or removed anything there, the run fails and lists the paths. Add the hook to every test project that runs Morphir code against the filesystem:

```csharp
public static class MachineTripwireHook
{
    private static MachineTripwire? _tripwire;

    [Before(HookType.Assembly)]
    public static void Capture() => _tripwire = MachineTripwire.Capture();

    [After(HookType.Assembly)]
    public static void Verify() => _tripwire?.AssertUnchanged();
}
```

If the tripwire fails, find the test that wrote to the listed path and move that test into a sandbox. Don't widen the tripwire's exclusions.
