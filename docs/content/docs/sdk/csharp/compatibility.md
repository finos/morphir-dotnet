---
title: "C# SDK compatibility"
weight: 30
---

The bounded contract is `morphir-dotnet-sdk-v1`, schema 1, profile
`dotnet-sdk-v1`: 67 operations, 207 required observations and eight separately
reported representation exclusions. It does not claim the entire Morphir SDK.
The checked-in fixtures retain independent upstream source evidence. Runtime
test expectations are read from those fixtures, not computed by this runtime.

The excluded observations concern Int range or negative Int powers,
supplementary Char, and exact Decimal96 representation/nontermination limits.
The precise IDs and reasons remain in `tests/Fixtures/MorphirSDK/cases.json`.
Strings support well-formed UTF-16 including supplementary scalars. Char accepts
one non-surrogate BMP scalar. Dates, parsing, transcendental operations and
unlisted collection functions are outside this inventory.

The runtime targets net10.0 and is designed for trimming and NativeAOT without
reflection or dynamic code discovery. Runtime targeting is separate from the
compiler SDK and numeric language version used by a consumer. Root global.json
is not changed for isolated compiler verification. Ordinary package consumption
must restore only Morphir.SDK.CSharp, without a project reference or repository
source files. This is separate from the deferred Roslyn generator adapter.

Exact compiler versions, package-consumer and publication commands and measured
results are recorded in the feature PRD and the change's validation evidence as
those checks complete. Pending checks are not a compatibility certification.
