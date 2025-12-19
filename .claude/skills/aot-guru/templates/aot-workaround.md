# Workaround: [Issue Description]

## Overview

[Brief description of what issue this workaround addresses]

**Related Issue**: [Link to aot-issue-report.md or GitHub issue]

## When to Use

This workaround applies when:
- [Condition 1]
- [Condition 2]
- [Condition 3]

**Do NOT use this workaround if:**
- [Negative condition 1]
- [Negative condition 2]

## Prerequisites

- [Required package version, e.g., System.Text.Json 9.0.0+]
- [Required .NET SDK version, e.g., .NET 10+]
- [Any other dependencies]

## Implementation

### Step 1: [First step title]

[Detailed explanation]

```csharp
// Code for step 1
```

### Step 2: [Second step title]

[Detailed explanation]

```csharp
// Code for step 2
```

### Step 3: [Third step title]

[Detailed explanation]

```csharp
// Code for step 3
```

## Complete Example

```csharp
// Full working example showing the workaround in context

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

// Before (problematic code)
// public class Example {
//     public void ProblematicMethod() {
//         var result = JsonSerializer.Deserialize<MyType>(json);
//     }
// }

// After (with workaround)
[JsonSerializable(typeof(MyType))]
internal partial class JsonContext : JsonSerializerContext { }

public class Example {
    public void FixedMethod() {
        var result = JsonSerializer.Deserialize(json, JsonContext.Default.MyType);
    }
}
```

## Testing the Workaround

### Verify It Works

```bash
# Build with AOT
dotnet publish -c Release -r linux-x64 /p:PublishAot=true

# Run tests
./bin/Release/net10.0/linux-x64/publish/morphir [test-command]
```

### Expected Results
- [What you should see if workaround is working]
- [How to verify no warnings/errors]

## Limitations

### Functional Limitations
- [What this workaround doesn't support]
- [Any feature gaps]

### Performance Implications
- [Impact on startup time, if any]
- [Impact on runtime performance, if any]
- [Impact on memory usage, if any]

### Maintenance Considerations
- [Extra code that needs to be maintained]
- [Manual steps required when adding new types/features]
- [When this workaround becomes obsolete]

## Proper Fix Timeline

**When will a proper fix be available?**
- [ ] Waiting for .NET framework fix (version X.Y)
- [ ] Planned for morphir-dotnet version X.Y
- [ ] Community contribution welcome
- [ ] Long-term workaround (no fix planned)

**How to migrate from workaround to proper fix:**
[Instructions for when proper fix is available]

## Alternatives Considered

### Alternative 1: [Name]
**Pros**: [Benefits]
**Cons**: [Drawbacks]
**Why not chosen**: [Reason]

### Alternative 2: [Name]
**Pros**: [Benefits]
**Cons**: [Drawbacks]
**Why not chosen**: [Reason]

## Related Workarounds

- [Link to similar workaround for related issue]
- [Link to pattern that might be useful]

## Community Feedback

[Space for community feedback on the workaround]

**Success Stories**: [Link to PRs/issues where this worked]
**Known Problems**: [Link to issues where this didn't work]

## References

- [Related Microsoft documentation]
- [Community blog posts or discussions]
- [AOT/Trimming Guide section](../../../docs/contributing/aot-trimming-guide.md#relevant-section)

---

**Last Updated**: YYYY-MM-DD
**Status**: Active | Deprecated | Superseded by [link]
**Tested With**: .NET X.Y, morphir-dotnet X.Y
