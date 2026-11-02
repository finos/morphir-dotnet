module Morphir.CodeGeneration.FSharp.Tests.ModelRefusalInputs

open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.Testing.GenerationConformance
open Morphir.CodeGeneration.FSharp.Tests.ModelConformanceInputs

let private intType = Type.Reference(TypeAttributes.empty, sdk "basics" "int", [])
let private literal = Value.Literal(ValueAttributes.empty, IntegerLiteral 42I)

let private library types body =
    let value: ValueDefinition = { InputTypes = []; Body = body }

    let module': ModuleDefinition = {
        Types = ordered types
        Values = ordered [ name "value", publicValue (documented value) ]
        Doc = None
    }

    Library(
        package,
        ordered [],
        {
            Modules = ordered [ modulePath "refusal", publicValue module' ]
        }
    )
    |> v4

let private body body = library [] body
let private expression value = body (ExpressionBody(intType, value))

let private v4Only id code reason materialize =
    id, code, [ "3", Unrepresentable reason; "4.0.0", Refused(materialize, code) ]

let cases = [
    "specs",
    "unsupported-distribution",
    [
        "3.1.0",
        Refused(
            (fun () -> {
                SourceVersion =
                    SourceFormatVersion.parse "3.1.0"
                    |> ok
                Distribution =
                    DistributionModel.Classic(
                        Morphir.IR.Classic.Distribution.Specs(
                            package,
                            Map.empty,
                            { Modules = Map.empty }
                        )
                    )
            }),
            "unsupported-distribution"
        )
        "4.0.0",
        Refused(
            (fun () ->
                Specs(package, ordered [], { Modules = ordered [] })
                |> v4
            ),
            "unsupported-distribution"
        )
    ]
    v4Only
        "application"
        "unsupported-distribution"
        "Classic has no Application distribution"
        (fun () ->
            Application(package, ordered [], { Modules = ordered [] }, [])
            |> v4
        )
    v4Only
        "document"
        "unsupported-document-literal"
        "Classic has no DocumentLiteral"
        (fun () ->
            expression (
                Value.Literal(
                    ValueAttributes.empty,
                    DocumentLiteral(
                        DocumentObject(
                            ordered [ "secret", DocumentString "retained, not executed" ]
                        )
                    )
                )
            )
        )
    v4Only
        "hole"
        "unsupported-hole"
        "Classic has no Hole expression"
        (fun () ->
            expression (
                Value.Hole(
                    ValueAttributes.empty,
                    UnresolvedReference(sdk "basics" "missing"),
                    Some intType
                )
            )
        )
    v4Only
        "incomplete-body"
        "unsupported-incomplete-body"
        "Classic has no IncompleteBody"
        (fun () -> body (IncompleteBody(Some intType, Incompleteness.Draft, Some literal)))
    v4Only
        "native-body"
        "unsupported-native-body"
        "Classic has no NativeBody"
        (fun () ->
            body (
                NativeBody(
                    intType,
                    {
                        Hint = Arithmetic
                        Description = Some "No invented implementation"
                    }
                )
            )
        )
    v4Only
        "external-body"
        "unsupported-external-body"
        "Classic has no ExternalBody"
        (fun () ->
            body (
                ExternalBody(
                    intType,
                    {
                        Externals = [
                            {
                                TargetPlatform = "dotnet"
                                ExternalName = "Unknown.Binding"
                            }
                        ]
                        Fallback = Some literal
                    }
                )
            )
        )
    v4Only
        "incomplete-type"
        "unsupported-incomplete-type"
        "Classic has no IncompleteTypeDefinition"
        (fun () ->
            library
                [
                    name "unfinished",
                    publicValue (
                        documented (
                            IncompleteTypeDefinition([], Incompleteness.Draft, Some intType)
                        )
                    )
                ]
                (ExpressionBody(intType, literal))
        )
    v4Only
        "constraints"
        "unsupported-constraints"
        "Classic attributes do not define v4 constraint semantics"
        (fun () ->
            let attributes =
                { TypeAttributes.empty with
                    Constraints = Some(DocumentObject(ordered [ "minimum", DocumentNumber "0" ]))
                }

            body (ExpressionBody(Type.Reference(attributes, sdk "basics" "int", []), literal))
        )
    "open-record",
    "unsupported-open-record",
    [
        for version in [ "3"; "4.0.0" ] ->
            version,
            Refused(
                (fun () ->
                    let json =
                        Morphir.CodeGeneration.FSharp.Tests.Fixtures.model [
                            "refusal",
                            [
                                Morphir.CodeGeneration.FSharp.Tests.Fixtures.typeAlias
                                    "open"
                                    [ "a" ]
                                    (Morphir.CodeGeneration.FSharp.Tests.Fixtures.a [
                                        box "ExtensibleRecord"
                                        null
                                        Morphir.CodeGeneration.FSharp.Tests.Fixtures.n "a"
                                        Morphir.CodeGeneration.FSharp.Tests.Fixtures.a []
                                    ])
                            ],
                            []
                        ]

                    classic json
                    |> forVersion version
                ),
                "unsupported-open-record"
            )
    ]
]

let opaqueMetadata () =
    let attributes =
        { ValueAttributes.empty with
            Extensions = Some(DocumentObject(ordered [ "note", DocumentString "opaque metadata" ]))
        }

    expression (Value.Literal(attributes, IntegerLiteral 42I))
