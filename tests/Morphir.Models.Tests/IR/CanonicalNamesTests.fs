module Morphir.IR.Tests.CanonicalNamesTests

open Expecto
open Morphir.IR

let private ok result =
    match result with
    | Ok value -> value
    | Error message -> failtestf "Expected a parsed value, got: %s" message

let private isError result =
    match result with
    | Ok value -> failtestf "Expected an error, got %A" value
    | Error(message: string) -> Expect.isNotEmpty message "The error explains itself"

/// Every row of decision 0001's and the naming corpus's legacy and round-trip tables that this name shape keeps.
let private canonicalAndLegacy = [
    "user-account", [ "user"; "account" ]
    "value-in-USD", [ "value"; "in"; "u"; "s"; "d" ]
    "in-usd", [ "in"; "usd" ]
    "get-HTML", [ "get"; "h"; "t"; "m"; "l" ]
    "my-API-client", [ "my"; "a"; "p"; "i"; "client" ]
    "user-ID", [ "user"; "i"; "d" ]
    "IO-error", [ "i"; "o"; "error" ]
    "HTML-parser", [ "h"; "t"; "m"; "l"; "parser" ]
    "a", [ "a" ]
    "max-n", [ "max"; "n" ]
    "2052", [ "2052" ]
    "data-ID-tables", [ "data"; "i"; "d"; "tables" ]
    "aux", [ "aux" ]
    "CON", [ "c"; "o"; "n" ]
    "nul-pointer", [ "nul"; "pointer" ]
    "com1", [ "com1" ]
    "morphir-SDK", [ "morphir"; "s"; "d"; "k" ]
    "point-XY", [ "point"; "x"; "y" ]
    "US", [ "u"; "s" ]
    "FR-2052-a", [ "f"; "r"; "2052"; "a" ]
    "1-2", [ "1"; "2" ]
    "u-1", [ "u"; "1" ]
    "v-2-api", [ "v"; "2"; "api" ]
    "item-2", [ "item"; "2" ]
]

[<Tests>]
let tests =
    testList "Canonical names" [
        testCase "Initialism"
        <| fun () ->
            Expect.equal
                (ok (CanonicalNames.parseName "value-in-USD"))
                (ok (CanonicalNames.parseLegacyName [ "value"; "in"; "u"; "s"; "d" ]))
                "value-in-USD equals the legacy array"

            Expect.equal
                (ok (CanonicalNames.parseName "value-in-USD"))
                (Name [ "value"; "in"; "u"; "s"; "d" ])
                "An uppercase segment becomes single-letter words"

        testCase "FQName parts"
        <| fun () ->
            let fqName = ok (CanonicalNames.parseFQName "morphir/SDK:list#map")

            Expect.equal
                fqName.PackagePath
                (PackageName(Path [ Name [ "morphir" ]; Name [ "s"; "d"; "k" ] ]))
                "The package is morphir/SDK"

            Expect.equal
                fqName.ModulePath
                (ModulePath(Path [ Name [ "list" ] ]))
                "The module is list"

            Expect.equal fqName.LocalName (Name [ "map" ]) "The local name is map"

        testList "Name round trips against decision 0001" [
            for canonical, legacy in canonicalAndLegacy do
                testCase canonical
                <| fun () ->
                    let parsed = ok (CanonicalNames.parseName canonical)

                    Expect.equal
                        parsed
                        (Name legacy)
                        "The canonical string parses to the legacy words"

                    Expect.equal
                        (CanonicalNames.nameToString parsed)
                        canonical
                        "The name writes back unchanged"

                    Expect.equal
                        (ok (CanonicalNames.parseLegacyName legacy))
                        parsed
                        "The legacy array is the same name"
        ]

        testCase "An initialism containing digits splits at its digit run"
        <| fun () ->
            Expect.equal
                (ok (CanonicalNames.parseName "FR2052A"))
                (Name [ "f"; "r"; "2052"; "a" ])
                "FR2052A"

            Expect.equal (ok (CanonicalNames.parseName "A1B")) (Name [ "a"; "1"; "b" ]) "A1B"

        testCase "A single uppercase letter is the word it spells"
        <| fun () -> Expect.equal (ok (CanonicalNames.parseName "A")) (Name [ "a" ]) "A"

        testList "Names that do not parse" [
            for input in
                [
                    ""
                    "Usd"
                    "value-in-(usd)"
                    "value-in-Usd"
                    "user_id"
                    "-user"
                    "user-"
                    "value-in--usd"
                    "user--id"
                    "morphir/SDK"
                    "user---id"
                    "--"
                    "MyName"
                    " a"
                    "a "
                    "café"
                ] do
                testCase $"'{input}'"
                <| fun () -> isError (CanonicalNames.parseName input)
        ]

        testList "Legacy arrays that do not parse" [
            for input in [ []; [ "" ]; [ "Usd" ]; [ "my-org" ]; [ "a b" ] ] do
                testCase $"%A{input}"
                <| fun () -> isError (CanonicalNames.parseLegacyName input)
        ]

        testCase "A path splits at slashes"
        <| fun () ->
            let path = ok (CanonicalNames.parsePath "US/FR2052A/data-tables")

            Expect.equal
                path
                (Path [
                    Name [ "u"; "s" ]
                    Name [ "f"; "r"; "2052"; "a" ]
                    Name [ "data"; "tables" ]
                ])
                "Each segment is a name"

            Expect.equal
                (CanonicalNames.pathToString (ok (CanonicalNames.parsePath "morphir/SDK")))
                "morphir/SDK"
                "A path writes back"

        testList "Paths that do not parse" [
            for input in [ ""; "/"; "a/"; "/a"; "a//b"; "a/B-c/Dd"; "a:b" ] do
                testCase $"'{input}'"
                <| fun () -> isError (CanonicalNames.parsePath input)
        ]

        testCase "An FQName writes back"
        <| fun () ->
            for canonical in [ "morphir/SDK:list#map"; "my-org:domain#value-in-USD" ] do
                Expect.equal
                    (CanonicalNames.fqNameToString (ok (CanonicalNames.parseFQName canonical)))
                    canonical
                    "The canonical string survives"

        testList "FQNames with no one reading do not parse" [
            for input in
                [
                    ""
                    "a"
                    "a:b"
                    "a#b"
                    "a#b:c"
                    "a:b:c#d"
                    "a:b#c#d"
                    "a:b#c:d"
                    ":b#c"
                    "a:#c"
                    "a:b#"
                    "a/:b#c"
                    "a:b#c/d"
                    "morphir/SDK:list#Map"
                ] do
                testCase $"'{input}'"
                <| fun () -> isError (CanonicalNames.parseFQName input)
        ]
    ]
