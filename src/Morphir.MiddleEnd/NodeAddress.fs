// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open System
open System.Globalization
open System.Text
open Morphir.IR

/// <summary>Why a text is not a node URI.</summary>
[<RequireQualifiedAccess>]
type NodeUriError =
    /// <summary>An address that is not pinned and has an ordered step needs a guard, or it can silently retarget.</summary>
    | MissingGuard
    /// <summary>The text is not one canonical node address. The message says what is wrong.</summary>
    | Invalid of message: string

    override this.ToString() =
        match this with
        | MissingGuard -> "An unpinned indexed node address requires a guard."
        | Invalid message -> $"Invalid node URI: {message}"

/// <summary>A lowercase SHA-256 digest in the canonical text form <c>sha256:</c> and 64 hexadecimal digits.</summary>
[<CustomEquality; NoComparison>]
type Sha256Digest =
    private
    | Sha256Digest of hex: string

    /// <summary>The 64 lowercase hexadecimal digits.</summary>
    member this.Hex = let (Sha256Digest hex) = this in hex

    override this.ToString() = "sha256:" + this.Hex

    override this.Equals(other: obj | null) =
        match other with
        | :? Sha256Digest as digest -> String.Equals(this.Hex, digest.Hex, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Hex

    /// <summary>Parses the canonical text <c>sha256:</c> and 64 lowercase hexadecimal digits.</summary>
    static member Parse(text: string) : Result<Sha256Digest, string> =
        if not (text.StartsWith("sha256:", StringComparison.Ordinal)) then
            Error "digest must use sha256"
        else
            let hex = text.Substring "sha256:".Length

            if
                hex.Length <> 64
                || hex |> Seq.exists (fun c -> not ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            then
                Error "digest must have 64 lowercase hex digits"
            else
                Ok(Sha256Digest hex)

    /// <summary>The digest of the exact bytes of an acquired immutable snapshot.</summary>
    static member OfBytes(bytes: byte[]) : Sha256Digest =
        use algorithm = System.Security.Cryptography.SHA256.Create()
        let hash = algorithm.ComputeHash bytes

        let hex = StringBuilder(64)

        for b in hash do
            hex.Append(b.ToString("x2", CultureInfo.InvariantCulture)) |> ignore

        Sha256Digest(hex.ToString())

/// <summary>
/// The release triplet of an IR format, written as three numbers in a node URI: <c>format=4.0.0</c>.
/// </summary>
[<Struct>]
type FormatTriplet = {
    Major: int
    Minor: int
    Patch: int
}

    with
    override this.ToString() = $"{this.Major}.{this.Minor}.{this.Patch}"

/// <summary>The release triplet of a format version.</summary>
[<RequireQualifiedAccess>]
module FormatTriplet =

    /// <summary>The triplet of a format version: <c>Classic 3</c> is <c>3.0.0</c> and a SemVer keeps its numbers.</summary>
    let ofVersion (version: Versioning.FormatVersion) : FormatTriplet =
        match version with
        | Versioning.Classic major -> { Major = major; Minor = 0; Patch = 0 }
        | Versioning.SemVer semantic -> { Major = semantic.Major; Minor = semantic.Minor; Patch = semantic.Patch }

/// <summary>The artifact that a node URI selects in the context of the caller.</summary>
[<RequireQualifiedAccess>]
type ArtifactSelector =
    /// <summary>One named package. It can be ambiguous until the context chooses a release.</summary>
    | Package of PackageName
    /// <summary>A current working artifact that the caller named with a lowercase slug.</summary>
    | Workspace of alias: string

/// <summary>The package that holds a module of the distribution.</summary>
[<RequireQualifiedAccess>]
type NodeOwner =
    | OwnPackage
    | Dependency of PackageName

/// <summary>The first node that an address selects in a distribution.</summary>
[<RequireQualifiedAccess>]
type NodeRoot =
    | Distribution
    | Package
    | Dependency of PackageName
    | EntryPoint of string
    | Module of owner: NodeOwner * modulePath: Path
    | Type of owner: NodeOwner * modulePath: Path * name: Name
    | Value of owner: NodeOwner * modulePath: Path * name: Name

/// <summary>One typed selection of a child. A case carries only the name or the index that its role needs.</summary>
[<RequireQualifiedAccess>]
type NodeStep =
    | TypeExpression
    | Body
    | ValueInputType of Name
    | ValueInputAnnotation of Name
    | ValueOutputType
    | DerivedBaseType
    | PartialTypeExpression
    | CustomConstructor of Name
    | ConstructorArgument of int
    | RecordField of Name
    | ExtensibleRecordField of Name
    | TypeFunctionParameter
    | TypeFunctionResult
    | ReferenceArgument of int
    | ApplyFunction
    | ApplyArgument
    | FieldSubject
    | DestructurePattern
    | DestructureValue
    | DestructureBody
    | IfCondition
    | IfThen
    | IfElse
    | LambdaPattern
    | LambdaBody
    | LetDefinition of Name
    | LetBody
    | ListElement of int
    | PatternMatchSubject
    | TupleElement of int
    | PatternMatchCasePattern of int
    | PatternMatchCaseBody of int
    | UpdateSubject
    | UpdateField of Name
    | AsPatternChild
    | PatternTupleElement of int
    | PatternConstructorArgument of int
    | HeadTailHead
    | HeadTailTail
    | ExternalFallback
    | IncompletePartialBody
    | HoleExpectedType
    /// <summary>The semantic type that a value or a pattern stores as its inferred type.</summary>
    | InferredType
    /// <summary>An ordered annotation entry of a specification.</summary>
    | AnnotationEntry of int
    /// <summary>An ordered value argument of a structured annotation.</summary>
    | AnnotationArgument of int

    /// <summary>True for a step that selects an ordered child. An address that is not pinned needs a guard for it.</summary>
    member this.IsPositional =
        match this with
        | ConstructorArgument _
        | AnnotationEntry _
        | AnnotationArgument _
        | ReferenceArgument _
        | ListElement _
        | TupleElement _
        | PatternMatchCasePattern _
        | PatternMatchCaseBody _
        | PatternTupleElement _
        | PatternConstructorArgument _ -> true
        | _ -> false

/// <summary>The release that an address selects: the current artifact of the caller, or one immutable snapshot.</summary>
[<RequireQualifiedAccess>]
type ArtifactRevision =
    | Current
    | Pinned of Sha256Digest

/// <summary>
/// A semantic node address of <c>finos/morphir</c> (<c>docs/spec/ir/node-addresses.md</c>, draft <c>0.1.0-draft.1</c>). It
/// names a position in the normalized model, not a file or a source position. The text form is a Morphir URI, which is
/// an absolute IRI. Only <see cref="NodeAddress.parse"/> and <see cref="NodeAddress.create"/> build an address that is
/// checked; the canonical spelling of an address parses back to the same address.
/// </summary>
[<CustomEquality; NoComparison>]
type NodeAddress =
    private
        {
            ArtifactValue: ArtifactSelector
            FormatValue: FormatTriplet
            RootValue: NodeRoot
            StepsValue: NodeStep list
            RevisionValue: ArtifactRevision
            GuardValue: Sha256Digest option
        }

    member this.Artifact = this.ArtifactValue
    member this.Format = this.FormatValue
    member this.Root = this.RootValue
    member this.Steps = this.StepsValue
    member this.Revision = this.RevisionValue
    member this.Guard = this.GuardValue

    override this.Equals(other: obj | null) =
        match other with
        | :? NodeAddress as address ->
            this.ArtifactValue = address.ArtifactValue
            && this.FormatValue = address.FormatValue
            && this.RootValue = address.RootValue
            && this.StepsValue = address.StepsValue
            && this.RevisionValue = address.RevisionValue
            && this.GuardValue = address.GuardValue
        | _ -> false

    override this.GetHashCode() =
        hash (this.ArtifactValue, this.FormatValue, this.RootValue, this.StepsValue, this.RevisionValue, this.GuardValue)

/// <summary>Builds, parses and writes node addresses.</summary>
[<RequireQualifiedAccess>]
module NodeAddress =

    let private invalid (message: string) = Error(NodeUriError.Invalid message)

    // ----- percent encoding of one path component -----

    let private isUnreserved (c: char) =
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '-' || c = '.' || c = '_' || c = '~'

    let private encodeComponent (value: string) =
        let builder = StringBuilder()

        for b in Encoding.UTF8.GetBytes value do
            let c = char b

            if b < 128uy && isUnreserved c then
                builder.Append c |> ignore
            else
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture)) |> ignore

        builder.ToString()

    let private decodeComponent (value: string) : Result<string, NodeUriError> =
        let bytes = ResizeArray<byte>()
        let mutable index = 0
        let mutable failure: NodeUriError option = None

        let hex (c: char) =
            if c >= '0' && c <= '9' then int c - int '0'
            elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
            else -1

        while failure.IsNone && index < value.Length do
            let c = value[index]

            if c = '%' then
                if index + 2 >= value.Length then
                    failure <- Some(NodeUriError.Invalid "truncated percent escape")
                else
                    let high = hex value[index + 1]
                    let low = hex value[index + 2]

                    if high < 0 || low < 0 then
                        failure <- Some(NodeUriError.Invalid "invalid percent escape")
                    else
                        bytes.Add(byte (high * 16 + low))
                        index <- index + 3
            elif c > '\127' then
                failure <- Some(NodeUriError.Invalid "raw non-ASCII URI component")
            else
                bytes.Add(byte c)
                index <- index + 1

        match failure with
        | Some error -> Error error
        | None ->
            try
                Ok(UTF8Encoding(false, true).GetString(bytes.ToArray()))
            with :? ArgumentException ->
                invalid "invalid UTF-8 escape"

    // ----- names and paths -----

    let private parsePackage (text: string) : Result<PackageName, NodeUriError> =
        match CanonicalNames.parsePath text with
        | Ok path when not (Path.isEmpty path) && CanonicalNames.pathToString path = text -> Ok(PackageName path)
        | Ok _ -> invalid "noncanonical package name"
        | Error _ -> invalid "invalid package name"

    let private parsePathComponent (raw: string) : Result<Path, NodeUriError> =
        decodeComponent raw
        |> Result.bind (fun decoded ->
            match CanonicalNames.parsePath decoded with
            | Ok path when not (Path.isEmpty path) && CanonicalNames.pathToString path = decoded -> Ok path
            | Ok _ -> invalid "noncanonical module path"
            | Error _ -> invalid "invalid module path")

    let private parseNameComponent (raw: string) : Result<Name, NodeUriError> =
        decodeComponent raw
        |> Result.bind (fun decoded ->
            match CanonicalNames.parseName decoded with
            | Ok name when CanonicalNames.nameToString name = decoded -> Ok name
            | Ok _ -> invalid "noncanonical name"
            | Error _ -> invalid "invalid name")

    let private parseIndex (text: string) : Result<int, NodeUriError> =
        if
            text.Length = 0
            || (text.Length > 1 && text[0] = '0')
            || text |> Seq.exists (fun c -> c < '0' || c > '9')
        then
            invalid "invalid positional index"
        else
            match Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, value -> Ok value
            | _ -> invalid "positional index overflow"

    let private parseFormat (text: string) : Result<FormatTriplet, NodeUriError> =
        let parts = text.Split('.')

        if
            parts.Length <> 3
            || parts |> Array.exists (fun part -> part.Length = 0 || (part.Length > 1 && part[0] = '0'))
        then
            invalid "format must have three canonical numeric components"
        else
            let parse (part: string) =
                match Int32.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture) with
                | true, value -> Ok value
                | _ -> invalid "invalid format component"

            match parse parts[0], parse parts[1], parse parts[2] with
            | Ok major, Ok minor, Ok patch -> Ok { Major = major; Minor = minor; Patch = patch }
            | Error error, _, _
            | _, Error error, _
            | _, _, Error error -> Error error

    let private validWorkspaceAlias (text: string) =
        text.Length > 0
        && text[0] >= 'a'
        && text[0] <= 'z'
        && (text.Split('-')
            |> Array.forall (fun part ->
                part.Length > 0
                && part |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))))

    // ----- writing -----

    let private writeOwner (parts: ResizeArray<string>) (owner: NodeOwner) =
        match owner with
        | NodeOwner.OwnPackage -> ()
        | NodeOwner.Dependency(PackageName path) ->
            parts.Add "dependency"
            parts.Add(encodeComponent (CanonicalNames.pathToString path))

    let private nameText (name: Name) = encodeComponent (CanonicalNames.nameToString name)

    let private writeStep (parts: ResizeArray<string>) (step: NodeStep) =
        let add (items: string list) = parts.AddRange items

        match step with
        | NodeStep.TypeExpression -> add [ "type-exp" ]
        | NodeStep.Body -> add [ "body" ]
        | NodeStep.ValueInputType name -> add [ "input"; nameText name ]
        | NodeStep.ValueInputAnnotation name -> add [ "input-annotation"; nameText name ]
        | NodeStep.ValueOutputType -> add [ "output" ]
        | NodeStep.DerivedBaseType -> add [ "derived"; "base-type" ]
        | NodeStep.PartialTypeExpression -> add [ "incomplete"; "partial-type" ]
        | NodeStep.CustomConstructor name -> add [ "constructor"; nameText name ]
        | NodeStep.ConstructorArgument index -> add [ "argument"; string index ]
        | NodeStep.RecordField name -> add [ "record"; "field"; nameText name ]
        | NodeStep.ExtensibleRecordField name -> add [ "extensible-record"; "field"; nameText name ]
        | NodeStep.TypeFunctionParameter -> add [ "function"; "parameter" ]
        | NodeStep.TypeFunctionResult -> add [ "function"; "result" ]
        | NodeStep.ReferenceArgument index -> add [ "reference"; "argument"; string index ]
        | NodeStep.ApplyFunction -> add [ "apply"; "function" ]
        | NodeStep.ApplyArgument -> add [ "apply"; "argument" ]
        | NodeStep.FieldSubject -> add [ "field"; "subject" ]
        | NodeStep.DestructurePattern -> add [ "destructure"; "pattern" ]
        | NodeStep.DestructureValue -> add [ "destructure"; "value" ]
        | NodeStep.DestructureBody -> add [ "destructure"; "body" ]
        | NodeStep.IfCondition -> add [ "if"; "condition" ]
        | NodeStep.IfThen -> add [ "if"; "then" ]
        | NodeStep.IfElse -> add [ "if"; "else" ]
        | NodeStep.LambdaPattern -> add [ "lambda"; "pattern" ]
        | NodeStep.LambdaBody -> add [ "lambda"; "body" ]
        | NodeStep.LetDefinition name -> add [ "let"; "definition"; nameText name ]
        | NodeStep.LetBody -> add [ "let"; "body" ]
        | NodeStep.ListElement index -> add [ "list"; "element"; string index ]
        | NodeStep.PatternMatchSubject -> add [ "pattern-match"; "subject" ]
        | NodeStep.TupleElement index -> add [ "tuple"; "element"; string index ]
        | NodeStep.PatternMatchCasePattern index -> add [ "pattern-match"; "case"; string index; "pattern" ]
        | NodeStep.PatternMatchCaseBody index -> add [ "pattern-match"; "case"; string index; "body" ]
        | NodeStep.UpdateSubject -> add [ "update"; "subject" ]
        | NodeStep.UpdateField name -> add [ "update"; "field"; nameText name ]
        | NodeStep.AsPatternChild -> add [ "as-pattern"; "pattern" ]
        | NodeStep.PatternTupleElement index -> add [ "tuple-pattern"; "element"; string index ]
        | NodeStep.PatternConstructorArgument index -> add [ "constructor-pattern"; "argument"; string index ]
        | NodeStep.HeadTailHead -> add [ "head-tail"; "head" ]
        | NodeStep.HeadTailTail -> add [ "head-tail"; "tail" ]
        | NodeStep.ExternalFallback -> add [ "external"; "fallback" ]
        | NodeStep.IncompletePartialBody -> add [ "incomplete"; "partial-body" ]
        | NodeStep.HoleExpectedType -> add [ "hole"; "expected-type" ]
        | NodeStep.InferredType -> add [ "inferred-type" ]
        | NodeStep.AnnotationEntry index -> add [ "annotation"; "entry"; string index ]
        | NodeStep.AnnotationArgument index -> add [ "argument"; string index ]

    /// <summary>The canonical Morphir URI of an address. One address has one spelling.</summary>
    [<CompiledName("ToUri")>]
    let toUri (address: NodeAddress) : string =
        let builder = StringBuilder("morphir://ir/")

        match address.Artifact with
        | ArtifactSelector.Package(PackageName path) -> builder.Append("pkg/").Append(CanonicalNames.pathToString path) |> ignore
        | ArtifactSelector.Workspace alias -> builder.Append("workspace/").Append(alias) |> ignore

        builder.Append("?format=").Append(string address.Format) |> ignore

        match address.Revision with
        | ArtifactRevision.Pinned digest -> builder.Append("&rev=").Append(string digest) |> ignore
        | ArtifactRevision.Current -> ()

        match address.Guard with
        | Some digest -> builder.Append("&guard=").Append(string digest) |> ignore
        | None -> ()

        builder.Append('#') |> ignore

        let parts = ResizeArray<string>()

        match address.Root with
        | NodeRoot.Distribution -> parts.Add "distribution"
        | NodeRoot.Package -> parts.Add "package"
        | NodeRoot.Dependency(PackageName path) ->
            parts.Add "dependency"
            parts.Add(encodeComponent (CanonicalNames.pathToString path))
        | NodeRoot.EntryPoint name ->
            parts.Add "entry-point"
            parts.Add(encodeComponent name)
        | NodeRoot.Module(owner, modulePath) ->
            writeOwner parts owner
            parts.Add "module"
            parts.Add(encodeComponent (CanonicalNames.pathToString modulePath))
        | NodeRoot.Type(owner, modulePath, name) ->
            writeOwner parts owner
            parts.Add "module"
            parts.Add(encodeComponent (CanonicalNames.pathToString modulePath))
            parts.Add "type"
            parts.Add(nameText name)
        | NodeRoot.Value(owner, modulePath, name) ->
            writeOwner parts owner
            parts.Add "module"
            parts.Add(encodeComponent (CanonicalNames.pathToString modulePath))
            parts.Add "value"
            parts.Add(nameText name)

        for step in address.Steps do
            writeStep parts step

        builder.Append('/').Append(String.Join("/", parts)) |> ignore
        builder.ToString()

    // ----- parsing -----

    let private parseRoot (parts: string list) : Result<NodeRoot * string list, NodeUriError> =
        let rootOf owner prefixRest =
            match prefixRest with
            | "module" :: modulePath :: "type" :: name :: rest ->
                parsePathComponent modulePath
                |> Result.bind (fun m -> parseNameComponent name |> Result.map (fun n -> NodeRoot.Type(owner, m, n), rest))
            | "module" :: modulePath :: "value" :: name :: rest ->
                parsePathComponent modulePath
                |> Result.bind (fun m -> parseNameComponent name |> Result.map (fun n -> NodeRoot.Value(owner, m, n), rest))
            | "module" :: modulePath :: rest -> parsePathComponent modulePath |> Result.map (fun m -> NodeRoot.Module(owner, m), rest)
            | _ -> invalid "invalid node root"

        match parts with
        | "distribution" :: rest -> Ok(NodeRoot.Distribution, rest)
        | "package" :: rest -> Ok(NodeRoot.Package, rest)
        | "entry-point" :: raw :: rest when raw.Length > 0 -> decodeComponent raw |> Result.map (fun name -> NodeRoot.EntryPoint name, rest)
        | "dependency" :: raw :: rest ->
            decodeComponent raw
            |> Result.bind parsePackage
            |> Result.bind (fun package ->
                match rest with
                | [] -> Ok(NodeRoot.Dependency package, [])
                | _ -> rootOf (NodeOwner.Dependency package) rest)
        | other -> rootOf NodeOwner.OwnPackage other

    let private parseSteps (parts: string list) : Result<NodeStep list, NodeUriError> =
        let name raw = parseNameComponent raw
        let index raw = parseIndex raw

        let rec loop (remaining: string list) (previous: NodeStep option) (acc: NodeStep list) =
            let next (step: Result<NodeStep, NodeUriError>) (rest: string list) =
                match step with
                | Ok step -> loop rest (Some step) (step :: acc)
                | Error error -> Error error

            match remaining with
            | [] -> Ok(List.rev acc)
            | "type-exp" :: rest -> next (Ok NodeStep.TypeExpression) rest
            | "body" :: rest -> next (Ok NodeStep.Body) rest
            | "input" :: n :: rest -> next (name n |> Result.map NodeStep.ValueInputType) rest
            | "input-annotation" :: n :: rest -> next (name n |> Result.map NodeStep.ValueInputAnnotation) rest
            | "output" :: rest -> next (Ok NodeStep.ValueOutputType) rest
            | "derived" :: "base-type" :: rest -> next (Ok NodeStep.DerivedBaseType) rest
            | "incomplete" :: "partial-type" :: rest -> next (Ok NodeStep.PartialTypeExpression) rest
            | "constructor" :: n :: rest -> next (name n |> Result.map NodeStep.CustomConstructor) rest
            | "argument" :: i :: rest ->
                match previous with
                | Some(NodeStep.AnnotationEntry _) -> next (index i |> Result.map NodeStep.AnnotationArgument) rest
                | _ -> next (index i |> Result.map NodeStep.ConstructorArgument) rest
            | "record" :: "field" :: n :: rest -> next (name n |> Result.map NodeStep.RecordField) rest
            | "extensible-record" :: "field" :: n :: rest -> next (name n |> Result.map NodeStep.ExtensibleRecordField) rest
            | "function" :: "parameter" :: rest -> next (Ok NodeStep.TypeFunctionParameter) rest
            | "function" :: "result" :: rest -> next (Ok NodeStep.TypeFunctionResult) rest
            | "reference" :: "argument" :: i :: rest -> next (index i |> Result.map NodeStep.ReferenceArgument) rest
            | "apply" :: "function" :: rest -> next (Ok NodeStep.ApplyFunction) rest
            | "apply" :: "argument" :: rest -> next (Ok NodeStep.ApplyArgument) rest
            | "field" :: "subject" :: rest -> next (Ok NodeStep.FieldSubject) rest
            | "destructure" :: "pattern" :: rest -> next (Ok NodeStep.DestructurePattern) rest
            | "destructure" :: "value" :: rest -> next (Ok NodeStep.DestructureValue) rest
            | "destructure" :: "body" :: rest -> next (Ok NodeStep.DestructureBody) rest
            | "if" :: "condition" :: rest -> next (Ok NodeStep.IfCondition) rest
            | "if" :: "then" :: rest -> next (Ok NodeStep.IfThen) rest
            | "if" :: "else" :: rest -> next (Ok NodeStep.IfElse) rest
            | "lambda" :: "pattern" :: rest -> next (Ok NodeStep.LambdaPattern) rest
            | "lambda" :: "body" :: rest -> next (Ok NodeStep.LambdaBody) rest
            | "let" :: "definition" :: n :: rest -> next (name n |> Result.map NodeStep.LetDefinition) rest
            | "let" :: "body" :: rest -> next (Ok NodeStep.LetBody) rest
            | "list" :: "element" :: i :: rest -> next (index i |> Result.map NodeStep.ListElement) rest
            | "pattern-match" :: "subject" :: rest -> next (Ok NodeStep.PatternMatchSubject) rest
            | "tuple" :: "element" :: i :: rest -> next (index i |> Result.map NodeStep.TupleElement) rest
            | "pattern-match" :: "case" :: i :: "pattern" :: rest -> next (index i |> Result.map NodeStep.PatternMatchCasePattern) rest
            | "pattern-match" :: "case" :: i :: "body" :: rest -> next (index i |> Result.map NodeStep.PatternMatchCaseBody) rest
            | "update" :: "subject" :: rest -> next (Ok NodeStep.UpdateSubject) rest
            | "update" :: "field" :: n :: rest -> next (name n |> Result.map NodeStep.UpdateField) rest
            | "as-pattern" :: "pattern" :: rest -> next (Ok NodeStep.AsPatternChild) rest
            | "tuple-pattern" :: "element" :: i :: rest -> next (index i |> Result.map NodeStep.PatternTupleElement) rest
            | "constructor-pattern" :: "argument" :: i :: rest -> next (index i |> Result.map NodeStep.PatternConstructorArgument) rest
            | "head-tail" :: "head" :: rest -> next (Ok NodeStep.HeadTailHead) rest
            | "head-tail" :: "tail" :: rest -> next (Ok NodeStep.HeadTailTail) rest
            | "external" :: "fallback" :: rest -> next (Ok NodeStep.ExternalFallback) rest
            | "incomplete" :: "partial-body" :: rest -> next (Ok NodeStep.IncompletePartialBody) rest
            | "hole" :: "expected-type" :: rest -> next (Ok NodeStep.HoleExpectedType) rest
            | "inferred-type" :: rest -> next (Ok NodeStep.InferredType) rest
            | "annotation" :: "entry" :: i :: rest -> next (index i |> Result.map NodeStep.AnnotationEntry) rest
            | _ -> invalid "unknown semantic child role"

        loop parts None []

    /// <summary>Parses one canonical node URI. Aliases, unknown roles and noncanonical spellings are refused.</summary>
    let parse (uri: string) : Result<NodeAddress, NodeUriError> =
        let prefix = "morphir://ir/"

        if not (uri.StartsWith(prefix, StringComparison.Ordinal)) then
            invalid "wrong URI authority"
        else
            let body = uri.Substring prefix.Length
            let hash = body.IndexOf '#'

            if hash < 0 then
                invalid "missing node fragment"
            elif body.IndexOf('#', hash + 1) >= 0 then
                invalid "duplicate fragment"
            else
                let beforeFragment = body.Substring(0, hash)
                let fragment = body.Substring(hash + 1)
                let question = beforeFragment.IndexOf '?'

                if question < 0 then
                    invalid "missing format query"
                else
                    let selector = beforeFragment.Substring(0, question)
                    let query = beforeFragment.Substring(question + 1)

                    let artifact =
                        if selector.StartsWith("pkg/", StringComparison.Ordinal) then
                            let raw = selector.Substring 4

                            if raw.Length = 0 then
                                invalid "empty package selector"
                            else
                                parsePackage raw |> Result.map ArtifactSelector.Package
                        elif selector.StartsWith("workspace/", StringComparison.Ordinal) then
                            let alias = selector.Substring 10

                            if validWorkspaceAlias alias then
                                Ok(ArtifactSelector.Workspace alias)
                            else
                                invalid "invalid workspace alias"
                        else
                            invalid "unknown artifact selector"

                    let parameters =
                        query.Split('&')
                        |> List.ofArray
                        |> List.fold
                            (fun state parameter ->
                                state
                                |> Result.bind (fun (format, revision, guard) ->
                                    let equals = parameter.IndexOf '='

                                    if equals < 0 then
                                        invalid "malformed query parameter"
                                    else
                                        let key = parameter.Substring(0, equals)
                                        let value = parameter.Substring(equals + 1)

                                        match key with
                                        | "format" when Option.isNone format ->
                                            parseFormat value |> Result.map (fun f -> Some f, revision, guard)
                                        | "rev" when Option.isNone revision ->
                                            Sha256Digest.Parse value
                                            |> Result.mapError NodeUriError.Invalid
                                            |> Result.map (fun d -> format, Some d, guard)
                                        | "guard" when Option.isNone guard ->
                                            Sha256Digest.Parse value
                                            |> Result.mapError NodeUriError.Invalid
                                            |> Result.map (fun d -> format, revision, Some d)
                                        | _ -> invalid "duplicate or unknown query parameter"))
                            (Ok(None, None, None))

                    match artifact, parameters with
                    | Error error, _
                    | _, Error error -> Error error
                    | Ok artifact, Ok(None, _, _) -> ignore artifact; invalid "missing format"
                    | Ok artifact, Ok(Some format, revision, guard) ->
                        if not (fragment.StartsWith("/", StringComparison.Ordinal)) then
                            invalid "node fragment must start with /"
                        else
                            let parts = fragment.Substring(1).Split('/') |> List.ofArray

                            parseRoot parts
                            |> Result.bind (fun (root, rest) -> parseSteps rest |> Result.map (fun steps -> root, steps))
                            |> Result.bind (fun (root, steps) ->
                                let pinned = revision |> Option.isSome

                                if not pinned && Option.isNone guard && steps |> List.exists (fun s -> s.IsPositional) then
                                    Error NodeUriError.MissingGuard
                                else
                                    let address =
                                        {
                                            ArtifactValue = artifact
                                            FormatValue = format
                                            RootValue = root
                                            StepsValue = steps
                                            RevisionValue = (match revision with Some d -> ArtifactRevision.Pinned d | None -> ArtifactRevision.Current)
                                            GuardValue = guard
                                        }

                                    if toUri address <> uri then invalid "noncanonical URI spelling" else Ok address)

    /// <summary>
    /// Builds an address from typed parts, only if its canonical spelling parses back to the same address. This checks the
    /// names and the guard rule.
    /// </summary>
    let create
        (artifact: ArtifactSelector)
        (format: FormatTriplet)
        (root: NodeRoot)
        (steps: NodeStep list)
        (revision: ArtifactRevision)
        (guard: Sha256Digest option)
        : Result<NodeAddress, NodeUriError> =
        let candidate =
            {
                ArtifactValue = artifact
                FormatValue = format
                RootValue = root
                StepsValue = steps
                RevisionValue = revision
                GuardValue = guard
            }

        match parse (toUri candidate) with
        | Ok parsed when parsed = candidate -> Ok parsed
        | Ok _ -> invalid "address is not canonical"
        | Error error -> Error error

    /// <summary>
    /// Builds an address from parts that are valid by construction, as the walk of a model gives them. No text round
    /// trip. The caller keeps the rule that an address that is not pinned has no ordered step.
    /// </summary>
    let internal unchecked
        (artifact: ArtifactSelector)
        (format: FormatTriplet)
        (root: NodeRoot)
        (steps: NodeStep list)
        (revision: ArtifactRevision)
        : NodeAddress =
        {
            ArtifactValue = artifact
            FormatValue = format
            RootValue = root
            StepsValue = steps
            RevisionValue = revision
            GuardValue = None
        }
