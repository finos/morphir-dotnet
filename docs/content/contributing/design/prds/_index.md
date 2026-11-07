# Product requirements

| PRD | Status | Phase | Current task |
| --- | --- | --- | --- |
| [F# source frontend and compile](./fsharp-source-frontend.md) | In Progress | Tracking and acceptance | #499; compile acceptance and native compilation contracts |
| [C# generation CLI](./integrate-csharp-generation-cli.md) | Implementation landed | Specification archive | #482 squash landed; 9/9 tasks, both deltas synced and archived; archive follow-up landing and coordinator cleanup pending, [#481](https://github.com/finos/morphir-dotnet/issues/481) |
| [C# code generation](./add-csharp-code-generation.md) | Implementation landed | Archive follow-up | #478 squash landed; 13/13 tasks, spec synced and archive prepared; follow-up landing and coordinator cleanup pending, #474 |
| [Immutable C# SDK](./add-immutable-csharp-sdk.md) | Implementation and archive landed | Cleanup pending | #470 and archive #472 landed; implementation worktree/artifacts removed; empty archive directory cleanup pending |
| [.NET SDKs and C# generation](dotnet-code-generation.md) | In Progress | C# archive follow-up | Generator #478 and SDK archive #472 landed; generator archive landing and coordinator cleanup pending; CLI undispatched |
| [Establish .NET SDK conformance](establish-dotnet-sdk-conformance.md) | In Progress | Cleanup pending | #462 and archive #463 landed; coordinator retains policy-blocked directory cleanup, #460 |
| [Introduce F# SDK package](introduce-fsharp-sdk-package.md) | Implementation and archive landed | Cleanup pending | #469 and archive #472 landed; implementation worktree/artifacts removed; empty archive directory cleanup pending |
| [CLI model generation](cli-model-generation.md) | Completed | Delivered | Repository review |
| [Morphir extension model](morphir-extension-model.md) | Approved | Roadmap | Built-in, WASM and process extensions on MEP; step 0 and the spikes not started |
| [Morphir application architect skill](morphir-application-architect-skill.md) | Draft | Design | Skill proposal |
| [morphir-dotnet as the default Morphir CLI](morphir-dotnet-default-cli.md) | Draft | Proof criteria | ADR 0001 proposed; first proof point is add-fsharp-compile |

The documentation site's existing [PRD index](../../../docs/contributing/design/prds/_index.md) includes the wider historical feature inventory.
