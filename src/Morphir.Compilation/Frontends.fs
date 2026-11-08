namespace Morphir.Compilation

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Morphir.IR

// The in-process compile contract (FE-1). It models the MEP 0.2.0-draft.1 compileModel concepts and adds
// no concept that MEP lacks, so the MEP adapter can be a pure codec.

/// <summary>Why a language id was refused.</summary>
[<RequireQualifiedAccess>]
type LanguageIdFailure =
    /// <summary>The text is null or empty.</summary>
    | Empty
    /// <summary>The character at this zero-based index is not allowed.</summary>
    | InvalidCharacter of index: int

    override this.ToString() =
        match this with
        | Empty -> "A language id is empty."
        | InvalidCharacter index ->
            $"Character {index} of the language id is not allowed. A language id starts with a lowercase letter and holds lowercase letters, digits, '-', '_' and '.'."

/// <summary>
/// The id of a source language, for example <c>fsharp</c> (MEP <c>languageId</c>): a lowercase letter, then
/// lowercase letters, digits, <c>-</c>, <c>_</c> and <c>.</c>. Ids are compared ordinally.
/// </summary>
[<CustomEquality; CustomComparison>]
type LanguageId =
    private
    | LanguageId of string

    /// <summary>The id text.</summary>
    member this.Value = let (LanguageId value) = this in value

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? LanguageId as id -> String.Equals(this.Value, id.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? LanguageId as id -> String.CompareOrdinal(this.Value, id.Value)
            | _ -> invalidArg (nameof other) "Not a LanguageId."

    /// <summary>Parses untrusted text. For F# callers.</summary>
    static member Parse(text: string | null) : Result<LanguageId, LanguageIdFailure> =
        match text with
        | null -> Error LanguageIdFailure.Empty
        | value when value.Length = 0 -> Error LanguageIdFailure.Empty
        | value ->
            let allowed index (c: char) =
                if index = 0 then
                    Char.IsAsciiLetterLower c
                else
                    Char.IsAsciiLetterLower c
                    || Char.IsAsciiDigit c
                    || c = '-'
                    || c = '_'
                    || c = '.'

            match
                value
                |> Seq.indexed
                |> Seq.tryFind (fun (index, c) -> not (allowed index c))
            with
            | Some(index, _) -> Error(LanguageIdFailure.InvalidCharacter index)
            | None -> Ok(LanguageId value)

    /// <summary>Parses untrusted text. For C# callers.</summary>
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] result: byref<LanguageId>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<LanguageIdFailure>)
        : bool =
        match LanguageId.Parse text with
        | Ok id ->
            result <- id
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>Builds an id from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : LanguageId =
        match LanguageId.Parse literal with
        | Ok id -> id
        | Error error -> invalidArg (nameof literal) (error.ToString())

/// <summary>A source document of a compile request (MEP <c>SourceDocument</c>).</summary>
type SourceDocument =
    {
        /// <summary>The document URI. Diagnostics refer to the document by this URI.</summary>
        Uri: string
        /// <summary>The language of the document. It must be the language of the request.</summary>
        LanguageId: LanguageId
        /// <summary>The document version. It goes up each time the document changes.</summary>
        Version: uint64
        /// <summary>The complete text of the document.</summary>
        Text: string
    }

    override this.ToString() = $"{this.Uri} (version {this.Version})"

/// <summary>Why a compile request was refused before analysis.</summary>
[<RequireQualifiedAccess>]
type CompileRequestFailure =
    /// <summary>There is no language.</summary>
    | MissingLanguage
    /// <summary>There is no package name.</summary>
    | MissingPackage
    /// <summary>There is no document.</summary>
    | NoDocuments
    /// <summary>The document at this zero-based index is null.</summary>
    | MissingDocument of index: int
    /// <summary>The document at this zero-based index has no URI.</summary>
    | EmptyUri of index: int
    /// <summary>The document at this zero-based index has no text.</summary>
    | MissingText of index: int
    /// <summary>The document at this zero-based index is not in the language of the request.</summary>
    | LanguageMismatch of index: int
    /// <summary>The URI appears more than once.</summary>
    | RepeatedUri of uri: string

    override this.ToString() =
        match this with
        | MissingLanguage -> "A compile request needs a language."
        | MissingPackage -> "A compile request needs a package name."
        | NoDocuments -> "A compile request needs at least one source document."
        | MissingDocument index -> $"Source document {index} is missing."
        | EmptyUri index -> $"Source document {index} has no URI."
        | MissingText index -> $"Source document {index} has no text."
        | LanguageMismatch index ->
            $"Source document {index} is not in the language of the request."
        | RepeatedUri uri -> $"The source document URI '{uri}' appears more than once."

/// <summary>
/// One complete compilation of a source set (MEP <c>CompileModelRequest</c>). Only <c>Parse</c>,
/// <c>TryParse</c> and <c>Create</c> build one, so a request always has a language, a package and at least
/// one document; every document has a URI, a text and the request's language; and the URIs are unique.
/// </summary>
type CompileRequest =
    private
        {
            LanguageValue: LanguageId
            RootValue: string option
            DocumentsValue: SourceDocument array
            PackageValue: PackageName
            ExposedValue: string array option
            OutputValue: IrTarget
            DigestValue: string
        }

    /// <summary>The language of the request.</summary>
    member this.LanguageId = this.LanguageValue

    /// <summary>The root that module names are derived against, if any. For F# callers.</summary>
    member this.Root = this.RootValue

    /// <summary>The root, or null. For C# callers.</summary>
    member this.RootOrNull = Option.toObj this.RootValue

    /// <summary>The documents, in compilation order.</summary>
    member this.Documents: IReadOnlyList<SourceDocument> = this.DocumentsValue

    /// <summary>The documents, in compilation order. For F# callers.</summary>
    member this.DocumentList = List.ofArray this.DocumentsValue

    /// <summary>The package the compilation builds.</summary>
    member this.Package = this.PackageValue

    /// <summary>
    /// The exposed module names. None exposes every public module; an empty list exposes none. For F# callers.
    /// </summary>
    member this.ExposedModules =
        this.ExposedValue
        |> Option.map List.ofArray

    /// <summary>The exposed module names, or null to expose every public module. For C# callers.</summary>
    member this.ExposedModulesOrNull =
        // Option.toObj gives the nullable type without a return annotation, which Fantomas 5 cannot parse.
        this.ExposedValue
        |> Option.map (fun modules -> modules :> IReadOnlyList<string>)
        |> Option.toObj

    /// <summary>The IR target the frontend must produce.</summary>
    member this.Output = this.OutputValue

    /// <summary>
    /// The caller's digest of the dependency and configuration context. The in-process F# frontend does not
    /// read it.
    /// </summary>
    member this.ContextDigest = this.DigestValue

    override this.ToString() =
        $"{this.LanguageValue} request with {this.DocumentsValue.Length} document(s) for {this.OutputValue}"

    static member private Check (language: LanguageId) (documents: SourceDocument array) =
        let problem index (document: SourceDocument) =
            if isNull (box document) then
                Some(CompileRequestFailure.MissingDocument index)
            elif String.IsNullOrEmpty document.Uri then
                Some(CompileRequestFailure.EmptyUri index)
            elif isNull (box document.Text) then
                Some(CompileRequestFailure.MissingText index)
            elif
                isNull (box document.LanguageId)
                || document.LanguageId
                   <> language
            then
                Some(CompileRequestFailure.LanguageMismatch index)
            else
                None

        match
            documents
            |> Seq.mapi problem
            |> Seq.tryPick id
        with
        | Some failure -> Some failure
        | None ->
            documents
            |> Array.countBy (fun document -> document.Uri)
            |> Array.tryFind (fun (_, count) -> count > 1)
            |> Option.map (fun (uri, _) -> CompileRequestFailure.RepeatedUri uri)

    /// <summary>Builds a request from untrusted input. For F# callers.</summary>
    static member Parse
        (languageId: LanguageId | null, root: string | null,
                                                       documents: IEnumerable<SourceDocument>,
                                                       package: PackageName | null,
                                                                              exposedModules:
                                                                                  IEnumerable<string> | null,
                                                                                                        output:
                                                                                                            IrTarget,
                                                                                                        contextDigest:
                                                                                                            string | null)
        : Result<CompileRequest, CompileRequestFailure> =
        let documents = Array.ofSeq documents

        match languageId with
        | null -> Error CompileRequestFailure.MissingLanguage
        | language ->
            match package with
            | null -> Error CompileRequestFailure.MissingPackage
            | package ->
                if Array.isEmpty documents then
                    Error CompileRequestFailure.NoDocuments
                else
                    match CompileRequest.Check language documents with
                    | Some failure -> Error failure
                    | None ->
                        Ok
                            {
                                LanguageValue = language
                                RootValue = Option.ofObj root
                                DocumentsValue = documents
                                PackageValue = package
                                ExposedValue =
                                    exposedModules
                                    |> Option.ofObj
                                    |> Option.map Array.ofSeq
                                OutputValue = output
                                DigestValue =
                                    Option.ofObj contextDigest
                                    |> Option.defaultValue ""
                            }

    /// <summary>Builds a request from untrusted input. For C# callers.</summary>
    static member TryParse
        (languageId: LanguageId | null, root: string | null,
                                                       documents: IEnumerable<SourceDocument>,
                                                       package: PackageName | null,
                                                                              exposedModules:
                                                                                  IEnumerable<string> | null,
                                                                                                        output:
                                                                                                            IrTarget,
                                                                                                        contextDigest:
                                                                                                            string | null,
                                                                                                                     [<Out;
                                                                                                                       MaybeNullWhen(false)>] result:
                                                                                                                         byref<CompileRequest>,
                                                                                                                     [<Out;
                                                                                                                       MaybeNullWhen(true)>] failure:
                                                                                                                         byref<CompileRequestFailure>)
        : bool =
        match
            CompileRequest.Parse(
                languageId,
                root,
                documents,
                package,
                exposedModules,
                output,
                contextDigest
            )
        with
        | Ok request ->
            result <- request
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// <summary>
    /// Builds a request from literals in source code. Throws <c>ArgumentException</c> that names the invalid
    /// parameter.
    /// </summary>
    static member Create
        (languageId: LanguageId, root: string | null,
                                                documents: IEnumerable<SourceDocument>,
                                                package: PackageName,
                                                exposedModules: IEnumerable<string> | null,
                                                                                      output:
                                                                                          IrTarget,
                                                                                      contextDigest:
                                                                                          string)
        : CompileRequest =
        match
            CompileRequest.Parse(
                languageId,
                root,
                documents,
                package,
                exposedModules,
                output,
                contextDigest
            )
        with
        | Ok request -> request
        | Error error ->
            let parameter =
                match error with
                | CompileRequestFailure.MissingLanguage -> nameof languageId
                | CompileRequestFailure.MissingPackage -> nameof package
                | _ -> nameof documents

            invalidArg parameter (error.ToString())

/// <summary>A source language a frontend reads (MEP <c>LanguageCapability</c>).</summary>
[<NoEquality; NoComparison>]
type LanguageCapability =
    {
        /// <summary>The language id.</summary>
        Id: LanguageId
        /// <summary>The file extensions of the language, with the leading dot, for example <c>.fs</c>.</summary>
        FileExtensions: IReadOnlyList<string>
    }

    override this.ToString() =
        $"""{this.Id} ({String.Join(", ", this.FileExtensions)})"""

/// <summary>What a frontend can compile (MEP <c>FrontendCapability</c>).</summary>
[<NoEquality; NoComparison>]
type FrontendCapability =
    {
        /// <summary>The languages the frontend reads.</summary>
        Languages: IReadOnlyList<LanguageCapability>
        /// <summary>The IR targets the frontend produces.</summary>
        Outputs: IReadOnlyList<IrTarget>
        /// <summary>True when the frontend compiles incrementally.</summary>
        Incremental: bool
        /// <summary>True when one request can hold more than one document.</summary>
        MultiDocument: bool
    }

    override this.ToString() =
        let languages =
            this.Languages
            |> Seq.map (fun language -> language.Id.Value)

        $"""{String.Join(", ", languages)} to {this.Outputs.Count} target(s)"""

/// <summary>A compiled package (MEP <c>ModelCompiled</c>): a model in the requested family, and its modules.</summary>
[<NoEquality; NoComparison>]
type CompiledPackage =
    {
        /// <summary>The model, in the family of the requested target.</summary>
        Model: FamilyModel
        /// <summary>The compiled module names, in compilation order.</summary>
        Modules: IReadOnlyList<string>
    }

    override this.ToString() =
        $"{this.Model} with {this.Modules.Count} module(s)"

/// <summary>How the host reaches a frontend.</summary>
[<RequireQualifiedAccess>]
type FrontendTransport =
    /// <summary>The frontend runs in the host process.</summary>
    | InProcess

    /// <summary>The text of the transport in reports and JSON output.</summary>
    member this.WireValue =
        match this with
        | InProcess -> "in-process"

    override this.ToString() = this.WireValue

/// <summary>
/// An operational or protocol failure of a frontend (exit code 2). It is not a source problem: source problems
/// are a failed <c>Checked</c> result. It keeps the diagnostics gathered before the failure.
/// </summary>
[<NoEquality; NoComparison>]
type FrontendFailure =
    {
        /// <summary>What failed.</summary>
        Message: string
        /// <summary>The diagnostics gathered before the failure, in order.</summary>
        Diagnostics: IReadOnlyList<Diagnostic>
    }

    override this.ToString() = this.Message

/// <summary>
/// The outcome of one compile request. <c>Completed</c> mirrors the MEP <c>CompileModelResult</c>: a passed
/// result is a success with a model, and a failed result holds the source problems. <c>Failed</c> is an
/// operational failure.
/// </summary>
[<RequireQualifiedAccess; NoEquality; NoComparison>]
type FrontendOutcome =
    /// <summary>The frontend ran: a compiled package, or the source problems.</summary>
    | Completed of result: Checked<CompiledPackage>
    /// <summary>The frontend could not run the request.</summary>
    | Failed of failure: FrontendFailure

    /// <summary>A completed outcome. For C# callers.</summary>
    static member Complete(result: Checked<CompiledPackage>) = Completed result

    /// <summary>An operational failure. For C# callers.</summary>
    static member Fail(failure: FrontendFailure) = Failed failure

    /// <summary>Calls <paramref name="completed"/> or <paramref name="failed"/>. For C# callers.</summary>
    member this.Match<'R>
        (
            completed: Func<Checked<CompiledPackage>, 'R>,
            failed: Func<FrontendFailure, 'R>
        ) : 'R =
        match this with
        | Completed result -> completed.Invoke result
        | Failed failure -> failed.Invoke failure

    override this.ToString() =
        match this with
        | Completed result -> $"completed: {result}"
        | Failed failure -> $"failed: {failure.Message}"

/// <summary>
/// A registered frontend (FE-1). A registry selects a frontend by its advertised capability only (FE-2).
/// </summary>
type IFrontend =
    /// <summary>A stable identity for reports and diagnostics.</summary>
    abstract Identity: ComponentId
    /// <summary>How the host reaches the frontend.</summary>
    abstract Transport: FrontendTransport
    /// <summary>What the frontend can compile.</summary>
    abstract Capability: FrontendCapability

    /// <summary>
    /// Compiles one request. Cancellation ends the task with <c>OperationCanceledException</c>.
    /// </summary>
    abstract CompileAsync:
        request: CompileRequest * cancellationToken: CancellationToken -> Task<FrontendOutcome>
