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

/// The JSON encoding of the values in a call observation. A case selects it with the
/// @value-codec:N tag, the value codec step or the suffix of a call step.
[<RequireQualifiedAccess>]
type ValueCodec =
    /// Value codec 1: the shape of the morphir-elm DataCodec, as in morphir-tests.json. A
    /// constructor is an array with its camelCase name first, and Maybe is null or the value.
    | ElmDataCodec
    /// Value codec 2: a constructor is tagged by its TitleCase name, "Small" or {"Large": 4}.
    | TaggedObjects

    /// The major version of the codec: 1 or 2.
    member this.Major =
        match this with
        | ElmDataCodec -> 1
        | TaggedObjects -> 2

    /// The semantic version of the codec: 1.0.0 or 2.0.0.
    member this.Version = $"{this.Major}.0.0"

    /// The codec that applies when a case declares none.
    static member Default = TaggedObjects

    /// Parses a codec version as cases write it: a semantic version such as 1.0.0, or its
    /// short form 1. The runner implements codecs 1.0.0 and 2.0.0. It refuses other versions,
    /// pre-releases included, so that it never reads a value in a codec it does not know. Build
    /// metadata has no effect on the version.
    static member Parse(text: string) : Result<ValueCodec, string> =
        let codec major =
            match major with
            | 1 -> Some ElmDataCodec
            | 2 -> Some TaggedObjects
            | _ -> None

        match Morphir.IR.Versioning.parse text with
        | Ok(Morphir.IR.Versioning.Classic major) -> codec major
        | Ok(Morphir.IR.Versioning.SemVer {
                                              Major = major
                                              Minor = 0
                                              Patch = 0
                                              PreRelease = None
                                          }) -> codec major
        | _ -> None
        |> function
            | Some codec -> Ok codec
            | None ->
                Error
                    $"Value codec {text} is not supported. The supported value codecs are 1.0.0 (short form 1) and 2.0.0 (short form 2)"

    override this.ToString() = $"value codec {this.Version}"

/// One call observation of a case: the value that is called, its arguments as a JSON array
/// (one element for each parameter), the expected result and the value codec of both JSON
/// texts. JSON texts are kept exactly as the case writes them.
type CallExpectation = {
    Value: string
    Arguments: string
    Result: string
    Codec: ValueCodec
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
