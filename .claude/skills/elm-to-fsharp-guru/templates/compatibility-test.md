# Compatibility Test Template

## Test: {Test Name}

**Purpose:** Verify behavioral equivalence between Elm and F# implementations.

**Test Data:** `{path/to/test-data/}`

---

## Setup

### Elm Output
```bash
cd morphir-elm
elm make src/{Module}.elm
# Run Elm implementation and capture output
```

### F# Output
```bash
cd morphir-dotnet
dotnet run --project src/{Project}
# Run F# implementation and capture output
```

---

## Test Cases

### Test Case 1: {Scenario}

**Input:**
```json
{
  "input": "value"
}
```

**Expected Output (Elm):**
```json
{
  "expected": "result"
}
```

**Actual Output (F#):**
```json
{
  "actual": "result"
}
```

**Status:** ✅ Pass | ❌ Fail | ⚠️ Divergence

**Notes:** {Any notes about differences}

---

## Automation

Run compatibility tests:
```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/verify-compatibility.fsx tests/fixtures/
```

---

## Results

| Test Case | Elm Output | F# Output | Status | Notes |
|-----------|-----------|-----------|--------|-------|
| {Case 1} | {hash} | {hash} | ✅/❌ | {notes} |
| {Case 2} | {hash} | {hash} | ✅/❌ | {notes} |

**Pass Rate:** {X}%  
**Total Tests:** {Y}  
**Passed:** {Z}  
**Failed:** {W}
