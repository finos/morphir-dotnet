# Pinned SDK evidence

All files below `evidence/` retain their original upstream bytes. They are
supporting source evidence, not locally executed upstream toolchains. Their
SHA-256 digests and exact source paths are in [provenance.json](provenance.json).
Each operation additionally binds an exact checked-in declaration line in
[inventory.json](inventory.json). `.gitattributes` preserves source bytes across
Windows checkouts so newline conversion cannot invalidate provenance.

| Evidence | Immutable revision | License and notice |
| --- | --- | --- |
| [Morphir Python](https://github.com/finos/morphir-python/tree/560a6503b8ffcab87ea667391579b747757bec0d) | `560a6503b8ffcab87ea667391579b747757bec0d` | [Apache-2.0](evidence/python/LICENSE), [NOTICE](evidence/python/NOTICE), [package license](evidence/python/packages/morphir/LICENSE) |
| [Morphir MoonBit](https://github.com/finos/morphir-moonbit/tree/10732eaf2f05b10ca6e94a78121e2753c814acd8) | `10732eaf2f05b10ca6e94a78121e2753c814acd8` | [Apache-2.0](evidence/moonbit/LICENSE), [NOTICE](evidence/moonbit/NOTICE) |
| [Morphir Gleam](https://github.com/finos/morphir-gleam/tree/dcfa73c218276dd8e172bbc0a2b579110bce8e46) | `dcfa73c218276dd8e172bbc0a2b579110bce8e46` | [Apache-2.0](evidence/gleam/LICENSE), [NOTICE](evidence/gleam/NOTICE) |
| [Morphir Elm vnext](https://github.com/finos/morphir-elm/tree/1be5323eb24d943c7cb3ac965d5a51d13fd83e5d) | `1be5323eb24d943c7cb3ac965d5a51d13fd83e5d` | [Apache-2.0](evidence/elm/LICENSE), [NOTICE](evidence/elm/NOTICE) |

Python's modules and tests supply independent readable behavior, immutability
and equality/hash contracts. Elm vnext SDK declarations supply Morphir signatures
and constructor/payload meaning. MoonBit supplies the separate exact-integer
error extension, typed arithmetic, decimal bounds and conformance documentation.
Gleam supplies mapping evidence for the Result parameter reversal and explicitly
limited backend coverage; it is not a numerical oracle.

The cached MoonBit `elm_oracle.json` records Elm commit
`bc99af69a8b24d391311fae3822a87eafef3c334`, Elm 0.19.1, elm/core 1.0.5,
elm/json 1.1.3, elm/regex 1.0.0 and chain-partners/elm-bignum 1.0.1. That
historical record is preserved verbatim, including its unexecuted cases. Its
internal source revision differs from the pinned vnext repository revision.
Copying it does not claim that vnext was executed or that every cached operation
belongs to this bounded .NET contract. The exact-integer and numeric-extension
files are also supporting evidence, with their original metadata intact.

`cases.json` is a local adaptation of source-described behavior with explicit
.NET representability restrictions/extensions. `basis: source-derived` means
reviewed source semantics and literal observations, not an execution transcript.
`basis: dotnet-extension` labels the selected checked/error behavior. The optional
Python authoring aid assembles reviewed literals and metadata; it never imports
or calls either .NET SDK. Re-running it cannot establish semantic correctness.

The inventory's discrepancies keep Python/Elm zero-divisor behavior, scalar versus
UTF-16 ordering, Float abs negative-zero behavior, decimal precision/exponent differences, Result parameter order
and historical-oracle revision separate. Expectations are not adjusted to make
an existing .NET implementation pass.

Original notices name FINOS for the 2026 Python, MoonBit and Gleam projects and
Morgan Stanley for 2014-2020 Morphir/Elm. Full original license/NOTICE files are
checked in above; root `NOTICE.txt` records this evidence adaptation. Root
`NOTICE` is preserved. No additional third-party executable or dependency is
redistributed by the fixture validator.
