# C# Coding Standards

Enforceable C# 15 / .NET 11 standards for `*.cs` and `*.csproj` in `finos/morphir-dotnet`. Read [CODING_STANDARDS.md](../CODING_STANDARDS.md) first.

- **Calling F# types/members from C#?** Also follow [.agents/skills/fsharp-csharp-interop/SKILL.md](skills/fsharp-csharp-interop/SKILL.md) and run `fsharp-csharp-interop-reviewer`.
- **Deep-dive AOT reference:** [.agents/aot-optimization.md](aot-optimization.md).

---

## Rules

### CS-1: C# 15 and multi-targeting safety
Use file-scoped namespaces, primary constructors, collection expressions (`[]`), `<Nullable>enable</Nullable>`, and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

Packable libraries target `$(MorphirLibraryTargetFrameworks)` (`net11.0;net10.0`). Never use a `.NET 11`-only runtime API or C# 15 feature requiring `.NET 11` runtime types in a library without an `#if NET11_0_OR_GREATER` guard.

```csharp
// Follow
namespace Morphir.Tooling.Features.ModelVerify;

public sealed class SchemaValidator(SchemaLoader loader)
{
    public IReadOnlyList<ValidationError> Validate(string json) => [];
}

// Avoid: block-scoped namespace, literal TFM in .csproj, or unguarded net11.0-only API in a net10.0 library
namespace Morphir.Tooling.Features.ModelVerify
{
    public class SchemaValidator { ... }
}
```

### CS-2: Immutable records and closed ADTs
Use `record` and `readonly record struct` for data, value types, commands, and results. Model discriminated states as closed `abstract record` hierarchies (or F# unions) rather than mutable classes with boolean flags.

```csharp
// Follow
public readonly record struct TypeId(string Value);

public abstract record ValidationOutcome
{
    private ValidationOutcome() { }
    public sealed record Valid(int FilesChecked) : ValidationOutcome;
    public sealed record Invalid(IReadOnlyList<ValidationError> Errors) : ValidationOutcome;
}

// Avoid: mutable DTO with nullable flag soup
public class ValidationOutcome
{
    public bool IsValid { get; set; }
    public int? FilesChecked { get; set; }
    public List<ValidationError>? Errors { get; set; }
}
```

### CS-3: Null safety and explicit control flow
Respect nullable reference types. Avoid `null!` suppressions to silence real nullability, and return typed results or `[MaybeNullWhen]` `Try*` methods instead of throwing exceptions for expected failures.

```csharp
// Follow
public static bool TryParseVersion(
    string? input,
    [NotNullWhen(true)] out SchemaVersion? version)
{
    if (string.IsNullOrWhiteSpace(input))
    {
        version = null;
        return false;
    }
    return SchemaVersion.TryParse(input, out version);
}

// Avoid: forgiving null or throwing for expected invalid input
public static SchemaVersion ParseVersion(string? input)
{
    var text = input!; // Masks null bug
    if (text.Length == 0) throw new InvalidOperationException("Bad version");
    ...
}
```

### CS-4: Exhaustive pattern matching
Use switch expressions over closed hierarchies. Throw `UnreachableException` only for truly unreachable branches — do not silently swallow unknown cases with a fallback value.

```csharp
// Follow
var exitCode = outcome switch
{
    ValidationOutcome.Valid => 0,
    ValidationOutcome.Invalid => 1,
    _ => throw new UnreachableException($"Unexpected outcome: {outcome.GetType().Name}")
};

// Avoid: silently mapping unknown cases to success or a generic default
var exitCode = outcome switch
{
    ValidationOutcome.Valid => 0,
    _ => 1 // Hides new outcome variants silently
};
```

### CS-5: Vertical slices, mediation, and CLI edge
Organize tooling features as vertical slices under `src/Morphir.Tooling/Features/{Feature}/`:
- Immutable Command and Result `record`s, static handler, and FluentValidation rules.
- Dispatch via `Foundatio.Mediator` (`IMediator`) — keep `System.CommandLine` wiring thin.
- Configure Serilog with `standardErrorFromLevel: LogEventLevel.Verbose` (and clear default logging providers) so logs never pollute `stdout`.

```csharp
// Follow: thin CLI edge + stderr-only logging
configuration.WriteTo.Console(
    standardErrorFromLevel: LogEventLevel.Verbose,
    outputTemplate: OutputTemplate);

var result = await mediator.InvokeAsync<VerifyModelResult>(command, ct);

// Avoid: writing logs to stdout or putting business logic inside CLI command setup
Console.WriteLine("Verifying model..."); // Corrupts `--json | jq` stdout!
```

### CS-6: Native AOT and trimming safety
Use `System.Text.Json` source generation (`JsonSerializerContext` + `JsonTypeInfo<T>`) for all serialized types. Never use reflection-based serialization, `dynamic`, `Reflection.Emit`, or `Microsoft.CodeAnalysis.*.Workspaces` in AOT-published projects.

```csharp
// Follow
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VerifyModelResult))]
internal partial class VerifyModelJsonContext : JsonSerializerContext;

var json = JsonSerializer.Serialize(result, VerifyModelJsonContext.Default.VerifyModelResult);

// Avoid: reflection-based serialization (breaks under PublishAot / PublishTrimmed)
var json = JsonSerializer.Serialize(result);
```

### CS-7: Interop when calling F# from C#
Follow [.agents/skills/fsharp-csharp-interop/SKILL.md](skills/fsharp-csharp-interop/SKILL.md). In particular:
- Compare F# records and unions with `Equals(...)`, **never** `==` or `!=` (F# does not emit C# equality operators, so `==` compares references).
- Branch on F# unions via F# functions or `Match(...)`, **never** `.Tag`, `.IsX`, or pattern-matching internal case types.

```csharp
// Follow
if (left.Equals(right)) { ... }
var label = host.Match(
    onNative: () => "native",
    onManaged: () => "managed");

// Avoid: reference equality on F# value types or switching on Tag/IsX
if (left == right) { ... }
if (host.IsNative) { ... }
```

### CS-8: Hermetic test hygiene
Write C# unit tests with TUnit, FluentAssertions, and `Verify.TUnit`, and follow [.agents/skills/test-manager/SKILL.md](skills/test-manager/SKILL.md) for placement. Keep all tests parallel-safe and hermetic via `Morphir.TestSupport`.

```csharp
// Follow: isolated sandbox & ChildProcess helper
var env = TestSandbox.Apply(sandboxDir);
var result = await ChildProcess.RunAsync(fileName, args, workingDir, env, timeout, ct);

// Avoid: mutating global process state in parallel tests
Environment.SetEnvironmentVariable("HOME", tempDir);
Directory.SetCurrentDirectory(tempDir);
```

---

## C# Review Checklist

Report findings as `path:line: rule CS-N: problem. fix.`

- [ ] `CS-1`: File-scoped namespaces, `<Nullable>enable</Nullable>`, warnings as errors, and `net10.0` compatibility in multi-targeted libraries.
- [ ] `CS-2`: Immutable `record` / `readonly record struct` and closed ADTs instead of mutable classes or boolean flag bags.
- [ ] `CS-3`: No unsafe `null!` suppressions; expected failures use typed results or `[MaybeNullWhen]` `Try*`, not exceptions.
- [ ] `CS-4`: Pattern matches over closed hierarchies are exhaustive without silent catch-alls.
- [ ] `CS-5`: Vertical-slice handlers via `Foundatio.Mediator`; CLI logging goes strictly to `stderr`.
- [ ] `CS-6`: `System.Text.Json` uses `JsonSerializerContext` / `JsonTypeInfo<T>`; no reflection/dynamic/AOT-unsafe calls.
- [ ] `CS-7`: F# values compared with `Equals` (never `==`/`!=`); F# unions use F# functions or `Match` (never `.Tag`/`.IsX`).
- [ ] `CS-8`: Tests follow `test-manager` placement and never mutate process-wide env/CWD.
