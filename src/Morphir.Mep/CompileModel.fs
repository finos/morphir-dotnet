// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Buffers
open System.Collections.Generic
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// The result of morphir.frontend.compileModel: a success with a complete model, or a failure without one. Source
/// problems are failures with diagnostics, not JSON-RPC errors.
[<RequireQualifiedAccess>]
type CompileModelResult =
    /// The compilation gave a model. The diagnostics have no error.
    | Compiled of
        model: MorphirModel *
        diagnostics: IReadOnlyList<Diagnostic> *
        modules: IReadOnlyList<string>
    /// The compilation failed. The wire result has no model member, not even null.
    | Failed of diagnostics: IReadOnlyList<Diagnostic>

    /// The diagnostics of either case.
    member this.Diagnostics =
        match this with
        | Compiled(_, diagnostics, _) -> diagnostics
        | Failed diagnostics -> diagnostics

    /// Handles every case for C# callers.
    member this.Match
        (
            compiled: Func<MorphirModel, IReadOnlyList<Diagnostic>, IReadOnlyList<string>, 'T>,
            failed: Func<IReadOnlyList<Diagnostic>, 'T>
        ) =
        match this with
        | Compiled(model, diagnostics, modules) -> compiled.Invoke(model, diagnostics, modules)
        | Failed diagnostics -> failed.Invoke diagnostics

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | Compiled(model, diagnostics, _) ->
            $"Compiled {model} with {diagnostics.Count} diagnostics"
        | Failed diagnostics -> $"Failed with {diagnostics.Count} diagnostics"

/// Why a compileModel request or result is refused. Each refusal is a -32602 at the protocol boundary.
[<RequireQualifiedAccess>]
type CompileModelFailure =
    /// The output tuple or the options IR version is not admitted (see ModelTransport.checkCompileOutput).
    | Admission of failure: AdmissionFailure
    /// The options use a member name that the contract reserves or refuses.
    | ReservedOption of name: string
    /// The result is not an object with a boolean success member and the members of its case.
    | MalformedResult of reason: string

    /// The stable code: the admission code, reserved_option or malformed_result.
    member this.Code =
        match this with
        | Admission failure -> failure.Code
        | ReservedOption _ -> "reserved_option"
        | MalformedResult _ -> "malformed_result"

    /// Stable diagnostic text.
    override this.ToString() =
        match this with
        | Admission failure -> failure.ToString()
        | ReservedOption name -> $"The compile options cannot have a member named {name}."
        | MalformedResult reason -> $"The compileModel result is malformed: {reason}"

/// Rules of morphir.frontend.compileModel that the generated codecs cannot express: the reserved option names, the
/// request checks, the result sum and the context digest.
[<RequireQualifiedAccess>]
module CompileModel =

    /// Option names that cannot be extra members: the named members, and the legacy source-root keys, whose value
    /// belongs in SourceSet.root.
    let private reserved = [ "typesOnly"; "irVersion"; "sourceRootUri"; "sourceRoot" ]

    /// Refuses options with an extra member that the contract reserves. A writer and a reader both call it.
    [<CompiledName "CheckOptions">]
    let checkOptions (options: CompileOptions) : Result<CompileOptions, CompileModelFailure> =
        match
            reserved
            |> List.tryFind (fun name -> options.Extra.ContainsKey name)
        with
        | Some name -> Error(CompileModelFailure.ReservedOption name)
        | None -> Ok options

    /// Checks a request against the session agreement: the output must be an agreed compileModel tuple, the options
    /// IR version must equal its IR version, and the options must not use a reserved member.
    [<CompiledName "CheckRequest">]
    let checkRequest
        (agreed: ModelTransportSupport option)
        (request: CompileModelRequest)
        : Result<CompileModelRequest, CompileModelFailure> =
        ModelTransport.checkCompileOutput agreed request.Output request.Options.IrVersion
        |> Result.mapError CompileModelFailure.Admission
        |> Result.bind (fun _ -> checkOptions request.Options)
        |> Result.map (fun _ -> request)

    let private listOf (name: string) (read: JsonElement -> 'T option) (element: JsonElement) =
        match element.TryGetProperty name with
        | false, _ -> Ok(Array.empty :> IReadOnlyList<'T>)
        | true, value when value.ValueKind = JsonValueKind.Array ->
            let items = [| for item in value.EnumerateArray() -> read item |]

            if
                items
                |> Array.forall Option.isSome
            then
                Ok(
                    items
                    |> Array.map Option.get
                    :> IReadOnlyList<'T>
                )
            else
                Error(CompileModelFailure.MalformedResult $"{name} has an invalid item")
        | true, _ -> Error(CompileModelFailure.MalformedResult $"{name} is not an array")

    let private diagnosticsOf =
        listOf
            "diagnostics"
            (fun item ->
                match JsonSerializer.Deserialize(item, MepJson.Default.Diagnostic) with
                | null -> None
                | diagnostic -> Some diagnostic
            )

    let private modulesOf =
        listOf
            "modules"
            (fun item ->
                if item.ValueKind = JsonValueKind.String then
                    Some(
                        item.GetString()
                        |> nonNull
                    )
                else
                    None
            )

    let private modelOf (element: JsonElement) =
        match element.TryGetProperty "model" with
        | false, _ -> Ok ModelMember.Absent
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok ModelMember.Null
        | true, value ->
            match JsonSerializer.Deserialize(value, MepJson.Default.MorphirModel) with
            | null -> Error(CompileModelFailure.MalformedResult "model is null")
            | model -> Ok(ModelMember.Present model)

    /// Reads a compileModel result. success must be a JSON boolean. A success must have a model, and a failure must
    /// not have a model member, also not as null. Absent diagnostics and modules are read as empty. The model still
    /// needs admission for the output tuple (ModelTransport.admit).
    [<CompiledName "ReadResult">]
    let readResult (element: JsonElement) : Result<CompileModelResult, CompileModelFailure> =
        try
            if
                element.ValueKind
                <> JsonValueKind.Object
            then
                Error(CompileModelFailure.MalformedResult "the result is not an object")
            else
                match element.TryGetProperty "success" with
                | true, success when
                    success.ValueKind = JsonValueKind.True
                    || success.ValueKind = JsonValueKind.False
                    ->
                    let succeeded = success.GetBoolean()

                    modelOf element
                    |> Result.bind (fun model ->
                        ModelTransport.checkCompileOutcome succeeded model
                        |> Result.mapError CompileModelFailure.Admission
                    )
                    |> Result.bind (fun model ->
                        diagnosticsOf element
                        |> Result.bind (fun diagnostics ->
                            match model with
                            | Some model ->
                                modulesOf element
                                |> Result.map (fun modules ->
                                    CompileModelResult.Compiled(model, diagnostics, modules)
                                )
                            | None -> Ok(CompileModelResult.Failed diagnostics)
                        )
                    )
                | _ -> Error(CompileModelFailure.MalformedResult "success is not a JSON boolean")
        with :? JsonException as error ->
            Error(CompileModelFailure.MalformedResult error.Message)

    let private writeDiagnostics (writer: Utf8JsonWriter) (diagnostics: IReadOnlyList<Diagnostic>) =
        writer.WriteStartArray "diagnostics"

        for diagnostic in diagnostics do
            JsonSerializer.Serialize(writer, diagnostic, MepJson.Default.Diagnostic)

        writer.WriteEndArray()

    /// Writes a compileModel result. A failure has no model member and no modules member.
    [<CompiledName "WriteResult">]
    let writeResult (result: CompileModelResult) : JsonElement =
        let buffer = ArrayBufferWriter<byte>()

        do
            use writer = new Utf8JsonWriter(buffer)
            writer.WriteStartObject()

            match result with
            | CompileModelResult.Compiled(model, diagnostics, modules) ->
                writer.WriteBoolean("success", true)
                writer.WritePropertyName "model"
                JsonSerializer.Serialize(writer, model, MepJson.Default.MorphirModel)
                writeDiagnostics writer diagnostics
                writer.WriteStartArray "modules"

                for name in modules do
                    writer.WriteStringValue name

                writer.WriteEndArray()
            | CompileModelResult.Failed diagnostics ->
                writer.WriteBoolean("success", false)
                writeDiagnostics writer diagnostics

            writer.WriteEndObject()

        use document = JsonDocument.Parse(buffer.WrittenMemory)
        document.RootElement.Clone()

    /// RFC 8785 (JCS) text of a JSON number: the ECMAScript Number-to-String form of the IEEE double.
    let private canonicalNumber (value: float) =
        if
            Double.IsNaN value
            || Double.IsInfinity value
        then
            raise (JsonException "RFC 8785 has no form for NaN or an infinity.")
        elif value = 0.0 then
            "0"
        else
            let sign = if value < 0.0 then "-" else ""
            let text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture)

            let mantissa, exponent =
                match text.IndexOfAny [| 'E'; 'e' |] with
                | -1 -> text, 0
                | index ->
                    text.Substring(0, index),
                    Int32.Parse(text.Substring(index + 1), CultureInfo.InvariantCulture)

            let point = mantissa.IndexOf '.'
            let integral = if point < 0 then mantissa else mantissa.Substring(0, point)
            let fraction = if point < 0 then "" else mantissa.Substring(point + 1)

            let raw =
                integral
                + fraction

            let leading =
                raw.Length
                - raw.TrimStart('0').Length

            let digits = raw.Trim('0')
            // value = 0.digits × 10^n
            let n =
                integral.Length
                + exponent
                - leading

            let k = digits.Length

            let body =
                if
                    k <= n
                    && n <= 21
                then
                    digits
                    + String('0', n - k)
                elif
                    0 < n
                    && n <= 21
                then
                    digits.Substring(0, n)
                    + "."
                    + digits.Substring n
                elif
                    -6 < n
                    && n <= 0
                then
                    "0."
                    + String('0', -n)
                    + digits
                else
                    let e = n - 1

                    let exponentText =
                        (if e < 0 then "-" else "+")
                        + string (abs e)

                    if k = 1 then
                        digits
                        + "e"
                        + exponentText
                    else
                        digits.Substring(0, 1)
                        + "."
                        + digits.Substring 1
                        + "e"
                        + exponentText

            sign
            + body

    let private canonicalString (builder: StringBuilder) (text: string) =
        builder.Append '"'
        |> ignore

        for character in text do
            match character with
            | '"' ->
                builder.Append "\\\""
                |> ignore
            | '\\' ->
                builder.Append "\\\\"
                |> ignore
            | '\b' ->
                builder.Append "\\b"
                |> ignore
            | '\f' ->
                builder.Append "\\f"
                |> ignore
            | '\n' ->
                builder.Append "\\n"
                |> ignore
            | '\r' ->
                builder.Append "\\r"
                |> ignore
            | '\t' ->
                builder.Append "\\t"
                |> ignore
            | c when c < ' ' ->
                builder
                    .Append("\\u")
                    .Append((int c).ToString("x4", CultureInfo.InvariantCulture))
                |> ignore
            | c ->
                builder.Append c
                |> ignore

        builder.Append '"'
        |> ignore

    /// RFC 8785 (JCS) text of a JSON value: object members sorted by their UTF-16 code units, no whitespace.
    let rec private canonical (builder: StringBuilder) (element: JsonElement) : unit =
        match element.ValueKind with
        | JsonValueKind.Object ->
            builder.Append '{'
            |> ignore

            element.EnumerateObject()
            |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.Name, right.Name))
            |> Seq.iteri (fun index property ->
                if index > 0 then
                    builder.Append ','
                    |> ignore

                canonicalString builder property.Name

                builder.Append ':'
                |> ignore

                canonical builder property.Value
            )

            builder.Append '}'
            |> ignore
        | JsonValueKind.Array ->
            builder.Append '['
            |> ignore

            element.EnumerateArray()
            |> Seq.iteri (fun index item ->
                if index > 0 then
                    builder.Append ','
                    |> ignore

                canonical builder item
            )

            builder.Append ']'
            |> ignore
        | JsonValueKind.String ->
            canonicalString
                builder
                (element.GetString()
                 |> nonNull)
        | JsonValueKind.Number ->
            builder.Append(canonicalNumber (element.GetDouble()))
            |> ignore
        | JsonValueKind.True ->
            builder.Append "true"
            |> ignore
        | JsonValueKind.False ->
            builder.Append "false"
            |> ignore
        | _ ->
            builder.Append "null"
            |> ignore

    /// The RFC 8785 (JCS) canonical text of a JSON value: object members sorted by their UTF-16 code units, numbers
    /// in the ECMAScript form of their IEEE double, the minimal string escapes, and no whitespace.
    [<CompiledName "CanonicalJson">]
    let canonicalJson (element: JsonElement) : string =
        let builder = StringBuilder()
        canonical builder element
        builder.ToString()

    /// The context object of a request, before canonicalization. See contextDigest.
    let private contextObject (request: CompileModelRequest) : JsonElement =
        let buffer = ArrayBufferWriter<byte>()

        do
            use writer = new Utf8JsonWriter(buffer)
            writer.WriteStartObject()
            writer.WriteString("languageId", request.LanguageId)

            match request.Sources.Root with
            | Some root -> writer.WriteString("sourceRoot", root)
            | None -> writer.WriteNull "sourceRoot"

            writer.WriteStartArray "documents"

            for document in request.Sources.Documents do
                writer.WriteStartObject()
                writer.WriteString("uri", document.Uri)
                writer.WriteString("languageId", document.LanguageId)

                writer.WriteString(
                    "textSha256",
                    (Sha256Digest.Compute(Encoding.UTF8.GetBytes document.Text)).Value
                )

                writer.WriteEndObject()

            writer.WriteEndArray()
            writer.WriteString("package", request.Package.Name)

            match request.Package.ExposedModules with
            | Some modules ->
                writer.WriteStartArray "exposedModules"

                for name in modules do
                    writer.WriteStringValue name

                writer.WriteEndArray()
            | None -> writer.WriteNull "exposedModules"

            writer.WriteStartArray "dependencies"

            for dependency in request.Dependencies do
                writer.WriteStartObject()
                writer.WriteString("packageName", dependency.PackageName)
                writer.WriteString("modelId", dependency.Model.ModelId)
                writer.WriteString("revision", dependency.Model.Revision)
                writer.WriteString("sha256", dependency.Model.Payload.Sha256.Value)
                writer.WriteEndObject()

            writer.WriteEndArray()
            writer.WritePropertyName "options"
            JsonSerializer.Serialize(writer, request.Options, MepJson.Default.CompileOptions)
            writer.WritePropertyName "output"
            JsonSerializer.Serialize(writer, request.Output, MepJson.Default.ModelTransportTuple)
            writer.WriteEndObject()

        use document = JsonDocument.Parse(buffer.WrittenMemory)
        document.RootElement.Clone()

    /// The context digest of a request: the lowercase SHA-256 of the RFC 8785 canonical UTF-8 JSON of an object with
    /// these members: languageId; sourceRoot (null when absent); documents, in order, each with uri, languageId and
    /// textSha256 (the SHA-256 of the UTF-8 text); package (the package name); exposedModules (null when absent);
    /// dependencies, in order, each with packageName, modelId, revision and sha256 (the payload digest); options (the
    /// wire options, extra members included); and output (the wire output tuple). The contextDigest member of the
    /// request does not take part. Document versions do not take part, because the text digest covers the content.
    [<CompiledName "ContextDigest">]
    let contextDigest (request: CompileModelRequest) : Sha256Digest =
        Sha256Digest.Compute(Encoding.UTF8.GetBytes(canonicalJson (contextObject request)))
