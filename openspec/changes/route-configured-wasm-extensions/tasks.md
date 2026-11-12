# Tasks

The 12 November maintainer instruction authorizes this product scope. The initial
test-first checkpoint is intentionally unexecuted until a Windows slot is granted.
Five hours is the landing target; generate may land first with compile tracked here.

## 1. Configured selection and generation

- [ ] 1.1 Run selection and real-bundle generate tests RED, implement configured WASM selection and shared backend session use, then verify the tests GREEN and existing process/built-in selection unchanged.
- [ ] 1.2 Add the experimental generate guide with exact pin/config/commands; verify its command in the real CLI test and remove the obsolete configuration-guide statement.

## 2. Python compilation

- [ ] 2.1 Run the real Python compile command RED, reuse the frontend adapter over the owned WASM session, and verify generated IR plus existing process frontend controls GREEN.
- [ ] 2.2 Document compile commands and verify named Windows x64 refusals for both commands, no partial publication and non-WASM controls unchanged.

## 3. Landing

- [ ] 3.1 Obtain the scoped F#/C# review, run affected validation under coordinator slots, and merge main normally after #570 lands; verify exact-head CI before landing.

## Workflow follow-up

- Leave generic-kit, report, qualification, packaging and Rust parity work deferred.
- Publish alpha.5 only through the separate release coordination.
