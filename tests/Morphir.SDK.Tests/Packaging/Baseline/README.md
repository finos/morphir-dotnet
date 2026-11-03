# Pre-migration compatibility inputs

`LegacyConsumer.dll` was compiled and successfully executed against the untouched
`Morphir.SDK` package before production packaging edits. Its SHA-256 is
`41afc3e57b4ebf350467d0d812d0fdb63eda1878eecd9099d30c992d60768228`.
Migration tests must copy this binary without recompiling it. The companion
deps/runtimeconfig files retain its original dependency declaration; tests run
with each fresh graph's resolved dependency files and runtime.

Source: `../LegacyConsumer.fs`. Captured HEAD:
`7e37689c55eae9510cf12dafd130dc4ae8e3baf1`. The source file's original bytes,
runtime/package/API hashes and executed-baseline statement are recorded in
`provenance.json`. `public-api.txt` contains 281 exported type/member declarations.
The migration permits additive declarations but must retain every old one.

These are deliberate test inputs, not package content. No old runtime DLL,
NuGet package or package cache is included here. Both the first failed attempt
and successful dependency-pinned attempt remain in the C:/Temp locations recorded
in the feature PRD. The failure was an implicit FSharp.Core downgrade; the
successful baseline used the old package's declared version `11.0.100`.
