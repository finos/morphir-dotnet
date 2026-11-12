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

To prepare an installable package declaration, retain the actual describe response and author a release descriptor
in the existing extension family using that provider's identity, version and capability claims. Embedded claims omit
the MEP `claimsVersion` wrapper. Do not fill missing claims from assumptions about the implementation. In particular,
the describe flags must agree with the flags actually reported at initialize.

Run the focused preparation target against that descriptor and the complete published folder and archive:

```powershell
dotnet run --project build/_build.csproj -c Release -- --target PrepareExtensionProcessPackage --rid win-x64 `
  --extension-release-template local/frontend-release-template.json `
  --extension-published-folder artifacts/extension-processes/morphir-fsharp-frontend/win-x64 `
  --extension-archive artifacts/extension-processes/morphir-fsharp-frontend-<build-version>-win-x64.zip `
  --extension-entry-point morphir-fsharp-frontend.exe `
  --extension-package-output local/frontend-package
```

The output directory must be new. It contains `release.json` and `manifest`. `ExtensionPackageProducer` feeds actual
file paths, lengths, hashes and the archive writer's executable declarations into the installer's canonical manifest
writer. It binds that manifest's digest separately from the delivered archive digest, removes any enclosing package
root, preserves release identity and unknown optional metadata, and validates the prepared descriptor with the same
package reader and executable-declaration checks used during installation. The backend uses the same target with its
own descriptor, folder, archive and `morphir-fsharp-backend.exe` entry point.

Preparation does not assert that the folder and archive are an authenticated release or that a guest is ready.
Installation still validates the archive and its complete extracted tree against the declaration. Local developer
publication must retain its explicit consent and generated local provenance. Publishing a descriptor does not install
or select a provider, qualify a composed generation, or prove portable-lock offline restore. Those paths consume the
existing extension contracts, installer and manager.
