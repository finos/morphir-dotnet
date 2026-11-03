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

The runtime targets net10.0 and works with trimming and NativeAOT without
reflection or dynamic code discovery. Runtime targeting is separate from the
compiler SDK and numeric language version used by a consumer. Root global.json
is not changed for isolated compiler verification. Ordinary package consumption
must restore only Morphir.SDK.CSharp, without a project reference or repository
source files. This is separate from the deferred Roslyn generator adapter.

The same ordinary consumer source was restored from an isolated local feed,
compiled and executed under both exact toolchains below. Each fresh package
cache contained only Morphir.SDK.CSharp; neither consumer referenced repository
projects or source files.

| Numeric language version | SDK/channel | Exact C# compiler build |
| --- | --- | --- |
| 14.0 | 10.0.403, stable | 5.9.0-1.26523.112, revision 5a2162bea6845eabc1fc721624a43568c65db3b1 |
| 15.0 | 11.0.100-rc.2.26504.105, RC2 | 5.12.0-1.26504.105, revision 820230c386db5b0106c5430b3e5aee92a7826bf5 |

Both target net10.0 and execute with runtime 10.0.14. SDK selection is disabled
from rolling forward in each isolated global.json. The RC toolchain uses its
matching 10.0.13 reference packs for net10.0 compilation; these tooling inputs
are separate from package dependencies. The RC ZIP was checked against
[Microsoft's release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json).
No root global.json, user setting or machine setting changed.

The same public consumer was also published and executed as self-contained
trimmed and NativeAOT applications on Windows arm64, RID win-arm64, using SDK
10.0.403 and runtime 10.0.14. Both passed the ordinary consumer's assertions and
produced the same output. Trim and AOT warnings were treated as errors. The
NativeAOT executable is an ARM64 native PE without a CLR header or managed
Consumer.dll/coreclr.dll payload. This verifies that representative runtime
consumer on that OS/RID, not every deployment target or every generic closure.

Reproduce with the checked-in PowerShell harness and installed exact toolchains:

```powershell
& tests/Morphir.SDK.CSharp.Tests/verify-package-consumers.ps1 `
  -EvidenceDirectory C:/Temp/owned-fresh-sdk-proof `
  -StableDotnet C:/path/to/stable/dotnet.exe `
  -RcDotnet C:/path/to/isolated-rc/dotnet.exe
```

The RC directory needs its matching net10.0 targeting packs. NativeAOT needs
[Microsoft's native prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).
The harness uses fresh local feeds/caches and process-local environment values,
runs commands serially with bounded parallelism, and preserves logs on failure.
SDK/compiler versions and numeric language versions are separate settings. This
proof does not require native C# unions or implement the deferred source generator.
