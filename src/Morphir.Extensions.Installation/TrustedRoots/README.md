# NuGet trusted roots

`codesignctl.pem` and `timestampctl.pem` are the Microsoft-maintained code signing and timestamping certificate trust
lists that the .NET SDK ships for NuGet signature verification on Linux and macOS. They are byte-exact Git blobs of
[dotnet/sdk](https://github.com/dotnet/sdk/tree/b8600c8854f5d1ac1442d717211d968f69a53acd/src/Layout/redist/trustedroots),
tag `v11.0.100`, commit `b8600c8854f5d1ac1442d717211d968f69a53acd`, under the MIT license:

| File | Git blob | SHA-256 |
| --- | --- | --- |
| `codesignctl.pem` | `021bd8997f77389801dc6e9445e4d802b2ee47df` | `aab671f52e5229906b2100727370007ef4b6d2e360b23f2cf12a6d87773be611` |
| `timestampctl.pem` | `7bb520e98647d283dba9b7972d1159a29044cb32` | `5ccb03367b52f047099f07e1653160e8578a83711cb18560c979febb65f8cb5d` |

NuGet reads these lists only from a `trustedroots` folder in `AppContext.BaseDirectory`. The Native AOT `morphir` is
one file, so the library embeds them, writes them to `<home>/tmp/morphir-dotnet-nuget-trust/trustedroots/`, and points
the base directory there only while `X509TrustStore.InitializeForDotNetSdk` builds its chain factories
(`NuGetTrustRoots` in `NuGetProvider.fs`). Without them, NuGet on Linux has no trusted root for timestamps (and, with
no system code signing bundle, none for signatures), so a `require` policy refuses every nuget.org package.

Refresh them with the .NET SDK version that `global.json` pins: copy each blob with `gh api` (not a local SDK copy,
whose line endings can differ), and update the table.
