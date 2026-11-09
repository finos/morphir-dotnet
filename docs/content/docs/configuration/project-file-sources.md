---
title: Project-file sources
description: Name a build-tool project file as the ordered sources of a compile
---

# Project-file sources

A build tool's project file can give the sources of `morphir compile` and their order. The command expands
the project file on the host into an ordered source set: a root (the directory of the project file) and
documents relative to that root. Frontends receive plain ordered documents. They do not know that a
project file existed.

## Name the project file

Use one of these. The command reads only the first that applies:

1. `--project-file <file>` on the command line;
2. `project_file` in the `[frontend.<language>]` table of the selected language.

```toml
[frontend]
language = "fsharp"

[frontend.fsharp]
project_file = "src/Acme.CarRental.fsproj"
```

Every `[frontend.<language>]` table accepts the same `project_file` key, with the same meaning as
`--project-file`. A relative path is relative to the directory of the file that sets it. The command reads
the key only from the table of the selected language (`--language`, else `frontend.language`). The key of
another language's table is not read. If no language is selected, no `project_file` key is read; if no
other source selection then applies, the command fails with `compile.language_required`.

`--input` or `--project-file` on the command line replaces a configured `project_file` for that run, and
the configured project file is not evaluated. `--project-file` and `--input` together fail with
`sources.conflicting_source_options` (exit code 2). An `--input` file that a provider recognizes as a
project file fails with `sources.project_file_as_input` (exit code 2); use `--project-file` for it.

The command evaluates a project file only when you name it. It never searches directories for project
files. A project file in the source directory that no option or key names is not read.

## Providers

A project-file provider expands one kind of project file. Each provider declares file-name patterns, and
the command gives the project file to the one provider whose pattern matches it. Providers are selected by
the project file, not by the language.

| Code | Exit | Meaning |
| --- | --- | --- |
| `sources.unknown_project_file` | 2 | No provider recognizes the file. The message names the file and the registered patterns. |
| `sources.ambiguous_project_file` | 2 | More than one provider recognizes the file. The message names them. |
| `sources.missing` | 2 | The project file does not exist. |

## The MSBuild provider

The MSBuild provider recognizes MSBuild project files (`*.*proj`), for example `.fsproj` and `.csproj`.
It reads the `Compile`, `ProjectReference` and `PackageReference` items through project evaluation only.
It does not restore, build or run targets, and it writes no output: no `obj/` or `bin/` directory.

Both executables, `morphir` and `dotnet-morphir`, have this provider.

The provider runs this command as a child process, with explicit arguments, no shell, and a deadline:

```text
dotnet msbuild <file> -getItem:Compile -getItem:ProjectReference -getItem:PackageReference -nologo
```

with `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1` and
`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`. No build process stays running after the command ends.

Requirements: a .NET SDK 8 or later on `PATH`. Without one, the command fails with `msbuild.unavailable`
(exit code 2), and the message names the required SDK.

The documents are the `Compile` items, in the order the project declares them. A `.csproj` expands the same
way. Frontend selection, not the provider, then decides whether the language is supported.

| Project content | Result |
| --- | --- |
| `<Compile Include="Rates.fs" />` | A document, in declared order. |
| A `Compile` item outside the project directory, for example `<Compile Include="../shared/Common.fs" Link="Common.fs" />` | `sources.outside_root` (exit code 2), naming `../shared/Common.fs`. Nothing is compiled. |
| `ProjectReference` | `msbuild.unsupported_project_reference` (exit code 2). |
| `PackageReference` | The warning `msbuild.ignored_package_reference`, naming the package. Compilation continues. |
| An evaluation error | `msbuild.evaluation_failed` (exit code 2), with the build tool's message. |

Example:

```text
dotnet-morphir compile --project-file src/Acme.CarRental.fsproj --package-name acme/car-rental --json
```

For a project that declares `Rates.fs` then `Pricing.fs`, the report has
`"sources": { "projectFile": "src/Acme.CarRental.fsproj", "root": "src", "documents": ["Rates.fs", "Pricing.fs"] }`.

## Evaluation runs project logic

MSBuild evaluation can run project logic: imports and property functions. That is why a provider runs only
on a file that you name. Name only project files that you trust.

## Expanded documents follow the source rules

The command reads expanded documents with the same rules as files that you name: strict UTF-8, links and
special files refused, and byte, document and path limits. A `Compile` item that is a symbolic link fails
before compilation with a diagnostic that names the document.

## See also

- [Compile]({{< relref "/docs/cli/compile" >}})
