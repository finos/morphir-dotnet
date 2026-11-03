# Morphir.SDK compatibility package

`Morphir.SDK.FSharp` is the canonical F# runtime package. This legacy package
depends on its exact matching version and ships no runtime assembly.

Existing package references can remain unchanged. For new consumers, install
`Morphir.SDK.FSharp`. Keep the `Morphir.SDK` namespace, generated source and runtime
type identities. Install either or both packages; each graph resolves one
`Morphir.SDK.dll` from the canonical package.

See the [canonical runtime migration guide](../Morphir.SDK.FSharp/README.md) for
FSharp.Core dependency selection, public API examples and covered behavior notes.
Project-reference consumers must reference the canonical project; this project
only builds the dependency and packs compatibility metadata.
