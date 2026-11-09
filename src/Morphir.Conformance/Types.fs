// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance

type CorpusOptions = {
    Suite: string
    VocabularyVersion: string
    IrVersions: string list
    InputKinds: string list
}

type Artifact = { Path: string; Bytes: byte array }

type CorpusIssue = {
    Code: string
    Path: string
    Line: int
    Message: string
}

type SourceDocument = { Uri: string; Text: string }

type CompileInput = {
    Package: string
    ExposedModules: string list
    Documents: SourceDocument list
    InputKind: string
    IrVersion: string
}

type DiagnosticKey = {
    Severity: string
    Code: string
    Uri: string
    StartLine: int
    StartCharacter: int
    EndLine: int
    EndCharacter: int
}

type ExpectedOutcome = {
    Success: bool
    Diagnostics: DiagnosticKey list
    AllowNonErrorDiagnostics: bool
    ModelJson: string option
}

/// One call observation of a case: the value that is called, its arguments as a JSON array
/// (one element for each parameter) and the expected result. JSON texts are kept exactly as
/// the case writes them.
type CallExpectation = {
    Value: string
    Arguments: string
    Result: string
}

type ConformanceCase = {
    Id: string
    File: string
    Line: int
    Input: CompileInput
    Expected: ExpectedOutcome
    Calls: CallExpectation list
    Exclusion: string option
}

type CaseSet = {
    Cases: ConformanceCase list
    Sha256: string
    VocabularyVersion: string
}

type ImplementationIdentity = {
    Name: string
    Version: string
    Sha256: string
}

type CompileObservation = {
    Success: bool
    ModelJson: string option
    Diagnostics: DiagnosticKey list
}

type ObservationFailure = {
    Message: string
    Partial: CompileObservation option
}

type Adapter = {
    Name: string
    Identity: ImplementationIdentity
    Observe:
        CompileInput
            -> System.Threading.CancellationToken
            -> System.Threading.Tasks.Task<Result<CompileObservation, ObservationFailure>>
}

/// Checks the call observations of a case against a compiled model, for the behavioral verdict.
/// Check gets the compile input, the model JSON and the calls of one case variant, and
/// returns the first difference as an error.
type CallChecker = {
    Name: string
    Check:
        CompileInput
            -> string
            -> CallExpectation list
            -> System.Threading.CancellationToken
            -> System.Threading.Tasks.Task<Result<unit, string>>
}

type Verdict =
    | Passed
    | Failed of difference: string
    | Excluded of reason: string

type CaseResult = {
    Case: ConformanceCase
    Observation: CompileObservation option
    Verdict: Verdict
}

type RunReport = {
    RunnerName: string
    RunnerVersion: string
    CaseSetSha256: string
    Implementation: ImplementationIdentity
    Adapter: string
    VocabularyVersion: string
    CallChecker: string option
    Results: CaseResult list
    Qualified: bool
}
