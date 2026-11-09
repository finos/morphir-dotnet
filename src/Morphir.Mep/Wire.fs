// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text.RegularExpressions
open Morphir.Myriad.SystemTextJson

/// A refused protocol version, before it can enter a protocol message.
[<RequireQualifiedAccess; JsonIgnoreType>]
type ParseFailure =
    /// Not a full SemVer protocol version and not the legacy 0.1.
    | InvalidProtocolVersion
    /// Not a full SemVer version.
    | InvalidSemanticVersion
    /// Not 64 lowercase hexadecimal digits.
    | InvalidSha256Digest

    /// Stable diagnostic text, without reflection.
    override this.ToString() =
        match this with
        | InvalidProtocolVersion -> "Expected a full SemVer protocol version or the frozen legacy version 0.1."
        | InvalidSemanticVersion -> "Expected a full SemVer version, such as 0.1.0-draft.1."
        | InvalidSha256Digest -> "Expected a SHA-256 digest of 64 lowercase hexadecimal digits."

module internal Syntax =
    let private matches (pattern: string) (value: string) =
        Regex.IsMatch(
            value,
            pattern,
            RegexOptions.CultureInvariant ||| RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds 1.
        )

    let private semVerPattern =
        let number = "(0|[1-9][0-9]*)"
        let identifier = "(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
        $"\\A{number}\\.{number}\\.{number}(-{identifier}(\\.{identifier})*)?(\\+[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?\\z"

    /// True when the text is a full SemVer 2.0.0 version.
    let isSemVer (value: string) = matches semVerPattern value

    /// True when the text is 64 lowercase hexadecimal digits.
    let isSha256 (value: string) =
        value.Length = 64 && value |> Seq.forall (fun c -> Char.IsAsciiDigit c || (c >= 'a' && c <= 'f'))

/// A full SemVer version or the frozen legacy spelling. Negotiation accepts only the pinned draft.
[<JsonParsedValue(TextMember = "Value"); CustomEquality; CustomComparison>]
type ProtocolVersion =
    private
    | ProtocolVersion of string

    /// The original wire spelling, used for exact draft negotiation.
    member this.Value = let (ProtocolVersion value) = this in value

    /// Parses untrusted protocol metadata. Only the frozen legacy spelling 0.1 is a major.minor version.
    static member Parse(text: string | null) : Result<ProtocolVersion, ParseFailure> =
        match text with
        | null -> Error ParseFailure.InvalidProtocolVersion
        | "0.1" -> Ok(ProtocolVersion "0.1")
        | value when Syntax.isSemVer value -> Ok(ProtocolVersion value)
        | _ -> Error ParseFailure.InvalidProtocolVersion

    /// Parses untrusted text for C# callers, with flow-aware result and failure outputs.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<ProtocolVersion>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) =
        match ProtocolVersion.Parse text with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Builds a version from a source literal and throws if it is invalid.
    static member Create(literal: string) =
        match ProtocolVersion.Parse literal with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())

    /// Exact wire spelling.
    override this.ToString() = this.Value

    /// Protocol identities compare ordinally.
    override this.Equals(other: obj | null) =
        match other with
        | :? ProtocolVersion as value -> String.Equals(this.Value, value.Value, StringComparison.Ordinal)
        | _ -> false

    /// Ordinal identity hash.
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? ProtocolVersion as value -> String.CompareOrdinal(this.Value, value.Value)
            | _ -> invalidArg (nameof other) "Not a protocol version."

/// A full SemVer 2.0.0 version, such as an envelope draft, an IR version or a claims version.
[<JsonParsedValue(TextMember = "Value"); CustomEquality; CustomComparison>]
type SemanticVersion =
    private
    | SemanticVersion of string

    /// The original wire spelling. Versions compare by exact spelling.
    member this.Value = let (SemanticVersion value) = this in value

    /// Parses untrusted text.
    static member Parse(text: string | null) : Result<SemanticVersion, ParseFailure> =
        match text with
        | NonNull value when Syntax.isSemVer value -> Ok(SemanticVersion value)
        | _ -> Error ParseFailure.InvalidSemanticVersion

    /// Parses untrusted text for C# callers, with flow-aware result and failure outputs.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<SemanticVersion>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) =
        match SemanticVersion.Parse text with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Builds a version from a source literal and throws if it is invalid.
    static member Create(literal: string) =
        match SemanticVersion.Parse literal with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())

    /// Exact wire spelling.
    override this.ToString() = this.Value

    /// Versions compare by exact ordinal spelling.
    override this.Equals(other: obj | null) =
        match other with
        | :? SemanticVersion as value -> String.Equals(this.Value, value.Value, StringComparison.Ordinal)
        | _ -> false

    /// Ordinal identity hash.
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? SemanticVersion as value -> String.CompareOrdinal(this.Value, value.Value)
            | _ -> invalidArg (nameof other) "Not a semantic version."

/// A SHA-256 digest: 64 lowercase hexadecimal digits.
[<JsonParsedValue(TextMember = "Value"); CustomEquality; CustomComparison>]
type Sha256Digest =
    private
    | Sha256Digest of string

    /// The 64 lowercase hexadecimal digits.
    member this.Value = let (Sha256Digest value) = this in value

    /// Parses untrusted text. Uppercase digits are refused, as the contract says.
    static member Parse(text: string | null) : Result<Sha256Digest, ParseFailure> =
        match text with
        | NonNull value when Syntax.isSha256 value -> Ok(Sha256Digest value)
        | _ -> Error ParseFailure.InvalidSha256Digest

    /// Parses untrusted text for C# callers, with flow-aware result and failure outputs.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<Sha256Digest>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) =
        match Sha256Digest.Parse text with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Builds a digest from bytes that are already hashed.
    static member OfHash(hash: ReadOnlySpan<byte>) =
        if hash.Length <> 32 then
            invalidArg (nameof hash) "A SHA-256 hash has 32 bytes."

        Sha256Digest(Convert.ToHexStringLower hash)

    /// Hashes the bytes.
    static member Compute(bytes: ReadOnlySpan<byte>) =
        Sha256Digest(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData bytes))

    /// Builds a digest from a source literal and throws if it is invalid.
    static member Create(literal: string) =
        match Sha256Digest.Parse literal with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())

    /// The digits.
    override this.ToString() = this.Value

    /// Digests compare ordinally.
    override this.Equals(other: obj | null) =
        match other with
        | :? Sha256Digest as value -> String.Equals(this.Value, value.Value, StringComparison.Ordinal)
        | _ -> false

    /// Ordinal identity hash.
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? Sha256Digest as value -> String.CompareOrdinal(this.Value, value.Value)
            | _ -> invalidArg (nameof other) "Not a SHA-256 digest."

/// The complete describe params object, including when it has only this one member.
type DescribeParams =
    {
        /// The MEP versions that the host offers.
        ProtocolVersions: IReadOnlyList<ProtocolVersion>
    }

    /// Stable text for diagnostics.
    override this.ToString() =
        $"Describe ({this.ProtocolVersions.Count} protocol versions)"

/// A JSON-RPC request id: an unsigned 64-bit number or a string. The reply writes the same token.
[<RequireQualifiedAccess; JsonUnion(BaseEncoding = (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.SelectByTokenKind))>]
type RequestId =
    /// A numeric id, 0 through 18446744073709551615.
    | Number of uint64
    /// A string id; it can be empty.
    | Text of string

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | Number value -> string value
        | Text value -> "\"" + value + "\""

/// A kind of extension. Wire values: frontend, backend, transform, validator, workspace.
[<RequireQualifiedAccess>]
type ExtensionType =
    | Frontend
    | Backend
    | Transform
    | Validator
    | Workspace

    /// The wire value.
    member this.WireValue =
        match this with
        | Frontend -> "frontend"
        | Backend -> "backend"
        | Transform -> "transform"
        | Validator -> "validator"
        | Workspace -> "workspace"

    /// The wire value.
    override this.ToString() = this.WireValue

/// What kind of peer introduced itself. A kind this contract does not know reads as Unspecified.
[<RequireQualifiedAccess; JsonParsedValue(TextMember = "WireValue")>]
type PeerKind =
    | Cli
    | Unspecified

    /// The wire value.
    member this.WireValue =
        match this with
        | Cli -> "cli"
        | Unspecified -> "unspecified"

    /// Reads a wire value. Every unknown value is Unspecified, as the contract says.
    static member Parse(text: string | null) : Result<PeerKind, string> =
        match text with
        | "cli" -> Ok Cli
        | _ -> Ok Unspecified

    /// The wire value.
    override this.ToString() = this.WireValue

/// A host or an extension, as it introduces itself. An absent kind reads as Unspecified.
type PeerInfo =
    {
        /// The kind of peer.
        [<JsonDefaultValue("unspecified")>]
        Kind: PeerKind
        /// The peer name.
        Name: string
        /// The peer version, as the peer writes it.
        Version: string
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Name} {this.Version} ({this.Kind})"

/// Who an extension is. The wire names of this record are snake_case, such as min_sdk_version.
[<JsonRecord(Naming = JsonNaming.SnakeCaseLower)>]
type ExtensionInfo =
    {
        /// The extension id.
        Id: string
        /// The display name.
        Name: string
        /// The extension version.
        Version: string
        /// An optional description; left out when absent.
        Description: string option
        /// The extension types.
        Types: IReadOnlyList<ExtensionType>
        /// An optional author; left out when absent.
        Author: string option
        /// An optional home page; left out when absent.
        Homepage: string option
        /// An optional license; left out when absent.
        License: string option
        /// The lowest SDK version the extension needs; left out when absent.
        MinSdkVersion: string option
    }

    /// For C# callers: the description, or null when absent.
    member this.DescriptionOrNull() : string | null = Option.toObj this.Description

    /// For C# callers: the author, or null when absent.
    member this.AuthorOrNull() : string | null = Option.toObj this.Author

    /// For C# callers: the home page, or null when absent.
    member this.HomepageOrNull() : string | null = Option.toObj this.Homepage

    /// For C# callers: the license, or null when absent.
    member this.LicenseOrNull() : string | null = Option.toObj this.License

    /// For C# callers: the lowest SDK version, or null when absent.
    member this.MinSdkVersionOrNull() : string | null = Option.toObj this.MinSdkVersion

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Id} {this.Version}"

/// Requirements that a claim set puts on the host. Each entry is one SemVer comparator, such as >=0.4.0.
type ClaimsRequirements =
    {
        /// Host comparators; all must hold. Left out when empty, read as empty when absent.
        [<JsonDefaultEmpty; JsonOmitEmpty>]
        Host: IReadOnlyList<string>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Requires {this.Host.Count} host comparators"

/// The result of morphir.extension.describe: the capabilities an extension claims, before a session starts.
/// Draft.1 claim sets (statementVersion) are read as draft.2 (claimsVersion).
type CapabilityClaimSet =
    {
        /// The claims format version. Writers send 0.1.0-draft.2.
        [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2", CriticalMember = "critical")>]
        ClaimsVersion: SemanticVersion
        /// The MEP versions the extension speaks.
        ProtocolVersions: IReadOnlyList<ProtocolVersion>
        /// Who the extension is.
        Extension: ExtensionInfo
        /// The open capability object. Members this contract does not name are kept.
        Capabilities: IReadOnlyDictionary<string, System.Text.Json.JsonElement>
        /// Requirements on the host; left out when absent.
        Requires: ClaimsRequirements option
        /// Member paths that a reader must understand. Left out when empty.
        [<JsonDefaultEmpty;
          JsonOmitEmpty;
          JsonKnownPaths("claimsVersion",
                         "protocolVersions",
                         "extension",
                         "capabilities",
                         "capabilities.frontend",
                         "capabilities.backend",
                         "capabilities.workspace",
                         "capabilities.modelTransport",
                         "requires",
                         "requires.host")>]
        Critical: IReadOnlyList<string>
    }

    /// For C# callers: the requirements on the host, or null when absent.
    member this.RequiresOrNull() : ClaimsRequirements | null = Option.toObj this.Requires

    /// Stable text for diagnostics.
    override this.ToString() = $"Claims {this.ClaimsVersion} of {this.Extension}"

/// The operation of a model-transport tuple. Wire values are the method names.
[<RequireQualifiedAccess>]
type ModelOperation =
    | [<JsonName "morphir.frontend.compileModel">] CompileModel
    | [<JsonName "morphir.backend.generateModel">] GenerateModel

    /// The wire value.
    member this.WireValue =
        match this with
        | CompileModel -> "morphir.frontend.compileModel"
        | GenerateModel -> "morphir.backend.generateModel"

    /// The wire value.
    override this.ToString() = this.WireValue

/// The representation of a model document. Wire values: ion-binary, morphir-json.
[<RequireQualifiedAccess>]
type ModelCodec =
    | [<JsonName "ion-binary">] IonBinary
    | [<JsonName "morphir-json">] MorphirJson

    /// The wire value.
    member this.WireValue =
        match this with
        | IonBinary -> "ion-binary"
        | MorphirJson -> "morphir-json"

    /// The wire value.
    override this.ToString() = this.WireValue

/// The payload layout. Wire value: single-document.
[<RequireQualifiedAccess>]
type ModelLayout =
    | [<JsonName "single-document">] SingleDocument

    /// The wire value.
    member _.WireValue = "single-document"

    /// The wire value.
    override this.ToString() = this.WireValue

/// One supported combination of operation, profile, codec, envelope version and IR version.
type ModelTransportTuple =
    {
        /// The model operation.
        Operation: ModelOperation
        /// The model-content profile id.
        Profile: string
        /// The codec of the payload.
        Codec: ModelCodec
        /// The envelope draft, such as 0.1.0-draft.1.
        EnvelopeVersion: SemanticVersion
        /// The exact IR format version, such as 4.0.0.
        IrVersion: SemanticVersion
    }

    /// Stable text for diagnostics.
    override this.ToString() =
        $"{this.Operation} {this.Profile} {this.Codec} {this.EnvelopeVersion} IR {this.IrVersion}"

/// Bounds applied before allocation and parsing. Each value is positive and at most 2147483647.
type ModelTransportLimits =
    {
        /// The most UTF-8 base64 content bytes in one message.
        MaxEncodedBytes: int
        /// The most decoded payload bytes in one message.
        MaxDecodedBytes: int
        /// The deepest decoded content.
        MaxDepth: int
        /// The most decoded value nodes.
        MaxNodes: int
    }

    /// Stable text for diagnostics.
    override this.ToString() =
        $"Limits {this.MaxEncodedBytes}/{this.MaxDecodedBytes} bytes, depth {this.MaxDepth}, {this.MaxNodes} nodes"

/// A model-transport offer or the support agreed for a session.
type ModelTransportSupport =
    {
        /// The complete and unique tuples, in preference order.
        Tuples: IReadOnlyList<ModelTransportTuple>
        /// The limits.
        Limits: ModelTransportLimits
        /// Guarantee ids the peer can interpret. Read as empty when absent.
        [<JsonDefaultEmpty>]
        Guarantees: IReadOnlyList<string>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Tuples.Count} tuples, {this.Limits}"

/// Parameters of morphir.initialize. An absent modelTransport means legacy-only.
type InitializeParams =
    {
        /// The MEP versions the host offers.
        ProtocolVersions: IReadOnlyList<ProtocolVersion>
        /// The host.
        Host: PeerInfo
        /// The model-transport offer; left out when absent.
        ModelTransport: ModelTransportSupport option
    }

    /// For C# callers: the model-transport offer, or null when absent (legacy-only).
    member this.ModelTransportOrNull() : ModelTransportSupport | null = Option.toObj this.ModelTransport

    /// Stable text for diagnostics.
    override this.ToString() = $"Initialize from {this.Host}"

/// A source language that a frontend reads.
type LanguageCapability =
    {
        /// The language id, such as elm.
        Id: string
        /// The file extensions, such as .elm.
        FileExtensions: IReadOnlyList<string>
    }

    /// Stable text for diagnostics.
    override this.ToString() = this.Id

/// What a frontend can compile. A frontend without multiDocument compiles one document for each request.
type FrontendCapability =
    {
        /// The languages.
        Languages: IReadOnlyList<LanguageCapability>
        /// The IR versions, as the extension writes them.
        IrVersions: IReadOnlyList<string>
        /// Legacy morphir.frontend.compile support.
        Compile: bool
        /// Incremental compilation support.
        Incremental: bool
        /// Fragment compilation support.
        Fragments: bool
        /// Multi-document support. Written only when true; read as false when absent.
        [<JsonDefaultValue(false); JsonOmitDefault>]
        MultiDocument: bool
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Frontend for {this.Languages.Count} languages"

/// What a backend can generate.
type BackendCapability =
    {
        /// The targets.
        Targets: IReadOnlyList<string>
        /// The IR versions, as the extension writes them.
        IrVersions: IReadOnlyList<string>
        /// Legacy morphir.backend.generate support.
        Generate: bool
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Backend for {this.Targets.Count} targets"

/// Workspace discovery support. The versions are of the workspace discovery protocol, not of MEP.
type WorkspaceCapability =
    {
        /// The workspace discovery protocol versions.
        ProtocolVersions: IReadOnlyList<SemanticVersion>
        /// morphir.workspace.discover support.
        Discover: bool
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Workspace discovery {this.Discover}"

/// A required guarantee for an exact model or node scope.
type ModelGuaranteeRequirement =
    {
        /// The namespaced guarantee id.
        Guarantee: string
        /// The model or node scope.
        Scope: string
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Guarantee} for {this.Scope}"

/// What an extension can do. Members that this contract does not name are kept beside the named members.
type ExtensionCapabilities =
    {
        /// The frontend capability; left out when absent.
        Frontend: FrontendCapability option
        /// The backend capability; left out when absent.
        Backend: BackendCapability option
        /// The workspace capability; left out when absent.
        Workspace: WorkspaceCapability option
        /// The model-transport support; left out when absent (legacy-only).
        ModelTransport: ModelTransportSupport option
        /// Streaming support; always written, read as false when absent.
        [<JsonDefaultValue(false)>]
        Streaming: bool
        /// Incremental support; always written, read as false when absent.
        [<JsonDefaultValue(false)>]
        Incremental: bool
        /// Cancellation support; always written, read as false when absent.
        [<JsonDefaultValue(false)>]
        Cancellation: bool
        /// Progress support; always written, read as false when absent.
        [<JsonDefaultValue(false)>]
        Progress: bool
        /// Unknown members, written beside the named members.
        [<System.Text.Json.Serialization.JsonExtensionData>]
        Extra: IReadOnlyDictionary<string, System.Text.Json.JsonElement>
    }

    /// For C# callers: the frontend capability, or null when absent.
    member this.FrontendOrNull() : FrontendCapability | null = Option.toObj this.Frontend

    /// For C# callers: the backend capability, or null when absent.
    member this.BackendOrNull() : BackendCapability | null = Option.toObj this.Backend

    /// For C# callers: the workspace capability, or null when absent.
    member this.WorkspaceOrNull() : WorkspaceCapability | null = Option.toObj this.Workspace

    /// For C# callers: the model-transport support, or null when absent (legacy-only).
    member this.ModelTransportOrNull() : ModelTransportSupport | null = Option.toObj this.ModelTransport

    /// Stable text for diagnostics.
    override this.ToString() =
        $"Capabilities (model transport: {this.ModelTransport.IsSome})"

/// Result of morphir.initialize.
type InitializeResult =
    {
        /// The agreed MEP version.
        ProtocolVersion: ProtocolVersion
        /// Who the extension is.
        Extension: ExtensionInfo
        /// What the extension can do in this session.
        Capabilities: ExtensionCapabilities
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Initialized {this.Extension} at {this.ProtocolVersion}"

/// Parameters of $/cancelRequest: the id of the request to cancel.
type CancelParams =
    {
        /// The request to cancel.
        Id: RequestId
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Cancel {this.Id}"

/// The stage of a progress report. Wire values: begin, report, end.
[<RequireQualifiedAccess>]
type ProgressKind =
    | Begin
    | Report
    | End

    /// The wire value.
    member this.WireValue =
        match this with
        | Begin -> "begin"
        | Report -> "report"
        | End -> "end"

    /// The wire value.
    override this.ToString() = this.WireValue

/// Parameters of morphir.progress, for an active request.
type ProgressParams =
    {
        /// The request that makes progress.
        RequestId: RequestId
        /// The stage.
        Kind: ProgressKind
        /// The message.
        Message: string
        /// A percentage from 0 through 100; left out when absent.
        Percentage: int option
    }

    /// For C# callers: the percentage, or no value when absent.
    member this.PercentageOrNull() : Nullable<int> = Option.toNullable this.Percentage

    /// Stable text for diagnostics.
    override this.ToString() = $"Progress {this.Kind} of {this.RequestId}"

/// The JSON-RPC 2.0 error object. The code is an ErrorCode value or a JSON-RPC standard code.
type RpcError =
    {
        /// The error code.
        Code: int
        /// The message.
        Message: string
        /// Structured data; left out when absent.
        Data: System.Text.Json.JsonElement option
    }

    /// For C# callers: the structured data, or no value when absent.
    member this.DataOrNull() : Nullable<System.Text.Json.JsonElement> = Option.toNullable this.Data

    /// Stable text for diagnostics.
    override this.ToString() = $"Error {this.Code}: {this.Message}"

/// How serious a diagnostic is. Wire values: error, warning, info, hint.
[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    | Error
    | Warning
    | Info
    | Hint

    /// The wire value.
    member this.WireValue =
        match this with
        | Error -> "error"
        | Warning -> "warning"
        | Info -> "info"
        | Hint -> "hint"

    /// The wire value.
    override this.ToString() = this.WireValue

/// A zero-based position. character counts UTF-16 code units from the start of the line, as in LSP.
type SourcePosition =
    {
        /// The zero-based line.
        Line: uint32
        /// The zero-based UTF-16 code unit in the line.
        Character: uint32
    }

    /// Stable text for diagnostics (one-based, as people read positions).
    override this.ToString() = $"{this.Line + 1u}:{this.Character + 1u}"

/// A half-open range: start is inclusive and end is exclusive.
type SourceRange =
    {
        /// The first position in the range.
        Start: SourcePosition
        /// The first position after the range.
        End: SourcePosition
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Start}-{this.End}"

/// A range in a source document, identified by URI.
type SourceLocation =
    {
        /// The document URI.
        Uri: string
        /// The range.
        Range: SourceRange
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Uri}({this.Range})"

/// More information about a diagnostic, at another location.
type RelatedInformation =
    {
        /// The other location.
        Location: SourceLocation
        /// The message.
        Message: string
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Location}: {this.Message}"

/// A diagnostic message. data is structured data that the extension owns.
type Diagnostic =
    {
        /// How serious it is.
        Severity: DiagnosticSeverity
        /// An optional rule code; left out when absent.
        Code: string option
        /// The message.
        Message: string
        /// An optional location; left out when absent.
        Location: SourceLocation option
        /// More information; left out when empty, read as empty when absent.
        [<JsonDefaultEmpty; JsonOmitEmpty>]
        Related: IReadOnlyList<RelatedInformation>
        /// Structured data that the extension owns; left out when absent.
        Data: System.Text.Json.JsonElement option
    }

    /// For C# callers: the rule code, or null when absent.
    member this.CodeOrNull() : string | null = Option.toObj this.Code

    /// For C# callers: the location, or null when absent.
    member this.LocationOrNull() : SourceLocation | null = Option.toObj this.Location

    /// For C# callers: the structured data, or no value when absent.
    member this.DataOrNull() : Nullable<System.Text.Json.JsonElement> = Option.toNullable this.Data

    /// Stable text for diagnostics.
    override this.ToString() =
        let code = this.Code |> Option.map (fun code -> $" {code}") |> Option.defaultValue ""
        $"{this.Severity}{code}: {this.Message}"

/// Opaque single-document payload bytes, bounded and hashed.
type ModelPayload =
    {
        /// The codec of the decoded bytes.
        Codec: ModelCodec
        /// The layout.
        Layout: ModelLayout
        /// The number of decoded bytes.
        DecodedByteLength: int
        /// 64 lowercase hexadecimal digits of the SHA-256 of the decoded bytes.
        Sha256: Sha256Digest
        /// Canonical padded base64 of the decoded bytes, without whitespace.
        Content: string
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Codec} payload, {this.DecodedByteLength} bytes"

/// A guarantee claim about a model; the receiver must admit it before it can rely on it.
type ModelGuaranteeEvidence =
    {
        /// The guarantee id.
        Guarantee: string
        /// The model id.
        ModelId: string
        /// The model revision.
        ModelRevision: string
        /// The SHA-256 of the dependency and configuration context.
        ContextDigest: Sha256Digest
        /// The model or node scope.
        Scope: string
        /// Who produced the evidence (attribution only).
        Producer: string
        /// The evidence bytes.
        Evidence: ModelPayload
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Guarantee} for {this.ModelId}@{this.ModelRevision}"

/// A Morphir model envelope.
type MorphirModel =
    {
        /// The envelope draft, such as 0.1.0-draft.1.
        EnvelopeVersion: SemanticVersion
        /// The stable model id.
        ModelId: string
        /// The logical immutable revision.
        Revision: string
        /// The exact IR format version.
        IrVersion: SemanticVersion
        /// The model-content profile id.
        Profile: string
        /// The payload.
        Payload: ModelPayload
        /// Guarantee evidence. Read as empty when absent.
        [<JsonDefaultEmpty>]
        Evidence: IReadOnlyList<ModelGuaranteeEvidence>
        /// Registered member paths that a receiver must understand. Left out when empty.
        [<JsonDefaultEmpty; JsonOmitEmpty>]
        Critical: IReadOnlyList<string>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Model {this.ModelId}@{this.Revision} IR {this.IrVersion}"

/// A source document that a host sends to a frontend.
type SourceDocument =
    {
        /// The document URI. Module names come from it, relative to the source root.
        Uri: string
        /// The language id of the document, such as fsharp.
        LanguageId: string
        /// The document version. It goes up each time the document changes.
        Version: uint64
        /// The complete document text.
        Text: string
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Uri} v{this.Version}"

/// The documents of one compilation, with the root that module names come from.
type SourceSet =
    {
        /// The source root; left out when absent.
        Root: string option
        /// The documents, in compilation order.
        Documents: IReadOnlyList<SourceDocument>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"{this.Documents.Count} documents"

/// The package that a compilation builds.
type CompilePackage =
    {
        /// The package name, such as acme/demo.
        Name: string
        /// The exposed modules; left out when absent. An absent list exposes every module; an empty list exposes none.
        ExposedModules: IReadOnlyList<string> option
    }

    /// Stable text for diagnostics.
    override this.ToString() = this.Name

/// Options of a morphir.frontend.compileModel compilation. Members that the contract does not name are kept beside
/// the named members. Use CompileModel.checkOptions before you write or accept options: the contract refuses some
/// keys. The pin shares this type with the legacy compile request, where irVersion can be "3". This record holds a
/// SemanticVersion, so it is for compileModel only; do not use it for the legacy morphir.frontend.compile.
type CompileOptions =
    {
        /// Compile only the type definitions. Always written.
        TypesOnly: bool
        /// The exact IR version. It must equal the IR version of the output tuple. Always written.
        IrVersion: SemanticVersion
        /// Other members, written beside the named members.
        [<System.Text.Json.Serialization.JsonExtensionData>]
        Extra: IReadOnlyDictionary<string, System.Text.Json.JsonElement>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"IR {this.IrVersion}, types only {this.TypesOnly}"

/// A complete dependency model. Its format must match the selected output tuple.
type CompileModelDependency =
    {
        /// The package name of the dependency.
        PackageName: string
        /// The dependency model.
        Model: MorphirModel
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Dependency {this.PackageName}"

/// Parameters of morphir.frontend.compileModel: a complete source-set compilation into one agreed output tuple.
type CompileModelRequest =
    {
        /// The language id of the compilation.
        LanguageId: string
        /// The documents.
        Sources: SourceSet
        /// The package to build.
        Package: CompilePackage
        /// The dependency models. Read as empty when absent.
        [<JsonDefaultEmpty>]
        Dependencies: IReadOnlyList<CompileModelDependency>
        /// The compile options.
        Options: CompileOptions
        /// The SHA-256 of the dependency and configuration context (see CompileModel.contextDigest).
        ContextDigest: Sha256Digest
        /// The selected output tuple. It must be a compileModel tuple of the session agreement.
        Output: ModelTransportTuple
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Compile {this.Package} ({this.LanguageId}) to {this.Output}"

/// An error code of a JSON-RPC error object. The value is the wire integer.
type ErrorCode =
    /// JSON-RPC: the bytes are not JSON.
    | ParseError = -32700
    /// JSON-RPC: the JSON is not a request object.
    | InvalidRequest = -32600
    /// JSON-RPC: MEP does not have the method, or the peer does not handle it.
    | MethodNotFound = -32601
    /// JSON-RPC: the params do not match the method.
    | InvalidParams = -32602
    /// JSON-RPC: the handler failed.
    | InternalError = -32603
    /// MEP: a general extension error.
    | ExtensionError = -32000
    /// MEP: compilation failed.
    | CompilationError = -32001
    /// MEP: generation failed.
    | GenerationError = -32002
    /// MEP: validation failed.
    | ValidationError = -32003
    /// MEP: transformation failed.
    | TransformationError = -32004
    /// MEP: the extension failed.
    | ExtensionFailure = -32010
    /// MEP: no protocol version is common to both peers.
    | ProtocolVersionMismatch = -32011
    /// MEP: the extension does not have the permission.
    | PermissionDenied = -32012
    /// MEP: the peer did not advertise the capability.
    | CapabilityUnavailable = -32013
    /// MEP: the request is not allowed before initialization or after shutdown.
    | NotInitialized = -32014
    /// The request was cancelled.
    | RequestCancelled = -32800

/// Whether a method takes a reply.
[<RequireQualifiedAccess; JsonIgnoreType>]
type MethodKind =
    | Request
    | Notification

    /// The text of the contract's method table: request or notification.
    member this.WireValue =
        match this with
        | Request -> "request"
        | Notification -> "notification"

    /// The same text as WireValue.
    override this.ToString() = this.WireValue

/// A MEP method. The wire value is the JSON-RPC method string.
[<RequireQualifiedAccess; JsonIgnoreType>]
type Method =
    | Initialize
    | Describe
    | Initialized
    | Ping
    | Info
    | Capabilities
    | Compile
    | CompileModel
    | Generate
    | GenerateModel
    | Validate
    | Transform
    | WorkspaceDiscover
    | Shutdown
    | Exit
    | CancelRequest
    | Progress

    /// The JSON-RPC method string.
    member this.WireValue =
        match this with
        | Initialize -> "morphir.initialize"
        | Describe -> "morphir.extension.describe"
        | Initialized -> "morphir.initialized"
        | Ping -> "morphir.ping"
        | Info -> "morphir.extension.info"
        | Capabilities -> "morphir.extension.capabilities"
        | Compile -> "morphir.frontend.compile"
        | CompileModel -> "morphir.frontend.compileModel"
        | Generate -> "morphir.backend.generate"
        | GenerateModel -> "morphir.backend.generateModel"
        | Validate -> "morphir.validator.validate"
        | Transform -> "morphir.transform.transform"
        | WorkspaceDiscover -> "morphir.workspace.discover"
        | Shutdown -> "morphir.shutdown"
        | Exit -> "morphir.exit"
        | CancelRequest -> "$/cancelRequest"
        | Progress -> "morphir.progress"

    /// A notification carries no request id and gets no response.
    member this.Kind =
        match this with
        | Initialized
        | Exit
        | CancelRequest
        | Progress -> MethodKind.Notification
        | _ -> MethodKind.Request

    /// Every method of the pinned contract.
    static member All: IReadOnlyList<Method> =
        [| Initialize
           Describe
           Initialized
           Ping
           Info
           Capabilities
           Compile
           CompileModel
           Generate
           GenerateModel
           Validate
           Transform
           WorkspaceDiscover
           Shutdown
           Exit
           CancelRequest
           Progress |]

    /// The method of a wire string, or None when MEP does not have it. The match is exact.
    static member Find(wireValue: string | null) : Method option =
        match wireValue with
        | null -> None
        | text -> Method.All |> Seq.tryFind (fun method -> String.Equals(method.WireValue, text, StringComparison.Ordinal))

    /// For C# callers: finds the method of a wire string, by exact match.
    static member TryFind(wireValue: string | null, [<Out; MaybeNullWhen(false)>] result: byref<Method>) : bool =
        match Method.Find wireValue with
        | Some method ->
            result <- method
            true
        | None ->
            result <- Unchecked.defaultof<_>
            false

    /// The JSON-RPC method string.
    override this.ToString() = this.WireValue
