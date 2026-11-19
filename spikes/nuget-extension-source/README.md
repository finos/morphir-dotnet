# Spike: NuGet as an extension source, under Native AOT

The question: which Microsoft NuGet client packages can a NuGet extension source provider use in the Native AOT
`morphir` binary, and what must the provider supply itself? AOT support is a requirement, not a preference. Where a
NuGet package works under AOT, the provider uses it, so that its behavior stays the same as NuGet's.

The spike is not in `Morphir.slnx`. Its `Directory.Build.props` and `Directory.Build.targets` keep the repository's
build settings, central package management and lock files out. It restores from nuget.org only, so Renovate can
restore it with the root `NuGet.config`.

| File | What it does |
| --- | --- |
| `SampleTool/` | A fixture: a .NET tool `Acme.Morphir.Python` 1.2.3, packed as RID-specific tool packages (`osx-arm64`, `linux-x64`). It has the package types `DotnetTool` and `MorphirExtension`, and carries `morphir/release.json` and a WASM module. |
| `NuGetProbe/` | An F# console, published as Native AOT. Each step runs one NuGet client operation and prints `PASS` or `FAIL`. `-p:ProbeLevel=` selects the packages: `baseline` (none), `core` (`NuGet.Versioning`, `NuGet.Packaging`, `NuGet.Configuration`), `full` (core and `NuGet.Protocol`, `NuGet.Credentials`), `rooted` (full, with `NuGet.Protocol` and `Newtonsoft.Json` kept whole by the trimmer), `stj` (full, with the `NuGet.UseSystemTextJsonDeserialization` feature switch on and trimmed). |
| `run.sh` | Packs the fixture into a folder feed, publishes the probe at each level (a clean publish each time), and writes `results/<rid>.md`: binary size, trim and AOT warnings, and the probe output. `LEVELS="core stj"` selects levels. |

```console
./spikes/nuget-extension-source/run.sh <scratch directory> [--online]
```

`--online` adds the nuget.org steps. `CREDPROVIDER=<folder>` adds two runs with a real credential provider plugin;
the folder is `plugins/netcore/CredentialProvider.Microsoft` from an extracted
[Azure Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider/releases) release. The signature steps read `nuget.versioning.7.9.0.nupkg` from the local NuGet
cache (`~/.nuget/packages`); a restore of this spike puts it there.

## Results on osx-arm64 (14 November 2026)

SDK 11.0.100, NuGet client packages 7.9.0. Full output: [results/osx-arm64.md](results/osx-arm64.md).

| Level | Binary | Trim/AOT warnings | Steps |
| --- | --- | --- | --- |
| baseline | 3.3 MiB | 0 | 1/1 pass |
| core | 9.8 MiB | 0 | 8/8 pass |
| full | 18.3 MiB | 119 | 11/13 pass: HTTP download and registration fail at run time |
| rooted | 21.9 MiB | 124 | 12/13 pass: registration fails at run time |
| **stj** | **14.2 MiB** | **0** | **13/13 pass, and the credential provider plugin runs** |

### The feature switch

`NuGet.Protocol` 7.9.0 has two JSON paths: Newtonsoft.Json, and System.Text.Json behind the AppContext switch
`NuGet.UseSystemTextJsonDeserialization`. The switch is declared with `FeatureSwitchDefinitionAttribute`, so when the
application sets it as a trimmed runtime option, the trimmer removes the Newtonsoft path:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="NuGet.UseSystemTextJsonDeserialization" Value="true" Trim="true" />
</ItemGroup>
```

The .NET SDK does the same kind of thing: `sdk/11.0.100/libdotnet-aot.dylib`, the AOT-compiled part of the `dotnet`
CLI, contains `NuGet.Configuration`, the `NuGet.UseSystemTextJsonDeserialization` switch name, and its own
source-generated `NugetServiceIndex` JSON context. It contains no Newtonsoft.Json.

### What works under AOT with no warnings (stj)

- `NuGet.Versioning`: parse, normalize and compare versions, and version ranges.
- `NuGet.Configuration`: the `nuget.config` hierarchy, enabled sources, package source mapping, and clear-text
  `packageSourceCredentials`.
- `NuGet.Packaging`: `PackageArchiveReader` and `NuspecReader` (id, version, package types, files, entry streams);
  reading `DotnetToolSettings.xml` from the package with `System.Xml.Linq`; signature reading
  (`GetPrimarySignatureAsync`, the repository countersignature, `ValidateIntegrityAsync`); and full signature
  verification with `PackageSignatureVerifier` and the default verify policy, which includes X.509 chain building.
- `NuGet.Protocol`: a local folder feed (`LocalV2FindPackageByIdResource`); the nuget.org V3 service index; the flat
  container version list and `.nupkg` download (`HttpFileSystemBasedFindPackageByIdResource`, which reads the
  repository signature resource first), with the same bytes as the local NuGet cache; and registration metadata
  (`PackageMetadataResource`).
- `NuGet.Credentials`: `SecurePluginCredentialProviderBuilder` and `CredentialService` with a real credential
  provider plugin: the Azure Artifacts Credential Provider v2.0.6 (osx-arm64 release). The probe started the plugin,
  ran the plugin protocol handshake and the credential request, and got back the credential that
  `VSS_NUGET_EXTERNAL_FEED_ENDPOINTS` gives for the test endpoint (about 0.1 to 0.25 s). It works both ways NuGet
  finds a plugin:
  - a `.dll` in `NUGET_PLUGIN_PATHS`: NuGet starts it with `dotnet`, so `dotnet` must be on `PATH`;
  - an executable named `nuget-plugin-*` on `PATH`: NuGet starts it directly, with no `dotnet`.

  A native executable in `NUGET_PLUGIN_PATHS` does not work: NuGet starts it with `dotnet`, and it exits with code
  1. That is NuGet's rule, the same in the `dotnet` CLI.

### Without the switch (full and rooted)

- 115 of the 119 warnings come from `Newtonsoft.Json`. The other 4 come from `NuGet.Protocol`: the service index,
  the repository signature resource and the plugin messages, all through Newtonsoft.
- The HTTP download fails: Newtonsoft cannot find the trimmed constructor of `RepositoryCertificateInfo`.
  Registration fails in the same way (`RegistrationIndex`).
- With the two assemblies rooted, the download runs, but registration fails on a type in `NuGet.Packaging`, and the
  warnings stay. That is not AOT support.

### What the fixture showed

- `dotnet pack` accepts the package types `DotnetTool;MorphirExtension` together, and `dotnet tool install`
  installs and runs the tool from them. The RID packages get the type `DotnetToolRidPackage`.
- The main package's tool settings are at `tools/any/any/DotnetToolSettings.xml`, and they list the RID packages in
  `<RuntimeIdentifierPackages>`. The tool payload is at `tools/net11.0/<rid>/`.
- One `dotnet pack` packs the main package and every RID package, and `None Pack="true"` items go into **every**
  package. So each RID package also carries `morphir/release.json` and the WASM module. Also, a `treeSha256` for a
  RID package is known only after that package is packed. A publisher therefore needs two passes: pack the RID
  packages, compute their tree digests, then pack the main package with the finished `release.json`.
- Entries outside the payload root in a RID package: `_rels/.rels`, the `.nuspec`, `[Content_Types].xml`,
  `package/services/metadata/core-properties/*.psmdcp`, and the duplicated `morphir/` files. A signed package also
  has `.signature.p7s`.

## Conclusion for the design

- Use `NuGet.Versioning`, `NuGet.Packaging`, `NuGet.Configuration`, `NuGet.Protocol` and `NuGet.Credentials`, and set
  `NuGet.UseSystemTextJsonDeserialization` as a trimmed runtime option in every AOT host (`morphir`). Together they
  add about 11 MiB to an AOT binary.
- A host that leaves the switch off gets 119 warnings and fails at run time. 33 of them are IL3050, which the AOT
  publish of `src/Morphir` already treats as errors, so today a missing switch fails that publish. The IL2xxx trim
  warnings are not errors there yet.
- Credential provider plugins work through `NuGet.Credentials`; the provider need not implement the plugin protocol.
- Not yet run: this spike on Linux or Windows.
