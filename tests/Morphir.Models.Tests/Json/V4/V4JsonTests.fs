module Morphir.Json.Tests.V4.V4JsonTests

open System.IO
open System.Numerics
open System.Text
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4
open Morphir.Json.Tests.V4.KitCases

let private readWith read (json: string) =
    use document = JsonDocument.Parse json
    read document.RootElement

let private readDocument = readWith V4Json.readDistribution
let private readValue = readWith V4Json.readValue
let private readType = readWith V4Json.readType
let private readPattern = readWith V4Json.readPattern

let private ok result =
    match result with
    | Ok value -> value
    | Error(error: V4JsonError) -> failtestf "Expected a read, got %A" error

let private rule result =
    match result with
    | Ok value -> failtestf "Expected a refusal, read %A" value
    | Error(error: V4JsonError) -> error.Rule

let private location result =
    match result with
    | Ok value -> failtestf "Expected a refusal, read %A" value
    | Error(error: V4JsonError) -> error.Location

let private fixture name =
    File.ReadAllText(Path.Combine(fixtureRoot, "v4", name))

let private name word = Name [ word ]
let private names (words: string list) = Name words

let private fq package moduleName local = {
    PackagePath = PackageName(Morphir.IR.Path package)
    ModulePath = ModulePath(Morphir.IR.Path moduleName)
    LocalName = local
}

let private addName =
    fq [ name "morphir"; names [ "s"; "d"; "k" ] ] [ name "basics" ] (name "add")

let private intName =
    fq [ name "morphir"; names [ "s"; "d"; "k" ] ] [ name "basics" ] (name "int")

let private intType = Type.Reference(TypeAttributes.empty, intName, [])

let private libraryOf (json: string) =
    match (ok (readDocument json)).Distribution with
    | Library(package, dependencies, definition) -> package, dependencies, definition
    | other -> failtestf "Expected a Library, got %A" other

let private moduleJson body =
    $"""{{ "formatVersion": 4, "distribution": {{ "Library": {{ "packageName": "t", "def": {{ "modules": {{ "m": {{ "Public": {body} }} }} }} }} }} }}"""

let private onlyModule json =
    let _, _, definition = libraryOf json

    match OrderedMap.toList definition.Modules with
    | [ _, entry ] -> entry.Value
    | other -> failtestf "Expected one module, got %A" other

let private variable word =
    Value.Variable(ValueAttributes.empty, name word)

let private typeVariable word =
    Type.Variable(TypeAttributes.empty, name word)

let private abc = fq [ name "a" ] [ name "b" ] (name "c")
let private noValueAttributes = ValueAttributes.empty
let private noTypeAttributes = TypeAttributes.empty

let private bound word =
    AsPattern(noValueAttributes, WildcardPattern noValueAttributes, name word)

let private boundJson word =
    $"""{{ "AsPattern": {{ "pattern": {{ "WildcardPattern": {{}} }}, "name": "{word}" }} }}"""

/// A definition whose every part differs, spelled as definitionJson.
let private definition: Documented<ValueDefinition> = {
    Doc = None
    Value = {
        InputTypes = [ name "input", typeVariable "intype" ]
        Body = ExpressionBody(typeVariable "outtype", variable "body")
    }
}

let private definitionJson =
    """{ "ExpressionBody": { "inputTypes": { "input": "intype" }, "outputType": "outtype", "body": "body" } }"""

/// Expected model values stated by hand, with a different word in every slot, so a swapped member fails.
/// The kit compares spellings with each other, so it cannot catch that.
let private valueFields = [
    "IfThenElse",
    """{ "IfThenElse": { "condition": "c", "then": "t", "else": "e" } }""",
    Value.IfThenElse(noValueAttributes, variable "c", variable "t", variable "e")
    "IfThenElse, legacy branches",
    """{ "IfThenElse": { "condition": "c", "thenBranch": "t", "elseBranch": "e" } }""",
    Value.IfThenElse(noValueAttributes, variable "c", variable "t", variable "e")
    "Apply",
    """{ "Apply": { "function": "f", "argument": "x" } }""",
    Value.Apply(noValueAttributes, variable "f", variable "x")
    "Field",
    """{ "Field": { "target": "r", "name": "n" } }""",
    Value.Field(noValueAttributes, variable "r", name "n")
    "Field, legacy members",
    """{ "Field": { "subject": "r", "fieldName": "n" } }""",
    Value.Field(noValueAttributes, variable "r", name "n")
    "Lambda",
    $"""{{ "Lambda": {{ "pattern": {boundJson "p"}, "body": "b" }} }}""",
    Value.Lambda(noValueAttributes, bound "p", variable "b")
    "LetDefinition",
    $"""{{ "LetDefinition": {{ "name": "n", "definition": {definitionJson}, "in": "i" }} }}""",
    Value.LetDefinition(noValueAttributes, name "n", definition, variable "i")
    "LetDefinition, legacy members",
    $"""{{ "LetDefinition": {{ "valueName": "n", "valueDefinition": {definitionJson}, "inValue": "i" }} }}""",
    Value.LetDefinition(noValueAttributes, name "n", definition, variable "i")
    "Destructure",
    $"""{{ "Destructure": {{ "pattern": {boundJson "p"}, "value": "v", "in": "i" }} }}""",
    Value.Destructure(noValueAttributes, bound "p", variable "v", variable "i")
    "PatternMatch",
    $"""{{ "PatternMatch": {{ "value": "v", "cases": [{{ "pattern": {boundJson "p"}, "body": "b" }}, {{ "pattern": {boundJson "q"}, "body": "d" }}] }} }}""",
    Value.PatternMatch(
        noValueAttributes,
        variable "v",
        [ bound "p", variable "b"; bound "q", variable "d" ]
    )
    "UpdateRecord",
    """{ "UpdateRecord": { "target": "r", "fields": { "n": "x", "m": "y" } } }""",
    Value.UpdateRecord(
        noValueAttributes,
        variable "r",
        [ name "n", variable "x"; name "m", variable "y" ]
    )
    "Tuple",
    """{ "Tuple": ["x", "y"] }""",
    Value.Tuple(noValueAttributes, [ variable "x"; variable "y" ])
    "Hole",
    """{ "Hole": { "reason": { "TypeMismatch": { "expected": "E", "found": "F" } }, "expectedType": "t" } }""",
    Value.Hole(noValueAttributes, TypeMismatch("E", "F"), Some(typeVariable "t"))
]

let private typeFields = [
    "Function",
    """{ "Function": { "parameterType": "p", "returnType": "r" } }""",
    Type.Function(noTypeAttributes, typeVariable "p", typeVariable "r")
    "Function, argumentType",
    """{ "Function": { "argumentType": "p", "returnType": "r" } }""",
    Type.Function(noTypeAttributes, typeVariable "p", typeVariable "r")
    "Function, arg and result",
    """{ "Function": { "arg": "p", "result": "r" } }""",
    Type.Function(noTypeAttributes, typeVariable "p", typeVariable "r")
    "Reference with arguments",
    """{ "Reference": ["a:b#c", "x", "y"] }""",
    Type.Reference(noTypeAttributes, abc, [ typeVariable "x"; typeVariable "y" ])
    "Reference, expanded",
    """{ "Reference": { "fqname": "a:b#c", "args": ["x", "y"] } }""",
    Type.Reference(noTypeAttributes, abc, [ typeVariable "x"; typeVariable "y" ])
    "ExtensibleRecord",
    """{ "ExtensibleRecord": { "variable": "v", "fields": { "n": "x" } } }""",
    Type.ExtensibleRecord(noTypeAttributes, name "v", [ name "n", typeVariable "x" ])
]

let private patternFields = [
    "HeadTailPattern",
    $"""{{ "HeadTailPattern": {{ "head": {boundJson "h"}, "tail": {boundJson "t"} }} }}""",
    HeadTailPattern(noValueAttributes, bound "h", bound "t")
    "AsPattern",
    """{ "AsPattern": { "pattern": { "UnitPattern": {} }, "name": "n" } }""",
    AsPattern(noValueAttributes, UnitPattern noValueAttributes, name "n")
    "ConstructorPattern",
    $"""{{ "ConstructorPattern": {{ "fqname": "a:b#c", "patterns": [{boundJson "x"}, {boundJson "y"}] }} }}""",
    ConstructorPattern(noValueAttributes, abc, [ bound "x"; bound "y" ])
    "TuplePattern",
    $"""{{ "TuplePattern": [{boundJson "x"}, {boundJson "y"}] }}""",
    TuplePattern(noValueAttributes, [ bound "x"; bound "y" ])
]

/// Reads arrays nested to the given depth, with a parser limit that lets all of them through.
let private deeplyNested depth =
    use document =
        JsonDocument.Parse(
            StringBuilder().Insert(0, "[", depth).Append(']', depth).ToString(),
            JsonDocumentOptions(MaxDepth = depth + 1)
        )

    V4Json.readValue document.RootElement

[<Tests>]
let tests =
    testList "IR v4 JSON reader" [
        testCase "Reference examples"
        <| fun () ->
            let package, dependencies, definition = libraryOf (fixture "complete-example.json")
            Expect.equal package (PackageName(Morphir.IR.Path [ name "regulation" ])) "The package"
            Expect.equal (OrderedMap.count dependencies) 1 "The SDK dependency"

            let modulePath =
                ModulePath(
                    Morphir.IR.Path [
                        names [ "u"; "s" ]
                        names [ "f"; "r"; "2052"; "a" ]
                        names [ "data"; "tables" ]
                    ]
                )

            let dataTables =
                match OrderedMap.tryFind modulePath definition.Modules with
                | Some entry -> entry
                | None -> failtest "The data tables module is listed"

            Expect.equal dataTables.Access Public "The module is public"

            Expect.equal
                dataTables.Value.Doc
                (Some "Data tables module for regulatory reporting")
                "Its doc"

            Expect.equal (OrderedMap.count dataTables.Value.Types) 2 "Its types"

            match OrderedMap.tryFind (names [ "calculate"; "total" ]) dataTables.Value.Values with
            | Some {
                       Access = Public
                       Value = {
                                   Doc = None
                                   Value = {
                                               InputTypes = [ parameter, _ ]
                                               Body = ExpressionBody(_,
                                                                     Value.Literal(_,
                                                                                   FloatLiteral body))
                                           }
                               }
                   } ->
                Expect.equal parameter (name "tables") "The parameter"
                Expect.equal body.Lexeme (Some "0.0") "The float's lexeme"
            | other -> failtestf "Unexpected calculate-total: %A" other

            let _, books, booksDefinition = libraryOf (fixture "books-and-records-example.json")
            Expect.equal (OrderedMap.count books) 1 "The SDK dependency"

            let booksModule =
                match OrderedMap.toList booksDefinition.Modules with
                | [ _, entry ] -> entry.Value
                | other -> failtestf "Expected one module, got %A" other

            Expect.equal
                (OrderedMap.keys booksModule.Values)
                [ names [ "open"; "deal" ]; names [ "close"; "deal" ] ]
                "Values stay in document order"

            Expect.isSome
                (OrderedMap.tryFind (names [ "product"; "i"; "d" ]) booksModule.Types)
                "product-ID holds its initialism as single-letter words"

            match OrderedMap.tryFind (names [ "deal"; "event" ]) booksModule.Types with
            | Some {
                       Value = {
                                   Value = CustomTypeDefinition(_, Public, constructors)
                               }
                   } -> Expect.equal (OrderedMap.count constructors) 6 "The constructors"
            | other -> failtestf "Unexpected deal-event: %A" other

        testCase "Incomplete nodes"
        <| fun () ->
            let module' =
                onlyModule (
                    moduleJson
                        """{
                            "types": { "pending": { "Public": { "IncompleteTypeDefinition": { "typeParams": [],
                                "incompleteness": { "Hole": { "reason": { "UnresolvedReference": { "target": "a:b#c" } } } } } } } },
                            "values": {
                              "unfinished": { "Public": { "IncompleteBody": { "inputTypes": {}, "incompleteness": { "Draft": {} } } } },
                              "with-hole": { "Public": { "ExpressionBody": { "inputTypes": {}, "outputType": "a:b#c",
                                "body": { "Hole": { "reason": { "TypeMismatch": { "expected": "Int", "found": "String" } } } } } } }
                            } }"""
                )

            let target = fq [ name "a" ] [ name "b" ] (name "c")

            match OrderedMap.tryFind (name "pending") module'.Types with
            | Some {
                       Value = {
                                   Value = IncompleteTypeDefinition([],
                                                                    Incompleteness.Hole(UnresolvedReference found,
                                                                                        None),
                                                                    None)
                               }
                   } -> Expect.equal found target "The incomplete type's reason"
            | other -> failtestf "Unexpected incomplete type: %A" other

            match OrderedMap.tryFind (name "unfinished") module'.Values with
            | Some {
                       Value = {
                                   Value = {
                                               Body = IncompleteBody(None,
                                                                     Incompleteness.Draft,
                                                                     None)
                                           }
                               }
                   } -> ()
            | other -> failtestf "Unexpected incomplete value: %A" other

            match OrderedMap.tryFind (names [ "with"; "hole" ]) module'.Values with
            | Some {
                       Value = {
                                   Value = {
                                               Body = ExpressionBody(_,
                                                                     Value.Hole(_,
                                                                                TypeMismatch("Int",
                                                                                             "String"),
                                                                                None))
                                           }
                               }
                   } -> ()
            | other -> failtestf "Unexpected hole: %A" other

            Expect.equal
                (rule (
                    readDocument (
                        moduleJson
                            """{ "values": { "v": { "Public": { "ExpressionBody": { "inputTypes": {}, "body": { "Unit": {} } } } } } }"""
                    )
                ))
                "missing_member"
                "An expression body must have an output type"

        testCase "Attribute payloads"
        <| fun () ->
            let read =
                ok (
                    readValue
                        """{ "Variable": { "attributes": {
                                "source": { "startLine": 3, "startColumn": 5, "endLine": 3, "endColumn": 6 },
                                "inferredType": "morphir/SDK:basics#int",
                                "extensions": { "custom": { "ratio": 0.10, "tags": ["x", null, true] } } },
                               "name": "x" } }"""
                )

            match read with
            | Value.Variable(attributes, variable) ->
                Expect.equal variable (name "x") "The name"

                Expect.equal
                    attributes.Source
                    (Some
                        {
                            StartLine = 3
                            StartColumn = 5
                            EndLine = 3
                            EndColumn = 6
                        })
                    "The source is a typed location"

                Expect.equal attributes.InferredType (Some intType) "The inferred type is a v4 type"

                let expected =
                    let inner =
                        OrderedMap.ofList [
                            "ratio", DocumentNumber "0.10"
                            "tags",
                            DocumentArray [ DocumentString "x"; DocumentNull; DocumentBool true ]
                        ]
                        |> function
                            | Ok map -> map
                            | Error key -> failtestf "Duplicate %s" key

                    match OrderedMap.ofList [ "custom", DocumentObject inner ] with
                    | Ok map -> DocumentObject map
                    | Error key -> failtestf "Duplicate %s" key

                Expect.equal
                    attributes.Extensions
                    (Some expected)
                    "The extensions are a document value"
            | other -> failtestf "Unexpected value: %A" other

        testCase "Invalid decimal"
        <| fun () ->
            let read = readValue """{ "Literal": { "DecimalLiteral": "NaN" } }"""
            Expect.equal (rule read) "invalid_literal" "NaN is not a decimal lexeme"
            Expect.stringContains (location read) "DecimalLiteral" "The error locates the payload"

        testCase "Decimal identity through the reader"
        <| fun () ->
            Expect.notEqual
                (ok (readValue """{ "Literal": { "DecimalLiteral": "10.50" } }"""))
                (ok (readValue """{ "Literal": { "DecimalLiteral": "10.5" } }"""))
                "Each lexeme is its own literal"

        testCase "Float meaning through the reader"
        <| fun () ->
            let left = ok (readValue """{ "Literal": { "FloatLiteral": 4.0 } }""")
            let right = ok (readValue """{ "Literal": { "FloatLiteral": 4e+0 } }""")
            Expect.equal left right "One meaning"

            let lexeme value =
                match value with
                | Value.Literal(_, FloatLiteral float) -> float.Lexeme
                | other -> failtestf "Expected a float, got %A" other

            Expect.equal (lexeme left) (Some "4.0") "The left lexeme stays"
            Expect.equal (lexeme right) (Some "4e+0") "The right lexeme stays"

        testCase "A float outside the finite range is refused"
        <| fun () ->
            Expect.equal
                (rule (readValue """{ "Literal": { "FloatLiteral": 1e999 } }"""))
                "invalid_literal"
                "Too large"

            Expect.equal (rule (readValue "1e999")) "invalid_literal" "Too large as a shorthand"

        testCase "Integer literals hold any size"
        <| fun () ->
            let big = "18446744073709551616123456789"

            Expect.equal
                (ok (readValue big))
                (Value.Literal(ValueAttributes.empty, IntegerLiteral(BigInteger.Parse big)))
                "No precision is lost"

            Expect.equal
                (ok (readValue "-9223372036854775809"))
                (Value.Literal(
                    ValueAttributes.empty,
                    IntegerLiteral(BigInteger.Parse "-9223372036854775809")
                ))
                "Negative too"

            Expect.equal
                (rule (readValue """{ "Literal": { "IntegerLiteral": 1.0 } }"""))
                "invalid_literal"
                "An integer's lexeme has no point"

        testCase "A document literal keeps number lexemes and has no value spelling"
        <| fun () ->
            match
                ok (
                    readValue
                        """{ "Literal": { "DocumentLiteral": { "value": 9007199254740993, "ratio": 0.10 } } }"""
                )
            with
            | Value.Literal(_, DocumentLiteral(DocumentObject members)) ->
                Expect.equal
                    (OrderedMap.toList members)
                    [ "value", DocumentNumber "9007199254740993"; "ratio", DocumentNumber "0.10" ]
                    "The payload is the document"
            | other -> failtestf "Unexpected document: %A" other

        testList "A scalar literal's expanded form without a value is invalid" [
            for tag in
                [
                    "BoolLiteral"
                    "CharLiteral"
                    "StringLiteral"
                    "IntegerLiteral"
                    "WholeNumberLiteral"
                    "FloatLiteral"
                    "DecimalLiteral"
                ] do
                testCase tag
                <| fun () ->
                    let read = readValue $"""{{ "Literal": {{ "{tag}": {{}} }} }}"""
                    Expect.equal (rule read) "invalid_literal" "The rule"
                    Expect.equal (location read) $"$.Literal.{tag}" "The location"

                    Expect.equal
                        (rule (readValue $"""{{ "Literal": {{ "{tag}": {{ "other": 1 }} }} }}"""))
                        "invalid_literal"
                        "An unrelated member is no value"
        ]

        testCase "A character is one Unicode scalar"
        <| fun () ->
            Expect.equal
                (ok (readValue "{ \"Literal\": { \"CharLiteral\": \"\U0001F600\" } }"))
                (Value.Literal(ValueAttributes.empty, CharLiteral "\U0001F600"))
                "An astral character is one character"

            Expect.equal
                (rule (readValue """{ "Literal": { "CharLiteral": "" } }"""))
                "invalid_literal"
                "Empty"

        testCase "Parameter order"
        <| fun () ->
            let letWith inputs =
                ok (
                    readValue
                        $"""{{ "LetDefinition": {{ "name": "f", "definition": {{ "ExpressionBody": {{
                                 "inputTypes": {inputs}, "outputType": "a:b#c", "body": {{ "Unit": {{}} }} }} }}, "in": "f" }} }}"""
                )

            Expect.notEqual
                (letWith """{ "x": "a:b#c", "y": "a:b#c" }""")
                (letWith """{ "y": "a:b#c", "x": "a:b#c" }""")
                "Parameter order counts"

        testCase "Module order"
        <| fun () ->
            let documentWith modules =
                $"""{{ "formatVersion": 4, "distribution": {{ "Library": {{ "packageName": "t", "def": {{ "modules": {modules} }} }} }} }}"""

            let left = documentWith """{ "a": { "Public": {} }, "b": { "Private": {} } }"""
            let right = documentWith """{ "b": { "Private": {} }, "a": { "Public": {} } }"""

            Expect.equal
                (ok (readDocument left))
                (ok (readDocument right))
                "Module order does not count"

            let listed json =
                let _, _, definition = libraryOf json
                OrderedMap.keys definition.Modules

            let path word =
                ModulePath(Morphir.IR.Path [ name word ])

            Expect.equal (listed left) [ path "a"; path "b" ] "Each lists its own order"
            Expect.equal (listed right) [ path "b"; path "a" ] "Each lists its own order"

        testCase "Reference shorthand"
        <| fun () ->
            let expected = Value.Reference(ValueAttributes.empty, addName)
            Expect.equal (ok (readValue "\"morphir/SDK:basics#add\"")) expected "The bare string"

            Expect.equal
                (ok (readValue """{ "Reference": "morphir/SDK:basics#add" }"""))
                expected
                "The wrapper"

        testCase "Expanded node"
        <| fun () ->
            Expect.equal
                (ok (
                    readValue
                        """{ "Reference": { "attributes": {}, "fqname": "morphir/SDK:basics#add" } }"""
                ))
                (ok (readValue "\"morphir/SDK:basics#add\""))
                "The expanded node equals its shorthand"

        testCase "Format version spellings"
        <| fun () ->
            let body = """{ "Library": { "packageName": "t" } }"""

            let fromInteger =
                ok (readDocument $"""{{ "formatVersion": 4, "distribution": {body} }}""")

            let fromString =
                ok (readDocument $"""{{ "formatVersion": "4.0.0", "distribution": {body} }}""")

            let reversed =
                ok (readDocument $"""{{ "distribution": {body}, "formatVersion": 4 }}""")

            Expect.equal fromInteger fromString "The string spelling"
            Expect.equal fromInteger reversed "formatVersion after distribution"
            Expect.equal fromInteger.FormatVersion "4.0.0" "Normalized"

        testCase "A later patch keeps its exact release"
        <| fun () ->
            Expect.equal
                (ok (
                    readDocument
                        """{ "formatVersion": "4.0.1", "distribution": { "Library": { "packageName": "t" } } }"""
                ))
                    .FormatVersion
                "4.0.1"
                "The exact release"

        testCase "Duplicate member"
        <| fun () ->
            let inNode = readValue """{ "Variable": { "name": "a", "name": "b" } }"""
            Expect.equal (rule inNode) "duplicate_member" "In a node"
            Expect.equal (location inNode) "$.Variable.name" "With its location"

            Expect.equal
                (rule (
                    readValue
                        """{ "Literal": { "DocumentLiteral": { "a": { "b": 1, "b": 2 } } } }"""
                ))
                "duplicate_member"
                "In a document payload"

            Expect.equal
                (rule (
                    readDocument
                        """{ "formatVersion": 4, "distribution": {}, "distribution": {} }"""
                ))
                "duplicate_member"
                "At the root"

            Expect.equal
                (rule (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Library": { "packageName": "t", "dependencies": { "a": {}, "a": {} } } } }"""
                ))
                "duplicate_member"
                "In a map"

        testCase "Two spellings of one key are a duplicate"
        <| fun () ->
            Expect.equal
                (rule (readValue """{ "Record": { "fields": { "USD": 1, "u-s-d": 2 } } }"""))
                "duplicate_member"
                "One name twice"

            Expect.equal
                (rule (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Specs": { "packageName": "t", "spec": { "modules": { "m": { "values": { "v": { "inputs": [["a", "x"], ["a", "y"]], "output": "x" } } } } } } } }"""
                ))
                "duplicate_member"
                "A legacy parameter list"

        testCase "Unknown node member"
        <| fun () ->
            let read = readValue """{ "Reference": "morphir/SDK:basics#add", "x": 1 }"""
            Expect.equal (rule read) "unknown_member" "A member beside the node"
            Expect.equal (location read) "$.x" "With its location"

            Expect.equal
                (rule (readValue """{ "Apply": { "function": 1, "argument": 2, "extra": 3 } }"""))
                "unknown_member"
                "A member inside the payload"

            Expect.equal
                (rule (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Library": { "packageName": "t" } }, "extra": 1 }"""
                ))
                "unknown_member"
                "At the root"

        testCase "Malformed name"
        <| fun () ->
            let badName = readValue """{ "Variable": "value-in-Usd" }"""
            Expect.equal (rule badName) "invalid_name" "A canonical name"
            Expect.equal (location badName) "$.Variable" "With its location"

            let badFQName = readValue """{ "Reference": "morphir/SDK:basics" }"""
            Expect.equal (rule badFQName) "invalid_name" "An FQName"
            Expect.equal (location badFQName) "$.Reference" "With its location"

            Expect.equal (rule (readValue "\"a:b:c#d\"")) "invalid_name" "A bare FQName"

            Expect.equal
                (rule (readValue """{ "Variable": ["Usd"] }"""))
                "invalid_name"
                "A legacy word"

            Expect.equal
                (rule (readValue """{ "Variable": [] }"""))
                "invalid_name"
                "An empty legacy name"

            Expect.equal
                (rule (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Library": { "packageName": "A/b_c" } } }"""
                ))
                "invalid_name"
                "A package name"

            Expect.equal
                (rule (readValue """{ "Record": { "fields": { "Bad_Name": 1 } } }"""))
                "invalid_name"
                "A map key"

        testCase "Wrong major version"
        <| fun () ->
            let read =
                readDocument
                    """{ "formatVersion": 3, "distribution": ["Library", [["a"]], [], { "modules": [] }] }"""

            Expect.equal (rule read) "unsupported_format_version_major" "A v3 document"
            Expect.equal (location read) "$.formatVersion" "With its location"

        testList "Format version refusals" [
            for version, expected in
                [
                    "0", "invalid_format_version_syntax"
                    "1", "unsupported_format_version_major"
                    "2", "unsupported_format_version_major"
                    "5", "unsupported_format_version_major"
                    "\"3.1.0\"", "unsupported_format_version_major"
                    "\"5.0.0\"", "unsupported_format_version_major"
                    "\"4.1.0\"", "unsupported_format_version_minor"
                    "\"1.0.0\"", "invalid_format_version_syntax"
                    "\"4.0\"", "invalid_format_version_syntax"
                    "\"4.0.0-beta\"", "invalid_format_version_syntax"
                    "\"4.0.0+build\"", "invalid_format_version_syntax"
                    "\" 4.0.0\"", "invalid_format_version_syntax"
                    "\"04.0.0\"", "invalid_format_version_syntax"
                    "\"4.00.0\"", "invalid_format_version_syntax"
                    "\"4294967296.0.0\"", "format_version_out_of_range"
                    "\"4.0.4294967296\"", "format_version_out_of_range"
                    "4294967296", "format_version_out_of_range"
                    "-4", "invalid_format_version_type"
                    "4.0", "invalid_format_version_type"
                    "true", "invalid_format_version_type"
                    "null", "invalid_format_version_type"
                    "[4]", "invalid_format_version_type"
                ] do
                testCase version
                <| fun () ->
                    let read =
                        readDocument
                            $"""{{ "formatVersion": {version}, "distribution": {{ "Library": {{ "packageName": "t" }} }} }}"""

                    Expect.equal (rule read) expected "The diagnostic"
                    Expect.equal (location read) "$.formatVersion" "The location"
        ]

        testCase "A repeated format version has its own diagnostic"
        <| fun () ->
            let read =
                readDocument """{ "formatVersion": 4, "formatVersion": 4, "distribution": {} }"""

            Expect.equal (rule read) "duplicate_format_version" "The upstream code"
            Expect.equal (location read) "$.formatVersion" "The location"

        testCase "A missing format version is refused"
        <| fun () ->
            Expect.equal
                (rule (readDocument """{ "distribution": { "Library": { "packageName": "t" } } }"""))
                "missing_format_version"
                "No member"

        testCase "The root must be an object"
        <| fun () -> Expect.equal (rule (readDocument "[]")) "invalid_type" "An array"

        testCase "A distribution must be a wrapper object with one kind"
        <| fun () ->
            for shape in [ """[]"""; "{}"; """{ "Bundle": {} }""" ] do
                Expect.equal
                    (rule (readDocument $"""{{ "formatVersion": 4, "distribution": {shape} }}"""))
                    "invalid_distribution_shape"
                    shape

            Expect.equal
                (rule (readDocument """{ "formatVersion": 4 }"""))
                "missing_member"
                "No distribution"

            Expect.equal
                (rule (readDocument """{ "formatVersion": 4, "distribution": { "Library": {} } }"""))
                "missing_member"
                "No package name"

            Expect.equal
                (rule (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Application": { "packageName": "t" } } }"""
                ))
                "missing_member"
                "An application needs entry points"

        testCase "An application's parts read"
        <| fun () ->
            let read =
                ok (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Application": { "packageName": "t",
                               "dependencies": { "d": { "modules": { "m": { "Private": {} } } } },
                               "entryPoints": { "free_form name": { "target": "t:m#run", "kind": "job", "doc": "Runs." } } } } }"""
                )

            match read.Distribution with
            | Application(_, dependencies, _, [ entry, point ]) ->
                Expect.equal
                    (OrderedMap.count dependencies)
                    1
                    "Dependencies are package definitions"

                Expect.equal entry "free_form name" "An entry point's name is arbitrary text"
                Expect.equal point.Kind Job "Its kind"
                Expect.equal point.Doc (Some "Runs.") "Its doc"
            | other -> failtestf "Unexpected application: %A" other

        testCase "A value's type and pattern positions keep their own shorthands"
        <| fun () ->
            Expect.equal
                (ok (readType """["a", "b"]"""))
                (Type.Tuple(
                    TypeAttributes.empty,
                    [
                        Type.Variable(TypeAttributes.empty, name "a")
                        Type.Variable(TypeAttributes.empty, name "b")
                    ]
                ))
                "A bare array is a tuple type"

            Expect.equal
                (ok (readPattern """[{ "UnitPattern": {} }]"""))
                (TuplePattern(ValueAttributes.empty, [ UnitPattern ValueAttributes.empty ]))
                "A bare array is a tuple pattern"

            Expect.equal
                (rule (readType "\"USD\""))
                "invalid_name"
                "A bare type variable follows the schema's lowercase pattern"

            Expect.equal
                (ok (readType """{ "Variable": "USD" }"""))
                (Type.Variable(TypeAttributes.empty, names [ "u"; "s"; "d" ]))
                "The wrapper takes any name"

        testList "Each member reaches its own field" [
            for label, json, expected in valueFields do
                testCase $"Value: {label}"
                <| fun () -> Expect.equal (ok (readValue json)) expected json

            for label, json, expected in typeFields do
                testCase $"Type: {label}"
                <| fun () -> Expect.equal (ok (readType json)) expected json

            for label, json, expected in patternFields do
                testCase $"Pattern: {label}"
                <| fun () -> Expect.equal (ok (readPattern json)) expected json

            testCase "Specification: DerivedTypeSpecification"
            <| fun () ->
                let read =
                    ok (
                        readDocument
                            """{ "formatVersion": 4, "distribution": { "Specs": { "packageName": "t", "spec": { "modules": { "m": { "types": { "t": { "DerivedTypeSpecification": {
                                     "typeParams": ["p"], "baseType": "x", "fromBaseType": "a:b#from", "toBaseType": "a:b#to" } } } } } } } } }"""
                    )

                match read.Distribution with
                | Specs(_, _, specification) ->
                    let moduleSpecification =
                        (OrderedMap.toList specification.Modules).Head
                        |> snd

                    match OrderedMap.toList moduleSpecification.Types with
                    | [ _,
                        {
                            Value = DerivedTypeSpecification([],
                                                             [ parameter ],
                                                             baseType,
                                                             fromBase,
                                                             toBase)
                        } ] ->
                        Expect.equal parameter (name "p") "typeParams"
                        Expect.equal baseType (typeVariable "x") "baseType"

                        Expect.equal
                            fromBase
                            (fq [ name "a" ] [ name "b" ] (name "from"))
                            "fromBaseType"

                        Expect.equal toBase (fq [ name "a" ] [ name "b" ] (name "to")) "toBaseType"
                    | other -> failtestf "Unexpected types: %A" other
                | other -> failtestf "Unexpected distribution: %A" other
        ]

        testCase "Legacy spellings read as their canonical form"
        <| fun () ->
            Expect.equal
                (ok (readValue """{ "Reference": [[["a"]], [["b"]], ["c"]] }"""))
                (ok (readValue """{ "Reference": "a:b#c" }"""))
                "A legacy FQName"

            Expect.equal
                (ok (readValue """{ "Reference": ["a", "b", "c"] }"""))
                (ok (readValue """{ "Reference": "a:b#c" }"""))
                "A legacy FQName of canonical strings"

            Expect.equal
                (ok (
                    readType
                        """{ "Reference": { "fqname": [[["a"]], [["b"]], ["c"]], "args": [] } }"""
                ))
                (ok (readType "\"a:b#c\""))
                "A legacy FQName in a type"

        testCase "A legacy record may hold a field named fields"
        <| fun () ->
            let sameType legacy canonical description =
                Expect.equal (ok (readType legacy)) (ok (readType canonical)) description

            let sameValue legacy canonical description =
                Expect.equal (ok (readValue legacy)) (ok (readValue canonical)) description

            sameType
                """{ "Record": { "fields": "a" } }"""
                """{ "Record": { "fields": { "fields": "a" } } }"""
                "A type field holding a bare variable"

            sameType
                """{ "Record": { "fields": ["a", "b"] } }"""
                """{ "Record": { "fields": { "fields": ["a", "b"] } } }"""
                "A type field holding a tuple"

            sameType
                """{ "Record": { "fields": { "Variable": "a" } } }"""
                """{ "Record": { "fields": { "fields": { "Variable": "a" } } } }"""
                "A type field holding a tagged node"

            sameType
                """{ "Record": { "fields": "a", "other": "b" } }"""
                """{ "Record": { "fields": { "fields": "a", "other": "b" } } }"""
                "A type field beside another field"

            sameValue
                """{ "Record": { "fields": 1 } }"""
                """{ "Record": { "fields": { "fields": 1 } } }"""
                "A value field holding a number"

            sameValue
                """{ "Record": { "fields": { "Variable": "a" } } }"""
                """{ "Record": { "fields": { "fields": { "Variable": "a" } } } }"""
                "A value field holding a tagged node"

        testCase "A fields member holding a field map is the expanded record"
        <| fun () ->
            Expect.equal
                (ok (readType """{ "Record": { "fields": { "n": "a" } } }"""))
                (ok (readType """{ "Record": { "n": "a" } }"""))
                "A field map under the member"

            Expect.equal
                (ok (readValue """{ "Record": { "fields": {} } }"""))
                (ok (readValue """{ "Record": {} }"""))
                "An empty record"

            Expect.equal
                (rule (readType """{ "Record": { "fields": { "n": "a" }, "other": "b" } }"""))
                "unknown_member"
                "A field map beside an unknown member"

        testCase "The nested documentation wrapper may not state two docs"
        <| fun () ->
            Expect.equal
                (rule (
                    readDocument (
                        moduleJson
                            """{ "types": { "t": { "Public": { "doc": "One", "value": { "doc": "Two", "TypeAliasDefinition": { "typeParams": [], "typeExp": "a:b#c" } } } } } }"""
                    )
                ))
                "duplicate_member"
                "Two docs"

        testCase "A compact annotation's text is one positional string"
        <| fun () ->
            let specWith annotation =
                ok (
                    readDocument
                        $"""{{ "formatVersion": 4, "distribution": {{ "Specs": {{ "packageName": "t", "spec": {{ "modules": {{ "m": {{ "annotations": [{annotation}] }} }} }} }} }} }}"""
                )

            Expect.equal
                (specWith "\"a:b#c:Use d:e instead\"")
                (specWith
                    """{ "name": "a:b#c", "arguments": [{ "Literal": { "StringLiteral": "Use d:e instead" } }] }""")
                "The text starts after the first colon that follows the hash"

            Expect.equal
                (rule (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Specs": { "packageName": "t", "spec": { "modules": { "m": { "annotations": ["a:b#c:"] } } } } } }"""
                ))
                "invalid_name"
                "A separator needs text after it"

        testCase "A named annotation argument keeps its name"
        <| fun () ->
            let read =
                ok (
                    readDocument
                        """{ "formatVersion": 4, "distribution": { "Specs": { "packageName": "t", "spec": { "modules": { "m": { "annotations": [{ "name": "a:b#c", "arguments": [{ "name": "why", "value": 1 }, 2] }] } } } } } }"""
                )

            match read.Distribution with
            | Specs(_, _, specification) ->
                match OrderedMap.toList specification.Modules with
                | [ _,
                    {
                        Annotations = [ {
                                            Arguments = [ NamedArgument(argument, _)
                                                          PositionalArgument _ ]
                                        } ]
                    } ] -> Expect.equal argument (name "why") "The argument's name"
                | other -> failtestf "Unexpected module: %A" other
            | other -> failtestf "Unexpected distribution: %A" other

        testList "Native hints" [
            for spelling, expected in
                [
                    """{ "Arithmetic": {} }""", Arithmetic
                    """{ "Comparison": {} }""", Comparison
                    """{ "StringOp": {} }""", StringOp
                    """{ "CollectionOp": {} }""", CollectionOp
                    """{ "PlatformSpecific": { "platform": "wasm" } }""", PlatformSpecific "wasm"
                ] do
                testCase spelling
                <| fun () ->
                    let module' =
                        onlyModule (
                            moduleJson
                                $"""{{ "values": {{ "v": {{ "Public": {{ "NativeBody": {{ "inputTypes": {{}}, "outputType": "a:b#c",
                                         "nativeInfo": {{ "hint": {spelling}, "description": "Why." }} }} }} }} }} }}"""
                        )

                    match OrderedMap.tryFind (name "v") module'.Values with
                    | Some {
                               Value = {
                                           Value = { Body = NativeBody(_, info) }
                                       }
                           } ->
                        Expect.equal info.Hint expected "The hint"
                        Expect.equal info.Description (Some "Why.") "The description"
                    | other -> failtestf "Unexpected native body: %A" other
        ]

        testCase "Legacy tagged arrays read as their object forms"
        <| fun () ->
            let specOf body =
                ok (
                    readDocument
                        $"""{{ "formatVersion": 4, "distribution": {{ "Specs": {{ "packageName": "t", "spec": {{ "modules": {{ "m": {{ "types": {{ "t": {body} }} }} }} }} }} }} }}"""
                )

            Expect.equal
                (specOf """["TypeAliasSpecification", ["a"], "a"]""")
                (specOf """{ "TypeAliasSpecification": { "typeParams": ["a"], "typeExp": "a" } }""")
                "An alias specification"

            Expect.equal
                (specOf
                    """["CustomTypeSpecification", [], [["just", [["value", "a:b#c"]]], ["none", []]]]""")
                (specOf
                    """{ "CustomTypeSpecification": { "typeParams": [], "constructors": { "just": [["value", "a:b#c"]], "none": [] } } }""")
                "A custom specification"

            Expect.equal
                (specOf
                    """["DerivedTypeSpecification", [], { "baseType": "a:b#c", "fromBaseType": "a:b#d", "toBaseType": "a:b#e" }]""")
                (specOf
                    """{ "DerivedTypeSpecification": { "typeParams": [], "baseType": "a:b#c", "fromBaseType": "a:b#d", "toBaseType": "a:b#e" } }""")
                "A derived specification"

            let defOf body =
                ok (
                    readDocument (
                        moduleJson $"""{{ "types": {{ "t": {{ "Public": {body} }} }} }}"""
                    )
                )

            Expect.equal
                (defOf """["TypeAliasDefinition", [], "a:b#c"]""")
                (defOf """{ "TypeAliasDefinition": { "typeParams": [], "typeExp": "a:b#c" } }""")
                "An alias definition"

            Expect.equal
                (defOf
                    """["CustomTypeDefinition", [], { "access": "Private", "value": [["none", []]] }]""")
                (defOf
                    """{ "CustomTypeDefinition": { "typeParams": [], "access": "Private", "constructors": { "none": [] } } }""")
                "A custom definition"

            Expect.equal
                (defOf
                    """{ "CustomTypeDefinition": { "typeParams": [], "constructors": { "none": [] } } }""")
                (defOf
                    """{ "CustomTypeDefinition": { "typeParams": [], "access": "Public", "constructors": { "none": [] } } }""")
                "Constructors without an access are public"

        testList "Bad input gives an error rather than an exception" [
            testCase "An undefined element"
            <| fun () ->
                Expect.isError (V4Json.readValue (Unchecked.defaultof<JsonElement>)) "A value"

                Expect.isError
                    (V4Json.readDistribution (Unchecked.defaultof<JsonElement>))
                    "A distribution"

            testList "A lone surrogate is refused where it occurs" [
                for json, expectedRule, expectedLocation in
                    [
                        """{ "Literal": { "CharLiteral": "\uD800" } }""",
                        "invalid_literal",
                        "$.Literal.CharLiteral"
                        """{ "Literal": { "StringLiteral": "\uD800" } }""",
                        "invalid_literal",
                        "$.Literal.StringLiteral"
                        """{ "Literal": { "DecimalLiteral": { "value": "\uD800" } } }""",
                        "invalid_literal",
                        "$.Literal.DecimalLiteral"
                        """{ "Literal": "\uD800" }""", "invalid_literal", "$.Literal"
                        "\"\\uD800\"", "invalid_name", "$"
                        """{ "Variable": "\uD800" }""", "invalid_name", "$.Variable"
                        """{ "Variable": ["\uD800"] }""", "invalid_name", "$.Variable"
                        """{ "Reference": "\uD800" }""", "invalid_name", "$.Reference"
                        """{ "Literal": { "DocumentLiteral": ["\uD800"] } }""",
                        "invalid_type",
                        "$.Literal.DocumentLiteral[0]"
                        """{ "Literal": { "DocumentLiteral": { "\uD800": 1 } } }""",
                        "invalid_type",
                        "$.Literal.DocumentLiteral"
                        """{ "Hole": { "reason": { "DeletedDuringRefactor": { "tx-id": "\uD800" } } } }""",
                        "invalid_type",
                        "$.Hole.reason.DeletedDuringRefactor.tx-id"
                    ] do
                    testCase json
                    <| fun () ->
                        let read = readValue json
                        Expect.equal (rule read) expectedRule "The rule"
                        Expect.equal (location read) expectedLocation "The location"
            ]

            testCase "A lone surrogate at type position or in a document"
            <| fun () ->
                let bareType = readType "\"\\uD800\""
                Expect.equal (rule bareType) "invalid_name" "A bare type"
                Expect.equal (location bareType) "$" "Its location"

                let version =
                    readDocument
                        """{ "formatVersion": "\uD800", "distribution": { "Library": { "packageName": "t" } } }"""

                Expect.equal (rule version) "invalid_format_version_syntax" "A format version"
                Expect.equal (location version) "$.formatVersion" "Its location"

                let rootMember = readDocument """{ "\uD800": 1, "formatVersion": 4 }"""
                Expect.equal (rule rootMember) "invalid_type" "A root member's name"
                Expect.equal (location rootMember) "$" "Its location"

            testCase "Nesting past the depth limit"
            <| fun () ->
                Expect.isOk (deeplyNested 512) "The limit itself reads"

                Expect.equal
                    (rule (deeplyNested 513))
                    "depth_limit_exceeded"
                    "One level past the limit"

                Expect.equal (rule (deeplyNested 20000)) "depth_limit_exceeded" "Far past the limit"

            testCase "A null where a node belongs"
            <| fun () ->
                Expect.equal (rule (readValue "null")) "invalid_type" "A value"
                Expect.equal (rule (readType "null")) "invalid_type" "A type"
                Expect.equal (rule (readPattern "null")) "invalid_type" "A pattern"

            testCase "A source location outside the integer range"
            <| fun () ->
                Expect.equal
                    (rule (
                        readValue
                            """{ "Unit": { "attributes": { "source": { "startLine": 1.5, "startColumn": 1, "endLine": 1, "endColumn": 1 } } } }"""
                    ))
                    "invalid_type"
                    "A fractional line"

                Expect.equal
                    (rule (
                        readValue
                            """{ "Unit": { "attributes": { "source": { "startLine": 99999999999, "startColumn": 1, "endLine": 1, "endColumn": 1 } } } }"""
                    ))
                    "invalid_type"
                    "A huge line"
        ]
    ]
