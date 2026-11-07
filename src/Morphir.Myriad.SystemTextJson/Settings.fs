// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

open System
open System.Collections.Generic

/// Why generation stopped. The MSBuild task reports each one as a build error.
[<RequireQualifiedAccess>]
type GenerationError =
    /// A required context setting is absent.
    | MissingSetting of name: string
    /// A context setting has a value that the generator cannot use.
    | InvalidSetting of name: string * value: string * reason: string
    /// A context setting that the generator does not know, often a misspelling.
    | UnknownSetting of name: string
    /// The input file is not valid F#.
    | ParseFailed of file: string * message: string

    /// The text of the error, for a person.
    static member describe(error: GenerationError) =
        match error with
        | MissingSetting name -> $"The context setting '{name}' is required."
        | InvalidSetting(name, value, reason) -> $"The context setting '{name}' has the value '{value}', which is not valid: {reason}"
        | UnknownSetting name -> $"The context setting '{name}' is not known."
        | ParseFailed(file, message) -> $"The input file '{file}' could not be parsed: {message}"

/// The rules for F# identifiers that the generator writes.
module internal Identifiers =
    let private isStart (c: char) = Char.IsLetter c || c = '_'
    let private isPart (c: char) = Char.IsLetterOrDigit c || c = '_' || c = '\''

    /// True when the text is one F# identifier that needs no double backticks.
    let isIdentifier (text: string) =
        text.Length > 0 && isStart text[0] && Seq.forall isPart text

/// The name of a generated context type: one F# identifier.
[<CustomEquality; NoComparison>]
type ContextName =
    private
    | ContextName of string

    /// The identifier text.
    member this.Value = let (ContextName value) = this in value

    override this.ToString() = this.Value
    override this.Equals(other: obj | null) =
        match other with
        | :? ContextName as name -> String.Equals(this.Value, name.Value, StringComparison.Ordinal)
        | _ -> false
    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Value

    /// Parses a context name.
    static member Parse(text: string) : Result<ContextName, GenerationError> =
        if Identifiers.isIdentifier text then
            Ok(ContextName text)
        else
            Error(GenerationError.InvalidSetting("Name", text, "it must be one F# identifier, such as AppJson."))

/// The namespace of a generated context: F# identifiers joined by dots.
[<CustomEquality; NoComparison>]
type ContextNamespace =
    private
    | ContextNamespace of string

    /// The dotted text.
    member this.Value = let (ContextNamespace value) = this in value

    override this.ToString() = this.Value
    override this.Equals(other: obj | null) =
        match other with
        | :? ContextNamespace as name -> String.Equals(this.Value, name.Value, StringComparison.Ordinal)
        | _ -> false
    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Value

    /// Parses a context namespace.
    static member Parse(text: string) : Result<ContextNamespace, GenerationError> =
        if text.Split '.' |> Array.forall Identifiers.isIdentifier then
            Ok(ContextNamespace text)
        else
            Error(GenerationError.InvalidSetting("Namespace", text, "it must be F# identifiers joined by dots, such as Sample.Json."))

/// The settings of one context, from the FSharpJsonContext item in the project file.
type ContextSettings =
    {
        /// The name of the generated context type.
        Name: ContextName
        /// The namespace of the generated file.
        Namespace: ContextNamespace
    }

/// Reads context settings from the Myriad additional parameters.
module ContextSettings =
    let private known = set [ "Name"; "Namespace" ]

    let private required (parameters: IDictionary<string, string>) name (parse: string -> Result<'T, GenerationError>) =
        match parameters.TryGetValue name with
        | true, value -> parse value
        | _ -> Error(GenerationError.MissingSetting name)

    /// Parses the settings. Returns every error, not only the first.
    let parse (parameters: IDictionary<string, string>) : Result<ContextSettings, GenerationError list> =
        let unknown =
            parameters.Keys
            |> Seq.filter (fun key -> not (known.Contains key))
            |> Seq.sort
            |> Seq.map GenerationError.UnknownSetting
            |> Seq.toList

        let name = required parameters "Name" ContextName.Parse
        let ns = required parameters "Namespace" ContextNamespace.Parse

        match name, ns, unknown with
        | Ok name, Ok ns, [] -> Ok { Name = name; Namespace = ns }
        | _ ->
            let errors =
                [ match name with Error e -> yield e | Ok _ -> ()
                  match ns with Error e -> yield e | Ok _ -> ()
                  yield! unknown ]

            Error errors
