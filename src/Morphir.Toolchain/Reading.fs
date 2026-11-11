// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System

/// A value with the path it was found at.
type internal Node = { Value: Value; Path: DocumentPath }

/// An object value with the path it was found at. Members not read are ignored: a reader understands only the members
/// it asks for.
type internal ObjectNode =
    { Members: (string * Value) list
      Path: DocumentPath }

/// <summary>
/// The reading rules shared by all five documents (overview page, "Rules shared by all five"): the order of checks,
/// unknown and critical members, credentials, numbers, tagged cases, and data-keyed maps.
/// </summary>
module internal Reading =

    type Read<'T> = Result<'T, ReaderRefusal>

    let refuse code (path: DocumentPath) : Read<'T> = Error { Code = code; Path = path }

    let node (value: Value) path = { Value = value; Path = path }

    /// The member names that are credentials, compared ASCII case-insensitively.
    let credentialNames =
        [ "password"
          "passwd"
          "secret"
          "token"
          "apiKey"
          "api-key"
          "api_key"
          "authorization"
          "credential"
          "credentials"
          "cookie" ]

    let isCredential (name: string) =
        credentialNames |> List.exists (equalsAsciiIgnoreCase name)

    /// <summary>The first credential member of a depth-first scan that visits each object's members in UTF-8 byte
    /// order of their names and each array's elements in index order.</summary>
    let rec firstCredential (path: DocumentPath) (value: Value) : DocumentPath option =
        match value with
        | Value.Object members ->
            members
            |> List.sortWith (fun (a, _) (b, _) -> compareUtf8 a b)
            |> List.tryPick (fun (name, item) ->
                if isCredential name then
                    Some(path.Child name)
                else
                    firstCredential (path.Child name) item)
        | Value.Array items -> items |> List.indexed |> List.tryPick (fun (index, item) -> firstCredential (path.At index) item)
        | _ -> None

    // ---- typed access ------------------------------------------------------------------------------------------

    let asObject (n: Node) : Read<ObjectNode> =
        match n.Value with
        | Value.Object members -> Ok { Members = members; Path = n.Path }
        | _ -> refuse ReaderCode.InvalidDocument n.Path

    let tryMember (name: string) (o: ObjectNode) =
        o.Members
        |> List.tryFind (fun (candidate, _) -> String.Equals(candidate, name, StringComparison.Ordinal))
        |> Option.map (fun (_, value) -> node value (o.Path.Child name))

    let has name o = (tryMember name o).IsSome

    /// A required member; absence is <c>invalid_document</c> at the member.
    let required name (read: Node -> Read<'T>) (o: ObjectNode) : Read<'T> =
        match tryMember name o with
        | Some n -> read n
        | None -> refuse ReaderCode.InvalidDocument (o.Path.Child name)

    let optional name (read: Node -> Read<'T>) (o: ObjectNode) : Read<'T option> =
        match tryMember name o with
        | Some n -> read n |> Result.map Some
        | None -> Ok None

    let objectMember name (o: ObjectNode) = required name asObject o

    let text (n: Node) : Read<string> =
        match n.Value with
        | Value.String value -> Ok value
        | _ -> refuse ReaderCode.InvalidDocument n.Path

    /// A string validated by <paramref name="parse"/>, whose error code is reported at the member.
    let validated (parse: string -> Result<'T, ReaderCode>) (n: Node) : Read<'T> =
        text n |> Result.bind (fun value -> parse value |> Result.mapError (fun code -> { Code = code; Path = n.Path }))

    /// A tagged member: a string naming one case of a closed set. An unknown case is <c>unsupported_case</c>.
    let tag (cases: (string * 'T) list) (n: Node) : Read<'T> =
        text n
        |> Result.bind (fun value ->
            match cases |> List.tryFind (fun (name, _) -> name = value) with
            | Some(_, case) -> Ok case
            | None -> refuse ReaderCode.UnsupportedCase n.Path)

    /// A non-negative integer in plain decimal. Any other spelling, such as <c>1.0</c>, <c>1e3</c> or <c>-1</c>, or
    /// another JSON type, is <c>invalid_document</c>; a value beyond 64 bits is <c>invalid_value</c>.
    let integer (n: Node) : Read<uint64> =
        match n.Value with
        | Value.Number spelling when
            spelling.Length > 0
            && Seq.forall isAsciiDigit spelling
            && (spelling = "0" || spelling[0] <> '0')
            ->
            match UInt64.TryParse(spelling, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
            | true, value -> Ok value
            | false, _ -> refuse ReaderCode.InvalidValue n.Path
        | _ -> refuse ReaderCode.InvalidDocument n.Path

    let items (n: Node) : Read<Node list> =
        match n.Value with
        | Value.Array values -> Ok(values |> List.mapi (fun index value -> node value (n.Path.At index)))
        | _ -> refuse ReaderCode.InvalidDocument n.Path

    /// An array whose elements are read in index order.
    let array (read: Node -> Read<'T>) (n: Node) : Read<'T list> =
        items n |> Result.bind (Result.traverse read)

    /// <summary>An array of strings whose elements are read in index order and listed once each: an element whose text
    /// equals an earlier one's, compared ordinally, is <c>inconsistent_document</c> at the repetition, checked as it is
    /// read. The earlier texts are kept in a hash set, so the check is linear in the array: a list scan made a recipe
    /// of 453 KB take 85 s (review of #582, R1). The values read are compared by their text, which is how every value
    /// read here is equal (<c>ProtocolVersion</c> and <c>Token</c>).</summary>
    let distinctArray (read: Node -> Read<'T>) (n: Node) : Read<'T list> =
        let seen = System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)

        let rec go (earlier: 'T list) (rest: Node list) =
            match rest with
            | [] -> Ok(List.rev earlier)
            | element :: rest ->
                match read element, element.Value with
                | Error refusal, _ -> Error refusal
                | Ok _, Value.String text when not (seen.Add text) -> refuse ReaderCode.InconsistentDocument element.Path
                | Ok value, _ -> go (value :: earlier) rest

        items n |> Result.bind (go [])

    /// <summary>A map keyed by platform ids. Every key is checked first: the first invalid key in UTF-8 byte order is
    /// <c>invalid_value</c> at that key. The entries are then read in platform table order.</summary>
    let platformMap (read: Platform -> Node -> Read<'T>) (n: Node) : Read<(Platform * 'T) list> =
        match n.Value with
        | Value.Object members ->
            let keys = members |> List.map fst |> List.sortWith compareUtf8

            match keys |> List.tryFind (fun key -> Result.isError (Platform.tryParse key)) with
            | Some invalid -> refuse ReaderCode.InvalidValue (n.Path.Child invalid)
            | None ->
                members
                |> List.map (fun (key, value) ->
                    match Platform.tryParse key with
                    | Ok platform -> platform, node value (n.Path.Child key)
                    | Error _ -> failwith "unreachable: every key was checked")
                |> List.sortBy (fst >> Platform.order)
                |> Result.traverse (fun (platform, entry) -> read platform entry |> Result.map (fun value -> platform, value))
        | _ -> refuse ReaderCode.InvalidDocument n.Path

    // ---- the document envelope ---------------------------------------------------------------------------------

    /// <summary>
    /// Checks 2 to 4 of the order of checks on a document's node: <c>schemaVersion</c>, then <c>critical</c>, then the
    /// credential scan. The body (check 5) is the caller's. A document nested in another, such as the lock inside a
    /// plan, reports its refusals under its own path.
    /// </summary>
    let envelopeAt (understood: Set<string>) (n: Node) : Read<ObjectNode> =
        result {
            let! o = asObject n
            let versionPath = n.Path.Child "schemaVersion"

            match tryMember "schemaVersion" o with
            | Some { Value = Value.String version } ->
                do! ContractVersion.check version |> Result.mapError (fun code -> { Code = code; Path = versionPath })
            | _ -> return! refuse ReaderCode.InvalidDocument versionPath

            match tryMember "critical" o with
            | None -> ()
            | Some critical ->
                let! entries =
                    match critical.Value with
                    | Value.Array values when values |> List.forall (function Value.String _ -> true | _ -> false) ->
                        Ok(values |> List.map (function Value.String path -> path | _ -> ""))
                    | _ -> refuse ReaderCode.InvalidDocument critical.Path

                if List.length (List.distinct entries) <> List.length entries then
                    return! refuse ReaderCode.InconsistentDocument critical.Path

                match entries |> List.tryFind (understood.Contains >> not) with
                | Some unknown ->
                    let at = if n.Path.Segments.IsEmpty then DocumentPath.verbatim unknown else n.Path.Child unknown
                    return! refuse ReaderCode.UnsupportedCriticalMember at
                | None -> ()

            match firstCredential n.Path n.Value with
            | Some path -> return! refuse ReaderCode.CredentialInDocument path
            | None -> ()

            return o
        }

    /// The envelope checks on a parsed document root.
    let envelope (understood: Set<string>) (root: Value) : Read<ObjectNode> =
        envelopeAt understood (node root DocumentPath.root)

    // ---- shared member shapes ----------------------------------------------------------------------------------

    let token = validated Token.tryParse
    let version = validated SemVer.tryParse
    let sha256 = validated Sha256.tryParse
    let targetPath = validated TargetPath.tryParse
    let platform = validated Platform.tryParse
    let extensionId = validated ExtensionId.tryParse
    let packageId = validated PackageId.tryParse

    [<RequireQualifiedAccess>]
    type OperationTag =
        | Compile
        | Generate

    /// <c>operation</c> with <c>language</c> for <c>compile</c> or <c>target</c> for <c>generate</c>.
    let operation (o: ObjectNode) : Read<Operation> =
        result {
            let! kind = required "operation" (tag [ "compile", OperationTag.Compile; "generate", OperationTag.Generate ]) o

            match kind with
            | OperationTag.Compile ->
                let! language = required "language" token o
                return Operation.Compile language
            | OperationTag.Generate ->
                let! target = required "target" token o
                return Operation.Generate target
        }

    let modelFormat (n: Node) : Read<ModelFormat> =
        result {
            let! o = asObject n
            let! profile = required "profile" token o
            let! codec = required "codec" token o
            let! irVersion = required "irVersion" version o

            return
                { Profile = profile
                  Codec = codec
                  IrVersion = irVersion }
        }

    /// <summary>The member a built-in uses for the model it exchanges with its host: <c>modelOutput</c> for a
    /// <c>compile</c> built-in, which writes the model, and <c>modelInput</c> for a <c>generate</c> built-in.</summary>
    let builtInModelMember (operation: Operation) =
        match operation with
        | Operation.Compile _ -> "modelOutput"
        | Operation.Generate _ -> "modelInput"

    /// <summary>A built-in's model: the member its operation names, required. A built-in has no MEP session, so
    /// <c>protocolVersion</c>, <c>modelTuple</c> and the other model member are each <c>inconsistent_document</c> at
    /// that member, checked in that order before the model member (index.md, order of checks).</summary>
    let builtInModel (operation: Operation) (o: ObjectNode) : Read<ModelFormat> =
        let wanted = builtInModelMember operation
        let other = if wanted = "modelInput" then "modelOutput" else "modelInput"

        match [ "protocolVersion"; "modelTuple"; other ] |> List.tryFind (fun name -> has name o) with
        | Some name -> refuse ReaderCode.InconsistentDocument (o.Path.Child name)
        | None -> required wanted modelFormat o

    let modelTuple (n: Node) : Read<ModelTuple> =
        result {
            let! o = asObject n
            let! operation = required "operation" (validated ModelOperation.tryParse) o
            let! profile = required "profile" token o
            let! codec = required "codec" token o
            let! envelopeVersion = required "envelopeVersion" version o
            let! irVersion = required "irVersion" version o

            return
                { Operation = operation
                  Profile = profile
                  Codec = codec
                  EnvelopeVersion = envelopeVersion
                  IrVersion = irVersion }
        }

    /// <summary>
    /// An external provider's protocol members: <c>protocolVersion</c>, a MEP version on the 0.2 line
    /// (<c>unsupported_case</c> off it), and one <c>modelTuple</c>. An external provider that declares
    /// <c>modelInput</c> or <c>modelOutput</c> is <c>inconsistent_document</c> at that member; those belong to
    /// built-ins. A tuple whose operation does not perform the provider's operation is <c>inconsistent_document</c> at
    /// <c>modelTuple.operation</c>.
    /// </summary>
    let protocol (operation: Operation) (o: ObjectNode) : Read<Protocol> =
        result {
            let! protocolVersion = required "protocolVersion" (validated ProtocolVersion.tryParse) o

            for name in [ "modelInput"; "modelOutput" ] do
                if has name o then
                    return! refuse ReaderCode.InconsistentDocument (o.Path.Child name)

            let! tuple = required "modelTuple" modelTuple o

            if not (ModelOperation.performs operation tuple.Operation) then
                return! refuse ReaderCode.InconsistentDocument ((o.Path.Child "modelTuple").Child "operation")

            return { Version = protocolVersion; Tuple = tuple }
        }
    let comparators (n: Node) = array (validated Comparator.tryParse) n

    [<RequireQualifiedAccess>]
    type PrerequisiteTag =
        | Runtime
        | Sdk
        | Native

    let prerequisite (n: Node) : Read<Prerequisite> =
        result {
            let! o = asObject n

            let! kind =
                required "kind" (tag [ "runtime", PrerequisiteTag.Runtime; "sdk", PrerequisiteTag.Sdk; "native", PrerequisiteTag.Native ]) o

            let! name = required "name" (validated NonBlankText.tryParse) o
            let! platforms = optional "platforms" (array platform) o

            let! kind =
                match kind with
                | PrerequisiteTag.Runtime -> required "versions" comparators o |> Result.map PrerequisiteKind.Runtime
                | PrerequisiteTag.Sdk ->
                    result {
                        let! versions = required "versions" comparators o
                        let! phase = required "phase" (tag [ "install", SdkPhase.Install; "run", SdkPhase.Run ]) o
                        return PrerequisiteKind.Sdk(versions, phase)
                    }
                | PrerequisiteTag.Native -> optional "versions" comparators o |> Result.map PrerequisiteKind.Native

            return
                { Kind = kind
                  Name = name
                  Platforms = platforms }
        }

    // ---- closed tags, matched exhaustively by the readers ---------------------------------------------------

    [<RequireQualifiedAccess>]
    type CheckStatusTag =
        | Passed
        | Failed
        | Skipped
        | Unsupported

    [<RequireQualifiedAccess>]
    type OwnershipTag =
        | ManagerOwned
        | DirectArchive
        | DotnetTool
        | SystemPackage

    [<RequireQualifiedAccess>]
    type AuthenticationTag =
        | RepositoryTarget
        | RecipeDigest
        | LocalDeveloper
        | ExternalOwner

    [<RequireQualifiedAccess>]
    type RuntimeTag =
        | None
        | Bundled
        | System

    [<RequireQualifiedAccess>]
    type SourceTag =
        | ToolRelease
        | DotnetTool

    [<RequireQualifiedAccess>]
    type RoleTag =
        | Host
        | Provider

    [<RequireQualifiedAccess>]
    type SelectionTag =
        | Cli
        | Environment
        | Project
        | UserDefault

    [<RequireQualifiedAccess>]
    type ComponentSourceTag =
        | RepositoryTarget
        | RecipeDigest
        | BuiltIn
        | ExternalOwner

    [<RequireQualifiedAccess>]
    type ClaimTag =
        | Probed
        | Unchecked

    /// Reads a document: parse its syntax, check the envelope, then read the body.
    let document parse (understood: Set<string>) (body: ObjectNode -> Read<'T>) (bytes: byte array) : Read<'T> =
        parse bytes |> Result.bind (envelope understood) |> Result.bind body
