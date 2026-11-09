# project-file-sources Specification

## Purpose
Lets a user name a build-tool project file as the source of a compilation. The file expands to an ordered, root-relative source set that does not depend on the source language. Frontends and protocols then receive plain ordered documents, whatever tool described them.

## Requirements

### Requirement: A named project file expands to an ordered source set
A project file named by `--project-file` or by `[frontend.<language>].project_file` SHALL expand to a source set. The root SHALL be the project file's directory. The documents SHALL be in the order the build tool declares them, and each document SHALL have a path relative to the root. `--project-file` SHALL name one file and SHALL NOT be combined with `--input`.

#### Scenario: F# project order
- **WHEN** `--project-file src/Acme.CarRental.fsproj` declares `Compile` items `Rates.fs` then `Pricing.fs`
- **THEN** the source set root is `src/`, and its documents are `Rates.fs` then `Pricing.fs`

#### Scenario: Project file with explicit inputs
- **WHEN** `--project-file Acme.CarRental.fsproj` and `--input Extra.fs` are both given
- **THEN** the command fails with `sources.conflicting_source_options` and exit code 2

#### Scenario: Project file passed as an input
- **WHEN** `--input Acme.CarRental.fsproj` is given, and a registered provider recognizes that file
- **THEN** the command fails with `sources.project_file_as_input` and exit code 2, and the message says to use `--project-file`

### Requirement: The configuration key is the same for every language
Every `[frontend.<language>]` table SHALL accept the same `project_file` key, with the same meaning as the `--project-file` flag. The command SHALL read the key only from the table of the selected language (`--language`, else `frontend.language`). Only `project.source_directory` SHALL remain as a project-level source setting.

#### Scenario: Configured F# project file
- **WHEN** `frontend.language = "fsharp"` and `[frontend.fsharp] project_file = "src/Acme.CarRental.fsproj"`
- **THEN** sources come from that project file, and `project.source_directory` is not scanned

#### Scenario: Other language's table is not read
- **WHEN** `frontend.language = "fsharp"` and only `[frontend.csharp] project_file` is set
- **THEN** that key is not used, and sources come from `project.source_directory`

#### Scenario: Language unknown
- **WHEN** a `[frontend.<language>].project_file` is set but neither `--language` nor `frontend.language` selects a language
- **THEN** no project file is read. If no other source selection applies, the command fails with `compile.language_required`.

### Requirement: Providers are selected by the project file, not by language
Each registered provider SHALL declare the project-file patterns it recognizes. Exactly one provider SHALL match a named project file. When none matches, or more than one does, the command SHALL fail and name the file and the candidate providers. Adding a provider SHALL NOT change any frontend.

#### Scenario: Unknown project file
- **WHEN** `[frontend.fsharp] project_file = "build.gradle"` and no provider recognizes it
- **THEN** the command fails with `sources.unknown_project_file`, naming the file and the registered patterns

#### Scenario: C# project expands the same way
- **WHEN** an MSBuild `.csproj` is named
- **THEN** it expands to its `Compile` items in order. Frontend selection, not the provider, then decides whether the language is supported.

### Requirement: Project files are evaluated only when named
A project file SHALL be evaluated only when `--project-file` or `[frontend.<language>].project_file` names it. The command SHALL NOT search directories for build-tool project files.

#### Scenario: Project file present but not named
- **WHEN** the source directory contains `Acme.fsproj` and no input or configuration names it
- **THEN** the build tool is not started, and sources come from the source directory

### Requirement: The MSBuild provider evaluates without building
The MSBuild provider SHALL recognize MSBuild project files (`*.*proj`). It SHALL read `Compile`, `ProjectReference` and `PackageReference` items through project evaluation only, with no restore, build, target execution or output written. It SHALL start the .NET SDK with explicit arguments and no shell, under a deadline. It SHALL turn off build-node reuse, so that no build process stays running.

#### Scenario: No side effects
- **WHEN** the provider expands a project that was never restored
- **THEN** expansion succeeds, no `obj/` or `bin/` directory is created, and no MSBuild process remains after the command ends

#### Scenario: SDK missing
- **WHEN** no .NET SDK 8 or later is available
- **THEN** the command fails with `msbuild.unavailable`, exit code 2, and a message naming the required SDK

### Requirement: Unsupported project content is reported, not ignored silently
These rules SHALL apply to project file content:

- A `ProjectReference` SHALL fail with `msbuild.unsupported_project_reference`.
- A `PackageReference` SHALL give the warning `msbuild.ignored_package_reference`, naming the package.
- A document outside the project file's root, for example a linked `../shared/Common.fs`, SHALL fail with `sources.outside_root`.
- An evaluation error SHALL fail with `msbuild.evaluation_failed` and keep the build tool's message.

#### Scenario: Linked shared file
- **WHEN** the project includes `<Compile Include="../shared/Common.fs" Link="Common.fs" />`
- **THEN** the command fails with `sources.outside_root` naming `../shared/Common.fs`, and nothing is compiled

#### Scenario: Package reference
- **WHEN** the project references a NuGet package
- **THEN** compilation continues, and the report has one `msbuild.ignored_package_reference` warning naming that package

### Requirement: Expanded sources follow the normal source rules
Documents from a project file SHALL be read under the same bounded-capture, link-refusal and UTF-8 rules as explicitly named files. The task record SHALL list the project file among its `inputs`.

#### Scenario: Linked document refused
- **WHEN** a `Compile` item in the project file is a symbolic link
- **THEN** the command fails before compilation with a diagnostic naming the document
