// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

open Fantomas.Core
open Fantomas.FCS.Syntax

/// What the generator reads from one input file.
type SourceFile =
    {
        /// The file name, for error messages.
        File: string
        /// The namespaces and top-level modules that the input declares, opened by the generated file.
        Scopes: string list
    }

/// Reads the untyped syntax tree of an input file.
module Syntax =
    let private dotted (ids: Fantomas.FCS.Syntax.LongIdent) =
        ids |> List.map (fun id -> id.idText) |> String.concat "."

    let private scopes (input: ParsedInput) =
        match input with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = contents)) ->
            contents
            |> List.choose (fun (SynModuleOrNamespace(longId = ids; kind = kind)) ->
                match kind with
                | SynModuleOrNamespaceKind.DeclaredNamespace
                | SynModuleOrNamespaceKind.NamedModule -> Some(dotted ids)
                | SynModuleOrNamespaceKind.AnonModule
                | SynModuleOrNamespaceKind.GlobalNamespace -> None)
        | ParsedInput.SigFile _ -> []

    /// Parses F# source text. A syntax error is a generation error that names the file.
    let read (file: string) (source: string) : Result<SourceFile, GenerationError list> =
        try
            match CodeFormatter.ParseAsync(false, source) |> Async.RunSynchronously |> Array.tryHead with
            | Some(input, _) -> Ok { File = file; Scopes = scopes input }
            | None -> Error [ GenerationError.ParseFailed(file, "the parser returned no syntax tree.") ]
        with ex ->
            Error [ GenerationError.ParseFailed(file, ex.Message) ]
