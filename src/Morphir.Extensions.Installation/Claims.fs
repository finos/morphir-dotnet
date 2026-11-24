// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open Morphir.Toolchain
open Morphir.Mep

type internal ReleaseClaims = {
    Json: Value
    Projection: Value
    Extension: ExtensionInfo
    ProtocolVersions: ProtocolVersion list
    HostRequirements: Comparator list
    SchemaVersion: ExtensionSchemaVersion
    HasModelAccess: bool
}

module internal Claims =
    open SharedJson

    // Array elements use the path of their array, as the published member tables require.
    let private baseKnownPaths =
        set [
            "protocolVersions"
            "extension"
            "extension.id"
            "extension.name"
            "extension.version"
            "extension.types"
            "extension.description"
            "extension.author"
            "extension.homepage"
            "extension.license"
            "extension.min_sdk_version"
            "capabilities"
            "capabilities.frontend"
            "capabilities.frontend.languages"
            "capabilities.frontend.languages.id"
            "capabilities.frontend.languages.fileExtensions"
            "capabilities.frontend.irVersions"
            "capabilities.frontend.compile"
            "capabilities.frontend.incremental"
            "capabilities.frontend.fragments"
            "capabilities.frontend.multiDocument"
            "capabilities.backend"
            "capabilities.backend.targets"
            "capabilities.backend.irVersions"
            "capabilities.backend.generate"
            "capabilities.workspace"
            "capabilities.workspace.protocolVersions"
            "capabilities.workspace.discover"
            "capabilities.validator"
            "capabilities.validator.irVersions"
            "capabilities.validator.rules"
            "capabilities.validator.rules.id"
            "capabilities.validator.rules.code"
            "capabilities.transform"
            "capabilities.modelTransport"
            "capabilities.modelTransport.tuples"
            "capabilities.modelTransport.tuples.operation"
            "capabilities.modelTransport.tuples.profile"
            "capabilities.modelTransport.tuples.codec"
            "capabilities.modelTransport.tuples.envelopeVersion"
            "capabilities.modelTransport.tuples.irVersion"
            "capabilities.modelTransport.limits"
            "capabilities.modelTransport.limits.maxEncodedBytes"
            "capabilities.modelTransport.limits.maxDecodedBytes"
            "capabilities.modelTransport.limits.maxDepth"
            "capabilities.modelTransport.limits.maxNodes"
            "capabilities.modelTransport.guarantees"
            "capabilities.streaming"
            "capabilities.incremental"
            "capabilities.cancellation"
            "capabilities.progress"
            "requires"
            "requires.host"
            "critical"
        ]

    let private modelAccessKnownPaths =
        set [
            "capabilities.modelAccess"
            "capabilities.modelAccess.tuples"
            "capabilities.modelAccess.tuples.protocolVersion"
            "capabilities.modelAccess.tuples.accessVersion"
            "capabilities.modelAccess.tuples.operation"
            "capabilities.modelAccess.tuples.irVersion"
            "capabilities.modelAccess.tuples.profile"
            "capabilities.modelAccess.tuples.codec"
            "capabilities.modelAccess.tuples.layout"
            "capabilities.modelAccess.tuples.contextualEncoding"
            "capabilities.modelAccess.limits"
            "capabilities.modelAccess.limits.maxFrameBytes"
            "capabilities.modelAccess.limits.maxEncodedBytesPerExchange"
            "capabilities.modelAccess.limits.maxDecodedBytesPerExchange"
            "capabilities.modelAccess.limits.maxDocumentsPerBatch"
            "capabilities.modelAccess.limits.maxDepth"
            "capabilities.modelAccess.limits.maxNodes"
            "capabilities.modelAccess.limits.maxDiagnosticsPerExchange"
            "capabilities.modelAccess.limits.maxOutstandingRequests"
            "capabilities.modelAccess.limits.maxGuestStateBytes"
            "capabilities.modelAccess.limits.maxHostCacheBytes"
            "capabilities.modelAccess.limits.maxArtifactChunkBytes"
            "capabilities.modelAccess.limits.maxArtifactBytes"
            "capabilities.modelAccess.limits.maxTotalArtifactBytes"
            "capabilities.modelAccess.limits.maxSessionDiskBytes"
            "capabilities.modelAccess.limits.maxSessionWorkUnits"
            "capabilities.modelAccess.limits.maxRounds"
            "capabilities.modelAccess.limits.maxCallMilliseconds"
            "capabilities.modelAccess.limits.maxSessionMilliseconds"
        ]

    let private preview4KnownPaths = Set.union baseKnownPaths modelAccessKnownPaths

    let knownPathsFor =
        function
        | ExtensionSchemaVersion.Preview3 -> baseKnownPaths
        | ExtensionSchemaVersion.Preview4 -> preview4KnownPaths

    let knownPaths = preview4KnownPaths

    let readCritical path known items =
        match optional "critical" items with
        | None -> []
        | Some value ->
            let paths =
                strings $"{path}.critical" value
                |> distinct $"{path}.critical"

            for memberPath in paths do
                if memberPath.Length = 0 then
                    invalid $"{path}.critical" "A critical path cannot be empty."

                if not (Set.contains memberPath known) then
                    fail
                        "extension.unsupported_critical_member"
                        path
                        $"Unknown critical member '{memberPath}'."

            paths

    let private version path value = parsed path SemVer.tryParse value

    let private fields path requiredNames textNames booleanNames items =
        let parsed = ResizeArray<string * Value>()

        for name in requiredNames do
            required path name items
            |> ignore

        for name, value in items do
            if List.contains name textNames then
                parsed.Add(name, Value.String(text $"{path}.{name}" value))
            elif List.contains name booleanNames then
                parsed.Add(name, Value.Boolean(boolean $"{path}.{name}" value))

        List.ofSeq parsed

    let private textArrays path names items = [
        for name in names do
            match optional name items with
            | Some value ->
                yield
                    name,
                    Value.Array(
                        strings $"{path}.{name}" value
                        |> List.map Value.String
                    )
            | None -> ()
    ]

    let private language path value =
        let items = members path value

        let id =
            required path "id" items
            |> nonempty $"{path}.id"

        let suffixPath = $"{path}.fileExtensions"

        let suffixes =
            required path "fileExtensions" items
            |> strings suffixPath
            |> distinct suffixPath

        if List.isEmpty suffixes then
            invalid suffixPath "At least one file extension is required."

        for suffix in suffixes do
            if
                not (suffix.StartsWith(".", StringComparison.Ordinal))
                || suffix
                   <> suffix.Trim()
            then
                invalid suffixPath "File extensions must be trimmed dot-prefixed suffixes."

        Value.Object [
            "id", Value.String id
            "fileExtensions", Value.Array(List.map Value.String suffixes)
        ]

    let private frontend path value =
        let items = members path value

        let languages =
            required path "languages" items
            |> array $"{path}.languages"
            |> List.mapi (fun index item -> language $"{path}.languages[{index + 1}]" item)

        required path "irVersions" items
        |> strings $"{path}.irVersions"
        |> ignore

        let booleans =
            fields
                path
                [ "compile" ]
                []
                [ "compile"; "incremental"; "fragments"; "multiDocument" ]
                items

        Value.Object(
            ("languages", Value.Array languages)
            :: (booleans
                @ textArrays path [ "irVersions" ] items)
        )

    let private backend path value =
        let items = members path value

        for name in [ "targets"; "irVersions" ] do
            required path name items
            |> strings $"{path}.{name}"
            |> ignore

        let booleans = fields path [ "generate" ] [] [ "generate" ] items

        Value.Object(
            booleans
            @ textArrays path [ "targets"; "irVersions" ] items
        )

    let private workspace path value =
        let items = members path value

        required path "protocolVersions" items
        |> strings $"{path}.protocolVersions"
        |> ignore

        let booleans = fields path [ "discover" ] [] [ "discover" ] items

        Value.Object(
            booleans
            @ textArrays path [ "protocolVersions" ] items
        )

    let private validator path value =
        let items = members path value

        required path "irVersions" items
        |> strings $"{path}.irVersions"
        |> ignore

        let rules =
            match optional "rules" items with
            | None -> []
            | Some value ->
                array $"{path}.rules" value
                |> List.mapi (fun index rule ->
                    let rulePath = $"{path}.rules[{index + 1}]"
                    let ruleItems = members rulePath rule
                    Value.Object(fields rulePath [ "id"; "code" ] [ "id"; "code" ] [] ruleItems)
                )

        Value.Object(
            ("rules", Value.Array rules)
            :: textArrays path [ "irVersions" ] items
        )

    let private modelTransport path value =
        let items = members path value

        let tuples =
            required path "tuples" items
            |> array $"{path}.tuples"
            |> List.mapi (fun index tuple ->
                let tuplePath = $"{path}.tuples[{index + 1}]"
                let tupleItems = members tuplePath tuple
                let names = [ "operation"; "profile"; "codec"; "envelopeVersion"; "irVersion" ]
                let projection = fields tuplePath names names [] tupleItems

                let operation =
                    required tuplePath "operation" tupleItems
                    |> text $"{tuplePath}.operation"

                if
                    not (
                        List.contains operation [
                            "morphir.frontend.compileModel"
                            "morphir.backend.generateModel"
                            "morphir.validator.validateModel"
                        ]
                    )
                then
                    invalid $"{tuplePath}.operation" "Unknown model transport operation."

                Value.Object projection
            )

        let limitsPath = $"{path}.limits"

        let limits =
            required path "limits" items
            |> members limitsPath

        let projectedLimits = [
            for name in [ "maxEncodedBytes"; "maxDecodedBytes"; "maxDepth"; "maxNodes" ] do
                let number = required limitsPath name limits

                match number with
                | Value.Number spelling when
                    spelling.Length > 0
                    && spelling[0]
                       <> '-'
                    && spelling
                       <> "0"
                    ->
                    yield name, number
                | _ -> invalid $"{limitsPath}.{name}" "Expected a positive integer."
        ]

        let guarantees =
            optional "guarantees" items
            |> Option.map (strings $"{path}.guarantees")
            |> Option.defaultValue []

        Value.Object [
            "tuples", Value.Array tuples
            "limits", Value.Object projectedLimits
            "guarantees", Value.Array(List.map Value.String guarantees)
        ]

    let private modelAccessLimitNames = [
        "maxFrameBytes"
        "maxEncodedBytesPerExchange"
        "maxDecodedBytesPerExchange"
        "maxDocumentsPerBatch"
        "maxDepth"
        "maxNodes"
        "maxDiagnosticsPerExchange"
        "maxOutstandingRequests"
        "maxGuestStateBytes"
        "maxHostCacheBytes"
        "maxArtifactChunkBytes"
        "maxArtifactBytes"
        "maxTotalArtifactBytes"
        "maxSessionDiskBytes"
        "maxSessionWorkUnits"
        "maxRounds"
        "maxCallMilliseconds"
        "maxSessionMilliseconds"
    ]

    let private modelAccess path value =
        let items = members path value

        let tuples =
            required path "tuples" items
            |> array $"{path}.tuples"
            |> List.mapi (fun index tuple ->
                let tuplePath = $"{path}.tuples[{index + 1}]"
                let tupleItems = members tuplePath tuple

                let names = [
                    "protocolVersion"
                    "accessVersion"
                    "operation"
                    "irVersion"
                    "profile"
                    "codec"
                    "layout"
                    "contextualEncoding"
                ]

                let projection = fields tuplePath names names [] tupleItems

                let operation =
                    required tuplePath "operation" tupleItems
                    |> text $"{tuplePath}.operation"

                if operation <> "morphir.backend.generateModel" then
                    invalid $"{tuplePath}.operation" "Unknown model access operation."

                Value.Object projection
            )

        let limitsPath = $"{path}.limits"

        let limits =
            required path "limits" items
            |> members limitsPath

        let projectedLimits = [
            for name in modelAccessLimitNames do
                let number = required limitsPath name limits

                match number with
                | Value.Number spelling when
                    match Int64.TryParse(spelling, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                    | true, parsed -> parsed > 0L && parsed <= 9007199254740991L
                    | false, _ -> false
                    ->
                    yield name, number
                | _ -> invalid $"{limitsPath}.{name}" "Expected a positive integer."
        ]

        let projected =
            Value.Object [
                "tuples", Value.Array tuples
                "limits", Value.Object projectedLimits
            ]

        let typed =
            try
                System.Text.Json.JsonSerializer.Deserialize(
                    JsonText.toBytes projected,
                    MepJson.Default.ModelAccessSupport
                )
            with
            | :? System.Text.Json.JsonException as error ->
                invalid path $"Invalid modelAccess claims: {error.Message}"

        match Option.ofObj typed with
        | None -> invalid path "Invalid modelAccess claims."
        | Some support ->
            match ModelAccess.validateSupport support with
            | Ok _ -> projected
            | Error message -> invalid path message

    let private capabilities (allowModelAccess: bool) path types value =
        let items = members path value

        for kind in types do
            required path kind items
            |> ignore

        let projected = ResizeArray<string * Value>()
        let mutable hasModelAccess = false

        for name, value in items do
            let location = $"{path}.{name}"

            let parsed =
                match name with
                | "frontend" -> Some(frontend location value)
                | "backend" -> Some(backend location value)
                | "workspace" -> Some(workspace location value)
                | "validator" -> Some(validator location value)
                | "transform" ->
                    members location value
                    |> ignore

                    Some(Value.Object [])
                | "modelTransport" -> Some(modelTransport location value)
                | "modelAccess" ->
                    if not allowModelAccess then
                        invalid location "capabilities.modelAccess requires extension contract 0.1.0-preview.4 and is not permitted under 0.1.0-preview.3."

                    hasModelAccess <- true
                    Some(modelAccess location value)
                | "streaming"
                | "incremental"
                | "cancellation"
                | "progress" -> Some(Value.Boolean(boolean location value))
                | _ -> None

            match parsed with
            | Some parsed -> projected.Add(name, parsed)
            | None -> ()

        Value.Object(List.ofSeq projected), hasModelAccess

    let private readAt (schemaVersion: ExtensionSchemaVersion) path known value =
        let items = members path value

        for forbidden in [ "claimsVersion"; "statementVersion" ] do
            if
                optional forbidden items
                |> Option.isSome
            then
                fail
                    "extension.unsupported_contract_version"
                    $"{path}.{forbidden}"
                    "Legacy claim formats are not converted."

        let protocolVersions =
            required path "protocolVersions" items
            |> array $"{path}.protocolVersions"
            |> List.mapi (fun index value ->
                let exact = version $"{path}.protocolVersions[{index + 1}]" value
                ProtocolVersion.Create exact.Text
            )

        if List.isEmpty protocolVersions then
            invalid $"{path}.protocolVersions" "Expected at least one protocol version."

        let identityPath = $"{path}.extension"

        let identity =
            required path "extension" items
            |> members identityPath

        let id =
            required identityPath "id" identity
            |> parsed $"{identityPath}.id" Morphir.Extensions.ExtensionId.Parse

        let name =
            required identityPath "name" identity
            |> nonempty $"{identityPath}.name"

        let extensionVersion =
            required identityPath "version" identity
            |> version $"{identityPath}.version"

        let types =
            required identityPath "types" identity
            |> strings $"{identityPath}.types"
            |> distinct $"{identityPath}.types"

        if List.isEmpty types then
            invalid $"{identityPath}.types" "Expected at least one capability kind."

        let kinds =
            types
            |> List.map (
                function
                | "frontend" -> ExtensionType.Frontend
                | "backend" -> ExtensionType.Backend
                | "transform" -> ExtensionType.Transform
                | "validator" -> ExtensionType.Validator
                | "workspace" -> ExtensionType.Workspace
                | other -> invalid $"{identityPath}.types" $"Unknown capability kind '{other}'."
            )

        let optionalText field =
            optional field identity
            |> Option.map (text $"{identityPath}.{field}")

        let extension = {
            Id = id.Value
            Name = name
            Version = extensionVersion.Text
            Description = optionalText "description"
            Types = List.toArray kinds
            Author = optionalText "author"
            Homepage = optionalText "homepage"
            License = optionalText "license"
            MinSdkVersion = optionalText "min_sdk_version"
        }

        let identityProjection =
            fields
                identityPath
                [ "id"; "name"; "version" ]
                [
                    "id"
                    "name"
                    "version"
                    "description"
                    "author"
                    "homepage"
                    "license"
                    "min_sdk_version"
                ]
                []
                identity
            @ [ "types", Value.Array(List.map Value.String types) ]

        let capProjection, hasModelAccess =
            required path "capabilities" items
            |> capabilities (ExtensionSchemas.supportsModelAccess schemaVersion) $"{path}.capabilities" types

        let critical = readCritical path known items

        let requirements =
            match optional "requires" items with
            | None -> []
            | Some requirements ->
                let hostPath = $"{path}.requires.host"
                let requiredItems = members $"{path}.requires" requirements

                let hosts =
                    required $"{path}.requires" "host" requiredItems
                    |> strings hostPath

                if List.isEmpty hosts then
                    invalid hostPath "Expected at least one comparator."

                let comparators =
                    hosts
                    |> List.map (fun value ->
                        parsed hostPath Comparator.tryParse (Value.String value)
                    )

                if not (List.contains "requires.host" critical) then
                    invalid $"{path}.critical" "requires.host must be critical."

                comparators

        let projection =
            [
                "protocolVersions",
                Value.Array(
                    protocolVersions
                    |> List.map (fun protocol -> Value.String protocol.Value)
                )
                "extension", Value.Object identityProjection
                "capabilities", capProjection
                "critical", Value.Array(List.map Value.String critical)
            ]
            @ (if List.isEmpty requirements then
                   []
               else
                   [
                       "requires",
                       Value.Object [
                           "host",
                           Value.Array(
                               requirements
                               |> List.map (fun comparator -> Value.String comparator.Text)
                           )
                       ]
                   ])

        {
            Json = value
            Projection = Value.Object projection
            Extension = extension
            ProtocolVersions = protocolVersions
            HostRequirements = requirements
            SchemaVersion = schemaVersion
            HasModelAccess = hasModelAccess
        }

    let readEmbeddedFor (schemaVersion: ExtensionSchemaVersion) path value =
        let items = members path value

        if
            optional "$schema" items
            |> Option.isSome
        then
            fail
                "extension.unsupported_contract_version"
                $"{path}.$schema"
                "Embedded claims have no schema or version member."

        readAt schemaVersion path (knownPathsFor schemaVersion) value

    let readEmbedded path value =
        readEmbeddedFor ExtensionSchemaVersion.Preview4 path value

    let readStandalone (bytes: byte array) : Result<ReleaseClaims, InstallationFailure> =
        match SharedJson.read "claims.json" bytes with
        | Error failure -> Error failure
        | Ok value ->
            protect (fun () ->
                let items = members "claims.json" value
                let schemaVersion = ExtensionSchemas.readVersion "claims.json" "claims" items
                readAt schemaVersion "claims.json" (Set.add "$schema" (knownPathsFor schemaVersion)) value
            )

    let equivalent (left: ReleaseClaims) (right: ReleaseClaims) =
        difference "claims" left.Projection right.Projection
        |> Option.isNone

    let supportsHost (host: SemVer) (claims: ReleaseClaims) =
        Comparator.allMet claims.HostRequirements host
    /// Checks an actual negotiated session against its installed static claims. This is not an invocation receipt.
    let agreeSession (claims: ReleaseClaims) (session: NegotiatedSession) : Result<unit,InstallationFailure> =
        protect (fun () ->
            let mismatch rule path = fail "extension.claims_mismatch" path $"Installed session agreement rule {rule} failed."
            if session.Extension.Id <> claims.Extension.Id || session.Extension.Name <> claims.Extension.Name || session.Extension.Version <> claims.Extension.Version then
                mismatch 1 "extension"
            if not (claims.ProtocolVersions |> List.contains session.ProtocolVersion) then mismatch 2 "protocolVersion"
            for kind in session.Extension.Types do
                if not (claims.Extension.Types |> Seq.contains kind) then mismatch 3 "extension.types"
            let staticFields = members "claims" claims.Projection |> required "claims" "capabilities" |> members "claims.capabilities"
            let actualBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(session.Capabilities,MepJson.Default.ExtensionCapabilities)
            let actual = Syntax.parseJson actualBytes |> function Ok value -> value | Error _ -> invalid "session" "Malformed session capabilities."
            for kind,capability in members "session.capabilities" actual do
                let rule = if List.contains kind ["frontend";"backend";"workspace";"validator";"transform"] then 3 else 4
                let known = optional kind staticFields |> Option.defaultWith(fun () -> mismatch rule kind; Value.Null)
                if kind = "modelTransport" || kind = "modelAccess" then
                    let declared = members kind known
                    let actual = members kind capability
                    for name,value in actual do
                        let expected = optional name declared |> Option.defaultWith(fun () -> mismatch 4 $"{kind}.{name}"; Value.Null)
                        match name with
                        | "tuples" ->
                            let allowed = array name expected
                            for item in array name value do
                                if not (allowed |> List.exists(fun candidate -> difference name candidate item |> Option.isNone)) then mismatch 4 $"{kind}.{name}"
                        | "guarantees" when kind = "modelTransport" ->
                            let allowed = array name expected
                            for item in array name value do
                                if not (allowed |> List.exists(fun candidate -> difference name candidate item |> Option.isNone)) then mismatch 4 $"{kind}.{name}"
                        | "limits" ->
                            let allowed = members name expected
                            for limit,bound in members name value do
                                let number = function
                                    | Value.Number text ->
                                        match Int64.TryParse(text, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                                        | true, parsed -> parsed
                                        | false, _ -> mismatch 4 $"{kind}.limits.{limit}"; 0L
                                    | _ -> mismatch 4 $"{kind}.limits.{limit}"; 0L
                                let actual = number bound
                                let maximum = required name limit allowed |> number
                                if actual <= 0L || actual > maximum then mismatch 4 $"{kind}.limits.{limit}"
                        | _ -> mismatch 4 $"{kind}.{name}"
                elif not (List.contains kind ["frontend";"backend";"workspace";"validator";"transform"]) then
                    if difference kind known capability |> Option.isSome then mismatch 4 kind
                else
                    let declared = members kind known
                    for name,value in members kind capability do
                        match optional name declared with
                        | Some expected when difference name expected value |> Option.isNone -> ()
                        | _ -> mismatch 4 $"capabilities.{kind}.{name}"
        )
