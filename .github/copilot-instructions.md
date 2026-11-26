# GitHub Copilot Instructions for morphir-dotnet

Primary guidance lives in [AGENTS.md](../AGENTS.md). Coding standards live in [CODING_STANDARDS.md](../CODING_STANDARDS.md) with language-specific guides loaded progressively:

- **F# (`*.fs`, `*.fsi`, `*.fsx`, `*.fsproj`)**: [.agents/FSHARP_CODING_STANDARDS.md](../.agents/FSHARP_CODING_STANDARDS.md) (also auto-applied via `.github/instructions/fsharp-coding-standards.instructions.md`)
- **C# (`*.cs`, `*.csproj`)**: [.agents/CSHARP_CODING_STANDARDS.md](../.agents/CSHARP_CODING_STANDARDS.md) (also auto-applied via `.github/instructions/csharp-coding-standards.instructions.md`)
- **F#/C# boundary**: [.agents/skills/fsharp-csharp-interop/SKILL.md](../.agents/skills/fsharp-csharp-interop/SKILL.md) + `fsharp-csharp-interop-reviewer` custom agent (`.github/agents/`)
- **Tests**: [.agents/skills/test-manager/SKILL.md](../.agents/skills/test-manager/SKILL.md) + `test-manager-reviewer` custom agent (`.github/agents/`)
- **All skills & specialized guides**: [.agents/skills-reference.md](../.agents/skills-reference.md), [.agents/aot-optimization.md](../.agents/aot-optimization.md), [.agents/qa-testing.md](../.agents/qa-testing.md)

## Copilot Coding Agent Firewall Configuration

The Copilot coding agent runs with a firewall that limits internet access by default. This project is configured to allow access to the GitHub API for monitoring CI/CD workflow status.

**Configuration File**: [`.github/workflows/copilot-setup-steps.yml`](./workflows/copilot-setup-steps.yml)

This file:
- Sets `COPILOT_AGENT_FIREWALL_ALLOW_LIST_ADDITIONS` to include `api.github.com`
- Configures the .NET SDK for the development environment
- Runs before the firewall is enabled

**Troubleshooting Firewall Issues**:
If you see `Firewall rules blocked me from connecting to one or more addresses`:
1. **Add to custom allowlist** (admin-only): Settings → Copilot → coding agent → Custom allowlist
2. **Update `copilot-setup-steps.yml`**: Add the blocked domain to `COPILOT_AGENT_FIREWALL_ALLOW_LIST_ADDITIONS`
3. **Use setup steps**: Run network calls in the `copilot-setup-steps` job before the firewall activates

**Documentation**:
- [Customizing the firewall](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/customize-the-agent-firewall)
- [Copilot allowlist reference](https://docs.github.com/en/copilot/reference/copilot-allowlist-reference)
- [Customizing the development environment](https://docs.github.com/en/copilot/how-tos/agents/copilot-coding-agent/customizing-the-development-environment-for-copilot-coding-agent)
