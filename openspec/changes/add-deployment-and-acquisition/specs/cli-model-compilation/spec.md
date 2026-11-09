# Spec Delta

## RENAMED Requirements

- FROM: `### Requirement: The in-process F# frontend ships with the dotnet tool`
- TO: `### Requirement: The in-process F# frontend ships with the managed offerings`

## MODIFIED Requirements

### Requirement: Frontend selection uses advertised capabilities
The language SHALL come from `--language`, else `frontend.language`, else the extension of every source document. The command SHALL choose among registered frontends using only what each one advertises: language, file extensions, IR versions and multi-document support. Every document's extension SHALL be one the selected frontend claims. If no frontend matches, or more than one does, the command SHALL fail before compilation and name the frontend and the capability that blocked the request.

#### Scenario: IR version not served
- **WHEN** a configured frontend admits only IR version `3.0.0`, and the command requests `--ir-version 4`, which the host cannot derive from classic IR
- **THEN** the command fails before compilation, naming that frontend and its IR versions, with exit code 2

#### Scenario: F# frontend not in this build
- **WHEN** the `native-aot` `morphir` executable compiles F# and no F# frontend extension is configured
- **THEN** the command fails with `compile.frontend_unavailable`. The message says that the managed offerings (`self-contained` and `framework-dependent` `morphir`, and `dotnet-morphir`) have a built-in F# frontend and that `[frontend.fsharp].extension` or `--extension` configures one.

### Requirement: The in-process F# frontend ships with the managed offerings
The managed offerings `dotnet-tool`, `self-contained` and `framework-dependent` SHALL register the in-process F# frontend. Unless an extension is configured, they SHALL select that frontend for F#. The `native-aot` `morphir` executable SHALL NOT contain or load the F# compiler service.

#### Scenario: Tool path without MEP
- **WHEN** `dotnet-morphir compile --project-file src/Acme.CarRental.fsproj --package-name acme/car-rental` runs and no extension is configured
- **THEN** the compile succeeds and starts no frontend process. Only the project-file provider starts a process.

#### Scenario: Managed morphir path without MEP
- **WHEN** the `self-contained` `morphir` runs `compile --language fsharp --input src/Rates.fs --package-name acme/demo` on a machine with no .NET SDK and no extension configured
- **THEN** the compile succeeds and starts no process
- **AND** the IR equals the IR that `dotnet-morphir` writes for the same command

#### Scenario: The AOT publish has no compiler service
- **WHEN** the `native-aot` `morphir` is published for a supported platform
- **THEN** neither its assembly closure nor its published files contain FSharp.Compiler.Service or `Morphir.Frontend.FSharp`
