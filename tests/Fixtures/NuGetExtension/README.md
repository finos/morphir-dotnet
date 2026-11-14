# NuGet extension fixture

`feed/` is a NuGet folder feed with a Morphir extension packed as a RID-specific Native AOT .NET tool, as
[Publish an extension as a NuGet package](../../../docs/content/docs/guides/publish-extension-nuget.md) says:

- `Acme.Morphir.Tool.1.0.0.nupkg`: the main package, with the package types `DotnetTool` and `MorphirExtension` and
  `morphir/release.json`.
- `Acme.Morphir.Tool.linux-arm64.1.0.0.nupkg`: the RID package, with the tool under `tools/any/linux-arm64`.

`build.sh` packs `AcmeTool/` in two passes in a Linux arm64 .NET SDK 11 container, computes the tree digest with the
shell function of the guide, checks that the second pass keeps the tree, and checks that `dotnet tool install`
installs the same packages. The tests read the checked-in packages and never run the script:

```bash
docker run --rm -v "$PWD/tests/Fixtures/NuGetExtension:/fixture" -w /fixture mcr.microsoft.com/dotnet/sdk:11.0 sh build.sh
```
