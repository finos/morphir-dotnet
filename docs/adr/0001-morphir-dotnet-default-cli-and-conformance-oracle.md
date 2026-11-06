---
status: proposed
date: 2026-11-06
---

# morphir-dotnet provides the default Morphir CLI and hosts the conformance oracle

We propose that morphir-dotnet provides the default `morphir` command-line interface (CLI) for the Morphir ecosystem, on one condition: it must first prove the viability criteria in the [default CLI PRD](../content/contributing/design/prds/morphir-dotnet-default-cli.md). We also propose that morphir-dotnet hosts the Morphir Compatibility Kit (MCK) and the conformance oracle. The protocol definition of the Morphir Extension Protocol (MEP) stays in `finos/morphir` (the Gleam contract and the Smithy model). The oracle is versioned case data with independently written expectations. It is not an implementation. Making this the default is a decision for the FINOS Morphir maintainers, so this record stays `proposed` until they accept it.

## Why

The current oracle in `finos/morphir` is the Rust CLI together with its kits. It has defects that a conformance oracle must not have:

- The Rust CLI writes `--json-lines` output as one compact report line (`crates/morphir/src/output.rs`). The intended meaning of the flag is one JSON record per line, written as each event occurs.
- The generic MEP guest kit (`generic-json-library-v1`, upstream commit `cde7722eb`) requires a legacy MEP `0.1` stage for every guest. It also requires each guest to be both a frontend and a backend for its language. A guest that implements only the current `0.2.0-draft.1` frontend contract therefore cannot qualify, even when it is correct.

When an implementation is the oracle, its defects become the specification. Case data with independently written expectations can qualify any implementation, including the Rust CLI and morphir-dotnet itself.

## Considered options

- **Keep the Rust CLI as default and the Rust kits as oracle.** Rejected: the defects above become requirements for every other implementation.
- **Move the oracle to morphir-dotnet, but keep the Rust CLI as the default.** Possible as an intermediate state, and it is the coexistence period in the PRD. It is not the end state, because the reference behavior and the default CLI would then differ.
- **Move the MEP contract definition to morphir-dotnet too.** Rejected for now: the Gleam contract and the Smithy model are a working, shared definition. Contract defects are reported upstream with MCK evidence.

## Consequences

- Each feature that morphir-dotnet adds must have MCK cases. The cases, not the .NET code, set the expected behavior. The first proof point is the `add-fsharp-compile` OpenSpec change, whose compile corpus is the seed of the first MCK suite. The MCK home is the `establish-morphir-compatibility-kit` change.
- Where morphir-dotnet sets the reference behavior and the Rust CLI differs (for example `--json-lines`), the .NET behavior, the evidence and an upstream issue are recorded together. The difference is never silent.
- The two CLIs coexist until the maintainers accept this record. Both run the same MCK suites, and the PRD publishes both results.
