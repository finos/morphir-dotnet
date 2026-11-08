// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Morphir.Compilation
open Morphir.IR

/// <summary>
/// The <c>library</c> adapter over an in-process frontend of the compile contract (FE-1). It receives no
/// expected outcome. It turns a case input into a compile request, and the frontend's result into an
/// observation, with the model written by the writer of the requested IR version.
/// </summary>
[<RequireQualifiedAccess>]
module FrontendAdapter =

    let private observationFailure message = { Message = message; Partial = None }

    let private keyOf (diagnostic: Diagnostic) : DiagnosticKey =
        let code =
            match diagnostic.CodeOrNull with
            | null -> ""
            | code -> code.Value

        match diagnostic.LocationOrNull with
        | null -> {
            Severity = diagnostic.Severity.WireValue
            Code = code
            Uri = ""
            StartLine = -1
            StartCharacter = -1
            EndLine = -1
            EndCharacter = -1
          }
        | location -> {
            Severity = diagnostic.Severity.WireValue
            Code = code
            Uri = location.Uri
            StartLine = location.Range.Start.Line
            StartCharacter = location.Range.Start.Character
            EndLine = location.Range.End.Line
            EndCharacter = location.Range.End.Character
          }

    let private keys (diagnostics: Diagnostic seq) =
        diagnostics
        |> Seq.map keyOf
        |> Seq.toList

    /// <summary>
    /// The documents in the order that the input kind selects. A directory gives its files in ordinal order of
    /// their paths. Explicit files keep the order of the case. This adapter has no project-file provider: a
    /// project file gives the documents in the order of the case, as a project file whose compile items are in
    /// that order does. The <c>cli</c> adapter checks real project-file expansion.
    /// </summary>
    let private ordered (input: CompileInput) =
        match input.InputKind with
        | "directory" ->
            input.Documents
            |> List.sortWith (fun left right -> String.CompareOrdinal(left.Uri, right.Uri))
        | _ -> input.Documents

    /// <summary>The one language of the frontend that claims the extension of every document.</summary>
    let private languageOf
        (frontend: IFrontend)
        (documents: Morphir.Conformance.SourceDocument list)
        =
        let claims (language: LanguageCapability) =
            documents
            |> List.forall (fun document ->
                language.FileExtensions
                |> Seq.exists (fun extension ->
                    String.Equals(
                        Path.GetExtension document.Uri,
                        extension,
                        StringComparison.Ordinal
                    )
                )
            )

        match
            frontend.Capability.Languages
            |> Seq.filter claims
            |> Seq.toList
        with
        | [ language ] -> Ok language.Id
        | [] ->
            Error
                $"The frontend {frontend.Identity} claims no language for every document of the case."
        | _ ->
            Error
                $"More than one language of the frontend {frontend.Identity} claims the documents of the case."

    let private observe
        (frontend: IFrontend)
        (writers: WriterRegistry)
        (input: CompileInput)
        (cancellationToken: CancellationToken)
        =
        task {
            let documents = ordered input

            let prepared =
                match Versioning.parse input.IrVersion with
                | Error error ->
                    Error $"The IR version '{input.IrVersion}' is not a format version: {error}."
                | Ok version ->
                    match Checked.tryValue (writers.Resolve version) with
                    | None -> Error $"No IR writer declares the format version '{input.IrVersion}'."
                    | Some writer ->
                        match CanonicalNames.parsePath input.Package with
                        | Error error ->
                            Error $"The package '{input.Package}' is not a package name: {error}."
                        | Ok path ->
                            match languageOf frontend documents with
                            | Error message -> Error message
                            | Ok language ->
                                let sources = [
                                    for document in documents ->
                                        {
                                            Uri = document.Uri
                                            LanguageId = language
                                            Version = 0UL
                                            Text = document.Text
                                        }
                                        : Morphir.Compilation.SourceDocument
                                ]

                                CompileRequest.Parse(
                                    language,
                                    null,
                                    sources,
                                    PackageName path,
                                    input.ExposedModules,
                                    writer.Target,
                                    ""
                                )
                                |> Result.mapError (fun failure -> failure.ToString())
                                |> Result.map (fun request -> writer, request)

            match prepared with
            | Error message -> return Error(observationFailure message)
            | Ok(writer, request) ->
                let! outcome = frontend.CompileAsync(request, cancellationToken)

                match outcome with
                | FrontendOutcome.Failed failure ->
                    return
                        Error
                            {
                                Message = failure.Message
                                Partial =
                                    Some
                                        {
                                            Success = false
                                            ModelJson = None
                                            Diagnostics = keys failure.Diagnostics
                                        }
                            }
                | FrontendOutcome.Completed result ->
                    let written =
                        result
                        |> Checked.bind (fun compiled ->
                            writer.Writer.Write(
                                writer.Target,
                                WriteOptions.Compact,
                                compiled.Model
                            )
                        )

                    return
                        Ok
                            {
                                Success = written.IsPassed
                                ModelJson =
                                    Checked.tryValue written
                                    |> Option.map Encoding.UTF8.GetString
                                Diagnostics = keys written.Diagnostics
                            }
        }

    /// <summary>
    /// The <c>library</c> adapter for <paramref name="frontend"/>. The model of a successful compilation is
    /// written as compact JSON by the writer that <paramref name="writers"/> selects for the case's IR version.
    /// </summary>
    let create identity (frontend: IFrontend) (writers: WriterRegistry) : Adapter =
        LibraryAdapter.create identity (observe frontend writers)
