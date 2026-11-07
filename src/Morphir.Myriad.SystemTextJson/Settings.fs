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
    /// A type or member that the generator cannot encode, at a place in the input file.
    | Unsupported of file: string * location: Location * typeName: string * memberName: string option * reason: string

    /// The text of the error, for a person.
    static member describe(error: GenerationError) =
        match error with
        | MissingSetting name -> $"The context setting '{name}' is required."
        | InvalidSetting(name, value, reason) -> $"The context setting '{name}' has the value '{value}', which is not valid: {reason}"
        | UnknownSetting name -> $"The context setting '{name}' is not known."
        | ParseFailed(file, message) -> $"The input file '{file}' could not be parsed: {message}"
        | Unsupported(_, _, typeName, Some memberName, reason) -> $"{typeName}.{memberName}: {reason}"
        | Unsupported(_, _, typeName, None, reason) -> $"{typeName}: {reason}"

    /// The error as one line in the MSBuild canonical form, which the MSBuild task reads from stderr.
    static member toBuildLine(error: GenerationError) =
        match error with
        | Unsupported(file, location, _, _, _) -> $"{file}({location.Line},{location.Column}): error MSTJ0010: {GenerationError.describe error}"
        | ParseFailed(file, _) -> $"{file}: error MSTJ0005: {GenerationError.describe error}"
        | _ -> $"error MSTJ0001: {GenerationError.describe error}"

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

/// How member and case names are made from F# names. Same names as JsonNaming in the annotations package.
[<RequireQualifiedAccess>]
type Naming =
    | AsIs
    | CamelCase
    | PascalCase
    | SnakeCaseLower
    | SnakeCaseUpper
    | KebabCaseLower
    | KebabCaseUpper

    /// Parses the name of a naming policy.
    static member Parse(text: string) : Result<Naming, string> =
        match text with
        | "AsIs" -> Ok AsIs
        | "CamelCase" -> Ok CamelCase
        | "PascalCase" -> Ok PascalCase
        | "SnakeCaseLower" -> Ok SnakeCaseLower
        | "SnakeCaseUpper" -> Ok SnakeCaseUpper
        | "KebabCaseLower" -> Ok KebabCaseLower
        | "KebabCaseUpper" -> Ok KebabCaseUpper
        | _ -> Error "use AsIs, CamelCase, PascalCase, SnakeCaseLower, SnakeCaseUpper, KebabCaseLower or KebabCaseUpper."

    /// Applies the policy to an F# name. The System.Text.Json policies give the same names as at run time.
    member this.Apply(name: string) : string =
        let policy (p: System.Text.Json.JsonNamingPolicy) = p.ConvertName name

        match this with
        | AsIs -> name
        | CamelCase -> policy System.Text.Json.JsonNamingPolicy.CamelCase
        | PascalCase when name.Length > 0 -> string (Char.ToUpperInvariant name[0]) + name.Substring 1
        | PascalCase -> name
        | SnakeCaseLower -> policy System.Text.Json.JsonNamingPolicy.SnakeCaseLower
        | SnakeCaseUpper -> policy System.Text.Json.JsonNamingPolicy.SnakeCaseUpper
        | KebabCaseLower -> policy System.Text.Json.JsonNamingPolicy.KebabCaseLower
        | KebabCaseUpper -> policy System.Text.Json.JsonNamingPolicy.KebabCaseUpper

/// How an F# map or dictionary is written. Same names as JsonMapFormat in the annotations package.
[<RequireQualifiedAccess>]
type MapFormat =
    | ObjectOrArrayOfPairs
    | Object
    | ArrayOfPairs

    /// Parses the name of a map format.
    static member Parse(text: string) : Result<MapFormat, string> =
        match text with
        | "ObjectOrArrayOfPairs" -> Ok ObjectOrArrayOfPairs
        | "Object" -> Ok Object
        | "ArrayOfPairs" -> Ok ArrayOfPairs
        | _ -> Error "use ObjectOrArrayOfPairs, Object or ArrayOfPairs."

/// The member rules of records: the context defaults, which JsonRecord can override for one record.
type RecordRules =
    {
        Naming: Naming
        /// True: a member whose value is None or ValueNone is left out.
        SkipAbsentOptions: bool
        /// True: a reader refuses a member that the record does not declare.
        RefuseUnknownMembers: bool
        /// True: a reader accepts null for a field that is not an option, where the field type accepts null.
        AllowNullFields: bool
    }

    /// The defaults of FSharp.SystemTextJson.
    static member Default =
        { Naming = Naming.AsIs; SkipAbsentOptions = false; RefuseUnknownMembers = false; AllowNullFields = false }

/// The settings of one context, from the FSharpJsonContext item in the project file.
type ContextSettings =
    {
        /// The name of the generated context type.
        Name: ContextName
        /// The namespace of the generated file.
        Namespace: ContextNamespace
        /// The default member rules of records.
        Records: RecordRules
        /// True: every number in the context is also read from a JSON string.
        NumbersFromStrings: bool
        /// How maps and dictionaries are written.
        MapFormat: MapFormat
    }

/// Reads context settings from the Myriad additional parameters.
module ContextSettings =
    let private known = set [ "Name"; "Namespace"; "Naming"; "SkippableOptionFields"; "UnknownMembers"; "AllowNullFields"; "AllowNumbersFromStrings"; "MapFormat" ]

    let private required (parameters: IDictionary<string, string>) name (parse: string -> Result<'T, GenerationError>) =
        match parameters.TryGetValue name with
        | true, value -> parse value
        | _ -> Error(GenerationError.MissingSetting name)

    let private optional (parameters: IDictionary<string, string>) name (fallback: 'T) (parse: string -> Result<'T, string>) =
        match parameters.TryGetValue name with
        | true, value ->
            parse value |> Result.mapError (fun reason -> GenerationError.InvalidSetting(name, value, reason))
        | _ -> Ok fallback

    /// Parses true or false.
    let parseBool (text: string) : Result<bool, string> =
        match text.ToLowerInvariant() with
        | "true" -> Ok true
        | "false" -> Ok false
        | _ -> Error "use true or false."

    /// Parses Always or Never (true and false are also accepted).
    let parseSkippable (text: string) : Result<bool, string> =
        match text with
        | "Always" -> Ok true
        | "Never" -> Ok false
        | _ -> parseBool text |> Result.mapError (fun _ -> "use Always or Never.")

    /// Parses Ignore or Refuse.
    let parseUnknownMembers (text: string) : Result<bool, string> =
        match text with
        | "Ignore" -> Ok false
        | "Refuse" -> Ok true
        | _ -> Error "use Ignore or Refuse."

    /// Parses the settings. Returns every error, not only the first.
    let parse (parameters: IDictionary<string, string>) : Result<ContextSettings, GenerationError list> =
        let unknown =
            parameters.Keys
            |> Seq.filter (fun key -> not (known.Contains key))
            |> Seq.sort
            |> Seq.map GenerationError.UnknownSetting
            |> Seq.toList

        let defaults = RecordRules.Default
        let name = required parameters "Name" ContextName.Parse
        let ns = required parameters "Namespace" ContextNamespace.Parse
        let naming = optional parameters "Naming" defaults.Naming Naming.Parse
        let skip = optional parameters "SkippableOptionFields" defaults.SkipAbsentOptions parseSkippable
        let refuse = optional parameters "UnknownMembers" defaults.RefuseUnknownMembers parseUnknownMembers
        let allowNull = optional parameters "AllowNullFields" defaults.AllowNullFields parseBool
        let numbers = optional parameters "AllowNumbersFromStrings" false parseBool
        let mapFormat = optional parameters "MapFormat" MapFormat.ObjectOrArrayOfPairs MapFormat.Parse

        match name, ns, naming, skip, refuse, allowNull, numbers, mapFormat, unknown with
        | Ok name, Ok ns, Ok naming, Ok skip, Ok refuse, Ok allowNull, Ok numbers, Ok mapFormat, [] ->
            Ok
                {
                    Name = name
                    Namespace = ns
                    Records = { Naming = naming; SkipAbsentOptions = skip; RefuseUnknownMembers = refuse; AllowNullFields = allowNull }
                    NumbersFromStrings = numbers
                    MapFormat = mapFormat
                }
        | _ ->
            let errorOf result =
                match result with
                | Error e -> [ e ]
                | Ok _ -> []

            Error(
                errorOf name
                @ errorOf ns
                @ errorOf naming
                @ errorOf skip
                @ errorOf refuse
                @ errorOf allowNull
                @ errorOf numbers
                @ errorOf mapFormat
                @ unknown
            )
