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

type ConformanceCase = {
    Id: string
    File: string
    Line: int
    Input: CompileInput
    Expected: ExpectedOutcome
    Exclusion: string option
}

type CaseSet = {
    Cases: ConformanceCase list
    Sha256: string
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
    Results: CaseResult list
    Qualified: bool
}
