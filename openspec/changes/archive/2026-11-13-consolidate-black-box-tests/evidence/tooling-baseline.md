# `Morphir.Tooling.Tests` before and after the CLI driver (tasks 3.1 and 3.4)

Date: 2026-11-12. Machine: macOS arm64, 18 processors, .NET SDK 11.0.100, Release configuration. Each run is the
complete assembly, alone on the machine, with the default process limit of the gate (9):

```bash
dotnet exec tests/Morphir.Tooling.Tests/bin/Release/net11.0/Morphir.Tooling.Tests.dll --report-trx --report-trx-filename <name>.trx --results-directory <folder>
python3 openspec/changes/consolidate-black-box-tests/evidence/trx-summary.py <folder>/<name>.trx
```

- **Before** (task 3.1): commit `fb23c5ae`. `CliTestHelper` starts the CLI with `dotnet run --project ... --no-build`.
- **After** (task 3.4): `CliTestHelper` and the four step classes that start the CLI themselves
  (`ConfigurationSteps`, `ConfigurationEditingSteps`, `ProjectMigrationSteps`, `WorkspaceMigrationSteps`) call the
  development build target of the CLI driver, which starts the built assembly with `dotnet`.

| | Before | After |
| --- | ---: | ---: |
| Tests | 1,287 | 1,287 |
| Passed | 1,283 | 1,283 |
| Failed | 0 | 0 |
| Skipped | 4 | 4 |
| Time of the run | 40 s | 33 s |
| Summed time of the tests | 1151 s | 750 s |

The pass, fail and skip counts of each class are equal in the two runs (104 classes).

The task named three step classes. `ProjectMigrationSteps` starts the CLI in the same way, so it changed too. The
four classes read the variable `MORPHIR_CONFIGURATION_CLI` for a published CLI; `DevelopmentCli` reads it now, and
`evidence/inventory.md` has the variable.

## The process limit and the time of the run

The same assembly (before the change) with a lower limit in `MORPHIR_TEST_MAX_PROCESSES`:

| Limit | Time of the run |
| ---: | ---: |
| 9 (default of this machine) | 40 s |
| 4 | 59 s |
| 2 | 107 s |

## Each class

Summed time of the tests of the class, in seconds. The classes are in the order of the time before.

| Class | Passed | Failed | Skipped | Before (s) | After (s) |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Features.Generate.CSharpCliGenerationTests` | 33 | 0 | 0 | 199.5 | 104.3 |
| `Features.Compile.CompileFSourceToMorphirIRFeature` | 25 | 0 | 0 | 148.8 | 103.3 |
| `Features.Configuration.EditOneConfigurationFileWithAReviewableEffectiveResultFeature` | 36 | 0 | 0 | 79.7 | 62.5 |
| `Features.WorkspaceMigration.AdoptClassicProjectsThroughOneBoundedWorkspaceMigrationFeature` | 34 | 0 | 0 | 68.6 | 45.8 |
| `Features.Configuration.InspectConfigurationWithoutMutatingTheProjectFeature` | 48 | 0 | 0 | 60.3 | 54.0 |
| `Features.Generate.GenerateExecutableLibrariesFromMorphirModelsFeature` | 11 | 0 | 0 | 49.2 | 23.2 |
| `Features.ProjectMigration.MigrateOneClassicProjectWithoutLosingItsConfigurationFeature` | 31 | 0 | 0 | 47.8 | 33.4 |
| `Features.Compile.Sources.MsBuildProjectFileProviderTests` | 17 | 0 | 0 | 39.3 | 24.1 |
| `Features.Generate.GenerateCommandTests` | 9 | 0 | 0 | 36.1 | 15.2 |
| `Features.Mck.WasmHostCommandTests` | 13 | 0 | 0 | 33.1 | 17.8 |
| `Features.Configuration.ContainConfigurationDiscoveryFeature` | 10 | 0 | 0 | 32.7 | 25.2 |
| `Integration.CLI.CLIIRVerificationIntegrationFeature` | 13 | 0 | 0 | 31.7 | 7.1 |
| `Features.Generate.GenerationWireConformanceTests` | 4 | 0 | 0 | 28.8 | 27.9 |
| `Features.Compile.CompileTutorialTests` | 4 | 0 | 0 | 27.1 | 18.0 |
| `Features.Compile.CompileDocumentationTests` | 2 | 0 | 0 | 26.4 | 17.3 |
| `Integration.CLI.CLIArtifactManagementCommandsAreNotImplementedYetFeature` | 14 | 0 | 0 | 24.7 | 8.6 |
| `Features.Generate.GenerateFormattingTests` | 11 | 0 | 0 | 22.4 | 11.7 |
| `Features.Generate.GenerationReportingTests` | 9 | 0 | 0 | 22.1 | 19.7 |
| `Features.Extensions.ConfiguredWasmCommandTests` | 12 | 0 | 2 | 21.2 | 15.2 |
| `Features.Validate.ValidateCliTests` | 8 | 0 | 0 | 19.9 | 9.1 |
| `Features.Compile.CompileFoldPropertyTests` | 1 | 0 | 0 | 19.2 | 22.5 |
| `Features.Generate.GenerateCThroughTheExistingCommandFeature` | 4 | 0 | 0 | 16.1 | 6.2 |
| `Features.Generate.SelectAnExternalGenerationBackendFeature` | 4 | 0 | 0 | 15.4 | 8.4 |
| `Features.Compile.CompileRecordTests` | 10 | 0 | 0 | 11.0 | 15.8 |
| `Features.Extensions.ExtensionListCliTests` | 4 | 0 | 0 | 8.9 | 5.6 |
| `Features.Generate.GenerateDocumentationTests` | 2 | 0 | 0 | 7.9 | 4.3 |
| `Features.Compile.MepExtensionCompileTests` | 6 | 0 | 0 | 6.1 | 6.3 |
| `Infrastructure.BoundedProcessTests` | 6 | 0 | 0 | 5.7 | 3.4 |
| `Features.Extensions.ExtensionRuntimesReportTests` | 15 | 0 | 0 | 5.1 | 3.3 |
| `Features.Configuration.ContainConfigurationEditsFeature` | 2 | 0 | 0 | 4.8 | 3.7 |
| `Features.Validate.ValidateDocumentationTests` | 3 | 0 | 0 | 4.7 | 2.4 |
| `Features.WorkspaceMigration.ContainWorkspaceMigrationFeature` | 3 | 0 | 0 | 4.5 | 3.4 |
| `Features.ProjectMigration.ContainProjectMigrationFeature` | 2 | 0 | 0 | 3.8 | 2.3 |
| `Features.Compile.CompileTargetTests` | 20 | 0 | 0 | 3.4 | 3.7 |
| `Features.Compile.CompileCommandTests` | 19 | 0 | 0 | 2.6 | 2.9 |
| `Features.Extensions.MepChannelGuestCancellationTests` | 4 | 0 | 0 | 2.0 | 2.0 |
| `Features.Configuration.TheGlobalMorphirHomeIsNeverAProjectFeature` | 2 | 0 | 0 | 1.7 | 2.0 |
| `Features.Extensions.ExtensionWorkerTests` | 47 | 0 | 0 | 1.2 | 1.2 |
| `Features.Compile.CompileRunnerTests` | 12 | 0 | 0 | 1.1 | 0.6 |
| `Features.Generate.GenerationHandlerTests` | 8 | 0 | 0 | 0.9 | 1.0 |
| `Features.Compile.CompileSettingsResolverTests` | 25 | 0 | 0 | 0.9 | 0.7 |
| `Features.Generate.GenerationContractsTests` | 4 | 0 | 0 | 0.8 | 0.8 |
| `Features.Generate.FSharpGenerationBackendTests` | 8 | 0 | 0 | 0.7 | 0.7 |
| `Features.TaskRecords.TaskInstallerTests` | 13 | 0 | 0 | 0.7 | 0.8 |
| `Features.Generate.RecordGenerationSettingsTests` | 9 | 0 | 0 | 0.6 | 0.9 |
| `Features.Compile.SourceSelectionTests` | 14 | 0 | 0 | 0.3 | 0.3 |
| `Features.Generate.GenerationExtensionSelectionTests` | 18 | 0 | 0 | 0.2 | 0.2 |
| `Features.Mck.MckTerminationTests` | 8 | 0 | 0 | 0.2 | 0.2 |
| `Features.TaskRecords.CompileTaskStoreTests` | 12 | 0 | 0 | 0.1 | 0.2 |
| `Features.Generate.GenerationModelLoaderTests` | 16 | 0 | 0 | 0.1 | 0.1 |
| `Features.TaskRecords.CompileLockTests` | 8 | 0 | 0 | 0.1 | 0.1 |
| `Features.Extensions.ExtensionWorkerRuntimeTests` | 16 | 0 | 0 | 0.1 | 0.1 |
| `Features.Validate.ModelValidationTests` | 15 | 0 | 0 | 0.1 | 0.1 |
| `Features.Validate.LayeredAnalyzerConfigurationTests` | 4 | 0 | 0 | 0.1 | 0.1 |
| `Features.Extensions.ExtensionListCommandTests` | 12 | 0 | 0 | 0.1 | 0.1 |
| `Features.Extensions.WasmModuleLoaderTests` | 26 | 0 | 0 | 0.1 | 0.1 |
| `Features.VerifyIR.VerifyMorphirIRFeature` | 8 | 0 | 0 | 0.1 | 0.1 |
| `Features.Extensions.WasmRuntimeProbeTests` | 33 | 0 | 1 | 0.1 | 0.1 |
| `Features.Generate.CSharpGenerationPublicationTests` | 7 | 0 | 0 | 0.1 | 0.0 |
| `Features.Generate.ArtifactPathCheckTests` | 40 | 0 | 0 | 0.0 | 0.0 |
| `Features.VerifyIR.VerifyIRHandlerTests` | 7 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.WasmRuntimeAgreementTests` | 8 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.GenerationPublisherTests` | 12 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.GenerationIonLoaderTests` | 16 | 0 | 0 | 0.0 | 0.0 |
| `Infrastructure.JsonSchema.ClassicWriterSchemaTests` | 3 | 0 | 0 | 0.0 | 0.1 |
| `Features.Generate.CSharpGenerationBackendTests` | 43 | 0 | 0 | 0.0 | 0.0 |
| `Infrastructure.JsonSchema.SchemaValidatorTests` | 6 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.ExtensionListWasmTests` | 17 | 0 | 0 | 0.0 | 0.0 |
| `Infrastructure.JsonSchema.EnhancedErrorDetailsTests` | 6 | 0 | 0 | 0.0 | 0.0 |
| `Configuration.OsPathsTests` | 3 | 0 | 0 | 0.0 | 0.0 |
| `Infrastructure.JsonSchema.SchemaLoaderTests` | 6 | 0 | 0 | 0.0 | 0.0 |
| `Features.VerifyIR.VerifyIRValidatorTests` | 8 | 0 | 0 | 0.0 | 0.0 |
| `Features.Configuration.ConfigurationEditingCommandTests` | 18 | 0 | 0 | 0.0 | 0.1 |
| `Features.Configuration.ConfigurationCommandTests` | 16 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.GenerationPublicationFailureTests` | 3 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.GenerationProfileTests` | 11 | 0 | 0 | 0.0 | 0.0 |
| `Features.Mck.MckToolResolutionTests` | 8 | 0 | 1 | 0.0 | 0.0 |
| `Features.Management.DistHandlersTests` | 10 | 0 | 0 | 0.0 | 0.0 |
| `Features.ProjectMigration.ProjectMigrationCommandTests` | 8 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.BoundedModelCaptureTests` | 10 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.OfferingReportTests` | 14 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.ExtensionsNullabilityContractTests` | 15 | 0 | 0 | 0.0 | 0.0 |
| `Features.Compile.Sources.SourceCaptureTests` | 11 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.ExtensionWorkerArgumentsTests` | 39 | 0 | 0 | 0.0 | 0.0 |
| `Infrastructure.ExtensionCommandResolutionTests` | 6 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.WorkerTestGuestTests` | 2 | 0 | 0 | 0.0 | 0.0 |
| `Features.Compile.FrontendResolverTests` | 8 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.BackendExtensionSourceTests` | 5 | 0 | 0 | 0.0 | 0.0 |
| `Features.TaskRecords.TaskRecordCodecTests` | 27 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.WorkerBuildIdentityTests` | 6 | 0 | 0 | 0.0 | 0.0 |
| `Features.Management.ArtifactPathResolverTests` | 14 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.ExtensionsCSharpInteropTests` | 9 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.GenerationHandlerFailureTests` | 2 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.ExtensionGenerationAgreementTests` | 2 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.GenerationSettingsTests` | 10 | 0 | 0 | 0.0 | 0.0 |
| `Infrastructure.ContainmentTests` | 21 | 0 | 0 | 0.0 | 0.0 |
| `Features.TaskRecords.TaskPathsTests` | 29 | 0 | 0 | 0.0 | 0.0 |
| `Features.VerifyIR.VersionDetectionTests` | 8 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.StaticMediatorTests` | 1 | 0 | 0 | 0.0 | 0.0 |
| `Features.Validate.AnalyzerExtensionSourceTests` | 2 | 0 | 0 | 0.0 | 0.0 |
| `Features.Extensions.ExtensionCatalogInteropTests` | 4 | 0 | 0 | 0.0 | 0.0 |
| `Features.Generate.GenerationBackendResolverTests` | 9 | 0 | 0 | 0.0 | 0.0 |
| `Infrastructure.RuntimeIdentifierTests` | 5 | 0 | 0 | 0.0 | 0.0 |
| `Features.WorkspaceMigration.WorkspaceMigrationRecoveryWorkerTests` | 1 | 0 | 0 | 0.0 | 0.0 |
