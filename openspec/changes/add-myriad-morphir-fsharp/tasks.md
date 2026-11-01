# Tasks

Implementation and local verification are complete. Verification evidence appears below. Follow [the design](design.html) and both [transpilation](specs/morphir-fsharp-transpilation/spec.md) and [integration](specs/myriad-model-generation/spec.md) contracts. Within every behavior task, first run a failing test, implement the minimum change, then refactor with green tests. Add BDD scenarios before implementing new user-visible behavior. Group numbers describe dependencies, not parallel agent assignments.

## 1. Establish the first packaged model-to-module build

- [x] 1.1 Reconcile `docs/design/PRD-fsharp-backend.md`, its status table and the PRD index with this delivery's v1-v3, Myriad-first and per-module scope; verify they link this change, preserve deferred roadmap features and record real implementation status. Link a tracking issue if one is available without publishing new issues implicitly.
- [x] 1.2 Add a Reqnroll consumer-build scenario for a tiny non-empty v3 car rental model with one module and an inventory or daily-rate function; run it and record failure because the real plugin/package does not yet exist. Assert the consumer's executed result, not just an output file's existence.
- [x] 1.3 Add `Morphir.CodeGeneration.FSharp`, `Morphir.Myriad`, `Morphir.Myriad.Build` and corresponding test projects to `Morphir.slnx` and applicable Nuke project selection; verify restore/build and the centrally pinned Myriad.Core, matching host/SDK, Fabulous.AST and Fantomas dependency graph. Keep build-task dependencies isolated and do not change internal generator behavior.
- [x] 1.4 Implement only the v3 decode, single-module plan, literal/function emission, `.json` Myriad adapter with `MorphirModule` selection, and coordinator/package assets needed for 1.2; locally pack and build a clean consumer using `MorphirModel` once. Verify the real host loads packaged dependencies and the original scenario passes without source-tree DLL references.
- [x] 1.5 Establish `examples/car-rental/` and `docs/content/docs/getting-started/morphir-to-fsharp.md` with exact package/host versions, local-feed setup, required runtime references, supplied JSON, `MorphirModel` configuration and the first generated inventory or daily-rate call; verify a clean copy builds and prints the documented result. Record the proven host executable resolution and plugin output contract in the package README.

## 2. Decode complete classic library models

Depends on group 1. Extend decoding before broadening emission, following IR-1.

- [x] 2.1 Add non-empty canonical v1/v2/v3 fixtures with equivalent definitions; test version dispatch and Library envelope decoding before implementing the missing branches. Verify fixtures against the repository's matching schemas and assert equivalent decoded models without relying on the empty verification fixtures.
- [x] 2.2 Add failing decoder tests for module/package definitions, access control, aliases, custom types and every classic type/pattern variant, then implement the missing codecs with existing primitive decoders; verify nested and malformed-node cases preserve qualified names and locations.
- [x] 2.3 Add failing decoder tests for every classic value variant, typed attributes and value definitions, then implement them; verify non-empty models decode through distribution level and duplicate map keys are diagnosed before map construction.
- [x] 2.4 Add BDD rejection scenarios and focused tests for missing/string/fractional/invalid versions, v4+, malformed JSON, document trees and non-Library distributions; implement stable typed diagnostics and verify no input falls back to v3 or produces successful partial output. Document the supported input envelope and failures beside the example.

## 3. Plan separate module files and declaration dependencies

Depends on group 2. Follow EMIT-1 and MAP-1.

- [x] 3.1 Add failing planner tests and expand the car rental example to multiple model modules, then implement a deterministic module manifest with dependencies before dependents; verify distinct paths and compilation in manifest order, retaining the nested `Domain.Types`/`Domain.Rules`/`Other.Types` fixture as a path-specific test. Document the car rental generated directory layout.
- [x] 3.2 Test lexical/qualified naming, F# keywords, canonical-name collisions, path traversal, duplicate model outputs and case-insensitive filename collisions before implementing escaping and path validation; verify generation either preserves reference identity or returns the conflicting symbols without writing outside its output root.
- [x] 3.3 Add failing tests for forward references, recursive type groups, recursive functions and cyclic module graphs; implement declaration ordering and supported within-module recursive groups. Verify valid fixtures compile and cross-module cycles fail with their dependency path. Document the cross-file recursion limit.
- [x] 3.4 Test empty libraries and empty modules before completing their output handling; verify an empty library has no generated files and an empty module produces exactly one compilable file. Add reviewed Verify.Expecto snapshots for these layouts.

## 4. Emit classic type definitions

Depends on group 3. Follow MAP-1 with Fabulous.AST computation expressions and fixed Fantomas configuration.

- [x] 4.1 Test and implement aliases, generic variables, type references, tuples, function types and unit one behavior at a time; verify generated F# compiles and review Verify.Expecto snapshots. Add a documented generated-type example.
- [x] 4.2 Test and implement closed named records, anonymous structural records and custom unions with nullary and multi-argument cases; verify construction, signatures and type parameter preservation in compiled consumer tests and reviewed snapshots.
- [x] 4.3 Test access-control preservation, private constructors, recursive types and open extensible-record rejection before implementing them; verify supported declarations compile, a separate assembly cannot access private model declarations, and open rows produce a location-bearing diagnostic. Document visibility rules and the open-record limitation.

## 5. Emit executable values and patterns

Depends on group 4. Add failing unit/compiler tests and corresponding BDD examples before each new behavior.

- [x] 5.1 Implement literals, variables, constructors, tuples, lists and unit with invariant literal formatting; verify compiled results for Unicode/escaping, decimal/float literals and integer boundaries, and review generated snapshots. Document numeric representation and rejected out-of-range literals.
- [x] 5.2 Implement curried application, partial application, lambdas, let bindings and local recursion; execute compiled fixtures to verify lexical shadowing, recursion and argument order, and add an example of a generated callable function.
- [x] 5.3 Implement every classic pattern family, destructuring, conditionals and pattern matching; execute branch-selection and binding tests, preserve ordered case lists and review snapshots. Document a pattern-matching example.
- [x] 5.4 Implement closed-record construction, field access, field functions and immutable updates using expected type information; verify named and anonymous record consumers compile, updates leave inputs unchanged and unsupported cases fail without placeholder expressions. Add a record-operation example.

## 6. Resolve the initial SDK mapping

Depends on group 5. Follow MAP-1 without implementing an unrelated SDK expansion.

- [x] 6.1 Test and implement explicit mappings for Basics Int/Float/Bool/Order, String, Char, List, Maybe and Result types/constructors; verify representative consumers compile, `Result<int, string>` preserves Morphir success/error meaning, and int64 literals are never narrowed silently. Publish the exact mapping table and required runtime packages.
- [x] 6.2 Test and implement add/subtract/multiply/divide, equality/ordered comparisons, List.map, Maybe.map and Result.map; execute fixtures covering currying and numeric behavior rather than assuming matching names imply matching semantics. Verify the documented operation table matches the implemented registry.
- [x] 6.3 Add BDD and unit tests for unmapped SDK operations, unresolved local references and specification-only external dependencies, then implement failure reporting; verify diagnostics include category, model path and FQName, and no output substitutes `obj`, default values or runtime failure stubs. Add troubleshooting examples for these failures.

## 7. Complete module build lifecycle and output publication

Depends on groups 3-6, extending the coordinator established in group 1. Follow BUILD-1 and BUILD-2.

- [x] 7.1 Test and finish real Myriad module selection and multi-module coordinator invocations; verify each invocation returns only its selected module, invalid selectors fail, generated Compile items precede handwritten consumers in manifest order, and JSON never becomes an F# Compile input. Document direct-host selection and the single model configuration.
- [x] 7.2 Add BDD tests for adding/removing modules, unchanged repeat builds and changed function bodies; implement staged publication, authoritative manifests and owned obsolete-file cleanup. Verify unchanged bytes, changed executable behavior, no duplicate declarations and no files removed outside the previous owned manifest.
- [x] 7.3 Test missing inputs, invalid JSON and v4+ after a successful build, plus a failure in one of several modules; finish failure propagation and verify CoreCompile does not consume stale or partially published output. Check plugin diagnostics use stderr and document how failed generation appears in a build.
- [x] 7.4 Test project paths containing spaces, invocation from another working directory, two models with identical basenames and concurrent configuration/target-framework outputs; implement project-relative resolution and publication isolation. Verify output ownership, clean behavior and stable paths, and document any supported output-directory override.
- [x] 7.5 Test deterministic output with reordered unordered maps, different cultures and relocated input directories; verify all module source bytes and manifest order remain stable while branch/argument order is preserved. Complete `docs/content/docs/guides/myriad-fsharp.md` with the plugin/build reference, mappings, output order, regeneration, runtime/version requirements, limitations, troubleshooting and ordinary versus design-time build behavior; verify its configuration snippets against the packaged consumer.

## 8. Deliver the complete car rental example and tutorial

Depends on groups 1-7. Follow EX-1 and DOC-1. Business logic belongs to the supplied Morphir model and its generated F#; the handwritten consumer only supplies inputs, sequences calls and displays results.

- [x] 8.1 Write and run failing Reqnroll scenarios for the complete rental lifecycle and each rejection before completing the sample model; assert inventory availability, confirmed reservation, active pickup, completed return, a 15000-cent invoice for three days at 5000 cents/day, and unchanged state on invalid operations.
- [x] 8.2 Complete equivalent v1/v2/v3 car rental model JSON fixtures and the thin consumer under `examples/car-rental/`, with Domain, Inventory, Reservation, Pickup, Return, Billing and Workflow modules, readable rule explanations and fixture provenance/update instructions; verify all three versions generate seven module files, compile via locally packed packages and pass 8.1 without handwritten business-rule replacements.
- [x] 8.3 Implement the model's typed-error paths for unavailable cars, pickup without a confirmed reservation, return without an active rental, nonpositive durations, billing before return and duplicate billing, starting with failing sample tests; verify each error preserves state and document one-active-reservation, captured-rate, whole-day and integer-cent assumptions beside the example.
- [x] 8.4 Write `docs/content/docs/guides/car-rental-tutorial.md` covering model JSON/rules, setup, generation, the seven module files, inventory/reservation/pickup/return/billing calls, expected state changes, invoice calculation and rejected operations; verify its commands and expected results against a clean example copy and distinguish business errors from generation failures in its troubleshooting section.
- [x] 8.5 Add the tutorial's model-edit exercise and automated check: change the sample daily rate from 5000 to 6000 cents at the documented JSON location, rebuild and run a fresh three-day rental; verify the invoice changes from 15000 to 18000 cents without editing generated F# or moving business rules into the driver.
- [x] 8.6 Finish and cross-link the package README, reference guide, getting-started article, full tutorial and example README using the documentation site's existing front matter and navigation; verify all links resolve, the site builds and the getting-started commands still run without a separate Morphir frontend. Keep command/output checks alongside the example.

## 9. Validate the complete delivery

Depends on groups 1-8. These checks exercise integration across the completed slices.

- [x] 9.1 Run the complete non-empty v1/v2/v3 compatibility matrix through locally packed packages in clean consumers; verify output compiles, documented functions execute, all promised construct families are covered and the README, reference, getting-started and car rental tutorial reproduce their expected results, including the full lifecycle and rate-change exercise.
- [x] 9.2 Run the repository's actual format, TUnit/Expecto, Reqnroll and IR contract/roundtrip commands plus coverage collection; verify all pass, existing generator and codec behavior remains green, new backend coverage meets project requirements and every snapshot change has been reviewed. Use existing wrapper scripts where present and equivalent configured commands where absent.
- [x] 9.3 Publish a representative generated consumer with `PublishTrimmed=true` and `PublishAot=true`, then run it; verify expected behavior and resolve generated-code trimming/AOT issues. Review package contents, contribution notices and dependency licenses, and update PRD status to reflect verified implementation rather than planning completion.
- [x] 9.4 Run `openspec validate add-myriad-morphir-fsharp --strict` and the repository's OpenSpec workflow checks; verify all contracts match the delivered behavior and all task evidence is recorded before proposing archive.

## Workflow follow-up

- Review this proposal before starting the apply workflow. Proposal creation does not implement or publish the plugin.
- Follow AGENTS.md for PR review, explicit merge/release authorization, spec synchronization and archiving once implementation is verified.

## Implementation evidence

- 1.1: Reconciled the existing backend PRD and PRD index with this delivery, preserving later roadmap features and recording current work.
- 1.2: `dotnet run --project tests/Morphir.Myriad.Integration.Tests -- --no-progress` ran one Reqnroll scenario and failed at `dotnet pack` because `src/Morphir.Myriad/Morphir.Myriad.fsproj` did not exist. The scenario asserts the generated daily-rate result from a clean package consumer.
- Groups 1-6: The solution builds with the new projects. Backend tests cover classic versions, every supported construct family, visibility across assemblies, recursion and dependency order, SDK mappings, explicit rejection, deterministic output and reviewed source snapshots. The suite has 43 passing tests and 92.6% backend line coverage. Private-union representation and nested generic-record tests caught and verified emission corrections; a renderer error-path regression confirms source-bound diagnostics instead of leaking formatter exceptions.
- Groups 2 and 8: AJV draft-07 validation passes for the non-empty car rental fixtures in versions 1, 2 and 3 and the shared daily-rate fixture against the matching repository schemas. Unit attributes use schema-compatible objects; typed record metadata carries an explicit type hint. F# authoring scripts reproduce the checked-in models.
- Group 7: Four adapter tests and 11 F# package-consumer checks pass. The latter use an isolated NuGet cache and verify first-build host availability, edits, module additions/removals, unchanged source/manifest timestamps, missing/invalid models, paths with spaces, output ownership, symlink rejection, concurrent configurations and cleanup.
- Group 8: Twelve Reqnroll scenarios pass through real locally packed plugin consumers. They execute the rental lifecycle for all three classic versions, verify seven business rejections and the 15000-to-18000-cent rate exercise on an existing consumer, and prove invalid-input rebuilds cannot compile stale output. Full IR documents are loaded from embedded resources, not inline JSON strings.
- Documentation: The getting-started commands run the supplied example. Hugo Extended builds the site successfully with the new reference, getting-started article and end-to-end tutorial. Shared fixture resources and loaders are documented under `tests/Fixtures/`.
- Native output: `dotnet publish` with `PublishTrimmed=true` and `PublishAot=true` produces an executable on osx-arm64 that prints the expected seven output lines. The driver uses explicit Console formatting after a failing AOT run exposed generic F# printf reflection. FSharp.Core dependency-level trim/AOT analysis warnings remain; the generated model executes successfully.
- Existing verification: The Core, Tooling, Internal.CodeGeneration, Models, IR.Pipeline, SDK, Configuration, IR.Pipeline.Plugins and Live executable suites pass. The repository's plain `dotnet test` command hits its existing mixed TUnit/.NET 10 runner incompatibility, so configured executable/Nuke runners are used. The existing `Morphir.Tests` placeholder contains no tests and is not counted as passing coverage.
- Build integration: Default Nuke `Test` runs the existing suites, Myriad backend/adapter/BDD tests, then cold package checks. `TestAll` reuses this execution without duplicating the cold suite; `TestMyriadPackages` also exposes those checks separately. Existing dependency/advisory warnings are preserved rather than changing unrelated version pins.
- BDD artifact reuse: Parallel scenarios share one lazily built package per test run. This removes concurrent restores against the source projects, fixes the observed `project.assets.json` race and avoids rebuilding the same package twelve times. Consumers and model mutations remain isolated per scenario.
- Final gates: Nuke `Test` passes with restore and solution compilation. Strict OpenSpec change/schema validation and shared-workflow ownership checks pass; both shared checker tests pass. The documented local package, example run, native AOT run and Hugo build pass on the final delivery. No publication, merge or archive was performed.
- PR preparation: Rebased onto `main` at `5404cb5`, refreshed the new package lock files for the current dependencies (including FSharp.Core 11.0.100), and aligned bundled-component documentation. Nuke `Test`, C# formatting, pinned Fantomas checks, strict OpenSpec validation, shared-workflow checks, both checker tests and Hugo pass again. The documented `0.1.0-local.5` package runs the example and publishes a working trimmed Native AOT consumer on osx-arm64. The 92.6% coverage measurement predates this dependency refresh; backend source is unchanged.
