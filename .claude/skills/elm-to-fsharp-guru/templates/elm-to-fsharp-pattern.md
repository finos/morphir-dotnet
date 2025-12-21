# Elm-to-F# Pattern Template

> **Instructions**: Replace all `{placeholder}` text with actual content. Remove this blockquote when done.

## Pattern Name

{Pattern Name} - Brief one-line description

## Category

- [ ] Type System Mapping
- [ ] Function Translation
- [ ] JSON Serialization
- [ ] UI Architecture
- [ ] Build/Code Generation
- [ ] Other: {specify}

## When to Use

{Describe scenarios where this pattern applies}

**Use Cases:**
- {Use case 1}
- {Use case 2}
- {Use case 3}

**Do NOT Use When:**
- {Anti-pattern 1}
- {Anti-pattern 2}

## Elm Code

```elm
{Elm source code example}
```

**Key Characteristics:**
- {Characteristic 1}
- {Characteristic 2}

## F# Translation

### Approach 1: {Name} (Recommended)

```fsharp
{F# code for approach 1}
```

**Pros:**
- {Pro 1}
- {Pro 2}

**Cons:**
- {Con 1}
- {Con 2}

**When to Use:** {Criteria for this approach}

### Approach 2: {Name} (Alternative)

```fsharp
{F# code for approach 2}
```

**Pros:**
- {Pro 1}
- {Pro 2}

**Cons:**
- {Con 1}
- {Con 2}

**When to Use:** {Criteria for this approach}

## Decision Matrix

| Scenario | Recommended Approach | Reason |
|----------|---------------------|--------|
| {Scenario 1} | Approach 1 | {Reason} |
| {Scenario 2} | Approach 2 | {Reason} |

## AOT Compatibility

- [ ] ✅ AOT-safe (no reflection)
- [ ] ⚠️ Requires source generators
- [ ] ⚠️ Requires Myriad plugin
- [ ] ❌ Uses reflection (mark with `[<RequiresUnreferencedCode>]`)

**Notes:** {Any AOT-specific considerations}

## Code Generation Opportunities

- [ ] Can be automated with Myriad
- [ ] Can use System.Text.Json source generators
- [ ] Should be manual (not repetitive enough)

**If automatable:** {Link to Myriad plugin or generation script}

## Testing Strategy

**Unit Tests:**
```fsharp
{Example unit test}
```

**Property Tests:**
```fsharp
{Example property-based test with FsCheck}
```

**BDD Scenarios:**
```gherkin
{Example Reqnroll scenario}
```

## Common Pitfalls

### Pitfall 1: {Name}

**Problem:** {Description}

**Solution:** {How to avoid}

### Pitfall 2: {Name}

**Problem:** {Description}

**Solution:** {How to avoid}

## Examples

### Example 1: {Simple Case}

**Elm:**
```elm
{Elm code}
```

**F#:**
```fsharp
{F# code}
```

### Example 2: {Complex Case}

**Elm:**
```elm
{Elm code}
```

**F#:**
```fsharp
{F# code}
```

## Related Patterns

- [{Related Pattern 1}]({link}) - {Brief description}
- [{Related Pattern 2}]({link}) - {Brief description}

## References

- Elm documentation: {link}
- F# documentation: {link}
- AGENTS.md: {link to relevant section}
- External resource: {link}

## History

**Version:** 1.0  
**Created:** {YYYY-MM-DD}  
**Last Updated:** {YYYY-MM-DD}  
**Times Used:** 0  
**Contributors:** {Names}

## Feedback

**Have improvements to this pattern?** Update the version history and note your changes.

---

**Remember:** This pattern is a living document. As we learn from migrations, we refine and evolve these patterns.
