# Inventory of tests that start a process, run a build, or read a built artifact

Made on 2026-11-12 from `main` at `4d9e10a0` with `evidence/inventory.py`, a source scan of `tests/`. A file is here
when it has a test and it names a process runner (`CliTestHelper`, `ExecutableRunner`, `ChildProcess`,
`ProcessStartInfo`, `Process.Start`, ...), a `dotnet` build verb, or a built artifact (a package, an archive, an
output folder). The scan has false results, and they are marked.

## How to read the destinations

- **A black-box test moves.** It reaches the product only through a built artifact and needs no product type.
- **A build test stays in `Morphir.Build.Tests`.** Its subject is the build of the repository: project files,
  targets, workflows, the helpers of the build. A test of a packed package or an installed tool is not a build
  test: it is a black-box test of the delivered form, and it moves.
- **A library test stays.** It tests a library in the test process, also when the library starts a helper process.
- **A file with `Product types` = yes** opens a product namespace. It moves only after a report type of the testing
  library replaces that use, or it is split.

The destination of each row is provisional. The first task of each pull request confirms its rows with the
classification, from the tests that the runner discovers, and records a row that changes. The scan does not find a
step class that reaches the product only through a shared step class of its project (for example
`Morphir.E2E.Tests/Features/ExecutableIRVerificationSteps.cs`); the move table of a project is made from the
discovered tests of the complete project, so such a file is in it.

| Pull request | Project | File | Process | Build verb | Artifact | Product types | Destination |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `Morphir.Tooling.Tests` | `Features/Generate/CSharpCliGenerationTests.cs` | yes | no | yes | yes | Split: the artifact-only cases move (integration); the wire-form rows that call `CSharpGenerator.Generate` stay. |
| 1 | `Morphir.Tooling.Tests` | `Integration/CLI/CliVerifyIRIntegrationSteps.cs` | yes | no | no | no | Move: integration. |
| 2 | `Morphir.E2E.Tests` | `Features/AOT/AssemblyTrimmingSteps.cs` | yes | yes | yes | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/AOT/NativeAOTCompilationSteps.cs` | yes | yes | yes | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/ExecutableExtensionListSteps.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/ExecutableExtensionRuntimesTests.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/ExecutableExtensionWorkerSteps.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/ExecutableExtensionWorkerTests.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/ExecutableValidateSteps.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/Packaged/PackagedArtifactsSteps.Engine.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/Packaged/PackagedArtifactsSteps.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Features/SharedSteps.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Infrastructure/ExecutableRunnerTests.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `Infrastructure/WorkerCorpus.cs` | yes | no | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `InfrastructureTests/LocalDeveloperToolchainTests.cs` | yes | yes | yes | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `InfrastructureTests/PackagedArtifactTests.cs` | no | no | yes | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `InfrastructureTests/PackagedInstallationTests.cs` | yes | no | yes | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.E2E.Tests` | `InfrastructureTests/PackagedRunTests.cs` | yes | yes | no | no | Move. Classify each discovered case by purpose with the classification steps; the published targets are the targets of the run, not the group. A test of a helper goes to `Morphir.TestSupport.Tests` with the driver that replaces the helper. |
| 2 | `Morphir.Frontend.FSharp.Tests` | `CliAdapterTests.fs` | yes | no | yes | yes | Stays as a conformance test (it runs the kit against an executable). In pull request 2 it selects the executable with the target of the testing library, not `MORPHIR_DOTNET_TOOL_CLI` and `MORPHIR_AOT_CLI`. |
| 3 | `Morphir.Managed.Tests` | `ManagedEntryPointTests.cs` | yes | no | yes | yes | Split: the checks that need only the built entry point move, with `@host-managed` so they run the executable of `src/Morphir.Managed`; the checks that use product types stay. |
| 3 | `Morphir.Managed.Tests` | `ManagedMckRoutingTests.cs` | yes | yes | no | no | Move: integration (the `mck` routing of the managed host), with `@host-managed`. |
| 3 | `Morphir.Tests` | `Hosts/HostVersionTests.cs` | yes | no | no | yes | Split: `--version` on both built hosts moves (smoke); the parse with `ReleaseVersion.Create` stays. |
| 3 | `Morphir.Tests` | `Hosts/MckRoutingHarnessTests.cs` | yes | no | no | no | Move with `MckRoutingTests.cs`, or to `Morphir.TestSupport.Tests` if it tests only the harness. |
| 3 | `Morphir.Tests` | `Hosts/MckRoutingTests.cs` | yes | yes | no | no | Move: integration (the `mck` routing of the built `morphir` host). |
| 3 | `Morphir.Tooling.Tests` | `Features/Compile/CompileDocumentationTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Compile/CompileSteps.cs` | yes | no | no | no | Move: integration, or journey for a tutorial or documentation test. |
| 3 | `Morphir.Tooling.Tests` | `Features/Compile/CompileTutorialTests.cs` | yes | no | no | no | Move: integration, or journey for a tutorial or documentation test. |
| 3 | `Morphir.Tooling.Tests` | `Features/Configuration/ConfigurationEditingSteps.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Configuration/ConfigurationSteps.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/ConfiguredWasmCommandTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/ExtensionListCliTests.cs` | yes | no | no | no | Move: integration, or journey for a tutorial or documentation test. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/ExtensionRuntimesReportTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/ExtensionWorkerArgumentsTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/ExtensionWorkerRuntimeTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/ExtensionWorkerTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/WasmRuntimeAgreementTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Extensions/WorkerBuildIdentityTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Generate/CSharpGenerationSteps.cs` | yes | no | yes | no | Move: integration, or journey for a tutorial or documentation test. |
| 3 | `Morphir.Tooling.Tests` | `Features/Generate/GenerateCommandTests.cs` | yes | no | yes | no | Move: integration, or journey for a tutorial or documentation test. |
| 3 | `Morphir.Tooling.Tests` | `Features/Generate/GenerateDocumentationTests.cs` | yes | no | no | no | Move: integration, or journey for a tutorial or documentation test. |
| 3 | `Morphir.Tooling.Tests` | `Features/Generate/GenerateFormattingTests.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Generate/GenerateSteps.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Generate/GenerationReportingTests.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Generate/GenerationWireConformanceTests.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Mck/WasmHostCommandTests.cs` | yes | yes | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/ProjectMigration/ProjectMigrationSteps.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/Validate/ValidateCliTests.cs` | yes | no | no | no | Move: integration, or journey for a tutorial or documentation test. |
| 3 | `Morphir.Tooling.Tests` | `Features/Validate/ValidateDocumentationTests.cs` | yes | no | no | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 3 | `Morphir.Tooling.Tests` | `Features/WorkspaceMigration/WorkspaceMigrationSteps.cs` | yes | no | yes | yes | Move after its report types replace the product types that it uses to read output. A case that calls a product function as the expected value is split and stays. |
| 4 | `Morphir.Build.Tests` | `AssemblyVersionTests.cs` | no | no | yes | no | Move: end-to-end, in the `packages` lane. It needs only the packed packages, and it keeps its pull request run after `PackAll` on each operating system. |
| 4 | `Morphir.Build.Tests` | `LocalInstallationTests.cs` | yes | no | yes | no | Move: end-to-end, in the `packages` lane. It needs only the packed packages, and it keeps its pull request run after `PackAll` on each operating system. It installs, starts and uninstalls the tool. |
| 4 | `Morphir.Build.Tests` | `PackageDependencyTests.cs` | no | no | yes | no | Move: end-to-end, in the `packages` lane. It needs only the packed packages, and it keeps its pull request run after `PackAll` on each operating system. |
| 4 | `Morphir.Build.Tests` | `PackageMetadataTests.cs` | no | no | yes | no | Move: end-to-end, in the `packages` lane. It needs only the packed packages, and it keeps its pull request run after `PackAll` on each operating system. |
| 4 | `Morphir.Build.Tests` | `PackageStructureTests.cs` | no | no | yes | no | Move: end-to-end, in the `packages` lane. It needs only the packed packages, and it keeps its pull request run after `PackAll` on each operating system. |
| stays | `Morphir.Build.Tests` | `ToolPackageInstallTests.cs` | yes | no | yes | no | Stays as a build test: its subject is the script `.github/scripts/install-tool-package.ps1`, which it runs with synthetic `Probe.Tool` packages that hold a stub executable. It does not test a shipped package. |
| 4 | `Morphir.CodeGeneration.FSharp.Extension.Tests` | `PackageTests.fs` | yes | yes | yes | yes | Split, as the frontend extension `PackageTests.fs`. |
| 4 | `Morphir.Frontend.FSharp.Extension.Tests` | `PackageTests.fs` | yes | yes | yes | yes | Split: the package layout and install checks move (end-to-end); the comparison with the built guest version stays if it needs a product type. |
| 4 | `Morphir.Frontend.FSharp.Extension.Tests` | `SelfContainedTests.fs` | yes | yes | yes | yes | Split: the run of the published self-contained process moves (end-to-end); the in-process comparison stays. |
| 4 | `Morphir.Myriad.Integration.Tests` | `CarRentalSteps.cs` | yes | yes | no | no | Move: journey (pack the plugin, build a consumer, run it). |
| 4 | `Morphir.Myriad.SystemTextJson.Tests` | `BuildTests.fs` | yes | yes | yes | no | Move: integration (a fixture build with the plugin), with `IPackageDriver`. |
| 4 | `Morphir.SDK.Tests` | `PackageMigrationTests.fs` | yes | no | no | no | Move: journey (the SDK package graphs), with `IPackageDriver` and `--artifacts-path`. |
| 5 | `Morphir.Toolchain.Tests` | `GlobalizationTests.fs` | yes | no | no | no | Move every discovered case; none needs a product type. The case that runs the `morphirup` executable is smoke. The cases that read built assemblies with `PEReader` and `morphirup.runtimeconfig.json` are end-to-end tests of the build output, with no lane, on an artifact inspection driver. Task 9.1 states how the driver gets the `morphirup` executable. |
| stays | `Morphir.Build.Tests` | `BuildProfileTargetsTests.cs` | yes | yes | no | yes | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.Build.Tests` | `CliDeliveryWorkflowTests.cs` | no | yes | no | no | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.Build.Tests` | `DevelopmentWorkflowTests.cs` | no | yes | no | yes | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.Build.Tests` | `EntryPointGuardTests.cs` | yes | yes | no | no | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.Build.Tests` | `ExtensionProcessLockTests.cs` | yes | yes | no | no | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.Build.Tests` | `Helpers/MorphirupReleaseTests.cs` | no | no | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/MyriadPluginPackageTests.cs` | no | no | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/NativeCheckTests.cs` | no | yes | no | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/NativeNoticeDeclarationsTests.cs` | no | yes | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/PackageValidatorTests.cs` | no | no | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/PackagedContentsTests.cs` | no | no | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/PackagedEngineTests.cs` | no | yes | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/PathHelperTests.cs` | no | no | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/PublishedLayoutTests.cs` | no | yes | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/ReleaseArchiveTests.cs` | no | no | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/ReleaseProfilesTests.cs` | no | no | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `Helpers/ThirdPartyNoticesTests.cs` | no | yes | yes | yes | Stays. A unit test of a build helper, in the test process. |
| stays | `Morphir.Build.Tests` | `PublishLockRedirectTests.cs` | yes | yes | no | yes | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.Build.Tests` | `PublishingWorkflowTests.cs` | no | yes | no | no | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.Build.Tests` | `VersionSourceTests.cs` | yes | yes | no | no | Stays as a build test: its subject is the build of the repository (project files, targets, workflows), not a built artifact of the product. |
| stays | `Morphir.CodeGeneration.FSharp.Extension.Tests` | `ProcessTests.fs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.CodeGeneration.FSharp.Tests` | `TranspilerTests.fs` | yes | yes | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Compilation.Mep.Tests` | `MepFrontendTests.fs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Compilation.Tests` | `RegistryTests.fs` | no | yes | no | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Configuration.IO.Tests` | `ConfigurationRecoveryProcessTests.cs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Configuration.IO.Tests` | `ConfigurationTransactionTests.cs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Configuration.IO.Tests` | `WorkspaceMigrationTests.cs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Configuration.Tests` | `ResolutionTests.fs` | no | yes | no | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Conformance.Tests` | `ReportTests.fs` | no | yes | no | yes | Stays. A test of the kit library in the test process. |
| stays | `Morphir.Extensions.Installation.Tests` | `ArchiveTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Extensions.Installation.Tests` | `PrivateStagingTests.fs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Extensions.Installation.Tests` | `RunCopyTests.fs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Extensions.Installation.Tests` | `SharedReaderTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Extensions.Wasm.Tests` | `WasmtimeCApiPinTests.cs` | no | yes | no | no | Stays as a build test: it reads the pin file of the build. |
| stays | `Morphir.Frontend.FSharp.Extension.Tests` | `ProcessReplayTests.fs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Frontend.FSharp.Tests` | `CompilerEdgeTests.fs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Mep.Tests` | `ChannelTests.fs` | yes | yes | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Mep.Tests` | `ContractTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Mep.Tests` | `GenerateModelTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Mep.Tests` | `InteropTests.fs` | yes | yes | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Models.Tests` | `IR/V4/ModelTests.fs` | no | yes | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Models.Tests` | `Json/ClassicJsonTests.fs` | no | yes | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Models.Tests` | `Json/V4/V4JsonWriterTests.fs` | no | yes | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Myriad.Tests` | `Program.fs` | yes | yes | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.TestSupport.Tests` | `ChildProcessTests.cs` | yes | no | no | no | Stays. It tests the testing library. |
| stays | `Morphir.TestSupport.Tests` | `DocumentationExampleTests.cs` | yes | no | no | no | Stays. It tests the testing library. |
| stays | `Morphir.TestSupport.Tests` | `TestSandboxTests.cs` | yes | no | no | no | Stays. It tests the testing library. |
| stays | `Morphir.Toolchain.IO.Tests` | `AcquisitionTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Toolchain.Tests` | `BindingReviewTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Toolchain.Tests` | `CliTests.fs` | yes | no | yes | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Toolchain.Tests` | `CorpusModeTests.fs` | yes | no | no | no | Stays. It starts its own test assembly to test the corpus runner of the test project; it does not run a product executable. |
| stays | `Morphir.Toolchain.Tests` | `DryRunTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Toolchain.Tests` | `HardeningTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Toolchain.Tests` | `LocalCandidateTests.fs` | no | yes | yes | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Toolchain.Tests` | `LocalProcessOwnershipTests.fs` | yes | no | no | yes | Stays. It tests a library in the test process; the library or the test starts a helper process or MSBuild. It keeps the `PlatformSpecific` marker. |
| stays | `Morphir.Toolchain.Tests` | `PlanningTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Toolchain.Tests` | `PrimitiveTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Toolchain.Tests` | `ReleaseDescriptorTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Toolchain.Tests` | `TufTests.fs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Tooling.Tests` | `Features/Compile/CompileTargetTests.cs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Tooling.Tests` | `Features/Compile/Sources/MsBuildProjectFileProviderTests.cs` | yes | yes | yes | yes | Stays. It tests the project file provider in the test process; the provider runs MSBuild. |
| stays | `Morphir.Tooling.Tests` | `Features/Generate/ArtifactPathCheckTests.cs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Tooling.Tests` | `Features/Generate/GenerationExtensionSelectionTests.cs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Tooling.Tests` | `Features/Management/ArtifactPathResolverTests.cs` | no | no | yes | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Tooling.Tests` | `Features/TaskRecords/TaskPathsTests.cs` | no | yes | no | yes | Stays. The scan matched a path or a name in test data; the test starts no process and reads no built artifact of the product. |
| stays | `Morphir.Tooling.Tests` | `Infrastructure/BoundedProcessTests.cs` | yes | no | no | yes | Stays. It tests the bounded process runner of the product in the test process. |

## Counts

| Destination | Files |
| --- | --- |
| Pull request 1 | 2 |
| Pull request 2 | 17 |
| Pull request 3 | 30 |
| Pull request 4 | 11 |
| Pull request 5 | 1 |
| Stays | 67 |
| Total | 128 |

## Scripts and programs that are not test files

| Pull request | Item | Destination |
| --- | --- | --- |
| 4 | `tests/Morphir.Myriad.Tests/package-tests.fsx`, `tests/Morphir.Myriad.SystemTextJson.Tests/package-tests.fsx` | Move: scenarios with `IPackageDriver`; remove the `RunMyriadPackageTests` step. |
| 4 | `tests/Morphir.SDK.Tests/Packaging/package-tests.ps1` | Move with `PackageMigrationTests.fs`. |
| 5 | `tests/Morphir.Compilation.Smoke`, `Morphir.Configuration.IO.Smoke`, `Morphir.Configuration.Inspection.Smoke`, `Morphir.Configuration.Microsoft.Smoke`, `Morphir.Conformance.Smoke` | A host for a library that the CLI does not reach stays as a subject, and its checks become smoke tests. Task 9.1 decides for each of the five. `Configuration.Inspection.Smoke` runs in the `Test` target now and keeps a pull request run. |
| 5 | `tests/Morphir.Myriad.SystemTextJson.AotProbe`, with the target `TestMyriadJsonAot` (pull request, Linux) | The program stays as a subject. Its checks become a test of the Native AOT lane. |
| 5 | `tests/Morphir.Mep.AotProbe`, with the target `TestMepAot` (pull request, Linux) | The program stays as a subject. Its checks become a test of the Native AOT lane. |
| 5 | `src/Morphirup`, which the target `TestMorphirupAot` publishes (pull request, Linux) | The checks become a test of the Native AOT lane. The subject is the `morphirup` executable of the product, not a probe program. |
| stays | `tests/Morphir.Extensions.Installation.AotProbe`, with the target `TestExtensionInstallationAot` | Stays a manual target. No pull request runs it now, and the target says that the official qualification belongs to finos/morphir. |
| stays | `tests/Morphir.Extensions.Wasm.AotProbe` | Stays the subject of `wasm-engine-linux-floor.yml`, `wasm-engine-static-link.yml` and `scripts/wasm-engine-linux-floor.ps1`. No target of the `Test` or `TestBuild` lane runs it. |

## Readers of the old environment variables

| Variable | Readers | Handling |
| --- | --- | --- |
| `MORPHIR_EXECUTABLE_TYPE`, `MORPHIR_EXECUTABLE_PATH` | `Morphir.E2E.Tests`, `scripts/run-e2e-tests.cs`, `manual-aot-test.yml`, three documents | Pull request 2: `MORPHIR_TEST_TARGET`, `MORPHIR_TEST_HOST` and `MORPHIR_TEST_EXECUTABLE`. |
| `MORPHIR_DOTNET_TOOL_CLI`, `MORPHIR_AOT_CLI` | `Morphir.Frontend.FSharp.Tests/CliAdapterTests.fs` (a kit test that stays), four files of `Morphir.Tooling.Tests`, one document | Pull request 2 for the kit test, which then uses the target of the testing library; pull request 3 for the others. |
| `MORPHIR_GENERATION_CLI` | Six files of `Morphir.Tooling.Tests`, `src/Morphir.Testing/README.md`, one document | Pull requests 1 and 3. |
| `MORPHIR_CONFIGURATION_CLI` | Found in pull request 1 (task 3.3), not in the first inventory: `ConfigurationSteps`, `ConfigurationEditingSteps`, `ProjectMigrationSteps` and `WorkspaceMigrationSteps` of `Morphir.Tooling.Tests`. From pull request 1 the one reader is `Integration/CLI/DevelopmentCli.cs`. | Pull request 3, with the configuration and migration features (task 7.4). Task 10.2 removes it with the other variables. |

Task 10.2 removes a variable only when no code, script or workflow reads it and no document tells a reader to set
it. The mapping table of the testing library and the evidence of this change can name an old variable.
