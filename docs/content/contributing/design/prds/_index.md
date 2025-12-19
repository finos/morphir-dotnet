---
title: "Product Requirements Documents"
linkTitle: "PRDs"
weight: 1
description: "Index of all Product Requirements Documents for Morphir .NET features"
---

# Product Requirements Documents

This directory contains comprehensive Product Requirements Documents (PRDs) for all major features in Morphir .NET. Each PRD is a living document that tracks feature requirements, design decisions, and implementation status.

## Active PRDs

| PRD | Status | Current Phase | Current Task | Last Updated |
|-----|--------|---------------|--------------|--------------|
| [IR JSON Schema Verification](./ir-json-schema-verification.md) | 🚧 In Progress | Phase 1 | Setup | 2025-12-15 |
| [Product Manager Skill](./product-manager-skill.md) | 📋 Draft | Phase 1 | Planning | 2025-12-18 |

## PRD Status Legend

- **📋 Draft**: Initial PRD being refined, not yet approved
- **✅ Approved**: PRD reviewed and ready for implementation to begin
- **🚧 In Progress**: Active implementation underway
- **✓ Completed**: All features implemented, PRD archived for reference
- **⏸️ Deferred**: PRD postponed, marked with reason and future timeline

## How to Use PRDs

### For Contributors

1. **Starting Work**: Check the "Current Task" column to see what's being worked on
2. **Implementation**: Update the PRD's Feature Status Tracking table as you complete features
3. **Design Decisions**: Add Implementation Notes to capture important decisions
4. **Questions**: Document answers to Open Questions as they're resolved

### For AI Agents

When asked "What should I work on?" or "What's the current status?":

1. Check this index for active PRDs
2. Open the relevant PRD and find the Feature Status Tracking table
3. Look for features with status ⏳ Planned (ready to start) or 🚧 In Progress (continue work)
4. Update feature status in real-time as work progresses
5. Add Implementation Notes for significant design decisions

### PRD Template Structure

Each PRD includes:

- **Overview**: Feature purpose and context
- **Problem Statement**: Why this feature is needed
- **Goals & Non-Goals**: Clear scope boundaries
- **User Stories**: Key use cases
- **Detailed Requirements**: Functional and non-functional requirements
- **Technical Design**: Architecture and component design
- **Feature Status Tracking**: Living table of all features and their status
- **Implementation Phases**: Phased rollout plan
- **Testing Strategy**: Unit, BDD, integration, and performance tests
- **Success Criteria**: Metrics for completion
- **Implementation Notes**: Design decisions made during development
- **Open Questions**: Decisions documented as they're made
- **References**: Links to related documentation

## Creating a New PRD

1. Copy an existing PRD as a template
2. Fill in all sections with comprehensive detail
3. Include Feature Status Tracking table with all planned features
4. Add to this index with "Draft" status
5. Submit for review and approval before implementation begins

## Related Documentation

- [Contributor Guide](../../_index.md)
- [Design Documents](../design/)
- [Architectural Decision Records](../../../../adr/)

---

**Last Updated**: 2025-12-18
