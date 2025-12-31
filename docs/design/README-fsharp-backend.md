# F# Backend for Morphir - Design Documentation

**Status**: Design Complete, Ready for Implementation
**Created**: 2025-12-31
**Version**: 1.0

---

## Overview

This directory contains the complete design documentation for the F# code generation backend for morphir-dotnet.

### Quick Links

- 📋 **[Product Requirements Document (PRD)](./PRD-fsharp-backend.md)** - Complete product requirements and specifications
- 📝 **[GitHub Issues Template](./github-issues-fsharp-backend.md)** - Detailed breakdown of 10 implementation issues
- 🚀 **[Issue Creation Script](../../scripts/create-fsharp-backend-issues.sh)** - Automated script to create GitHub issues
- 🔍 **[Morphir-Elm Migration Assessment](./morphir-elm-migration-assessment.md)** - **CRITICAL** - Systematic evaluation of morphir-elm functionality for migration/adaptation
- 📦 **[Morphir.SDK Library Plan](./morphir-sdk-library-plan.md)** - F# SDK runtime library implementation guide
- 🎯 **[Maturity Milestones](./fsharp-backend-maturity-milestones.md)** - Incremental value delivery roadmap (M0-M5)

---

## What is This Project?

The F# Backend enables morphir-dotnet to generate production-ready F# code from Morphir IR (JSON format). This eliminates manual translation of business logic from Morphir models to F# applications.

### Key Features

- ✅ **Complete IR Coverage**: Generate F# code for all Morphir IR v3 type and value constructs
- ✅ **Idiomatic F# Output**: Uses Fabulous.AST + Fantomas for type-safe, formatted code generation
- ✅ **SDK Integration**: Maps Morphir SDK types to F# built-ins (`Maybe → Option`, `Result → Result`, etc.)
- ✅ **CLI Integration**: `morphir gen fsharp --input morphir-ir.json --output ./generated`
- ✅ **Advanced Features**: Optional JSON codecs (Thoth.Json) and lens generation
- ✅ **AOT Compatible**: Generated code works with Native AOT compilation
- ✅ **93% Boilerplate Reduction**: Fabulous.AST eliminates manual AST construction

---

## Architecture

### High-Level Flow

```
Morphir IR (JSON) → Fabulous.AST Oak → Fantomas → Formatted F# Code
```

### Core Components

| Component | Purpose | Implementation |
|-----------|---------|----------------|
| **Mapper.fs** | Transform Morphir IR → Fabulous.AST | Maps types, values, patterns, literals |
| **Generator.fs** | Render Oak → F# string | Uses `Gen.mkOak \|> Gen.run` + Fantomas |
| **Helpers.fs** | Morphir-specific DSL helpers | Curried functions, ROP, pipelines |
| **SDK.fs** | SDK type/function mappings | `Maybe → Option`, `List.map → List.map` |
| **Plugin.fs** | Pipeline integration | Pluggable transformation plugin |

### Dependencies

- **Fabulous.AST v1.9.0+**: F# code generation DSL (computation expressions)
- **Fantomas.Core v7.0.5+**: Auto-formatting for F# style compliance
- **Morphir.Models**: Classic IR (F# discriminated unions)
- **Morphir.IR.Pipeline**: Pluggable transformation infrastructure

---

## Implementation Plan

### Timeline: 13 Weeks (9 Phases + Maturity Milestones)

| Phase | Duration | Focus | Deliverable | Maturity Level |
|-------|----------|-------|-------------|----------------|
| **0** | 1 week | SDK Library | Morphir.SDK F# library | **M0** |
| **1** | 2 weeks | Foundation | Project setup, Fabulous.AST exploration | - |
| **2** | 2 weeks | Type Mapping | Morphir types → F# types | **M1** |
| **3** | 2 weeks | Value Mapping | Morphir values → F# functions | **M2-M3** |
| **4** | 1 week | CLI Integration | `morphir gen fsharp` command | - |
| **5** | 1 week | SDK Translation | SDK functions → F# stdlib | **M4** |
| **6** | 2 weeks | Advanced Features | JSON codecs, lenses | **M5** |
| **7** | 1 week | Testing & Docs | E2E tests, user guides, examples | - |
| **8** | 1 week | Release Prep | Code review, CI/CD, v1.0.0 release | **v1.0** |

### GitHub Issues

| Issue | Title | Priority | Dependencies | Milestone |
|-------|-------|----------|--------------|-----------|
| **[#363](https://github.com/finos/morphir-dotnet/issues/363)** | F# Code Generation Backend | P0 | - | - |
| **[#364](https://github.com/finos/morphir-dotnet/issues/364)** | Phase 0: Morphir.SDK F# Library Implementation | P0 | #363 | M0 |
| **[#365](https://github.com/finos/morphir-dotnet/issues/365)** | Phase 1: Foundation - Project Setup and Fabulous.AST Exploration | P0 | #364 | - |
| **[#366](https://github.com/finos/morphir-dotnet/issues/366)** | Phase 2: Type Mapping - Morphir IR Types → F# Types | P0 | #365 | M1 |
| **[#367](https://github.com/finos/morphir-dotnet/issues/367)** | Phase 3: Value Mapping - Morphir IR Values → F# Functions | P0 | #366 | M2-M4 |
| **[#368](https://github.com/finos/morphir-dotnet/issues/368)** | Phase 4: CLI Integration - `morphir gen fsharp` Command | P0 | #367 | - |
| **[#369](https://github.com/finos/morphir-dotnet/issues/369)** | Phase 5: SDK Translation - Morphir SDK → F# Standard Library | P0 | #367 | M4 |
| **[#370](https://github.com/finos/morphir-dotnet/issues/370)** | Phase 6: Advanced Features - JSON Codecs and Lenses | P1 | #368, #369 | M5 |
| **[#372](https://github.com/finos/morphir-dotnet/issues/372)** | Phase 7: Testing and Documentation | P0 | #370 | - |
| **[#371](https://github.com/finos/morphir-dotnet/issues/371)** | Phase 8: Release Preparation | P0 | #372 | v1.0 |

---

## How to Use This Documentation

### For Project Managers

1. **Read the [PRD](./PRD-fsharp-backend.md)** to understand goals, scope, and success criteria
2. **Review [Maturity Milestones](./fsharp-backend-maturity-milestones.md)** to understand incremental value delivery (M0-M5)
3. **Review [GitHub Issues](./github-issues-fsharp-backend.md)** for detailed work breakdown
4. **Run the [issue creation script](../../scripts/create-fsharp-backend-issues.sh)** to create GitHub issues
5. **Track progress** using GitHub Projects or Milestones (map issues to M0-M5)

### For Developers

1. **Start with the PRD** to understand the overall architecture and requirements
2. **⚠️ READ THE MIGRATION ASSESSMENT** ([morphir-elm-migration-assessment.md](./morphir-elm-migration-assessment.md)) - **CRITICAL**
   - **Before each phase**: Review assessment for components you'll work on
   - **During implementation**: Follow NATIVE/ADAPT/MIGRATE/SKIP/NEW guidelines
   - **Add traceability**: Link code to morphir-elm sources with comments
   - **Document decisions**: Use Decision Template for deviations
3. **Understand maturity milestones** ([fsharp-backend-maturity-milestones.md](./fsharp-backend-maturity-milestones.md))
   - Know which milestone (M0-M5) you're working toward
   - Understand what "works" vs. "doesn't work yet" at each level
   - Follow testing and documentation requirements for your milestone
4. **Review the design walkthrough** in this README (architecture, components, examples)
5. **Assign yourself an issue** from the GitHub board
6. **Follow TDD practices**: Red-Green-Refactor for all implementation
7. **Use the morphir-architect skill** (`@skill morphir-architect`) for guidance
8. **Reference knowledge bases**:
   - [Ecosystem Knowledge Base](../../.agents/kbs/ecosystem-knowledge-base.md)
   - [Language Design Patterns](../../.agents/kbs/language-design-patterns.md)
   - [Computation Expressions for AST](../../.agents/kbs/computation-expressions-for-ast.md)

### For Reviewers

1. **Check PRD alignment**: Does the implementation match the requirements?
2. **Verify milestone goals**: Does the PR meet the success criteria for its maturity milestone (M0-M5)?
3. **Verify test coverage**: Is coverage ≥ 80%?
4. **Validate generated code**: Does it compile? Is it idiomatic F#?
5. **Review AOT compatibility**: No reflection warnings?
6. **Check documentation**: Are examples clear and runnable?
7. **Validate limitations**: Are "doesn't work yet" features properly documented?

---

## Creating GitHub Issues

### Automated (Recommended)

```bash
# Ensure GitHub CLI is installed and authenticated
gh auth login

# Run the issue creation script
./scripts/create-fsharp-backend-issues.sh
```

This will create:
- 1 Epic issue
- 8 Implementation issues
- All with proper labels, milestones, and dependencies

### Manual

Use the [GitHub Issues Template](./github-issues-fsharp-backend.md) as a reference to create issues manually in GitHub.

---

## Example Generated Code

### Input: Morphir IR

```json
{
  "formatVersion": 3,
  "distribution": {
    "packagePath": [["my"], ["app"]],
    "modules": {
      "Model": {
        "types": {
          "Person": {
            "doc": "Represents a person",
            "value": {
              "typeAliasDefinition": {
                "typeParams": [],
                "typeExpr": {
                  "record": [
                    {"name": "name", "type": {"reference": "String"}},
                    {"name": "age", "type": {"reference": "Int"}},
                    {"name": "email", "type": {"maybe": {"reference": "String"}}}
                  ]
                }
              }
            }
          }
        }
      }
    }
  }
}
```

### Output: Generated F# Code

```fsharp
namespace Generated.My.App.Model

open System
open Morphir.SDK

/// <summary>Represents a person</summary>
type Person = {
    name: string
    age: int
    email: Option<string>
}

// With --codecs flag:
module Codecs =
    open Thoth.Json

    let personEncoder (p: Person) : JsonValue =
        Encode.object [
            "name", Encode.string p.name
            "age", Encode.int p.age
            "email", Encode.option Encode.string p.email
        ]

    let personDecoder : Decoder<Person> =
        Decode.object (fun get -> {
            name = get.Required.Field "name" Decode.string
            age = get.Required.Field "age" Decode.int
            email = get.Optional.Field "email" Decode.string
        })

// With --lenses flag:
module Lenses =
    type Lens<'S, 'A> = {
        Get: 'S -> 'A
        Set: 'A -> 'S -> 'S
    }

    let (>>>) outer inner = {
        Get = fun s -> inner.Get (outer.Get s)
        Set = fun a s -> outer.Set (inner.Set a (outer.Get s)) s
    }

    let personNameLens = {
        Get = fun p -> p.name
        Set = fun n p -> { p with name = n }
    }

    let personAgeLens = {
        Get = fun p -> p.age
        Set = fun a p -> { p with age = a }
    }

    let personEmailLens = {
        Get = fun p -> p.email
        Set = fun e p -> { p with email = e }
    }
```

---

## Success Metrics

### Must-Have (P0)

- ✅ Generate compilable F# code for all Morphir IR v3 constructs
- ✅ CLI command `morphir gen fsharp` works end-to-end
- ✅ All SDK types map to F# built-ins
- ✅ Generated code passes all tests
- ✅ Test coverage ≥ 80%

### Should-Have (P1)

- ✅ JSON codec generation (`--codecs`)
- ✅ Lens generation (`--lenses`)
- ✅ Performance < 5s for large IRs (1000+ types)
- ✅ Comprehensive documentation

### Nice-to-Have (P2)

- Auto-generate .fsproj files
- Incremental generation (only changed modules)
- Watch mode for live regeneration
- IDE integration (F# language service)

---

## Related Resources

### Project Documentation

- [AGENTS.md](../../AGENTS.md) - Primary project guidance
- [CLAUDE.md](../../CLAUDE.md) - Claude Code-specific instructions
- [F# Coding Guide](../../docs/contributing/fsharp-coding-guide.md)

### Knowledge Bases

- [Ecosystem Knowledge Base](../../.agents/kbs/ecosystem-knowledge-base.md)
- [Language Design Patterns](../../.agents/kbs/language-design-patterns.md)
- [Visitor Pattern Implementations](../../.agents/kbs/visitor-pattern-implementations.md)
- [Computation Expressions for AST](../../.agents/kbs/computation-expressions-for-ast.md)
- [Functional Programming Patterns](../../.agents/kbs/functional-programming-patterns.md)

### External Resources

- [Morphir Homepage](https://morphir.finos.org)
- [morphir-elm GitHub](https://github.com/finos/morphir-elm)
- [Fabulous.AST Documentation](https://edgarfgp.github.io/Fabulous.AST/)
- [Fantomas Documentation](https://fsprojects.github.io/fantomas/)
- [F# Style Guide](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/)

---

## Decision Log

### Why Fabulous.AST Instead of Custom AST?

**Decision**: Use Fabulous.AST for code generation

**Rationale**:
- **93% boilerplate reduction** vs. manual FSharp.Compiler.Service AST construction
- **Type-safe DSL**: Computation expressions catch errors at compile time
- **Auto-formatted**: Fantomas integration ensures style compliance
- **Proven in production**: Used by Fabulous framework
- **Faster development**: Focus on mapping logic, not AST construction

**Trade-offs**: External dependency, but well-maintained and stable

### Why Start with Classic IR (F#)?

**Decision**: Use `Morphir.IR.Classic` (F# discriminated unions) instead of `Morphir.Core` (C# sealed records)

**Rationale**:
- **Native F#**: Pattern matching is more natural with discriminated unions
- **Better tested**: Classic IR has more test coverage
- **F# → F# mapping**: More direct transformation path
- **Future extensibility**: Can add Modern IR support later

### Why Focus on IR v3?

**Decision**: Support only Morphir IR v3 (current standard)

**Rationale**:
- **morphir-elm default**: v3 is the current output format
- **Reduced complexity**: No need for multi-version support
- **Future-proof**: v3 is stable and unlikely to change soon

**Non-goal**: Backwards compatibility with v1/v2 (use morphir-elm migration if needed)

---

## FAQ

### Q: When will the F# backend be available?

**A**: Following the 12-week implementation plan, we expect v1.0.0 release in Q2 2025.

### Q: Will this work with my existing F# projects?

**A**: Yes! Generated code uses F# standard library types and can be integrated into any F# project.

### Q: Can I customize the generated code?

**A**: The backend generates idiomatic F# code. For customization beyond CLI options, you can extend the mapper or post-process the output.

### Q: Does this replace morphir-elm?

**A**: No, morphir-elm is still needed to author Morphir IR. This backend consumes the IR and generates F# code.

### Q: What about C# code generation?

**A**: C# backend is planned as a separate project (future work). This PRD focuses on F# only.

### Q: Will generated code work with Native AOT?

**A**: Yes! The F# backend is designed for AOT compatibility (no reflection).

---

## Next Steps

1. ✅ **Review this documentation** with the team
2. ✅ **Create GitHub issues** using the automation script
3. ✅ **Assign issues** to team members
4. ✅ **Create GitHub Projects board** for progress tracking
5. ✅ **Begin Phase 1 implementation** (Foundation)

---

**Status**: Ready for Implementation
**Last Updated**: 2025-12-31
**Maintainer**: Morphir Architecture Team
