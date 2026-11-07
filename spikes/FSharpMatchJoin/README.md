# F# match-range join design probe

Run `dotnet run --project spikes/FSharpMatchJoin -- --sequenced` from the repository
root. The command deliberately exits with failure: it checks the proposed FSF-2
range-join invariant, which the compiler does not preserve.

This project is not a product or a CI test suite. It is kept outside the solution
and the Nuke test target so that the failed assumption stays reproducible without
presenting it as implemented frontend behavior. Its FCS package override is local
to the probe. The known FSharp.Core dependency warning is recorded in the
[findings](../../openspec/changes/add-fsharp-compile/spikes/match-range-join.md).

The normal frontend test project owns the pinned-input provenance tests. No
production frontend, compiler-service upgrade or lowering strategy is approved
by this probe.
