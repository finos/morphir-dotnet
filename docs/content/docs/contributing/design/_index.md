---
title: "Design Documentation"
linkTitle: "Design"
weight: 10
description: "Design documents, PRDs, and architectural specifications for Morphir .NET"
---

This section contains design documentation for Morphir .NET, including AI Skill Framework architecture, Product Requirements Documents, and architectural decision records.

## AI Skill Framework

The morphir-dotnet project uses a sophisticated AI skill framework (gurus) for cross-agent development assistance:

| Document | Description |
|----------|-------------|
| [Skill Framework Design](skill-framework-design) | Comprehensive architecture for unified, cross-agent AI skills |
| [Guru Philosophy](guru-philosophy) | The collaborative AI stewardship philosophy behind morphir-dotnet gurus |
| [Guru Creation Guide](guru-creation-guide) | Step-by-step guide for creating new AI gurus |
| [Technical Writer Skill](technical-writer-skill-requirements) | Requirements for the Technical Writer skill |

## Product Requirements Documents

PRDs track feature requirements, design decisions, and implementation status:

- [PRD Index](prds/) - All active and completed PRDs
- [IR JSON Schema Verification](prds/ir-json-schema-verification) - Schema validation feature
- [Deployment Architecture Refactor](prds/deployment-architecture-refactor) - Build and deployment improvements

## Design Process

### For Standard Features

1. **PRD Creation**: Major features start with a comprehensive PRD
2. **Review & Approval**: PRDs are reviewed before implementation begins
3. **Implementation**: PRDs are updated with implementation notes as work progresses
4. **Completion**: Completed PRDs serve as historical reference

### For AI Skills & Gurus

1. **Philosophy First**: Understand guru principles before design
2. **Framework Definition**: Follow skill framework architecture
3. **Review Capability**: Every guru includes proactive review capability
4. **Cross-Agent Design**: Ensure portability across Claude, Copilot, Cursor, and other agents
5. **Retrospective Integration**: Plan for continuous improvement through feedback loops
