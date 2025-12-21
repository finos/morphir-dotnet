# Decision Tree Template

## Decision: {Decision Name}

**Context:** {When does this decision need to be made?}

---

## Decision Tree

```
{Root Question}
│
├─ {Option A}
│  │
│  ├─ {Sub-question A1}
│  │  │
│  │  ├─ {Sub-option A1a} → {Outcome}
│  │  │
│  │  └─ {Sub-option A1b} → {Outcome}
│  │
│  └─ {Sub-question A2}
│     └─ {Outcome}
│
├─ {Option B}
│  └─ {Outcome}
│
└─ {Option C}
   └─ {Outcome}
```

---

## Outcomes

### Outcome 1: {Name}

**When:** {Criteria}

**Approach:** {What to do}

**Pros:**
- {Pro 1}
- {Pro 2}

**Cons:**
- {Con 1}
- {Con 2}

**Example:**
```fsharp
{Code example}
```

### Outcome 2: {Name}

**When:** {Criteria}

**Approach:** {What to do}

**Pros/Cons:** {List}

**Example:** {Code}

---

## Usage Statistics

**Times Used:** {X}  
**Last Used:** {YYYY-MM-DD}  
**Most Common Path:** {A → A1a}

---

## Refinements

**Refinement 1 ({Date}):** {What changed and why}

**Refinement 2 ({Date}):** {What changed and why}

---

## Related Decisions

- [{Related Decision 1}]({link})
- [{Related Decision 2}]({link})
