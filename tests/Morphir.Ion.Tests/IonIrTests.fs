module Morphir.Ion.Tests.IonIrTests

open System
open System.IO
open System.Numerics
open System.Threading
open Eleven19.IonDotnet.Builders
open Expecto
open Morphir.IR
open Morphir.IR.V4
open Morphir.Ion
open Morphir.Ion.Tests

let private readText (text: string) =
    IonIr.read IonReadLimits.defaults (Fixtures.utf8 text)

let private succeeds what result =
    match result with
    | Ok value -> value
    | Error(error: IonIrError) -> failtestf "%s: expected success, got %A" what error

let private failure result =
    match result with
    | Ok _ -> failtest "Expected an error, read a document."
    | Error(error: IonIrError) -> error

let private failsWith code text =
    let error = failure (readText text)
    Expect.equal error.Code code $"The code for {text}"
    error

let private ionVersion = """ionVersion: "0.1.0-draft.1", """

let private v3Header =
    "morphir::{"
    + ionVersion
    + """formatVersion: "3.0.0", kind: library, packageName: "p"}"""

let private v3SpecsHeader =
    "morphir::{"
    + ionVersion
    + """formatVersion: "3.1.0", kind: specs, packageName: "p"}"""

let private v4Header =
    "morphir::{"
    + ionVersion
    + """formatVersion: "4.0.0", kind: library, packageName: "p"}"""

let private v4SpecsHeader =
    "morphir::{"
    + ionVersion
    + """formatVersion: "4.0.0", kind: specs, packageName: "p"}"""

let private v4AppHeader =
    "morphir::{"
    + ionVersion
    + """formatVersion: "4.0.0", kind: application, packageName: "p"}"""

let private footer = "morphir_footer::{}"

let private datagram (elements: string list) = String.concat "\n" elements

let private intType = "\"morphir/SDK:basics#int\""

/// A v4 library datagram with one expression value holding the body.
let private v4WithBody body =
    datagram [
        v4Header
        $"public::def::module::{{name: \"m\", values: [public::def::value::{{name: \"v\", outputType: {intType}, body: {body}}}]}}"
        footer
    ]

let private v3WithBody body =
    datagram [
        v3Header
        $"public::def::module::{{name: \"m\", values: [public::def::value::{{name: \"v\", outputType: {intType}, body: {body}}}]}}"
        footer
    ]

let private v4Library text =
    match readText text with
    | Ok(V4Ir(_, Library(name, dependencies, definition))) -> name, dependencies, definition
    | other -> failtestf "Expected a v4 library, read %A" other

let private v4Module (ModuleName: string) text =
    let _, _, (definition: PackageDefinition) = v4Library text

    match
        CanonicalNames.parsePath ModuleName
        |> Result.map ModulePath
    with
    | Ok key ->
        match OrderedMap.tryFind key definition.Modules with
        | Some found -> found
        | None -> failtestf "No module %s" ModuleName
    | Error message -> failtest message

let private nameOf (text: string) =
    match CanonicalNames.parseName text with
    | Ok name -> name
    | Error message -> failtest message

let private fqNameOf (text: string) =
    match CanonicalNames.parseFQName text with
    | Ok name -> name
    | Error message -> failtest message

let private packageOf (text: string) =
    match CanonicalNames.parsePath text with
    | Ok path -> PackageName path
    | Error message -> failtest message

let private modulePathOf (text: string) =
    match CanonicalNames.parsePath text with
    | Ok path -> ModulePath path
    | Error message -> failtest message

/// Reads the single value of a v4 body.
let private v4Body body =
    let m = v4Module "m" (v4WithBody body)

    match OrderedMap.tryFind (nameOf "v") m.Value.Values with
    | Some definition ->
        match definition.Value.Value.Body with
        | ExpressionBody(_, value) -> value
        | other -> failtestf "Expected an expression body, read %A" other
    | None -> failtest "No value v"

/// Reads a single v4 alias type.
let private v4Type typeText =
    let m =
        v4Module
            "m"
            (datagram [
                v4Header
                $"public::def::module::{{name: \"m\", types: [public::def::alias::type::{{name: \"t\", typeExp: {typeText}}}]}}"
                footer
            ])

    match OrderedMap.tryFind (nameOf "t") m.Value.Types with
    | Some definition ->
        match definition.Value.Value with
        | TypeAliasDefinition(_, found) -> found
        | other -> failtestf "Expected an alias, read %A" other
    | None -> failtest "No type t"

let private noAttributes = ValueAttributes.empty
let private noTypeAttributes = TypeAttributes.empty

let private location: SourceLocation = {
    StartLine = 1
    StartColumn = 1
    EndLine = 1
    EndColumn = 2
}

let private sourceText =
    "{ source: { startLine: 1, startColumn: 1, endLine: 1, endColumn: 2 } }"

let private withSource: ValueAttributes =
    { noAttributes with
        Source = Some location
    }

let private binding name =
    AsPattern(noAttributes, WildcardPattern noAttributes, nameOf name)

/// Reads a classic library's only module.
let private v3Distribution text =
    match readText text with
    | Ok(ClassicIr(_, distribution)) -> distribution
    | other -> failtestf "Expected a classic document, read %A" other

let private layoutTests =
    testList "A reader accepts both single-file layouts" [
        testCase "Canonical empty library"
        <| fun () ->
            let text =
                "morphir::{\n  ionVersion: \"0.1.0-draft.1\",\n  formatVersion: \"3.0.0\",\n  kind: library,\n  packageName: \"example\",\n}\nmorphir_footer::{\n}\n"

            match v3Distribution text with
            | Morphir.IR.Classic.Distribution.Library(name, dependencies, definition) ->
                Expect.equal name (packageOf "example") "The package"
                Expect.isEmpty dependencies "No dependencies"
                Expect.isEmpty definition.Modules "No modules"
            | other -> failtestf "Expected a Library, read %A" other

        testCase "Collapsed record"
        <| fun () ->
            for name in [ "v3-library"; "library" ] do
                let record =
                    IonIr.read
                        IonReadLimits.defaults
                        (ReadOnlyMemory(Fixtures.bytes $"records/{name}.record.ion"))

                let datagram =
                    IonIr.read
                        IonReadLimits.defaults
                        (ReadOnlyMemory(Fixtures.bytes $"records/{name}.datagram.ion"))

                match succeeds "record" record, succeeds "datagram" datagram with
                | ClassicIr(_, a), ClassicIr(_, b) ->
                    Expect.equal
                        (ClassicComparison.comparable a)
                        (ClassicComparison.comparable b)
                        name
                | V4Ir(_, a), V4Ir(_, b) -> Expect.equal a b name
                | a, b -> failtestf "The layouts read to different families: %A, %A" a b

        testCase "Missing footer"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [ v3Header; "public::def::module::{name: \"m\"}" ])
            |> ignore

        testCase "Header not first"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [ "public::def::module::{name: \"m\"}"; v3Header; footer ])
            |> ignore

        testCase "An empty input is not a document"
        <| fun () ->
            failsWith "invalid_document" ""
            |> ignore

        testCase "A footer with members is not a footer"
        <| fun () ->
            failsWith "invalid_document" (datagram [ v3Header; "morphir_footer::{a: 1}" ])
            |> ignore

        testCase "A second header is refused"
        <| fun () ->
            failsWith "invalid_document" (datagram [ v3Header; v3Header; footer ])
            |> ignore

        testCase "A datagram header holds no modules or dependencies"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    "morphir::{formatVersion: \"3.0.0\", kind: library, packageName: \"p\", modules: [public::def::module::{name: \"m\"}]}"
                    footer
                ])
            |> ignore

        testCase "A record's dependencies hold package elements"
        <| fun () ->
            failsWith
                "invalid_document"
                "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\", dependencies: [public::def::module::{name: \"m\"}]}"
            |> ignore

        testCase "Member order and whitespace do not matter"
        <| fun () ->
            let compact = v4Library (v4WithBody "1")

            let spaced =
                v4Library
                    "\n\n  morphir::{ packageName : \"p\" ,\n kind:library,\n\tformatVersion:\"4.0.0\" }\n public::def::module::{ values : [ public::def::value::{ body: 1, outputType: \"morphir/SDK:basics#int\", name: \"v\" } ], name: \"m\" }  morphir_footer::{ }\n"

            Expect.equal spaced compact "One document"

        testCase "Binary Ion reads as its text does"
        <| fun () ->
            for name in [ "records/library.datagram.ion"; "records/library.record.ion" ] do
                let source = Fixtures.bytes name
                use output = new MemoryStream()
                use reader = IonReaderBuilder.Build(new MemoryStream(source))
                use writer = IonBinaryWriterBuilder.Build output
                writer.WriteValues reader
                writer.Finish()
                let binary = output.ToArray()
                Expect.equal binary[0] 0xE0uy "The writer produced binary Ion"

                let read =
                    IonIr.read IonReadLimits.defaults (ReadOnlyMemory binary)
                    |> succeeds "binary"

                let text =
                    IonIr.read IonReadLimits.defaults (ReadOnlyMemory source)
                    |> succeeds "text"

                Expect.equal read text name

        testCase "A top-level member joins its module"
        <| fun () ->
            let split =
                datagram [
                    v4Header
                    "public::def::module::{name: \"m\", types: [public::def::alias::type::{name: \"a\", typeExp: \"x\"}]}"
                    "public::def::alias::type::{module: \"m\", name: \"b\", typeExp: \"y\"}"
                    footer
                ]

            let m = v4Module "m" split

            Expect.equal
                (OrderedMap.keys m.Value.Types)
                [ nameOf "a"; nameOf "b" ]
                "Inline first, then top level"

        testCase "A top-level member names a module that has appeared"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    v3Header
                    "public::def::alias::type::{module: \"m\", name: \"b\", typeExp: \"y\"}"
                    "public::def::module::{name: \"m\"}"
                    footer
                ])
            |> ignore

        testCase "A top-level member states its module"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    v3Header
                    "public::def::module::{name: \"m\"}"
                    "public::def::alias::type::{name: \"b\", typeExp: \"y\"}"
                    footer
                ])
            |> ignore

        testCase "A definition belongs to the distribution package"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    v3Header
                    "public::def::module::{name: \"m\", package: \"other\"}"
                    footer
                ])
            |> ignore

        testCase "An inline member belongs to its module"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    v3Header
                    "public::def::module::{name: \"m\", types: [public::def::alias::type::{module: \"n\", name: \"a\", typeExp: \"x\"}]}"
                    footer
                ])
            |> ignore
    ]

let private versionTests =
    testList "Versions select the reader exactly" [
        testCase "Missing format version"
        <| fun () ->
            failsWith
                "missing_format_version"
                (datagram [ "morphir::{kind: library, packageName: \"p\"}"; footer ])
            |> ignore

        testCase "Other Ion draft"
        <| fun () ->
            failsWith
                "unsupported_ion_version"
                (datagram [
                    "morphir::{ionVersion: \"0.1.0-draft.2\", formatVersion: \"4.0.0\", kind: library, packageName: \"p\"}"
                    footer
                ])
            |> ignore

        testCase "Missing Ion version"
        <| fun () ->
            match
                readText (
                    datagram [
                        "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\"}"
                        footer
                    ]
                )
            with
            | Ok(V4Ir("4.0.0", _)) -> ()
            | other -> failtestf "Expected a v4 document, read %A" other

        testCase "Unsupported format version"
        <| fun () ->
            failsWith
                "unsupported_format_version_minor"
                (datagram [
                    "morphir::{formatVersion: \"4.1.0\", kind: library, packageName: \"p\"}"
                    footer
                ])
            |> ignore

        testList "Other format versions" [
            for version, code in
                [
                    "\"3.2.0\"", "unsupported_format_version_minor"
                    "\"3.0.1\"", "unsupported_format_version_minor"
                    "\"4.0.1\"", "unsupported_format_version_minor"
                    "\"5.0.0\"", "unsupported_format_version_major"
                    "\"2.0.0\"", "unsupported_format_version_major"
                    "\"4.0.0-beta\"", "invalid_format_version_syntax"
                    "\"4.0\"", "invalid_format_version_syntax"
                    "\"04.0.0\"", "invalid_format_version_syntax"
                    "4", "invalid_format_version_type"
                ] do
                testCase version
                <| fun () ->
                    failsWith
                        code
                        (datagram [
                            $"morphir::{{formatVersion: {version}, kind: library, packageName: \"p\"}}"
                            footer
                        ])
                    |> ignore
        ]

        testCase "A repeated format version"
        <| fun () ->
            failsWith
                "duplicate_format_version"
                (datagram [
                    "morphir::{formatVersion: \"4.0.0\", formatVersion: \"4.0.0\", kind: library, packageName: \"p\"}"
                    footer
                ])
            |> ignore

        testCase "An Ion version that is not a string"
        <| fun () ->
            failsWith
                "unsupported_ion_version"
                (datagram [
                    "morphir::{ionVersion: 1, formatVersion: \"4.0.0\", kind: library, packageName: \"p\"}"
                    footer
                ])
            |> ignore

        testCase "Binary Ion of another version"
        <| fun () ->
            let error =
                IonIr.read
                    IonReadLimits.defaults
                    (ReadOnlyMemory [| 0xE0uy; 0x01uy; 0x01uy; 0xEAuy |])
                |> failure

            Expect.equal error.Code "unsupported_ion_version" "The reader's code stays"

        testCase "A syntax error keeps the reader's code"
        <| fun () ->
            let error = failure (readText "morphir::{")
            Expect.equal error.Code "malformed_ion" "The reader's code stays"

        testCase "A limit keeps the reader's code"
        <| fun () ->
            let limits =
                { IonReadLimits.defaults with
                    MaxNodes = 1
                }

            let error =
                IonIr.read limits (Fixtures.utf8 (v4WithBody "1"))
                |> failure

            Expect.equal error.Code "node_limit_exceeded" "The reader's code stays"

        testCase "A header's kind is known"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    "morphir::{formatVersion: \"4.0.0\", kind: other, packageName: \"p\"}"
                    footer
                ])
            |> ignore

        testCase "A header needs a kind and a package"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [ "morphir::{formatVersion: \"4.0.0\", packageName: \"p\"}"; footer ])
            |> ignore

            failsWith
                "invalid_document"
                (datagram [ "morphir::{formatVersion: \"4.0.0\", kind: library}"; footer ])
            |> ignore

        testCase "A header's package is a canonical path"
        <| fun () ->
            failsWith
                "invalid_name"
                (datagram [
                    "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"not a name\"}"
                    footer
                ])
            |> ignore
    ]

let private criticalTests =
    testList "Unknown header members follow the critical rule" [
        testCase "Unknown optional member"
        <| fun () ->
            match
                readText (
                    datagram [
                        "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\", futureHint: [1, 2]}"
                        footer
                    ]
                )
            with
            | Ok(V4Ir _) -> ()
            | other -> failtestf "Expected a v4 document, read %A" other

        testCase "Unknown critical member"
        <| fun () ->
            let error =
                failsWith
                    "unknown_critical_member"
                    (datagram [
                        "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\", futureHint: 1, critical: [\"futureHint\"]}"
                        footer
                    ])

            Expect.stringContains error.Message "futureHint" "The error names the member"

        testCase "A critical name that no member answers to is accepted"
        <| fun () ->
            match
                readText (
                    datagram [
                        "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\", critical: [\"futureHint\"]}"
                        footer
                    ]
                )
            with
            | Ok(V4Ir _) -> ()
            | other -> failtestf "Expected a v4 document, read %A" other

        testCase "A critical member this reader understands"
        <| fun () ->
            match
                readText (
                    datagram [
                        "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\", critical: [\"kind\", \"packageName\"]}"
                        footer
                    ]
                )
            with
            | Ok(V4Ir _) -> ()
            | other -> failtestf "Expected a v4 document, read %A" other

        testCase "A critical list is a list"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\", critical: 1}"
                    footer
                ])
            |> ignore
    ]

let private kindTests =
    testList "The result is typed by IR version" [
        testCase "Distribution kinds"
        <| fun () ->
            match readText (datagram [ v3Header; footer ]) with
            | Ok(ClassicIr("3.0.0", Morphir.IR.Classic.Distribution.Library _)) -> ()
            | other -> failtestf "Expected a classic library, read %A" other

            match readText (datagram [ v3SpecsHeader; footer ]) with
            | Ok(ClassicIr("3.1.0", Morphir.IR.Classic.Distribution.Specs _)) -> ()
            | other -> failtestf "Expected classic specs, read %A" other

            match readText (datagram [ v4Header; footer ]) with
            | Ok(V4Ir("4.0.0", Library _)) -> ()
            | other -> failtestf "Expected a v4 library, read %A" other

            match readText (datagram [ v4SpecsHeader; footer ]) with
            | Ok(V4Ir("4.0.0", Specs _)) -> ()
            | other -> failtestf "Expected v4 specs, read %A" other

            let application =
                datagram [
                    "morphir::{formatVersion: \"4.0.0\", kind: application, packageName: \"p\", entryPoints: {zeta: {target: \"p:m#run\", kind: main, doc: \"First.\"}, alpha: {target: \"p:m#run\", kind: job}, 'in the middle': {target: \"p:m#run\", kind: policy}}}"
                    footer
                ]

            match readText application with
            | Ok(V4Ir(_, Application(_, _, _, entryPoints))) ->
                Expect.equal
                    entryPoints
                    [
                        "zeta",
                        {
                            Target = fqNameOf "p:m#run"
                            Kind = Main
                            Doc = Some "First."
                        }
                        "alpha",
                        {
                            Target = fqNameOf "p:m#run"
                            Kind = Job
                            Doc = None
                        }
                        "in the middle",
                        {
                            Target = fqNameOf "p:m#run"
                            Kind = Policy
                            Doc = None
                        }
                    ]
                    "Entry points in document order"
            | other -> failtestf "Expected an application, read %A" other

        testCase "Specs below 3.1"
        <| fun () ->
            failsWith
                "specs_before_3_1"
                (datagram [
                    "morphir::{formatVersion: \"3.0.0\", kind: specs, packageName: \"p\"}"
                    footer
                ])
            |> ignore

        testCase "A v3 library at 3.1.0 keeps its version"
        <| fun () ->
            match
                readText (
                    datagram [
                        "morphir::{formatVersion: \"3.1.0\", kind: library, packageName: \"p\"}"
                        footer
                    ]
                )
            with
            | Ok(ClassicIr("3.1.0", _)) -> ()
            | other -> failtestf "Expected a classic document, read %A" other

        testCase "A v3 document has no application kind"
        <| fun () ->
            failsWith
                "unsupported_node"
                (datagram [
                    "morphir::{formatVersion: \"3.1.0\", kind: application, packageName: \"p\"}"
                    footer
                ])
            |> ignore

        testCase "Only an application has entry points"
        <| fun () ->
            failsWith
                "unsupported_node"
                (datagram [
                    "morphir::{formatVersion: \"4.0.0\", kind: library, packageName: \"p\", entryPoints: {}}"
                    footer
                ])
            |> ignore

        testCase "An application may state no entry points"
        <| fun () ->
            match readText (datagram [ v4AppHeader; footer ]) with
            | Ok(V4Ir(_, Application(_, _, _, []))) -> ()
            | other -> failtestf "Expected an application, read %A" other

        testCase "An entry point's kind is one of five"
        <| fun () ->
            failsWith
                "invalid_type"
                (datagram [
                    "morphir::{formatVersion: \"4.0.0\", kind: application, packageName: \"p\", entryPoints: {a: {target: \"p:m#r\", kind: startup}}}"
                    footer
                ])
            |> ignore

        testCase "An entry point's name appears once"
        <| fun () ->
            failsWith
                "duplicate_name"
                (datagram [
                    "morphir::{formatVersion: \"4.0.0\", kind: application, packageName: \"p\", entryPoints: {a: {target: \"p:m#r\", kind: main}, a: {target: \"p:m#r\", kind: job}}}"
                    footer
                ])
            |> ignore
    ]

let private spellingTests =
    testList "Type and value spellings decode to their IR nodes" [
        testCase "Type shorthand"
        <| fun () ->
            Expect.equal
                (v4Type "\"a\"")
                (Type.Variable(noTypeAttributes, nameOf "a"))
                "A local name is a variable"

            Expect.equal
                (v4Type "\"morphir/SDK:basics#int\"")
                (Type.Reference(noTypeAttributes, fqNameOf "morphir/SDK:basics#int", []))
                "An FQName is a reference"

            failsWith
                "invalid_name"
                (datagram [
                    v4Header
                    "public::def::module::{name: \"m\", types: [public::def::alias::type::{name: \"t\", typeExp: \"not a name\"}]}"
                    footer
                ])
            |> ignore

        testCase "Value shorthand"
        <| fun () ->
            Expect.equal
                (v4Body "42")
                (Value.Literal(noAttributes, IntegerLiteral(BigInteger 42)))
                "An int"

            Expect.equal (v4Body "x") (Value.Variable(noAttributes, nameOf "x")) "A symbol"
            Expect.equal (v4Body "true") (Value.Literal(noAttributes, BoolLiteral true)) "A bool"
            Expect.equal (v4Body "()") (Value.Unit noAttributes) "Unit"

        testCase "Lambda shorthand"
        <| fun () ->
            Expect.equal
                (v4Body "(lambda [score] score)")
                (Value.Lambda(
                    noAttributes,
                    binding "score",
                    Value.Variable(noAttributes, nameOf "score")
                ))
                "An as pattern over a wildcard"

        testCase "Binary apply"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "(apply f x y)")
            |> ignore

        testCase "Attributes force the expanded form"
        <| fun () ->
            Expect.equal
                (v4Body
                    "(variable { source: { startLine: 1, startColumn: 1, endLine: 1, endColumn: 2 } } x)")
                (Value.Variable(withSource, nameOf "x"))
                "The variable keeps its location"

        testCase "A bare list in type position is a tuple"
        <| fun () ->
            Expect.equal
                (v4Type "[\"a\", \"b\"]")
                (Type.Tuple(
                    noTypeAttributes,
                    [
                        Type.Variable(noTypeAttributes, nameOf "a")
                        Type.Variable(noTypeAttributes, nameOf "b")
                    ]
                ))
                "A tuple"

        testCase "A symbol in type position is refused"
        <| fun () ->
            failsWith
                "invalid_type"
                (datagram [
                    v4Header
                    "public::def::module::{name: \"m\", types: [public::def::alias::type::{name: \"t\", typeExp: a}]}"
                    footer
                ])
            |> ignore

        testCase "Type attributes are members of the expanded struct"
        <| fun () ->
            let attributes: TypeAttributes =
                { noTypeAttributes with
                    Source = Some location
                    Constraints =
                        Some(
                            DocumentObject(
                                OrderedMap.ofList [ "min", DocumentNumber "0" ]
                                |> Result.defaultWith (fun _ -> failtest "map")
                            )
                        )
                }

            Expect.equal
                (v4Type
                    "variable::{ name: \"a\", source: { startLine: 1, startColumn: 1, endLine: 1, endColumn: 2 }, constraints: { min: 0 } }")
                (Type.Variable(attributes, nameOf "a"))
                "Expanded"

            Expect.equal
                (v4Type
                    "tuple::{ elements: [\"a\"], source: { startLine: 1, startColumn: 1, endLine: 1, endColumn: 2 } }")
                (Type.Tuple(
                    { noTypeAttributes with
                        Source = Some location
                    },
                    [ Type.Variable(noTypeAttributes, nameOf "a") ]
                ))
                "A tuple with attributes"

        testCase "Attributes hold only their members"
        <| fun () ->
            failsWith "unknown_member" (v4WithBody "(variable { colour: \"red\" } x)")
            |> ignore

            failsWith
                "unknown_member"
                (datagram [
                    v4Header
                    "public::def::module::{name: \"m\", types: [public::def::alias::type::{name: \"t\", typeExp: variable::{name: \"a\", colour: \"red\"}}]}"
                    footer
                ])
            |> ignore

        testCase "Constraints and extensions are structs"
        <| fun () ->
            failsWith "invalid_type" (v4WithBody "(variable { extensions: 5 } x)")
            |> ignore

        testCase "A value carries no Ion annotation"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "a::x")
            |> ignore

        testCase "An unknown head is refused"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "(native x)")
            |> ignore

        testCase "A bare string is not a value"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "\"x\"")
            |> ignore
    ]

/// The draft's letrec row, separately from the writer's list.
let private letrecDraftTest =
    testCase "A letrec reads the draft's S-expression and the writer's list"
    <| fun () ->
        let definition = $"{{ outputType: {intType}, body: f }}"

        Expect.equal
            (v4Body $"(letrec ((f {definition})) f)")
            (v4Body $"(letrec [(f {definition})] f)")
            "One value"

let private lambdaListTests =
    testList "Lambda parameter lists" [
        testCase "The draft's explicit form"
        <| fun () ->
            Expect.equal
                (v4Body "(lambda [(as (wildcard) score)] score)")
                (v4Body "(lambda [score] score)")
                "One value"

        testCase "A list of names is a tuple"
        <| fun () ->
            Expect.equal
                (v4Body "(lambda [a, b] a)")
                (Value.Lambda(
                    noAttributes,
                    TuplePattern(noAttributes, [ binding "a"; binding "b" ]),
                    Value.Variable(noAttributes, nameOf "a")
                ))
                "The reference CLI reads a tuple"

        testCase "An empty list is the empty-list pattern"
        <| fun () ->
            Expect.equal
                (v4Body "(lambda [] x)")
                (Value.Lambda(
                    noAttributes,
                    EmptyListPattern noAttributes,
                    Value.Variable(noAttributes, nameOf "x")
                ))
                "Pattern position"
    ]

let private literalTests =
    testList "Literals keep their exact form" [
        testCase "Large integer"
        <| fun () ->
            Expect.equal
                (v4Body "(int \"99999999999999999999999999\")")
                (Value.Literal(
                    noAttributes,
                    IntegerLiteral(BigInteger.Parse "99999999999999999999999999")
                ))
                "Every digit"

        testCase "Decimal scale"
        <| fun () ->
            match v4Body "(decimal \"10.50\")" with
            | Value.Literal(_, DecimalLiteral lexeme) ->
                Expect.equal (DecimalLexeme.value lexeme) "10.50" "The lexeme"
            | other -> failtestf "Expected a decimal, read %A" other

        testCase "Float lexeme"
        <| fun () ->
            match v4Body "(float \"4e+0\")" with
            | Value.Literal(_, FloatLiteral value) ->
                Expect.equal value.Value 4.0 "The value"
                Expect.equal value.Lexeme (Some "4e+0") "The lexeme"
            | other -> failtestf "Expected a float, read %A" other

        testCase "Document number"
        <| fun () ->
            Expect.equal
                (v4Body "(document number::\"1.5e+2\")")
                (Value.Literal(noAttributes, DocumentLiteral(DocumentNumber "1.5e+2")))
                "The lexeme"

        testList "Non-JSON Ion in a document" [
            for name, payload in
                [
                    "timestamp", "2026-01-15T"
                    "symbol", "abc"
                    "typed null", "null.string"
                    "float", "1.5e0"
                    "blob", "{{ aGk= }}"
                    "clob", "{{ \"hi\" }}"
                    "sexp", "[(a)]"
                    "annotation", "[a::1]"
                    "number that is not one", "number::\"abc\""
                    "number annotation on an int", "number::1"
                    "repeated member", "[{a: 1, a: 2}]"
                ] do
                testCase name
                <| fun () ->
                    failsWith "invalid_document_payload" (v4WithBody $"(document {payload})")
                    |> ignore
        ]

        testCase "v3 whole number out of range"
        <| fun () ->
            failsWith "literal_out_of_range" (v3WithBody "99999999999999999999")
            |> ignore

            failsWith "literal_out_of_range" (v3WithBody "(int 99999999999999999999)")
            |> ignore

        testCase "A v3 decimal that would be rounded is refused"
        <| fun () ->
            failsWith
                "literal_out_of_range"
                (v3WithBody "(decimal \"1.2345678901234567890123456789012345678\")")
            |> ignore

            failsWith "literal_out_of_range" (v3WithBody "(decimal \"1e100\")")
            |> ignore

        testCase "A v3 decimal that fits is kept with its scale"
        <| fun () ->
            match v3Distribution (v3WithBody "(decimal \"10.50\")") with
            | Morphir.IR.Classic.Distribution.Library(_, _, definition) ->
                let m =
                    definition.Modules
                    |> Map.toList
                    |> List.head
                    |> snd

                let v =
                    m.Value.Values
                    |> Map.toList
                    |> List.head
                    |> snd

                match Morphir.IR.Classic.Documented.value v.Value with
                | {
                      Body = Morphir.IR.Classic.Value.Literal(_,
                                                              Morphir.IR.Classic.DecimalLiteral number)
                  } ->
                    Expect.equal
                        (number.ToString(Globalization.CultureInfo.InvariantCulture))
                        "10.50"
                        "The scale"
                | other -> failtestf "Expected a decimal, read %A" other
            | other -> failtestf "Expected a library, read %A" other

        testCase "A v3 character is one UTF-16 unit"
        <| fun () ->
            failsWith "literal_out_of_range" (v3WithBody "(char \"\U0001F600\")")
            |> ignore

            failsWith "invalid_node" (v3WithBody "(char \"ab\")")
            |> ignore

        testCase "A v3 float that is not finite is refused"
        <| fun () ->
            failsWith "literal_out_of_range" (v3WithBody "nan")
            |> ignore

        testCase "A v4 float that is not finite is refused"
        <| fun () ->
            failsWith "literal_out_of_range" (v4WithBody "+inf")
            |> ignore

            failsWith "literal_out_of_range" (v4WithBody "(float \"1e999\")")
            |> ignore

        testCase "A v4 float's lexeme is a number"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "(float \"NaN\")")
            |> ignore

            failsWith "invalid_node" (v4WithBody "(float 1.5e0)")
            |> ignore

        testCase "A v4 character is one Unicode scalar"
        <| fun () ->
            Expect.equal
                (v4Body "(char \"\U0001F600\")")
                (Value.Literal(noAttributes, CharLiteral "\U0001F600"))
                "An astral scalar"

            failsWith "invalid_node" (v4WithBody "(char \"ab\")")
            |> ignore

        testCase "A v4 decimal's lexeme follows the grammar"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "(decimal \"ten\")")
            |> ignore

        testCase "An integer's digits are digits"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "(int \"1.5\")")
            |> ignore

        testCase "A document has attributes only when two arguments follow its head"
        <| fun () ->
            match v4Body $"(document {sourceText} {{ a: 1 }})" with
            | Value.Literal(attributes, DocumentLiteral _) ->
                Expect.equal attributes withSource "Attributes"
            | other -> failtestf "Expected a document, read %A" other

            match v4Body "(document { source: 1 })" with
            | Value.Literal(attributes, DocumentLiteral _) ->
                Expect.equal attributes noAttributes "The struct is the payload"
            | other -> failtestf "Expected a document, read %A" other

            failsWith "invalid_node" (v4WithBody "(document 1 2 3)")
            |> ignore
    ]

let private mergeTests =
    testList "Fragments merge by the draft's rules" [
        testCase "Dependency fragments"
        <| fun () ->
            let text =
                datagram [
                    v3Header
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"a\"}]}"
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"b\"}]}"
                    footer
                ]

            match v3Distribution text with
            | Morphir.IR.Classic.Distribution.Library(_, dependencies, _) ->
                Expect.equal (Map.count dependencies) 1 "One dependency"
                let modules = dependencies[packageOf "d"].Modules

                Expect.equal
                    (modules
                     |> Map.toList
                     |> List.map fst)
                    [ modulePathOf "a"; modulePathOf "b" ]
                    "Both modules"
            | other -> failtestf "Expected a library, read %A" other

            let v4Text =
                datagram [
                    v4Header
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"z\"}]}"
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"a\"}]}"
                    footer
                ]

            let _, dependencies, _ = v4Library v4Text

            match OrderedMap.tryFind (packageOf "d") dependencies with
            | Some package ->
                Expect.equal
                    (OrderedMap.keys package.Modules)
                    [ modulePathOf "z"; modulePathOf "a" ]
                    "Document order"
            | None -> failtest "No dependency d"

        testCase "A dependency's module fragments merge"
        <| fun () ->
            let text =
                datagram [
                    v4Header
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"m\", doc: \"Doc.\", types: [public::spec::opaque::type::{name: \"a\"}]}]}"
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"m\", types: [public::spec::opaque::type::{name: \"b\"}]}]}"
                    "module::spec::{package: \"d\", name: \"m\", types: [public::spec::opaque::type::{name: \"c\"}]}"
                    "public::spec::opaque::type::{package: \"d\", module: \"m\", name: \"e\"}"
                    footer
                ]

            let _, dependencies, _ = v4Library text

            match OrderedMap.tryFind (packageOf "d") dependencies with
            | Some package ->
                match OrderedMap.tryFind (modulePathOf "m") package.Modules with
                | Some found ->
                    Expect.equal
                        (OrderedMap.keys found.Types)
                        [ nameOf "a"; nameOf "b"; nameOf "c"; nameOf "e" ]
                        "Document order"

                    Expect.equal found.Doc (Some "Doc.") "The doc stays"
                | None -> failtest "No module m"
            | None -> failtest "No dependency d"

        testCase "Module fragments that state two docs are refused"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    v4Header
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"m\", doc: \"One.\"}, module::spec::{name: \"n\"}]}"
                    "module::spec::{package: \"d\", name: \"m\", doc: \"Two.\"}"
                    footer
                ])
            |> ignore

        testCase "A member stated by two fragments is refused"
        <| fun () ->
            failsWith
                "duplicate_name"
                (datagram [
                    v4Header
                    "package::spec::{name: \"d\", modules: [module::spec::{name: \"m\", types: [public::spec::opaque::type::{name: \"a\"}]}]}"
                    "module::spec::{package: \"d\", name: \"m\", types: [public::spec::opaque::type::{name: \"a\"}]}"
                    footer
                ])
            |> ignore

        testCase "Repeated own module"
        <| fun () ->
            for header in [ v3SpecsHeader; v4SpecsHeader ] do
                failsWith
                    "duplicate_name"
                    (datagram [
                        header
                        "module::spec::{name: \"m\"}"
                        "module::spec::{name: \"m\"}"
                        footer
                    ])
                |> ignore

        testCase "Repeated definition module"
        <| fun () ->
            for header in [ v3Header; v4Header ] do
                failsWith
                    "duplicate_name"
                    (datagram [
                        header
                        "public::def::module::{name: \"m\"}"
                        "private::def::module::{name: \"m\"}"
                        footer
                    ])
                |> ignore

        testCase "Repeated member after merging"
        <| fun () ->
            for header in [ v3Header; v4Header ] do
                failsWith
                    "duplicate_name"
                    (datagram [
                        header
                        $"public::def::module::{{name: \"m\", types: [public::def::alias::type::{{name: \"a\", typeExp: {intType}}}]}}"
                        $"public::def::alias::type::{{module: \"m\", name: \"a\", typeExp: {intType}}}"
                        footer
                    ])
                |> ignore

        testCase "Two spellings of one name are a repeat"
        <| fun () ->
            failsWith
                "duplicate_name"
                (datagram [
                    v4Header
                    $"public::def::module::{{name: \"m\", types: [public::def::alias::type::{{name: \"USD\", typeExp: {intType}}}, public::def::alias::type::{{name: \"u-s-d\", typeExp: {intType}}}]}}"
                    footer
                ])
            |> ignore

        testCase "An own module does not take top-level members"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    v4SpecsHeader
                    "module::spec::{name: \"m\"}"
                    "public::spec::opaque::type::{module: \"m\", name: \"a\"}"
                    footer
                ])
            |> ignore

        testCase "A top-level member specification states its module"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [
                    v4Header
                    "public::spec::opaque::type::{package: \"d\", name: \"a\"}"
                    footer
                ])
            |> ignore

        testCase "A package::def repeated module is refused"
        <| fun () ->
            failsWith
                "duplicate_name"
                (datagram [
                    v4AppHeader
                    "package::def::{name: \"d\", modules: [public::def::module::{name: \"m\"}]}"
                    "package::def::{name: \"d\", modules: [public::def::module::{name: \"m\"}]}"
                    footer
                ])
            |> ignore

        testCase "package::def fragments merge"
        <| fun () ->
            match
                readText (
                    datagram [
                        v4AppHeader
                        "package::def::{name: \"d\", modules: [public::def::module::{name: \"a\"}]}"
                        "package::def::{name: \"d\", modules: [public::def::module::{name: \"b\"}]}"
                        footer
                    ]
                )
            with
            | Ok(V4Ir(_, Application(_, dependencies, _, _))) ->
                match OrderedMap.tryFind (packageOf "d") dependencies with
                | Some package ->
                    Expect.equal
                        (OrderedMap.keys package.Modules)
                        [ modulePathOf "a"; modulePathOf "b" ]
                        "Both modules"
                | None -> failtest "No dependency d"
            | other -> failtestf "Expected an application, read %A" other
    ]

let private refusalTests =
    testList "Nodes outside the document's version or kind are refused" [
        testCase "v4 node in a v3 document"
        <| fun () ->
            let error =
                failsWith
                    "unsupported_node"
                    (datagram [
                        v3Header
                        $"public::def::native::value::{{name: \"add\", outputType: {intType}, hint: arithmetic}}"
                        footer
                    ])

            Expect.stringContains
                error.Message
                "public::def::native::value"
                "The error names the node"

            Expect.equal error.Path (Some "$[1]") "The error names the location"

        testCase "v4 nodes inside a v3 module"
        <| fun () ->
            for node in
                [
                    "public::def::external::value::{name: \"a\"}"
                    "public::def::incomplete::value::{name: \"a\"}"
                ] do
                let error =
                    failsWith
                        "unsupported_node"
                        (datagram [
                            v3Header
                            $"public::def::module::{{name: \"m\", values: [{node}]}}"
                            footer
                        ])

                Expect.equal error.Path (Some "$[1].values[0]") "The location"

            failsWith
                "unsupported_node"
                (datagram [
                    v3Header
                    "public::def::module::{name: \"m\", types: [public::def::incomplete::type::{name: \"a\"}]}"
                    footer
                ])
            |> ignore

            failsWith
                "unsupported_node"
                (datagram [ v3Header; "package::def::{name: \"d\"}"; footer ])
            |> ignore

        testCase "v4 heads in a v3 body"
        <| fun () ->
            failsWith
                "unsupported_node"
                (v3WithBody "(hole unresolvedReference::{target: \"a:b#c\"})")
            |> ignore

            failsWith "unsupported_node" (v3WithBody "(document 1)")
            |> ignore

        testCase "v4 attribute forms in a v3 type"
        <| fun () ->
            failsWith
                "unsupported_node"
                (datagram [
                    v3Header
                    "public::def::module::{name: \"m\", types: [public::def::alias::type::{name: \"t\", typeExp: tuple::{elements: []}}]}"
                    footer
                ])
            |> ignore

        testCase "A v3 record update is the JSON reader's UpdateRecord"
        <| fun () ->
            // The pinned CLI writes this node as Update, which morphir-elm and the classic reader refuse, so
            // no reference pair holds it. The JSON here is the canonical spelling from the kit's versions-0028.
            let unitType = """["Unit", {}]"""

            let json =
                $"""{{ "formatVersion": 3, "distribution": ["Library", [["p"]], [], {{ "modules": [[[["m"]], {{ "access": "Public", "value": {{ "types": [], "values": [[["v"], {{ "access": "Public", "value": {{ "doc": "", "value": {{ "inputTypes": [], "outputType": ["Reference", {{}}, [[["morphir"], ["s", "d", "k"]], [["basics"]], ["int"]], []], "body": ["UpdateRecord", {unitType}, ["Variable", {unitType}, ["r"]], [[["a"], ["Literal", {unitType}, ["WholeNumberLiteral", 1]]]]] }} }} }}]], "doc": null }} }}]] }}] }}"""

            use document = System.Text.Json.JsonDocument.Parse json

            let fromJson =
                match Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement with
                | Ok read -> read.Distribution
                | Error error -> failtestf "The JSON should read: %A" error

            Expect.equal
                (ClassicComparison.comparable (v3Distribution (v3WithBody "(update r (a 1))")))
                (ClassicComparison.comparable fromJson)
                "One value"

        testCase "A v3 definition takes a list of inputs"
        <| fun () ->
            failsWith
                "invalid_type"
                (datagram [
                    v3Header
                    $"public::def::module::{{name: \"m\", values: [public::def::value::{{name: \"v\", inputTypes: {{a: {intType}}}, outputType: {intType}, body: 1}}]}}"
                    footer
                ])
            |> ignore

        testCase "A v3 attribute struct holds a type"
        <| fun () ->
            failsWith "invalid_node" (v3WithBody "(list {} 1)")
            |> ignore

            failsWith
                "unknown_member"
                (v3WithBody $"(list {{inferredType: {intType}, source: 1}} 1)")
            |> ignore

        testCase "Definition in specs"
        <| fun () ->
            for header in [ v3SpecsHeader; v4SpecsHeader ] do
                failsWith
                    "definition_in_specs"
                    (datagram [ header; "public::def::module::{name: \"m\"}"; footer ])
                |> ignore

        testCase "Collapsed v3 specs record"
        <| fun () ->
            failsWith
                "unsupported_node"
                "morphir::{formatVersion: \"3.1.0\", kind: specs, packageName: \"p\", modules: [module::spec::{name: \"m\"}]}"
            |> ignore

            failsWith
                "unsupported_node"
                "morphir::{formatVersion: \"3.1.0\", kind: specs, packageName: \"p\"}"
            |> ignore

        testCase "A v4 specs record is read"
        <| fun () ->
            match
                readText
                    "morphir::{formatVersion: \"4.0.0\", kind: specs, packageName: \"p\", modules: [module::spec::{name: \"m\"}]}"
            with
            | Ok(V4Ir(_, Specs _)) -> ()
            | other -> failtestf "Expected v4 specs, read %A" other

        testCase "Header package as a dependency"
        <| fun () ->
            failsWith
                "invalid_document"
                (datagram [ v3Header; "package::spec::{name: \"p\"}"; footer ])
            |> ignore

        testCase "Document in a pattern"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "(match x [(document 1), 2])")
            |> ignore

        testCase "Annotations on a definition"
        <| fun () ->
            for element in
                [
                    "public::def::module::{name: \"m\", annotations: []}"
                    $"public::def::module::{{name: \"m\", types: [public::def::alias::type::{{name: \"t\", annotations: [], typeExp: {intType}}}]}}"
                    $"public::def::module::{{name: \"m\", values: [public::def::value::{{name: \"v\", annotations: [], outputType: {intType}, body: 1}}]}}"
                ] do
                failsWith "invalid_node" (datagram [ v4Header; element; footer ])
                |> ignore

        testCase "A package specification has no annotations"
        <| fun () ->
            failsWith
                "invalid_node"
                (datagram [
                    v4Header
                    "package::spec::{name: \"d\", annotations: [\"a:b#c\"]}"
                    footer
                ])
            |> ignore

        testCase "A library's dependencies are specifications"
        <| fun () ->
            failsWith
                "unsupported_node"
                (datagram [ v4Header; "package::def::{name: \"d\"}"; footer ])
            |> ignore

        testCase "An application's dependencies are definitions"
        <| fun () ->
            failsWith
                "unsupported_node"
                (datagram [ v4AppHeader; "package::spec::{name: \"d\"}"; footer ])
            |> ignore

        testCase "A library's own modules are definitions"
        <| fun () ->
            failsWith
                "unsupported_node"
                (datagram [ v4Header; "module::spec::{name: \"m\"}"; footer ])
            |> ignore

        testCase "An unknown top-level value is refused"
        <| fun () ->
            failsWith "invalid_document" (datagram [ v4Header; "something::{}"; footer ])
            |> ignore

        testCase "A node's unknown member is refused"
        <| fun () ->
            let error =
                failsWith
                    "unknown_member"
                    (datagram [ v4Header; "public::def::module::{name: \"m\", future: 1}"; footer ])

            Expect.equal error.Path (Some "$[1].future") "The location"

        testCase "A repeated member of a node is refused"
        <| fun () ->
            failsWith
                "invalid_node"
                (datagram [
                    v4Header
                    "public::def::module::{name: \"m\", doc: \"a\", doc: \"b\"}"
                    footer
                ])
            |> ignore

        testCase "A symbol with no text is refused"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "$0")
            |> ignore

        testCase "An error names where it is"
        <| fun () ->
            let error = failsWith "invalid_node" (v4WithBody "(tuple 1 (apply f x y))")
            Expect.equal error.Path (Some "$[1].values[0].body[2]") "The location"
    ]

let private ruleTests =
    testList "Rules the v4 model cannot enforce" [
        testCase "A literal pattern never holds a document"
        <| fun () ->
            failsWith "invalid_node" (v4WithBody "(match x [(document {a: 1}), 2])")
            |> ignore

        testCase "An external body's externals are not empty"
        <| fun () ->
            failsWith
                "invalid_node"
                (datagram [
                    v4Header
                    $"public::def::module::{{name: \"m\", values: [public::def::external::value::{{name: \"v\", outputType: {intType}, externals: []}}]}}"
                    footer
                ])
            |> ignore

        testCase "A target platform appears once"
        <| fun () ->
            failsWith
                "duplicate_name"
                (datagram [
                    v4Header
                    $"public::def::module::{{name: \"m\", values: [public::def::external::value::{{name: \"v\", outputType: {intType}, externals: [{{targetPlatform: \"js\", externalName: \"a\"}}, {{targetPlatform: \"js\", externalName: \"b\"}}]}}]}}"
                    footer
                ])
            |> ignore

        testList "List-shaped maps refuse a repeat" [
            for name, element in
                [
                    "parameters",
                    $"public::def::value::{{name: \"v\", inputTypes: {{a: {intType}, a: {intType}}}, outputType: {intType}, body: 1}}"
                    "two spellings of a parameter",
                    $"public::def::value::{{name: \"v\", inputTypes: {{USD: {intType}, 'u-s-d': {intType}}}, outputType: {intType}, body: 1}}"
                    "let parameters",
                    $"public::def::value::{{name: \"v\", outputType: {intType}, body: (let x {{inputTypes: {{a: {intType}, a: {intType}}}, outputType: {intType}, body: 1}} x)}}"
                    "record values",
                    $"public::def::value::{{name: \"v\", outputType: {intType}, body: (record (a 1) (a 2))}}"
                    "record updates",
                    $"public::def::value::{{name: \"v\", outputType: {intType}, body: (update r (a 1) (a 2))}}"
                    "letrec bindings",
                    $"public::def::value::{{name: \"v\", outputType: {intType}, body: (letrec [(f {{outputType: {intType}, body: 1}}), (f {{outputType: {intType}, body: 2}})] f)}}"
                ] do
                testCase name
                <| fun () ->
                    failsWith
                        "duplicate_name"
                        (datagram [
                            v4Header
                            $"public::def::module::{{name: \"m\", values: [{element}]}}"
                            footer
                        ])
                    |> ignore

            for name, typeText in
                [
                    "record fields",
                    "record::{fields: [{name: \"a\", type: \"x\"}, {name: \"a\", type: \"y\"}]}"
                    "extensible record fields",
                    "extensibleRecord::{variable: \"r\", fields: [{name: \"a\", type: \"x\"}, {name: \"a\", type: \"y\"}]}"
                ] do
                testCase name
                <| fun () ->
                    failsWith
                        "duplicate_name"
                        (datagram [
                            v4Header
                            $"public::def::module::{{name: \"m\", types: [public::def::alias::type::{{name: \"t\", typeExp: {typeText}}}]}}"
                            footer
                        ])
                    |> ignore

            testCase "constructor args"
            <| fun () ->
                failsWith
                    "duplicate_name"
                    (datagram [
                        v4Header
                        "public::def::module::{name: \"m\", types: [public::def::custom::type::{name: \"t\", access: public, constructors: [{name: \"c\", args: [{name: \"a\", type: \"x\"}, {name: \"a\", type: \"y\"}]}]}]}"
                        footer
                    ])
                |> ignore

            testCase "constructors"
            <| fun () ->
                failsWith
                    "duplicate_name"
                    (datagram [
                        v4Header
                        "public::def::module::{name: \"m\", types: [public::def::custom::type::{name: \"t\", access: public, constructors: [{name: \"c\"}, {name: \"c\"}]}]}"
                        footer
                    ])
                |> ignore
        ]

        testCase "Only the incomplete body may lack an output type"
        <| fun () ->
            for element in
                [
                    "public::def::value::{name: \"v\", body: 1}"
                    "public::def::native::value::{name: \"v\", hint: arithmetic}"
                    "public::def::external::value::{name: \"v\", externals: [{targetPlatform: \"js\", externalName: \"a\"}]}"
                ] do
                failsWith
                    "invalid_node"
                    (datagram [
                        v4Header
                        $"public::def::module::{{name: \"m\", values: [{element}]}}"
                        footer
                    ])
                |> ignore

            failsWith "invalid_node" (v4WithBody "(let x {body: 1} x)")
            |> ignore

            let m =
                v4Module
                    "m"
                    (datagram [
                        v4Header
                        "public::def::module::{name: \"m\", values: [public::def::incomplete::value::{name: \"v\", incompleteness: draft}]}"
                        footer
                    ])

            match OrderedMap.tryFind (nameOf "v") m.Value.Values with
            | Some found ->
                Expect.equal
                    found.Value.Value.Body
                    (IncompleteBody(None, Incompleteness.Draft, None))
                    "No output type"
            | None -> failtest "No value v"
    ]

/// A value nested so that the deepest value is at the given depth, where the root value is depth one.
let private nestedBody depth =
    // The module is depth 1, its values list 2, the value 3, and the body 4.
    let levels = depth - 4

    String.replicate levels "(list "
    + "1"
    + String.replicate levels ")"

let private depthTests =
    testList "Deep input" [
        testCase "A value nested to exactly the default depth limit is read"
        <| fun () ->
            let limit = IonReadLimits.defaults.MaxDepth
            let mutable value = v4Body (nestedBody limit)
            let mutable levels = 0
            let mutable walking = true

            while walking do
                match value with
                | Value.List(_, [ inner ]) ->
                    levels <- levels + 1
                    value <- inner
                | Value.Literal _ -> walking <- false
                | other -> failtestf "Unexpected node %A" other

            Expect.equal levels (limit - 4) "Every level is kept"

        testCase "One level more gives depth_limit_exceeded"
        <| fun () ->
            failsWith
                "depth_limit_exceeded"
                (v4WithBody (
                    nestedBody (
                        IonReadLimits.defaults.MaxDepth
                        + 1
                    )
                ))
            |> ignore

        testCase "A v3 value and a type nested to the limit are read"
        <| fun () ->
            let limit = IonReadLimits.defaults.MaxDepth

            match readText (v3WithBody (nestedBody limit)) with
            | Ok(ClassicIr _) -> ()
            | other -> failtestf "Expected a classic document, read %A" other

            // The module is depth 1, its types list 2, the alias 3, and the tuple 4.
            let levels = limit - 4

            let deepType =
                String.replicate levels "["
                + "\"a\""
                + String.replicate levels "]"

            let text header =
                datagram [
                    header
                    $"public::def::module::{{name: \"m\", types: [public::def::alias::type::{{name: \"t\", typeExp: {deepType}}}]}}"
                    footer
                ]

            match readText (text v4Header), readText (text v3Header) with
            | Ok(V4Ir _), Ok(ClassicIr _) -> ()
            | other -> failtestf "Expected both to read, read %A" other

        testCase "A v3 attribute nested to the limit is read"
        <| fun () ->
            // The module 1, values 2, value 3, body 4, attribute struct 5, and the tuple type at 6.
            let levels =
                IonReadLimits.defaults.MaxDepth
                - 6

            let deepType =
                String.replicate levels "["
                + "\"a\""
                + String.replicate levels "]"

            match readText (v3WithBody $"(variable {{inferredType: {deepType}}} x)") with
            | Ok(ClassicIr _) -> ()
            | other -> failtestf "Expected a classic document, read %A" other

        testCase
            "The Ion reader gives an error for input too deep for the stack, on the caller's own thread"
        <| fun () ->
            let limits =
                { IonReadLimits.defaults with
                    MaxDepth = 300_000
                }

            match IonReader.read limits (Fixtures.utf8 (v4WithBody (nestedBody 200_000))) with
            | Error(IonReadError.DepthLimitExceeded _) -> ()
            | other -> failtestf "Expected a depth error, read %s" (other.GetType().Name)

        testCase "A value that may not fit any stack gives a result rather than a crash"
        <| fun () ->
            let limits =
                { IonReadLimits.defaults with
                    MaxDepth = 300_000
                }

            // No helper thread: IonIr.read must find its own room, and give up cleanly when there is none.
            // Whether 200,000 levels fit the larger stack depends on the build's frame sizes, so either is fine.
            match IonIr.read limits (Fixtures.utf8 (v4WithBody (nestedBody 200_000))) with
            | Error error -> Expect.equal error.Code "depth_limit_exceeded" "The code"
            | Ok _ -> ()

        testCase "A value past the raised limit and past any stack gives depth_limit_exceeded"
        <| fun () ->
            let limits =
                { IonReadLimits.defaults with
                    MaxDepth = 300_000
                }

            // Where the larger stack runs out first, the stack is the reason. Where it does not, the limit is.
            match IonIr.read limits (Fixtures.utf8 (v4WithBody (nestedBody 300_100))) with
            | Error error -> Expect.equal error.Code "depth_limit_exceeded" "The code"
            | Ok _ -> failtest "Expected depth_limit_exceeded, read a document."

        testCase "A depth within a raised limit reads on a 256 KB thread"
        <| fun () ->
            let limits =
                { IonReadLimits.defaults with
                    MaxDepth = 4_000
                }

            let result =
                ref (
                    Error
                        {
                            Code = "not_run"
                            Message = ""
                            Path = None
                            Location = None
                        }
                )

            let text = v4WithBody (nestedBody 2_000)

            let thread =
                Thread(
                    (fun () -> result.Value <- IonIr.read limits (Fixtures.utf8 text)),
                    256 * 1024
                )

            thread.Start()
            thread.Join()

            succeeds "the deep value" result.Value
            |> ignore

        testCase "A small stack still reads the default depth limit"
        <| fun () ->
            let result =
                ref (
                    Error
                        {
                            Code = "not_run"
                            Message = ""
                            Path = None
                            Location = None
                        }
                )

            let text = v4WithBody (nestedBody IonReadLimits.defaults.MaxDepth)

            let thread =
                Thread(
                    (fun () -> result.Value <- readText text),
                    1024
                    * 1024
                )

            thread.Start()
            thread.Join()

            succeeds "the deep value" result.Value
            |> ignore
    ]
    |> testLabel "PlatformSpecific"

let private unexpectedFailureTests =
    testList "An unexpected exception becomes an error" [
        testCase "A null node gives ion_ir_decode_failed, not an exception"
        <| fun () ->
            match IonIr.decode [ Unchecked.defaultof<IonValue> ] with
            | Error error -> Expect.equal error.Code "ion_ir_decode_failed" error.Message
            | Ok _ -> failtest "Expected an error."
    ]

[<Tests>]
let tests =
    testList "Ion IR reading" [
        unexpectedFailureTests
        layoutTests
        versionTests
        criticalTests
        kindTests
        spellingTests
        letrecDraftTest
        lambdaListTests
        literalTests
        mergeTests
        refusalTests
        ruleTests
        depthTests
    ]
