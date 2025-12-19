---
title: "Product Requirements Documents"
linkTitle: "PRDs"
weight: 50
description: "Feature specifications and implementation tracking for Morphir .NET"
---

Product Requirements Documents (PRDs) track feature requirements, design decisions, and implementation status for all major features in Morphir .NET.

## Active PRDs

| PRD | Status | Description |
|-----|--------|-------------|
| [IR JSON Schema Verification](ir-json-schema-verification) | 🚧 In Progress | Schema validation for Morphir IR |
| [IR JSON Schema Verification BDD](ir-json-schema-verification-bdd) | 🚧 In Progress | BDD scenarios for schema verification |
| [Deployment Architecture Refactor](deployment-architecture-refactor) | 📋 Draft | Build and deployment improvements |
| [Product Manager Skill](product-manager-skill) | 📋 Draft | AI skill for product management |

## Status Legend

| Status | Meaning |
|--------|---------|
| 📋 Draft | Initial PRD being refined, not yet approved |
| ✅ Approved | PRD reviewed and ready for implementation |
| 🚧 In Progress | Active implementation underway |
| ✓ Completed | All features implemented, PRD archived |
| ⏸️ Deferred | PRD postponed with reason and timeline |

## How to Use PRDs

### For Contributors

1. **Starting Work**: Check the status to see what's being worked on
2. **Implementation**: Update the PRD's Feature Status Tracking table as you complete features
3. **Design Decisions**: Add Implementation Notes to capture important decisions
4. **Questions**: Document answers to Open Questions as they're resolved

### For AI Agents

When asked "What should I work on?" or "What's the current status?":

1. Check this index for active PRDs
2. Open the relevant PRD and find the Feature Status Tracking table
3. Look for features with status ⏳ Planned (ready to start) or 🚧 In Progress
4. Update feature status in real-time as work progresses
5. Add Implementation Notes for significant design decisions

## Creating a New PRD

1. Copy an existing PRD as a template
2. Fill in all sections with comprehensive detail
3. Include Feature Status Tracking table with all planned features
4. Add to this index with "Draft" status
5. Submit for review and approval before implementation begins
