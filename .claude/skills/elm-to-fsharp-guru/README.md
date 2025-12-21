# Elm-to-F# Guru Skill

Expert in converting Elm code from [finos/morphir-elm](https://github.com/finos/morphir-elm) to idiomatic F# for morphir-dotnet.

## Quick Start

This skill is automatically activated when you mention:
- "elm" or "elm-to-fsharp"
- "migrate elm" or "convert elm"
- "morphir-elm"
- "myriad" (in context of code generation)
- "fun.blazor" or "elm architecture"

## What This Skill Does

The Elm-to-F# Guru helps with:

1. **Language Translation** - Convert Elm syntax to idiomatic F#
2. **Type System Mapping** - Map Elm types to F# preserving safety
3. **Compile-Time Code Generation** - Use Myriad to avoid reflection (AOT-compatible)
4. **Test Migration** - Extract tests from Elm docs
5. **Behavioral Verification** - Ensure Elm-F# equivalence
6. **Pattern Catalog** - Maintain growing library of translation patterns
7. **UI Migration** - Convert Elm Architecture to Fun.Blazor
8. **Continuous Learning** - Evolve patterns and automation

## Core Philosophy

### 1. Logical Compatibility Over Literal Translation

Don't just translate syntax—translate intent. F# has different idioms and ecosystem integration patterns. Use them.

**Example:**
```elm
-- Elm: Explicit Maybe everywhere
type alias User = { name : Maybe String }
```

```fsharp
// F# Option 1: Use Option if truly optional
type User = { Name: string option }

// F# Option 2: Make non-nullability explicit
type User = { Name: string }  // Never null, enforce at boundaries
```

### 2. Reflection is Last Resort

Always explore compile-time code generation first:
- ✅ Myriad for F# code generation
- ✅ C# source generators for interop
- ✅ Build-time scripts for one-offs
- ❌ Reflection only when absolutely necessary

### 3. Incremental and Testable

Each migration should:
- Be independently verifiable
- Have test coverage >= 80%
- Include BDD scenarios for user flows
- Verify behavioral equivalence with Elm
- Pass AOT Guru review

## Common Use Cases

### "I need to migrate an Elm module to F#"

**Elm-to-F# Guru will:**
1. Analyze the Elm module structure
2. Identify dependencies and test cases
3. Recommend translation patterns
4. Identify code generation opportunities
5. Guide implementation with F# idioms
6. Create tests (unit, BDD, property-based)
7. Verify compatibility with Elm output
8. Coordinate with AOT Guru and QA Tester

**Example workflow:**
```bash
# Step 1: Analyze Elm module
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/analyze-elm-module.fsx \
    path/to/Morphir/IR/Type.elm

# Step 2: Extract tests from docs
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/extract-elm-tests.fsx \
    path/to/Morphir/IR/Type.elm \
    tests/Morphir.Core.Tests/IR/TypeTests.feature

# Step 3: Implement F# translation (guided by patterns)
# [Manual work with pattern catalog guidance]

# Step 4: Verify compatibility
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/verify-compatibility.fsx \
    tests/fixtures/elm-output/ \
    tests/fixtures/fsharp-output/
```

### "How do I convert Elm custom types to F#?"

**Elm-to-F# Guru will:**
1. Show pattern: Custom Types → Discriminated Unions
2. Provide examples (simple and complex)
3. Explain F# conventions (Error vs Err, PascalCase)
4. Show active patterns for convenient matching
5. Link to pattern catalog entry

**Quick Example:**
```elm
-- Elm
type Result error value
    = Ok value
    | Err error
```

```fsharp
// F# (idiomatic)
type Result<'error, 'value> =
    | Ok of 'value
    | Error of 'error  // Use 'Error' (F# convention)
```

### "Should I use Myriad for JSON codecs?"

**Elm-to-F# Guru will:**
1. Assess the scenario (C# interop? Type complexity?)
2. Provide decision matrix
3. Show all approaches (Myriad, source generators, manual)
4. Recommend best fit
5. Provide code examples

**Decision Matrix:**

| Scenario | Recommended Approach |
|----------|---------------------|
| C# interop heavy | System.Text.Json + Source Generators |
| Pure F# library | Myriad or Manual |
| Simple types (< 5 fields) | Manual |
| Complex IR types (10+ fields) | Myriad |
| Prototype/exploration | Manual |

### "I need to convert an Elm UI to Blazor"

**Elm-to-F# Guru will:**
1. Analyze Elm Architecture (Model-Msg-Update-View)
2. Map to Fun.Blazor architecture
3. Show MudBlazor component equivalents
4. Handle state management
5. Integrate with Blazor lifecycle

**Quick Example:**
```elm
-- Elm Architecture
type Msg = Increment | Decrement
update : Msg -> Model -> Model
view : Model -> Html Msg
```

```fsharp
// Fun.Blazor
type Msg = Increment | Decrement
let update (msg: Msg) (model: Model) : Model = ...
let view (model: Model) (dispatch: Msg -> unit) = adaptiview() { ... }
```

### "How do I extract tests from Elm docs?"

**Elm-to-F# Guru will:**
1. Parse Elm doc comments
2. Identify test examples
3. Generate BDD scenarios (Reqnroll)
4. Generate unit tests (TUnit)
5. Generate property tests (FsCheck)

**Automation:**
```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/extract-elm-tests.fsx \
    src/Morphir/IR/Type.elm \
    tests/Morphir.Core.Tests/IR/TypeTests.feature
```

**Elm Doc:**
```elm
{-| Create a user ID from a string.

    userId "abc123" == Just (UserId "abc123")
    userId "" == Nothing

-}
userId : String -> Maybe UserId
```

**Generated BDD:**
```gherkin
Feature: UserId Creation

  Scenario: Valid user ID
    Given the string "abc123"
    When I create a UserId
    Then the result should be Some (UserId "abc123")

  Scenario: Empty string
    Given the string ""
    When I create a UserId
    Then the result should be None
```

### "I need a custom Myriad plugin"

**Elm-to-F# Guru will:**
1. Assess if custom plugin is warranted (5+ types)
2. Scaffold plugin project structure
3. Provide template implementation
4. Set up MSBuild integration
5. Show usage examples

**Automation:**
```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/generate-myriad-plugin.fsx \
    MorphirJsonCodec
```

## Tools Provided

### Automation Scripts (.fsx)

Located in `.claude/skills/elm-to-fsharp-guru/scripts/`:

1. **analyze-elm-module.fsx** - Analyze Elm module structure
   ```bash
   dotnet fsi analyze-elm-module.fsx <elm-file>
   ```
   - Types, functions, dependencies
   - Code generation opportunities
   - Complexity metrics

2. **extract-elm-tests.fsx** - Extract tests from Elm docs
   ```bash
   dotnet fsi extract-elm-tests.fsx <elm-file> <output-feature>
   ```
   - BDD scenarios generated
   - Test cases from doc comments

3. **verify-compatibility.fsx** - Verify Elm-F# equivalence
   ```bash
   dotnet fsi verify-compatibility.fsx <test-data-dir>
   ```
   - JSON output comparison
   - Behavioral equivalence check

4. **migration-metrics.fsx** - Track migration progress
   ```bash
   dotnet fsi migration-metrics.fsx
   ```
   - Modules completed vs pending
   - Test coverage metrics
   - Feature parity percentage

5. **generate-myriad-plugin.fsx** - Scaffold Myriad plugin
   ```bash
   dotnet fsi generate-myriad-plugin.fsx <plugin-name>
   ```
   - Plugin project structure
   - Template implementation
   - MSBuild integration

6. **codegen-helpers.fsx** - Build-time code generation
   ```bash
   dotnet fsi codegen-helpers.fsx json-codec <type-file>
   dotnet fsi codegen-helpers.fsx visitor <type-file>
   dotnet fsi codegen-helpers.fsx lenses <type-file>
   ```
   - Generate JSON codecs
   - Generate visitors
   - Generate lenses

### Pattern Catalog

Located in `.claude/skills/elm-to-fsharp-guru/patterns/`:

1. **custom-types.md** - Elm custom types → F# discriminated unions
2. **encoders-decoders.md** - JSON serialization approaches
3. **opaque-types.md** - Smart constructors and phantom types
4. **maybe-result.md** - Option/Result equivalence
5. **dict-limitations.md** - Working around Elm Dict restrictions
6. **myriad-basics.md** - Using Myriad for code generation
7. **custom-myriad-plugins.md** - Writing custom Myriad plugins
8. **fun-blazor-basics.md** - Elm Architecture to Fun.Blazor

### Templates

Located in `.claude/skills/elm-to-fsharp-guru/templates/`:

1. **elm-to-fsharp-pattern.md** - Pattern catalog entry
2. **migration-task.md** - Migration task planning
3. **compatibility-test.md** - Compatibility test
4. **decision-tree.md** - Decision tree
5. **myriad-plugin.fs** - Myriad plugin
6. **build-codegen.targets** - MSBuild targets

## Decision Trees

### When to Use Myriad vs Manual

```
Is the pattern repetitive (3+ types)?
├─ YES → Consider code generation
│   ├─ Existing Myriad plugin available?
│   │   ├─ YES → Use existing plugin
│   │   └─ NO → Worth writing custom plugin (5+ types)?
│   │       ├─ YES → Write custom Myriad plugin
│   │       └─ NO → Use build script or manual
│   └─ For C# interop?
│       ├─ YES → Use C# source generators
│       └─ NO → Myriad is appropriate
└─ NO → Write manually
```

### Which JSON Serialization Approach?

```
What's the primary use case?
├─ C# Interop Heavy → System.Text.Json + Source Generators
├─ Pure F# Library
│   ├─ Complex types (10+ fields) → Myriad-Generated Codecs
│   └─ Simple types (< 5 fields) → Manual Implementation
└─ Prototyping → Manual Implementation
```

### UI Migration Path

```
Elm UI Component
├─ Server-side rendering? → Blazor Server + Fun.Blazor
├─ Rich client app? → Blazor WASM + Fun.Blazor
├─ Desktop app? → Avalonia.FuncUI
├─ Complex state? → Use Elmish (TEA for .NET)
└─ Material Design? → Add MudBlazor components
```

## Integration with Other Gurus

### With AOT Guru
- **Trigger**: After code generation or migration
- **Flow**: Elm-to-F# → AOT safety review → Fix reflection issues
- **Checks**: No reflection, Myriad code is AOT-safe, PublishTrimmed passes

### With QA Tester
- **Trigger**: After migration completes
- **Flow**: Elm-to-F# → Coverage verification → Add missing tests
- **Checks**: Coverage >= 80%, BDD scenarios, property tests, compatibility tests

### With Release Manager
- **Trigger**: Planning releases
- **Flow**: Release Manager queries → Elm-to-F# reports progress
- **Provides**: Modules completed, feature parity %, blockers

### With Technical Writer
- **Trigger**: New pattern discovered
- **Flow**: Elm-to-F# documents pattern → Technical Writer publishes
- **Outcome**: Hugo docs site updated, diagrams created

## Migration Workflow

### Phase 1: Analysis & Planning
1. Identify Elm module
2. Analyze dependencies
3. Extract test cases
4. Identify code generation opportunities
5. Create migration task
6. Estimate effort

### Phase 2: Implementation
1. Set up code generation (if needed)
2. Create F# types
3. Implement functions (F# idioms)
4. Generate or create JSON serialization
5. Write tests (TDD)
6. Write BDD scenarios
7. Write property tests

### Phase 3: Verification
1. Verify no reflection warnings
2. Test with PublishTrimmed=true
3. Run compatibility tests
4. Verify JSON roundtrip
5. Compare with Elm output
6. Document divergences
7. Get code reviews (AOT Guru, QA Tester)

### Phase 4: Documentation
1. Update migration tracking
2. Add to pattern catalog (if new patterns)
3. Document code generation approach
4. Update compatibility matrix
5. Document learnings

## Common Patterns Quick Reference

### Custom Types → Discriminated Unions
```elm
type Result error value = Ok value | Err error
```
```fsharp
type Result<'error, 'value> = Ok of 'value | Error of 'error
```

### Type Aliases → Records or Type Abbreviations
```elm
type alias Point = { x : Float, y : Float }
type alias Name = String
```
```fsharp
type Point = { X: float; Y: float }
type Name = string
```

### Opaque Types → Phantom Types
```elm
type UserId = UserId String
```
```fsharp
type UserId = private UserId of string
```

### Maybe → Option
```elm
type Maybe a = Nothing | Just a
```
```fsharp
// Built-in: type Option<'a> = None | Some of 'a
```

### JSON Encoders → Multiple Approaches
```elm
encoder : User -> Value
encoder user = E.object [("id", E.int user.id)]
```
```fsharp
// Approach 1: System.Text.Json (C# interop)
// Approach 2: Myriad-Generated (Pure F#, AOT-safe)
// Approach 3: Manual (Full control)
```

### Elm Architecture → Fun.Blazor
```elm
type Msg = Increment | Decrement
update : Msg -> Model -> Model
view : Model -> Html Msg
```
```fsharp
type Msg = Increment | Decrement
let update (msg: Msg) (model: Model) : Model = ...
let view (model: Model) (dispatch: Msg -> unit) = adaptiview() { ... }
```

## Success Criteria

- ✅ Types encode same invariants as Elm
- ✅ Functions are behaviorally equivalent
- ✅ JSON roundtrip tests pass
- ✅ No reflection warnings
- ✅ Test coverage >= 80%
- ✅ BDD scenarios cover user flows
- ✅ Code is idiomatic F#
- ✅ Patterns documented
- ✅ AOT Guru review passed
- ✅ QA Tester coverage verified

## Getting Help

If the Elm-to-F# Guru encounters something it can't solve:
1. Documents the issue thoroughly
2. Researches patterns from F# community
3. Consults morphir-elm source code
4. Escalates to maintainers with full context
5. Updates pattern catalog with resolution

## Resources

### Essential Docs
- [SKILL.md](./SKILL.md) - Comprehensive skill documentation
- [IMPLEMENTATION.md](./IMPLEMENTATION.md) - Implementation tracking
- [Pattern Catalog](./patterns/) - All translation patterns

### Elm Resources
- [Elm Guide](https://guide.elm-lang.org/)
- [Elm JSON](https://package.elm-lang.org/packages/elm/json/latest/)
- [morphir-elm](https://github.com/finos/morphir-elm)

### F# Resources
- [F# for Fun and Profit](https://fsharpforfunandprofit.com/)
- [Railway Oriented Programming](https://fsharpforfunandprofit.com/rop/)
- [Domain Modeling Made Functional](https://pragprog.com/titles/swdddf/)

### Myriad Resources
- [Myriad Repository](https://github.com/MoiraeSoftware/myriad)
- [Myriad Docs](https://moiraesoftware.github.io/myriad/)
- [Custom Plugin Guide](https://moiraesoftware.github.io/myriad/how-to/Create-Plugins.html)

### Fun.Blazor & MudBlazor
- [Fun.Blazor Repository](https://github.com/slaveOftime/Fun.Blazor)
- [Fun.Blazor Docs](https://slaveoftime.github.io/Fun.Blazor.Docs/)
- [MudBlazor](https://mudblazor.com/)
- [Elmish](https://elmish.github.io/elmish/)

### morphir-dotnet
- [AGENTS.md](../../../AGENTS.md)
- [F# Coding Guide](../../../docs/contributing/fsharp-coding-guide.md)
- [AOT Guru](../aot-guru/)
- [QA Tester](../qa-tester/)

---

**Philosophy**: We're not just translating code—we're porting functional domain models between ecosystems while maintaining type safety, behavioral equivalence, and idiomatic quality. Every migration is an opportunity to learn and improve our patterns.
