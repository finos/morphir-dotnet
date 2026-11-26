# Windsurf Rules for morphir-dotnet

Start with [AGENTS.md](../../AGENTS.md) as the authoritative project guidance, and follow [CODING_STANDARDS.md](../../CODING_STANDARDS.md) for all code changes and reviews.

## Progressive Disclosure

- **F# (`*.fs`, `*.fsi`, `*.fsx`, `*.fsproj`)**: [.agents/FSHARP_CODING_STANDARDS.md](../../.agents/FSHARP_CODING_STANDARDS.md)
- **C# (`*.cs`, `*.csproj`)**: [.agents/CSHARP_CODING_STANDARDS.md](../../.agents/CSHARP_CODING_STANDARDS.md)
- **F#/C# boundary**: [.agents/skills/fsharp-csharp-interop/SKILL.md](../../.agents/skills/fsharp-csharp-interop/SKILL.md)
- **Tests**: [.agents/skills/test-manager/SKILL.md](../../.agents/skills/test-manager/SKILL.md)
- **Native AOT & trimming**: [.agents/aot-optimization.md](../../.agents/aot-optimization.md)
- **QA & skills catalog**: [.agents/qa-testing.md](../../.agents/qa-testing.md), [.agents/skills-reference.md](../../.agents/skills-reference.md)

## Quick Checks Before Committing

```bash
./build.sh --target Precommit
./build.sh --target Test
```
