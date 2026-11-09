---
title: "Upstream proposals"
linkTitle: "Upstream proposals"
description: >
  Changes that morphir-dotnet proposes to upstream Morphir contracts, written here first for maintainer review.
---

# Upstream proposals

These documents propose changes to upstream Morphir contracts, for example the Morphir Extension Protocol (MEP) in `finos/morphir`. morphir-dotnet writes each proposal here first. A maintainer reviews it. After approval, the proposal goes upstream as a draft pull request, and its record here gets the pull request URL and commit.

A proposal adds only what its upstream contract does not have. It follows the conventions of the upstream files that it changes.

| Proposal | Upstream target | Status |
| --- | --- | --- |
| [The validateModel payload for MEP](mep-validate-model.md) | `finos/morphir`, `spec/mep` | Merged upstream: [finos/morphir#1139](https://github.com/finos/morphir/pull/1139) as `acc529aac`; decisions VAL-1 to VAL-6 |
