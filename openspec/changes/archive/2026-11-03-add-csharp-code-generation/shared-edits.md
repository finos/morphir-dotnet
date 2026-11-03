# Proposed serial shared edits

Issue [#474](https://github.com/finos/morphir-dotnet/issues/474).
Root approved a narrow serial edit lease. The implementation lead applied these
edits and verified the integrated Test, PackAll, TestBuild and package-path checks.
The additional Models release-note property was separately approved after its
metadata test failed. All unrelated content remains intact.

1. `Morphir.slnx`, after each `Morphir.CodeGeneration.FSharp` project line,
   insert its CSharp sibling, at
   `src/Morphir.CodeGeneration.CSharp/Morphir.CodeGeneration.CSharp.fsproj` and
   `tests/Morphir.CodeGeneration.CSharp.Tests/Morphir.CodeGeneration.CSharp.Tests.fsproj`.
2. `build/Build.Testing.cs`, explicit `myriadExitCodes` sequential suite array,
   after `"Morphir.CodeGeneration.FSharp.Tests",` insert
   `"Morphir.CodeGeneration.CSharp.Tests",`. In its selector replace
   `RunCommand("dotnet", "exec", testDll)` with a conditional passing
   `"--sequenced"` only for the new CSharp suite. Existing runners retain their
   arguments. This registers the suite in actual Nuke Test execution.
3. `build/Build.cs`, immediately after `MorphirSDKCSharpProject`, add
   `AbsolutePath MorphirModelsProject => SourceDirectory / "Morphir.Models" / "Morphir.Models.fsproj";`
   and `AbsolutePath MorphirCodeGenerationCSharpProject => SourceDirectory / "Morphir.CodeGeneration.CSharp" / "Morphir.CodeGeneration.CSharp.fsproj";`.
4. `build/Build.Packaging.cs`, in PackLibs immediately before
   `// Validate library packages`, add a sequential foreach over
   `new[] { MorphirModelsProject, MorphirCodeGenerationCSharpProject }` calling
   `DotNetPack(s => s.SetProject(project).SetConfiguration(Configuration).SetOutputDirectory(OutputDir).SetProperty("Version", versionString))`.
   Append `"Morphir.Models", "Morphir.CodeGeneration.CSharp"` to
   `libraryProjects`. Models must be packed before its dependent generator.
5. `build/Build.Publishing.cs`, append `"Morphir.Models", "Morphir.CodeGeneration.CSharp"`
   to both exact package-ID foreach arrays in PublishLibs and PublishLocalLibs.
   These use PathHelper's exact versioned lookup and existing existence checks.
   No publishing target is executed and no NuGet source configuration is changed.
6. `docs/content/contributing/design/prds/_index.md`, after the table separator,
   add `| [C# code generation](./add-csharp-code-generation.md) | In Progress | Compiler-host foundation | In-memory API and packaged modern-host smoke, #474 |`.
   The root-owned umbrella's current task should say the generator is implementing
   after both runtime and archival landings; historical delivery fields stay intact.
7. `README.md`, before `## Getting Started`, add a short paragraph linking
   `docs/content/docs/backends/csharp/_index.md` and describing the independent
   in-memory typed generator, explicit numeric language profiles and separate
   future CLI adapter. The foundation checkpoint documents its limited current
   behavior rather than claiming the initiative delivered.
8. `CHANGELOG.md`, under the first `### Added`, insert
   `- In-memory C# generation foundation and packaged modern compiler-host verification, tracked in [#474](https://github.com/finos/morphir-dotnet/issues/474).`
   At final acceptance revise this entry to reflect the complete delivered scope.
9. `NOTICE.txt`, append a paragraph identifying original FINOS contributors'
   2026 typed C# generator, tests and documentation, under Apache-2.0. No upstream
   implementation source is copied; all existing evidence notices are retained.

No global.json, central package version, workflow, production adapter, CLI or
runtime source edit is proposed.
