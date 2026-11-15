# Provenance of the node address fixture

`node-addresses-draft.json` is a copy of `docs/spec/ir/fixtures/node-addresses-draft.json` of
[`finos/morphir`](https://github.com/finos/morphir), contract version `0.1.0-draft.1`, read on 2026-11-15. The last
commit that changed the file before the copy is `02f96874` (2026-09-25, "feat: deliver early-access V4 linked
metadata (#997)"). The file is licensed as the repository `finos/morphir` (Apache-2.0).

The tests of `Morphir.MiddleEnd.Tests` use the `uriCases` array: 14 addresses that parse and write back to their
canonical spelling, and 15 texts that parse to an error. The other arrays need a resolver and are not used.

Do not edit this copy. A newer draft replaces it as a whole, with the new commit named here.
