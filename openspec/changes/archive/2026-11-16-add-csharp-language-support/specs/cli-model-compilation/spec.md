# Spec Delta

## ADDED Requirements

### Requirement: The in-process C# frontend ships in both hosts

Both `morphir` (Native AOT) and `dotnet-morphir` SHALL register the in-process C# frontend. Unless an extension is configured, the command SHALL select it for `.cs` documents. The C# frontend SHALL NOT need a separate download.

#### Scenario: AOT host compiles C#

- **WHEN** `morphir compile --project-file src/Acme.Orders.csproj --package-name acme/orders` runs and no extension is configured
- **THEN** the compile succeeds in the AOT `morphir` and starts no frontend process

#### Scenario: Documents from an SDK-style project

- **WHEN** the project file provider evaluates an SDK-style `.csproj`
- **THEN** it gives the `.cs` items of the project, with the default `**/*.cs` items included

### Requirement: Compile runs the passes after the frontend

`morphir compile` SHALL run the passes of the stage after a frontend on the model, before it writes IR or continues into generation. A pass error SHALL fail the compile with the diagnostics.

#### Scenario: Pass after the frontend

- **WHEN** a pass of the stage after a frontend rewrites a node
- **THEN** the written IR has the rewritten node and `--json` lists the application

### Requirement: Analyzer diagnostics appear in the compile result

The compile result SHALL include the diagnostics of the C# analyzers that the frontend runs, in the frontend diagnostic shape, with the same codes as the IDE reports.

#### Scenario: Refused loop in the report

- **WHEN** `morphir compile --json` runs on a project with a refused loop
- **THEN** the report has the loop rule code, the document and the range, and `success` is false
