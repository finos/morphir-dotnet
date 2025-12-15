# AGENTS.md

Guidance for AI coding agents contributing to finos/morphir-dotnet (a .NET
binding for the Morphir ecosystem). Produce correct, minimal, and well‑tested
changes aligned with Morphir IR and tooling.

Project links
- Morphir: https://morphir.finos.org/
- morphir (core/tooling): https://github.com/finos/morphir
- morphir-elm: https://github.com/finos/morphir-elm
- morphir-dotnet (this repo): https://github.com/finos/morphir-dotnet

## 1) Project Overview

Purpose
- Provide .NET bindings, libraries, codecs, and tooling interoperable with the
  Morphir IR (intermediate representation) and developer workflows.

Architecture (high-level)
- domain/: Pure Morphir domain/IR models, types, values, invariants.
- adapters/: Edges (serialization, CLI integration, file I/O, codegen).
- app/: Composition root, configuration, hosting/CLI entry points.
- docs/spec/: IR specification and schemas (JSON/OpenAPI schemas and samples).
- tests/: Unit (TUnit), property-based (via generators), and BDD/acceptance (Reqnroll).

Stacks and baselines
- C# 14, .NET 10 (SDK pinned in global.json).
- F# for ADT-heavy components where appropriate.
- TypeScript optional for dev tooling/schema checks.
- Critter Stack (WolverineFx & Marten) used in development for messaging and persistence.

Principles
- Immutability-first; push effects to edges.
- ADTs: make illegal states unrepresentable; avoid nulls.
- Strong testing: TUnit for units, Reqnroll for behavior, contract/roundtrip tests.

## 2) Agent Scope

Do
- Implement small features end-to-end (domain → adapter → tests).
- Fix bugs with minimal diffs and add regression tests (TUnit and/or Reqnroll).
- Improve domain types to encode Morphir invariants.
- Keep docs and scripts consistent with code.

Avoid or escalate
- Breaking public API changes (coordinate with maintainers).
- IR/JSON compatibility changes without ADR and version bump.
- Security/auth/crypto changes.
- Destructive migrations without explicit approval.

## 3) Repository Map

Adjust to actual repo layout.

- src/
    - Morphir: C# CLI/host (.csproj, C# 14)
    - Morphir.Core: Core domain model and IR definition  (.csproj, C# 14)
- docs/spec/: IR specification and JSON/OpenAPI schemas, IR samples/fixtures
- tests/**.csproj: TUnit, Reqnroll, contract tests
- scripts/: format/test/contract/codegen utilities
- docs/: ADRs, architecture, contribution notes
- .github/: CI workflows, CODEOWNERS, PR templates

External touchpoints
- Morphir CLI and JSON IR formats
- morphir-elm and morphir repos for canonical IR samples

## 4) Build, Run, Test

Environment
- Required env vars documented in .env.example. Do not commit secrets.

Commands
- .NET (C# 14 / F#, .NET 10)
    - Build: `dotnet build`
    - Test (TUnit + Reqnroll): `dotnet test --nologo`
    - Format: `dotnet format`
- TypeScript (if present)
    - Install: `npm ci`
    - Build: `npm run build`
    - Test: `npm test`
    - Lint/Format: `npm run lint && npm run format`

CI
- All formatters/linters must pass.
- Tests must be green; coverage must not decrease (>= 80% unless stated).
- Snapshot/golden updates require justification in PRs.

## 5) Coding Conventions

General
- Domain is pure; side effects in adapters.
- Prefer Option/Result/Either-like models; avoid nulls/throws for expected flows.
- Exhaustive pattern matching for ADTs; avoid default fall-throughs.
- Use value types for IDs/quantities over raw primitives.

Error handling
- Typed domain errors (sum types). Map boundary errors at edges and log once.

Naming
- Use Morphir terms consistently (Package, Module, Type, Value, IR nodes).

Formatting
- `.editorconfig` + `dotnet format`. Prettier/ESLint for TS (if present).

C# 14 / .NET 10 specifics
- Prefer readonly `record struct`/`record` where appropriate.
- Use file‑scoped namespaces, primary constructors, and newer pattern features.
- Favor spans and efficient collections only with benchmarks backing changes.

## 6) Morphir-Specific Modeling

Model Morphir IR precisely:
- Names: validated segments, non-empty paths, canonical casing rules.
- Types: aliases, custom (union) types, records, tuples, functions.
- Values/Expr: literals, lambdas, application, pattern matching.
- Keep IR fidelity; avoid lossy representations.

F# example
```fsharp
type NameSegment = private NameSegment of string
module NameSegment =
  let tryCreate s =
    if System.Text.RegularExpressions.Regex.IsMatch(s, "^[a-z][a-z0-9_]*$")
    then Ok (NameSegment s) else Error "InvalidNameSegment"

type QualifiedName = { Package: string list; Module: string list; Local: string }

type TypeExpr =
  | TInt
  | TString
  | TBool
  | TTuple of TypeExpr list
  | TRecord of Map<string, TypeExpr>
  | TFunc of input: TypeExpr * output: TypeExpr
```

C# example
```csharp
public abstract record TypeExpr {
  public sealed record TInt() : TypeExpr;
  public sealed record TString() : TypeExpr;
  public sealed record TBool() : TypeExpr;
  public sealed record TTuple(IReadOnlyList<TypeExpr> Items) : TypeExpr;
  public sealed record TRecord(IReadOnlyDictionary<string, TypeExpr> Fields) : TypeExpr;
  public sealed record TFunc(TypeExpr Input, TypeExpr Output) : TypeExpr;
}
```

TypeScript (tooling)
```ts
export type TypeExpr =
  | { _tag: "TInt" }
  | { _tag: "TString" }
  | { _tag: "TBool" }
  | { _tag: "TTuple"; items: ReadonlyArray<TypeExpr> }
  | { _tag: "TRecord"; fields: Readonly<Record<string, TypeExpr>> }
  | { _tag: "TFunc"; input: TypeExpr; output: TypeExpr };
```

## 7) Interfaces and Contracts

- IR JSON compatibility with Morphir toolchains is mandatory.
- Roundtrip codec tests: serialize → deserialize → equals.
- Backward compatibility
    - Additive OK.
    - Breaking changes require ADR, migration notes, and version bump.
- If OpenAPI/JSON Schemas exist, keep in `docs/spec/schemas/` and validate in CI.

## 8) Tooling and Scripts for Agents

Scripts to provide/use:
- scripts/format-all — format .NET (and TS if present)
- scripts/test-all — run TUnit and Reqnroll suites (and TS if present)
- scripts/check-contracts — run IR/JSON roundtrip and contract tests
- scripts/gen — codegen or schema sync steps (if any)

Run before committing:
```bash
scripts/format-all
scripts/test-all
scripts/check-contracts
```

Suggested implementations
- scripts/format-all
```bash
#!/usr/bin/env bash
set -euo pipefail
dotnet format
if [ -f "package.json" ]; then npm run format; fi
```

- scripts/test-all
```bash
#!/usr/bin/env bash
set -euo pipefail
dotnet test --nologo
if [ -f "package.json" ]; then npm test; fi
```

- scripts/check-contracts
```bash
#!/usr/bin/env bash
set -euo pipefail
# Implement IR/JSON codec roundtrip tests and any schema checks.
# Example (placeholder):
# dotnet test --filter "Category=Contract"
echo "Run IR JSON roundtrip + contract tests against Morphir samples."
```

Make scripts executable:
```bash
chmod +x scripts/format-all scripts/test-all scripts/check-contracts scripts/gen || true
```

## 9) Testing Strategy

Unit testing (TUnit)
- Place unit tests in tests/csharp.unit or equivalent.
- Cover smart constructors, ADT exhaustiveness, and edge cases.

Behavior/acceptance (Reqnroll)
- Feature files under tests/csharp.bdd/Features/*.feature
- Step definitions in tests/csharp.bdd/Steps/
- Use Reqnroll hooks for setup/teardown and environment orchestration.

Property-based tests
- Use FsCheck or generators within TUnit where appropriate:
    - Name normalization/validation
    - Codec roundtrips for random IR fragments
    - Structural invariants (non-empty lists where required, arity constraints)

Contract tests
- Roundtrip JSON using canonical samples from morphir-elm/morphir.
- Cross-validate against Morphir CLI when feasible.

Coverage targets
- Maintain or improve coverage (>= 80% overall unless specified).

## 10) Decision Policies

- Favor IR fidelity and correctness.
- Minimize dependencies; justify new packages.
- Performance changes require benchmarks and tests.
- Keep effects at the edges; domain remains pure.
- Prefer explicit ADTs over booleans/flags; update exhaustive matches on change.

## 11) Review and Contribution Rules

- Small, focused PRs with tests (TUnit and/or Reqnroll).
- Conventional Commits: feat:, fix:, refactor:, test:, docs:
- **Do not list Claude (or any AI assistant) as a co-author on commits.** Our CLA does not support AI assistants as co-authors. Note: GitHub Copilot is supported and may be listed as a co-author when it is an actual co-author.
- PR checklist:
    - [ ] Tests added/updated and passing
    - [ ] IR/JSON compatibility preserved or versioned with ADR
    - [ ] Formatters/lints run
    - [ ] Docs/ADR updated if behavior changed

## 12) Security and Compliance

- No secrets in code or tests.
- Respect FINOS policies and repository license.
- Auth/crypto/legal changes require maintainer review.

## 13) PRD Management and Implementation Tracking

Product Requirements Documents (PRDs)
- Location: `docs/content/contributing/design/prds/`
- Each feature starts with a comprehensive PRD before implementation
- PRDs are living documents updated during implementation

PRD Structure
- **Status tracking**: PRDs include a "Feature Status Tracking" table with all features and their implementation status
- **Implementation notes**: Add "Implementation Notes" sections to capture:
    - Design decisions made during implementation
    - Deviations from original design with rationale
    - Architectural insights discovered during development
    - Dependencies or blockers encountered
- **Open questions**: Document decisions as they're made in the "Open Questions" section

PRD Status Workflow
1. **Draft**: Initial PRD being refined
2. **Approved**: PRD reviewed and ready for implementation
3. **In Progress**: Active implementation underway
4. **Completed**: All features implemented, PRD archived
5. **Deferred**: PRD postponed, marked with reason

Cross-Agent Collaboration
- When starting work, check the PRD Feature Status Tracking table for current task
- Update feature status in real-time: ⏳ Planned → 🚧 In Progress → ✅ Implemented
- Add implementation notes directly in the PRD under relevant sections
- PRD serves as source of truth for "what's next" across multiple AI agent sessions

Example Implementation Notes Section
```markdown
## Implementation Notes

### Phase 1: Core Verification (Current)

#### VerifyIR Command Handler (2025-12-15)
- **Decision**: Used WolverineFx's `IMessageBus.InvokeAsync<T>()` instead of `IMessageContext.Send()`
- **Rationale**: InvokeAsync provides request-response pattern needed for CLI
- **Impact**: Simpler than setting up reply queues
- **Files**: `src/Morphir/Program.cs:397-401`, `src/Morphir.Tooling/Features/VerifyIR/VerifyIR.cs`

#### Schema Loading (2025-12-15)
- **Change**: Used `Assembly.GetManifestResourceStream()` instead of `EmbeddedFileProvider`
- **Rationale**: Simpler, no additional dependencies
- **Impact**: None, works as designed
- **Files**: `src/Morphir.Tooling/Infrastructure/JsonSchema/SchemaLoader.cs:15-25`
```

PRD Index (Markdown)
- Maintain `docs/content/contributing/design/prds/_index.md` with all PRDs and their status
- Format:
```markdown
| PRD | Status | Phase | Current Task |
|-----|--------|-------|--------------|
| [IR Schema Verification](./ir-json-schema-verification.md) | In Progress | Phase 1 | WolverineFx setup |
| [Migration Tooling](./ir-migration.md) | Draft | - | Design review |
```

## 14) Known Issues / TODOs

- Maintain a prioritized list or link GitHub issues:
    - TODO: <short> [link]
    - BUG: <short> [link]
    - COMPAT: <short> [link]

## 15) ADRs and Rationale

- docs/adr/*.md — key decisions (IR mapping, codec strategy, versioning).
- Include ADRs for breaking changes or cross-tool compatibility shifts.

## 16) Maintainers and Ownership

- CODEOWNERS defines required reviewers.
- Primary contacts: <handles/emails>
- Escalation: label `maintainer-attention` or use project channels.

## 17) Agent Execution Rules (Important)

- Keep diffs minimal; follow existing patterns and style.
- Update all exhaustive matches and affected tests when changing ADTs.
- Always run formatters, TUnit, and Reqnroll suites; run contract/roundtrip checks.
- If uncertain about Morphir compatibility, add a TODO with a question and take
  the conservative path.
