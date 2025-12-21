# Migration Task Template

> **Instructions**: Use this template to plan and track Elm module migrations. Update as you progress.

## Module Migration: {Elm Module Name}

**Status:** 🔄 In Progress | ✅ Complete | ⏸️ Blocked | 📋 Planned

**Assignee:** {Name or @mention}  
**Started:** {YYYY-MM-DD}  
**Target Completion:** {YYYY-MM-DD}  
**Actual Completion:** {YYYY-MM-DD or TBD}

---

## Source

**Elm Module:** `{Full.Module.Path}`  
**Location:** `{path/to/Elm/Module.elm}`  
**Package:** `{package name}`  
**Dependencies:**
- `{Dependency.Module.1}`
- `{Dependency.Module.2}`

**Complexity:** Low | Medium | High | Very High  
**Estimated Effort:** {X hours/days}

---

## Target

**F# Module:** `{Full.Module.Path}`  
**Location:** `{path/to/FSharp/Module.fs}`  
**Project:** `{Project.Name.fsproj}`  
**Dependencies:**
- `{Dependency.Module.1}`
- `{Dependency.Module.2}`

---

## Code Generation Strategy

- [ ] Identify repetitive patterns suitable for Myriad
- [ ] Evaluate need for custom Myriad plugin
- [ ] Consider build-time script generation
- [ ] Choose: Myriad / Source Generators / Manual

**Decision:** {Myriad | Source Generators | Manual | Hybrid}

**Rationale:** {Why this approach?}

---

## Migration Checklist

### Phase 1: Analysis
- [ ] Run `analyze-elm-module.fsx` on source file
- [ ] Document type definitions (custom types, aliases, records)
- [ ] Document function signatures
- [ ] Extract test cases with `extract-elm-tests.fsx`
- [ ] Identify code generation opportunities
- [ ] Map dependencies to F# equivalents
- [ ] Estimate complexity and effort

### Phase 2: Type Translation
- [ ] Create F# discriminated unions (from Elm custom types)
- [ ] Create F# records (from Elm type aliases)
- [ ] Create phantom types (from Elm opaque types)
- [ ] Apply smart constructors where appropriate
- [ ] Ensure illegal states are unrepresentable

### Phase 3: Function Implementation
- [ ] Implement functions with F# idioms
- [ ] Use Option/Result instead of Maybe/Result
- [ ] Apply railway-oriented programming where appropriate
- [ ] Use computation expressions for chaining
- [ ] Ensure pure functions remain pure

### Phase 4: JSON Serialization
- [ ] **Myriad-generated codecs** (if applicable)
- [ ] **System.Text.Json source generators** (if C# interop)
- [ ] **Manual codecs** (if simple types)
- [ ] Test JSON roundtrip (Elm JSON → F# → JSON)
- [ ] Verify field name compatibility

### Phase 5: Testing
- [ ] Write TUnit unit tests (TDD)
- [ ] Write Reqnroll BDD scenarios
- [ ] Write FsCheck property-based tests
- [ ] Create compatibility tests (Elm vs F# output)
- [ ] Achieve >= 80% code coverage
- [ ] Run `verify-compatibility.fsx`

### Phase 6: Code Generation Setup
- [ ] Set up Myriad plugin (if using)
- [ ] Configure MSBuild integration
- [ ] Test code generation in dev workflow
- [ ] Validate generated code compiles
- [ ] Ensure generated code is AOT-safe

### Phase 7: Verification
- [ ] Verify no reflection warnings (`IL2026`, `IL3050`)
- [ ] Test with `PublishTrimmed=true`
- [ ] Compare outputs with Elm implementation
- [ ] Document any intentional divergences
- [ ] Get code review from AOT Guru
- [ ] Get coverage review from QA Tester

### Phase 8: Documentation
- [ ] Update IMPLEMENTATION.md
- [ ] Add new patterns to pattern catalog (if discovered)
- [ ] Document code generation approach
- [ ] Update migration metrics
- [ ] Document learnings and challenges

---

## Elm Patterns Identified

- [ ] Custom types → F# discriminated unions
- [ ] Type aliases → F# records or type abbreviations
- [ ] Opaque types → F# phantom types
- [ ] Maybe/Result → Option/Result
- [ ] JSON encoders/decoders → {approach}
- [ ] Extensible records → {F# approach}
- [ ] Dict with custom keys → {workaround}
- [ ] {Other pattern}

---

## Code Generation Approach

**Selected:** None | Myriad Built-in | Custom Myriad Plugin | Build Script | C# Source Generator

**Details:**
- {What gets generated}
- {Tool/plugin used}
- {Build integration}
- {AOT compatibility verified}

---

## Test Coverage

**Extracted from Elm Docs:**
- Test cases found: {X}
- BDD scenarios created: {Y}

**Created:**
- Unit tests: {count}
- Property tests: {count}
- Compatibility tests: {count}

**Coverage:** {XX}%

**Coverage Report:** {Link or attach}

---

## AOT Compatibility

- [ ] ✅ No reflection usage
- [ ] ✅ No `[<RequiresUnreferencedCode>]` attributes
- [ ] ✅ Tested with `PublishTrimmed=true`
- [ ] ✅ JSON serialization is AOT-compatible
- [ ] ✅ Myriad-generated code is AOT-safe
- [ ] ✅ AOT Guru review passed

**Notes:** {Any AOT-specific concerns or workarounds}

---

## Compatibility Notes

**Intentional Divergences:**
1. {Divergence 1}: {Reason}
2. {Divergence 2}: {Reason}

**Elm vs F# Behavioral Differences:**
1. {Difference 1}: {Explanation}
2. {Difference 2}: {Explanation}

**JSON Compatibility:**
- [ ] ✅ Field names match Elm output
- [ ] ✅ JSON structure identical
- [ ] ✅ Roundtrip tests pass
- [ ] ⚠️ Known differences: {describe}

---

## Implementation Notes

### Decisions Made

**Decision 1:** {What was decided}  
**Rationale:** {Why}  
**Alternatives Considered:** {What else was considered}

**Decision 2:** {What was decided}  
**Rationale:** {Why}  
**Alternatives Considered:** {What else was considered}

### Challenges Encountered

**Challenge 1:** {Description}  
**Impact:** {How it affected migration}  
**Resolution:** {How it was resolved}  
**Pattern Updated:** {Yes/No} - {Link to pattern}

**Challenge 2:** {Description}  
**Impact:** {How it affected migration}  
**Resolution:** {How it was resolved}  
**Pattern Updated:** {Yes/No} - {Link to pattern}

### Learnings

1. {Learning 1}
2. {Learning 2}
3. {Learning 3}

### Recommendations for Future Migrations

1. {Recommendation 1}
2. {Recommendation 2}

---

## Blockers

| Issue | Impact | Owner | Status | Resolution |
|-------|--------|-------|--------|------------|
| {Issue 1} | {High/Med/Low} | {Name} | {Open/Resolved} | {Solution} |

---

## Code Review

**Reviewers:**
- AOT Guru: {Status} - {Feedback}
- QA Tester: {Status} - {Feedback}
- Maintainer: {Status} - {Feedback}

**Feedback Addressed:**
- [ ] {Feedback item 1}
- [ ] {Feedback item 2}

---

## Related Issues/PRs

- Issue #{number}: {Description}
- PR #{number}: {Description}

---

## References

- Elm source: {link to morphir-elm}
- F# implementation: {link to file}
- Pattern catalog: {links to relevant patterns}
- ADRs: {links if applicable}

---

**Version:** 1.0  
**Last Updated:** {YYYY-MM-DD}  
**Next Review:** {YYYY-MM-DD}
