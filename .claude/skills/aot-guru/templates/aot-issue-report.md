# AOT Issue: [Brief Description]

## Metadata
- **Date**: YYYY-MM-DD
- **Category**: Reflection | Dynamic Code | Trimming | Size | Performance
- **Severity**: Critical | High | Medium | Low
- **Status**: Open | Workaround Available | Fixed | Won't Fix
- **Affects Version**: [e.g., .NET 10, morphir-dotnet 1.0.0]

## Symptoms

[Detailed description of the problem. What happens? When does it happen?]

## Error Messages

```
[Paste build warnings/errors, runtime exceptions, or relevant log output]
```

## Environment

- **OS**: [e.g., Ubuntu 22.04, Windows 11, macOS 14]
- **Runtime**: [e.g., linux-x64, win-x64, osx-arm64]
- **.NET SDK Version**: [e.g., 10.0.100]
- **Project Type**: [e.g., Console App, CLI Tool, Library]

## Steps to Reproduce

1. [First step]
2. [Second step]
3. [...]

**Minimal Reproduction** (if applicable):
```csharp
// Minimal code that reproduces the issue
```

## Root Cause

[Explain why this issue occurs. Technical details about what AOT/trimming is doing that causes the problem.]

### Analysis
- **What code pattern triggers this?** [e.g., JsonSerializer.Deserialize without source generators]
- **Why does it fail in AOT?** [e.g., Reflection.Emit not supported, types trimmed away]
- **Is this a known .NET limitation?** [Yes/No, with reference if known]

## Workaround

[Immediate solution that allows development to continue]

### Implementation

```csharp
// Code showing the workaround
```

### Limitations
- [What this workaround doesn't solve]
- [Any performance or functionality trade-offs]

## Proper Fix

[Long-term solution that properly addresses the root cause]

### Implementation

```csharp
// Code showing the proper fix
```

### Why This Is Better
- [Advantages over the workaround]
- [Long-term maintainability benefits]

## Impact Assessment

- **Build Impact**: [Does this block AOT compilation? Yes/No]
- **Runtime Impact**: [Does this cause runtime failures? Yes/No]
- **Size Impact**: [Does this significantly affect binary size? Yes/No, how much?]
- **Performance Impact**: [Any performance implications? Yes/No, details]

## Related Issues

- Related issue: #123
- Similar issue in .NET: [link to dotnet/runtime issue]
- Documentation: [link to relevant docs]
- Community discussion: [link to discussion]

## Testing

### Test Case

[Describe how to test that the issue is fixed]

```bash
# Commands to verify the fix
dotnet publish -c Release -r linux-x64 /p:PublishAot=true
./bin/Release/net10.0/linux-x64/publish/morphir --version
```

### Expected Behavior After Fix
[What should happen after the fix is applied]

## Documentation Updates

- [ ] Update AOT/Trimming Guide with this pattern
- [ ] Add to known issues database
- [ ] Update diagnostic scripts if applicable
- [ ] Add BDD test scenario

## References

- [Microsoft AOT Documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [AOT/Trimming Guide](../../../docs/contributing/aot-trimming-guide.md)
- [AGENTS.md](../../../AGENTS.md)

---

**Notes**: [Any additional context, observations, or future considerations]
