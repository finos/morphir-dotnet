// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge

open System
open System.Diagnostics.CodeAnalysis
open System.Net
open System.Net.Sockets
open System.Runtime.InteropServices

/// Why an external identifier could not be parsed.
[<RequireQualifiedAccess>]
type IdentifierError =
    | Empty
    | InvalidIri

    override this.ToString() =
        match this with
        | IdentifierError.Empty -> "An identifier cannot be empty."
        | IdentifierError.InvalidIri -> "Expected an absolute IRI."

module private IriSyntax =
    let private subDelimiters = "!$&'()*+,;="

    let private asciiUnreserved c =
        Char.IsAsciiLetterOrDigit c || c = '-' || c = '.' || c = '_' || c = '~'

    // RFC 3987 section 2.2.
    let private ucschar code =
        (code >= 0xA0 && code <= 0xD7FF)
        || (code >= 0xF900 && code <= 0xFDCF)
        || (code >= 0xFDF0 && code <= 0xFFEF)
        || (code >= 0x10000
            && code <= 0xEFFFD
            && (code < 0xE0000 || code >= 0xE1000)
            && (code &&& 0xFFFF) <= 0xFFFD)

    let private iprivate code =
        (code >= 0xE000 && code <= 0xF8FF)
        || (code >= 0xF0000 && code <= 0xFFFFD)
        || (code >= 0x100000 && code <= 0x10FFFD)

    let private validComponent (extra: string) allowPrivate (value: ReadOnlySpan<char>) =
        let mutable index = 0
        let mutable valid = true

        while valid && index < value.Length do
            let current = value[index]

            if current = '%' then
                valid <-
                    index <= value.Length - 3
                    && Uri.IsHexDigit value[index + 1]
                    && Uri.IsHexDigit value[index + 2]

                index <- index + 3
            elif Char.IsHighSurrogate current then
                if index < value.Length - 1 && Char.IsLowSurrogate value[index + 1] then
                    let code = Char.ConvertToUtf32(current, value[index + 1])
                    valid <- ucschar code || (allowPrivate && iprivate code)
                else
                    valid <- false

                index <- index + 2
            else
                let code = int current

                valid <-
                    asciiUnreserved current
                    || subDelimiters.Contains current
                    || extra.Contains current
                    || ucschar code
                    || (allowPrivate && iprivate code)

                index <- index + 1

        valid

    let private port (suffix: ReadOnlySpan<char>) =
        if suffix.IsEmpty then
            true
        elif suffix[0] <> ':' then
            false
        else
            let mutable offset = 1

            while offset < suffix.Length && Char.IsAsciiDigit suffix[offset] do
                offset <- offset + 1

            offset = suffix.Length

    let private ipvFuture (value: ReadOnlySpan<char>) =
        let dot = value.IndexOf '.'

        if dot < 2 || dot = value.Length - 1 || (value[0] <> 'v' && value[0] <> 'V') then
            false
        else
            let mutable index = 1

            while index < dot && Uri.IsHexDigit value[index] do
                index <- index + 1

            if index <> dot then
                false
            else
                index <- dot + 1

                while
                    index < value.Length
                    && (asciiUnreserved value[index] || subDelimiters.Contains value[index] || value[index] = ':')
                    do
                    index <- index + 1

                index = value.Length

    let private ipLiteral (value: ReadOnlySpan<char>) =
        ipvFuture value
        || (value.IndexOf '%' < 0
            && match IPAddress.TryParse value with
               | true, NonNull address -> address.AddressFamily = AddressFamily.InterNetworkV6
               | _ -> false)

    let private hostAndPort (value: ReadOnlySpan<char>) =
        if not value.IsEmpty && value[0] = '[' then
            let closing = value.IndexOf ']'
            closing > 0 && ipLiteral (value.Slice(1, closing - 1)) && port (value.Slice(closing + 1))
        else
            let colon = value.IndexOf ':'

            if colon < 0 then
                validComponent "" false value
            else
                validComponent "" false (value.Slice(0, colon)) && port (value.Slice colon)

    let private authority (value: ReadOnlySpan<char>) =
        let at = value.IndexOf '@'

        if at < 0 then
            hostAndPort value
        else
            validComponent ":" false (value.Slice(0, at)) && hostAndPort (value.Slice(at + 1))

    let private hierarchy (value: ReadOnlySpan<char>) =
        if value.Length < 2 || value[0] <> '/' || value[1] <> '/' then
            validComponent "/:@" false value
        else
            let rest = value.Slice 2
            let slash = rest.IndexOf '/'

            if slash < 0 then
                authority rest
            else
                authority (rest.Slice(0, slash)) && validComponent "/:@" false (rest.Slice slash)

    let private hierarchyAndQuery (value: ReadOnlySpan<char>) =
        let query = value.IndexOf '?'

        if query < 0 then
            hierarchy value
        else
            hierarchy (value.Slice(0, query)) && validComponent "/?:@" true (value.Slice(query + 1))

    let isAbsolute (value: string) =
        let colon = value.IndexOf ':'

        if colon < 1 || not (Uri.CheckSchemeName(value.Substring(0, colon))) then
            false
        else
            let rest = value.AsSpan(colon + 1)
            let fragment = rest.IndexOf '#'

            if fragment < 0 then
                hierarchyAndQuery rest
            else
                hierarchyAndQuery (rest.Slice(0, fragment))
                && validComponent "/?:@" false (rest.Slice(fragment + 1))

/// An absolute internationalized resource identifier. Equality and ordering use its original spelling.
[<CustomEquality; CustomComparison>]
type Iri =
    private
    | Iri of string

    /// The spelling supplied by the caller.
    member this.Value =
        let (Iri value) = this
        value

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? Iri as iri -> String.Equals(this.Value, iri.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? Iri as iri -> String.CompareOrdinal(this.Value, iri.Value)
            | null -> 1
            | _ -> invalidArg (nameof other) "Expected an IRI."

    /// Parses external text. For F# callers.
    static member Parse(text: string | null) : Result<Iri, IdentifierError> =
        match text with
        | null
        | "" -> Error IdentifierError.Empty
        | value when IriSyntax.isAbsolute value -> Ok(Iri value)
        | _ -> Error IdentifierError.InvalidIri

    /// Parses external text. For C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<Iri>,
            [<Out; MaybeNullWhen(true)>] failure: byref<IdentifierError>
        ) =
        match Iri.Parse text with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Constructs an identifier from a source literal and throws if it is invalid.
    static member Create(literal: string) =
        match Iri.Parse literal with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())

/// A durable logical document identity independent of its storage path.
type DocumentId =
    private
    | DocumentId of Iri

    /// The document IRI.
    member this.Iri =
        let (DocumentId iri) = this
        iri

    override this.ToString() = this.Iri.Value

    /// Wraps a parsed absolute IRI.
    static member Create(iri: Iri) =
        ArgumentNullException.ThrowIfNull iri
        DocumentId iri

/// An opaque snapshot key scoped to the source identity.
[<CustomEquality; CustomComparison>]
type RevisionId =
    private
    | RevisionId of string

    /// The original revision key.
    member this.Value =
        let (RevisionId value) = this
        value

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? RevisionId as revision -> String.Equals(this.Value, revision.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? RevisionId as revision -> String.CompareOrdinal(this.Value, revision.Value)
            | null -> 1
            | _ -> invalidArg (nameof other) "Expected a revision identifier."

    /// Parses an opaque revision key. For F# callers.
    static member Parse(text: string | null) : Result<RevisionId, IdentifierError> =
        match text with
        | null -> Error IdentifierError.Empty
        | value when String.IsNullOrWhiteSpace value -> Error IdentifierError.Empty
        | value -> Ok(RevisionId value)

    /// Parses an opaque revision key. For C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<RevisionId>,
            [<Out; MaybeNullWhen(true)>] failure: byref<IdentifierError>
        ) =
        match RevisionId.Parse text with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Constructs a revision from a source literal and throws if it is empty.
    static member Create(literal: string) =
        match RevisionId.Parse literal with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())
