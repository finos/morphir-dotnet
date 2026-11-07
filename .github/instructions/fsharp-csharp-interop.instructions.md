---
applyTo: "**/*.fs,**/*.fsi,**/*.fsproj"
---

# F# code that C# calls

When you add or change a public F# type, member or module, or the settings of an F# project, check whether a C# project uses it. If it does, load the `fsharp-csharp-interop` skill (`.agents/skills/fsharp-csharp-interop/SKILL.md`) and follow its rules. The most important ones:

- The F# project sets `<Nullable>enable</Nullable>`, warnings as errors, and the trim and AOT properties. Without F# nullness, C# cannot see which values may be null.
- Parse, don't validate: a value with a rule is a type with a private case and `Parse`, `TryParse` and `Create` members. No public constructor on a type with an invariant.
- On the C# surface: no F# function types, curried members, `option`, `list`, `Set`, `Map` or `Async<'T>`.
- Override `ToString()`; do not use `%A` in shipped code.

After the change, run the `fsharp-csharp-interop-reviewer` agent on the diff.
