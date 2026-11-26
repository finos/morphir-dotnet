# Coding Standards

Universal coding standards for `finos/morphir-dotnet`. Every rule has a stable ID (`GEN-N`) for authoring and code review.

## Progressive Disclosure

Read the language- or boundary-specific guide before writing or reviewing those files:

| When working on... | Also read |
|---|---|
| F# (`*.fs`, `*.fsi`, `*.fsx`, `*.fsproj`) | [.agents/FSHARP_CODING_STANDARDS.md](.agents/FSHARP_CODING_STANDARDS.md) |
| C# (`*.cs`, `*.csproj`) | [.agents/CSHARP_CODING_STANDARDS.md](.agents/CSHARP_CODING_STANDARDS.md) |
| F# types/members called by C# (or C# calling F#) | [.agents/skills/fsharp-csharp-interop/SKILL.md](.agents/skills/fsharp-csharp-interop/SKILL.md) |
| Adding, moving, renaming, or deleting tests | [.agents/skills/test-manager/SKILL.md](.agents/skills/test-manager/SKILL.md) |
| Native AOT / trimming diagnostics & size | [.agents/aot-optimization.md](.agents/aot-optimization.md) |

---

## General Rules

### GEN-1: Pure domain, effects at edges
Keep IR models, transformations, validation, and business decisions pure and deterministic. Push file I/O, environment reads, process execution, clock access, and logging to outer adapters and CLI edges.

- **Follow**: Domain function takes parsed inputs and returns a result (`Distribution -> Result<Output, DomainError>`).
- **Avoid**: Reading `File.ReadAllText`, `DateTime.Now`, environment variables, or writing to loggers/console inside domain logic.

### GEN-2: Parse, don't validate; make illegal states unrepresentable
Encode domain invariants in types (smart constructors, discriminated unions, closed hierarchies) at input boundaries rather than passing raw primitives or boolean flag bags through the codebase.

- **Follow**: Separate valid vs. invalid states into distinct union cases; wrap constrained identifiers (names, paths, versions) in validated domain types.
- **Avoid**: Passing raw `string` for validated identifiers, or DTOs with `{ IsValid, Value?, Errors? }` where invalid combinations can exist.

### GEN-3: Explicit absence and typed errors
Represent expected absence and failure explicitly (`option`/`Result` in F#; typed result unions or `[MaybeNullWhen]` `Try*` patterns in C#). Do not use `null` or exceptions for expected business control flow. Map boundary exceptions once at the edge.

- **Follow**: Return structured domain error types; reserve exceptions for programmer bugs or unrecoverable infrastructure failures.
- **Avoid**: Throwing exceptions for invalid user input or validation failures, swallowing exceptions silently, or returning sentinel strings/nulls to signal failure.

### GEN-4: Immutability by default
Model domain data, commands, results, and configuration as immutable values. Confine mutation to local scopes where profiling or benchmarks justify it.

- **Follow**: Records, immutable collections, and pure transformations returning new values.
- **Avoid**: Mutable public properties/setters, shared mutable static state, or mutating caller-owned collections.

### GEN-5: Preserve Morphir IR fidelity and contract stability
Use canonical Morphir terminology (`Package`, `Module`, `Type`, `Value`, `Distribution`, IR nodes) consistently and avoid lossy IR representations.

- **Follow**: Roundtrip codec tests (`serialize -> deserialize -> equals`) against canonical fixtures/mothers; additive schema/API changes.
- **Avoid**: Breaking IR/JSON serialization formats or public library APIs without an ADR, version bump, and maintainer approval.

### GEN-6: Native AOT and trimming by default
Shipped CLI binaries and libraries must remain compatible with `PublishAot=true` and `PublishTrimmed=true`.

- **Follow**: Source generators (`System.Text.Json` `JsonSerializerContext`, `Foundatio.Mediator`, Myriad) and explicit, statically analyzable code.
- **Avoid**: Runtime reflection (`typeof(T).GetProperties()`, reflection-based `JsonSerializer.Serialize(value)`), `dynamic`, `Reflection.Emit`, or `%A` formatting in shipped code.

### GEN-7: CLI stdout/stderr and exit code contract
CLI tools must never write logs or diagnostics to `stdout`. Users pipe structured command output (e.g., `morphir model verify file.json --json | jq`).

- **Follow**:
  - `stdout`: command data only (JSON or formatted command output).
  - `stderr`: all logging and diagnostics.
  - Exit codes: `0` = success, `1` = expected domain/validation failure, `2` = CLI usage or operational error.
- **Avoid**: Writing progress messages, warnings, or Serilog output to `stdout`.

### GEN-8: Project configuration and formatting hygiene
Keep project files, dependencies, and formatting consistent across the solution.

- **Follow**:
  - Target frameworks via `Directory.Build.props` properties (`$(MorphirCurrentTargetFramework)`, `$(MorphirLibraryTargetFrameworks)`, `$(MorphirHostedTargetFramework)`), never literal TFMs.
  - Central package versions in `Directory.Packages.props`; justify any new dependency.
  - Formatting enforced by `.editorconfig` and `dotnet format` / `./build.sh --target Precommit`.
  - Include FINOS Apache-2.0 SPDX headers on new source files where neighboring files use them.
- **Avoid**: Hardcoding `<TargetFramework>net11.0</TargetFramework>` directly, pinning package versions inside `.csproj`/`.fsproj`, or committing unformatted code.

### GEN-9: Test-driven and hermetic testing
Follow Red–Green–Refactor (write a failing test first, implement minimally, refactor while green). Place and tag tests according to the `test-manager` skill.

- **Follow**: Behavior-focused tests, Verify snapshots for complex serialized/generated outputs, and hermetic isolation via `Morphir.TestSupport` (`ChildProcess.RunAsync`, `TestSandbox.Apply`, `TolerantDirectory`).
- **Avoid**: Mutating process-wide state (`Environment.SetEnvironmentVariable`, `Directory.SetCurrentDirectory`), reading real `~/.morphir` / XDG folders, testing private implementation details, or committing unreviewed snapshot changes.

---

## General Review Checklist

When reviewing a diff, report violations as `path:line: rule GEN-N: problem. fix.` and also apply the relevant language checklist:

- [ ] `GEN-1`: Domain/IR logic is pure; I/O, clock, env, and logging stay at edges.
- [ ] `GEN-2`: Constrained values and states use domain types/ADTs rather than raw primitives or boolean flag bags.
- [ ] `GEN-3`: No nulls or exceptions used for expected business flows; boundary errors mapped cleanly.
- [ ] `GEN-4`: Data types are immutable; no shared mutable state or unintended collection mutation.
- [ ] `GEN-5`: IR fidelity and JSON/API backward compatibility preserved; roundtrip tests cover codec changes.
- [ ] `GEN-6`: No runtime reflection, `dynamic`, or AOT/trim-unsafe patterns introduced in shipped code.
- [ ] `GEN-7`: CLI logging goes strictly to `stderr`; `stdout` contains command output only.
- [ ] `GEN-8`: Uses `Directory.Build.props` TFM properties, central package versions, and repo formatting.
- [ ] `GEN-9`: Tests cover the change, follow `test-manager` placement, and stay hermetic/parallel-safe.
