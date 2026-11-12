# Packaging the F# process providers

The frontend and backend have separate self-contained process deliveries. Each folder includes its executable,
managed dependencies and .NET runtime. Preserve the complete folder when distributing or installing it. The frontend
also needs its compiler references; the backend needs its generator, Fantomas and Fabulous.AST dependencies.

Run the existing build targets for the required RID, for example:

```powershell
dotnet run --project build/_build.csproj -c Release -- --target PublishFSharpFrontendProcess --rid win-x64 --extension-processes-dir artifacts/extension-processes
dotnet run --project build/_build.csproj -c Release -- --target PublishFSharpBackendProcess --rid win-x64 --extension-processes-dir artifacts/extension-processes
```

Both targets use `PublishExtensionProcess` and the existing `ReleaseArchive` writer. They produce
`<output>/<command>/<rid>/`, an archive named `<command>-<build-version>-<rid>.zip` on Windows or `.tar.gz` elsewhere,
and a sibling `.packages.lock.json`. RID-specific restore locks stay under each project's `obj/publish-lock/<rid>/`.
Publication disables trimming, single-file output, ReadyToRun and Native AOT. The ordinary framework-dependent
dotnet tools remain available through `PackTool`.

The archive name uses the build's release version. Each guest reports its own assembly's release version through
`morphir.extension.describe`; record that actual response when preparing provider identities. These executables
serve MEP over stdio and do not provide a `--version` command.

Producing these folders and archives does not install or select a provider. It does not create extension package
manifests, establish publisher trust, or qualify a composed generation or portable-lock offline restore. Those paths
must consume the existing extension contracts, installer and manager. A producer readiness check alone cannot prove
installed inventory or generation selection.
