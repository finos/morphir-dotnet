# Route configured WASM extensions through product commands

## Why

#570 supplies the runtime but normal CLI commands do not select it. Alpha.5 must
let a user point `[extensions.python].path` at the published Python 0.6.0 bundle
and compile Python or generate Python without using a worker or development kit.

## What Changes

- Select the named configured WASM module for Python compile/generate requests.
- Reuse the existing MEP model adapters and normal publication paths over an
  owned in-process WASM session, preserving process-extension selection.
- Document exact experimental commands and the Windows x64 unavailability reason.
- Test configuration selection first and run the real digest-pinned bundle through
  both product commands. Generate is the first landing cut if both exceed five hours.

## Capabilities

### New Capabilities

- `wasm-product-commands`: configured module selection and execution by normal
  compile/generate commands, including session ownership and named failures.

### Modified Capabilities

None. Existing runtime, model and publication contracts are reused.

## Tracking

Depends on #570 and follows #564. The maintainer explicitly authorized source and
tests on 12 November 2026. Generic-kit/report/qualification work remains deferred.

## Impact

Tooling settings/connectors and the existing MEP adapter session boundary, focused
tests and the experimental user guide. No production download, new package,
protocol, security setting, conformance checker or generator implementation.
