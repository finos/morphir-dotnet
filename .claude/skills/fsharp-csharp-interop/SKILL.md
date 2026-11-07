---
name: fsharp-csharp-interop
description: Rules for F# code that C# calls, and C# code that calls F#, in morphir-dotnet. Covers parsed domain types, nullness metadata, TryParse/Create members, Match members for unions, collections, option and null, equality, ToString and AOT safety, and the .fsproj settings. Use when you add or change a public F# type, member or module that a C# project uses; when C# code calls F# types (unions, records, Result, option, list); when you create an F# project that C# references; or when you review a diff that crosses the F#/C# boundary. Triggers include "F# interop", "C# interop", "call F# from C#", "F# union in C#", "NotNullWhen", "CompiledName", "F# nullness", "Nullable enable fsproj", "oblivious", "TryParse", "parse don't validate".
---

# F# and C# interop

morphir-dotnet writes domain types in F# and keeps the CLI edge in C# where C# is required (System.Text.Json source generation, System.CommandLine, Foundatio.Mediator). This skill holds the rules for the boundary between the two languages.

The rules apply only to the **C# surface**: the F# types, members and modules that a C# project calls. Inside F#, write idiomatic F#.

Code examples for each rule are in [references/patterns.md](references/patterns.md). Read that file before you write new boundary code.

## Before you start

1. Find the C# surface. List the F# types and members that a C# project uses. Search the `.cs` files for the F# namespace.
2. Decide where each decision lives. A decision that branches on an F# union goes in an F# function. C# calls that function and does not branch.
3. Check the project settings (rule 1). Without them, the C# compiler cannot see F# nullness.

## Rules

### 1. Project settings

Every F# project that C# references sets:

```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<IsTrimmable>true</IsTrimmable>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<IsAotCompatible>true</IsAotCompatible>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

`<Nullable>enable</Nullable>` makes F# write nullable metadata. Without it, C# treats every F# reference type as "oblivious" and gives no warning when it passes `null`. The C# projects already set `<Nullable>enable</Nullable>` and treat warnings as errors.

### 2. Parse, don't validate

- A domain value with a rule (an id, a name, a version, a target) is a single-case union with a **private** case, or a record with a **private** representation. Only its own module can build it.
- Give it `static member Parse(text) : Result<T, ParseFailure>` for F# callers, `static member TryParse(text, out result, out failure) : bool` for C# callers, and `static member Create(literal)` for literals in source code. `Create` throws on invalid text. A unit test calls `Create` for each literal.
- A record with an invariant must not have a public constructor. C# can call a public F# record constructor and skip every rule.
- Never use `[<Struct>]` for a parsed value. `default(T)` skips the parser.
- Derive dependent facts. Do not store a value that another value already decides (for example, entry types that come from capability claims).

### 3. Names and calls

- Put the C# surface on types as static or instance members. Use `[<CompiledName("PascalCase")>]` when a module function must be called from C#.
- Use tupled parameters on members. Never put an F# function type (`'a -> 'b`) in a C# parameter or return type. Use `Func<...>` or `Action<...>`.
- Put the code in a `namespace`, not in a top-level module, and use `[<RequireQualifiedAccess>]` on unions and modules.
- Return `Task<T>` or `ValueTask<T>` and take a `CancellationToken`. Never return `Async<'T>` to C#.

### 4. Nullness

- On the C# surface, an absent value is `T | null`. Inside F#, it is `option`. Convert at the surface with `Option.toObj` and `Option.ofObj`.
- A parameter that receives text from outside takes `string | null`, so C# can pass what it has and the parser decides.
- `TryParse` and similar members use `[<Out; NotNullWhen(true)>]` on the result and `[<Out; NotNullWhen(false)>]` on the failure, so C# flow analysis knows which one is set.
- Nullness checks are compile-time only. `null!`, reflection and deserialization can still put `null` into a value. Parse at every real input boundary.

### 5. Unions seen from C#

- Prefer an F# function that makes the decision. C# then calls it and never branches on the union.
- When C# must branch, add a `Match` member with one `Func` for each case. The signature forces C# to handle every case. Do not ask C# to use `Tag`, `IsX` or type tests.
- C# builds a fieldless case through the generated static property (`MorphirHost.Native`). For a case with fields, give C# a named factory member when it needs one.
- When C# must write a union as text, give the union a `WireValue` member. C# reads it and does not map cases by hand.

### 6. Collections

- On the C# surface, use `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `ImmutableArray<T>` or a project type such as `NonEmptySet<T>`.
- Never expose F# `list`, `Set`, `Map` or `seq` that C# must enumerate more than once.

### 7. Equality, order and text

- Use ordinal comparison for text identities: `[<CustomEquality; CustomComparison>]` with `StringComparison.Ordinal`.
- Override `ToString()` on every type on the surface. The default prints through reflection (`%A`), which is not AOT-safe and gives unstable text.
- Do not use `sprintf "%A"`, `printfn "%A"` or `string` on a record or union in code that ships. Use string interpolation over explicit members.

### 8. Documentation

- Every public F# member has an XML doc comment. Say which members exist for C# callers.

## Review checklist

Use this list when you review a diff that crosses the boundary. Report each failed item with its file, line and rule number.

- [ ] Rule 1: the F# project sets nullness, warnings as errors, trim and AOT properties.
- [ ] Rule 2: no public constructor or public case on a type with an invariant; no `[<Struct>]` parsed value; `Create` literals are tested.
- [ ] Rule 2: no `string`, C# `enum` or flag on the surface where a parsed type or union belongs.
- [ ] Rule 3: no F# function type, curried member or `Async<'T>` on the C# surface.
- [ ] Rule 4: no `option` on the C# surface; `TryParse` uses `NotNullWhen`; external text enters as `string | null`.
- [ ] Rule 5: C# code does not switch on `Tag`, `IsX` or F# case types; `Match` or an F# function is used instead.
- [ ] Rule 6: no F# `list`, `Set` or `Map` on the C# surface.
- [ ] Rule 7: ordinal equality for text identities; `ToString` overridden; no `%A` in shipped code.
- [ ] Rule 8: XML doc comments on the public F# surface.

## Review mode

To review as a separate agent, run the reviewer agent `fsharp-csharp-interop-reviewer` (Codex: `fsharp_csharp_interop_reviewer`). The repository defines it for Claude Code, Codex, GitHub Copilot, Cursor and Grok Build. On a tool with no file-based agents (for example Muse Code), start a subagent with this prompt:

> Read `.agents/skills/fsharp-csharp-interop/SKILL.md` and `references/patterns.md`. Get the diff with `git diff <base>...HEAD -- '*.fs' '*.fsi' '*.fsproj' '*.cs'`. Apply the review checklist only to the F#/C# boundary. Do not edit files. Report each finding as `path:line: rule N: problem. fix.`, most severe first. If there is no finding, say so.

## Related

- [references/patterns.md](references/patterns.md): code for each rule.
- `docs/contributing/fsharp-coding-guide.md`: F# style, including the nullness section.
- `.agents/aot-optimization.md`: AOT and trimming rules.
- `AGENTS.md` section 5: functional design rules.
