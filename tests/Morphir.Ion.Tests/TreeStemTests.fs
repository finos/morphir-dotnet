/// Stems in a document tree: the escape of decision 0001, the cut stems of decisions 0001 and 0012, and the
/// version segment of decision 0015. The digests below were computed with PowerShell's SHA256, not with this
/// library.
module Morphir.Ion.Tests.TreeStemTests

open Expecto
open Morphir.IR
open Morphir.Ion

let private name (input: string) =
    match CanonicalNames.parseName input with
    | Ok parsed -> parsed
    | Error message -> failtestf "'%s' is not a name: %s" input message

let private stemOf (input: string) =
    match TreeStems.parseStem input with
    | Ok stem -> stem
    | Error message -> failtestf "'%s' is not a stem: %s" input message

let private expectWhole (input: string) (expected: string) =
    Expect.equal (stemOf input) (WholeStem(name expected)) $"{input} reads as {expected}"

let private expectRefused (input: string) =
    match TreeStems.parseStem input with
    | Ok stem -> failtestf "'%s' should not be a stem, but reads as %A" input stem
    | Error _ -> ()

/// SHA-256 of the untruncated escaped stem, first eight digits.
let private longName = "some-extremely-long-canonical-type-name-that-overflows"
let private longDigest = "6d98776c"
let private initialismName = "USD-value-in-a-very-long-name"
let private initialismEscaped = "_usd-value-in-a-very-long-name"
let private initialismDigest = "92b9a5b1"

[<Tests>]
let tests =
    testList "Tree stems" [
        testList "Escaping a name" [
            for canonical, stem in
                [
                    "user", "user"
                    "user-ID", "user-_id"
                    "value-in-USD", "value-in-_usd"
                    "USD-value", "_usd-value"
                    "item-2", "item-2"
                    "FR2052A", "_fr-2052-a"
                    "a", "a"
                    "point-x-y", "point-_xy"
                ] do
                testCase $"{canonical} is {stem}"
                <| fun () ->
                    Expect.equal (TreeStems.escapeName (name canonical)) stem "The escaped stem"

            for device in
                [ "con"; "prn"; "aux"; "nul"; "com0"; "com1"; "com9"; "lpt0"; "lpt1"; "lpt9" ] do
                testCase $"The Windows device name {device} takes a trailing underscore"
                <| fun () ->
                    Expect.equal
                        (TreeStems.escapeName (name device))
                        $"{device}_"
                        "The device name is escaped"

            testCase "An initialism that spells a device name has a leading underscore instead"
            <| fun () ->
                Expect.equal (TreeStems.escapeName (name "CON")) "_con" "No trailing underscore"

            testCase "A name that only starts with a device name is not escaped"
            <| fun () ->
                Expect.equal (TreeStems.escapeName (name "con-x")) "con-x" "A longer stem"
                Expect.equal (TreeStems.escapeName (name "console")) "console" "A longer word"
                Expect.equal (TreeStems.escapeName (name "com10")) "com10" "Not com0 to com9"
        ]

        testList "Reading a stem back" [
            testCase "A plain stem is its name"
            <| fun () ->
                expectWhole "user" "user"
                expectWhole "item-2" "item-2"
                expectWhole "manifest" "manifest"
                expectWhole "module" "module"

            testCase "An underscore prefix marks an initialism"
            <| fun () ->
                expectWhole "user-_id" "user-ID"
                expectWhole "value-in-_usd" "value-in-USD"
                expectWhole "_fr-2052-a" "FR-2052-a"
                expectWhole "_con" "CON"

            testCase "A trailing underscore on a device name is removed"
            <| fun () ->
                expectWhole "con_" "con"
                expectWhole "aux_" "aux"
                expectWhole "lpt9_" "lpt9"

            testCase "A trailing underscore on anything else is refused"
            <| fun () ->
                expectRefused "foo_"
                expectRefused "con-x_"
                expectRefused "com10_"
                expectRefused "_con_"

            testCase "Anything the escape never writes is refused"
            <| fun () ->
                for stem in
                    [
                        ""
                        "_"
                        "-"
                        "a-"
                        "-a"
                        "a--b"
                        "a_b"
                        "User"
                        "user-ID"
                        "a b"
                        "a.b"
                        "a/b"
                        "ü"
                        "__12345678"
                    ] do
                    expectRefused stem

            testCase "A stem ending in two underscores and eight hex digits is a cut stem"
            <| fun () ->
                Expect.equal
                    (stemOf $"some-extremely-long-canon__{longDigest}")
                    (CutStem("some-extremely-long-canon", longDigest))
                    "Prefix and digest"

                Expect.equal
                    (stemOf $"_usd-value-in__{initialismDigest}")
                    (CutStem("_usd-value-in", initialismDigest))
                    "A prefix keeps its initialism marker"

            testCase "A cut stem needs lowercase hex, exactly eight digits, and nothing after them"
            <| fun () ->
                expectRefused "abc__6D98776C"
                expectRefused "abc__6d98776"
                expectRefused "abc__6d98776cc"
                expectRefused "abc__6d98776g"
                expectRefused "abc__6d98776c_"
                expectRefused "abc___6d98776c"
                expectRefused "abc-__6d98776c"

            testCase "Every escaped name reads back as itself"
            <| fun () ->
                for canonical in
                    [
                        "user"
                        "user-ID"
                        "value-in-USD"
                        "FR2052A"
                        "con"
                        "CON"
                        "aux"
                        "com1"
                        "lpt9"
                        "a"
                        "x-1-y"
                        "point-x-y"
                        "ID"
                        "XML-HTTP-request"
                    ] do
                    let original = name canonical

                    Expect.equal
                        (stemOf (TreeStems.escapeName original))
                        (WholeStem original)
                        canonical
        ]

        testList "Cut stems" [
            testCase "A name with the prefix and the digest matches"
            <| fun () ->
                Expect.isTrue
                    (TreeStems.matchesCut "some-extremely-long-canon" longDigest (name longName))
                    "The stem belongs to the name"

            testCase "The digest is of the escaped stem, so an initialism name matches too"
            <| fun () ->
                Expect.equal
                    (TreeStems.escapeName (name initialismName))
                    initialismEscaped
                    "The escaped stem"

                Expect.isTrue
                    (TreeStems.matchesCut "_usd-value-in" initialismDigest (name initialismName))
                    "The digest follows the escaped form"

            testCase "A different digest does not match"
            <| fun () ->
                Expect.isFalse
                    (TreeStems.matchesCut "some-extremely-long-canon" "00000000" (name longName))
                    "The digest differs"

            testCase "A different prefix does not match"
            <| fun () ->
                Expect.isFalse
                    (TreeStems.matchesCut "some-extremely-short" longDigest (name longName))
                    "The prefix differs"

            testCase "A different name does not match"
            <| fun () ->
                Expect.isFalse
                    (TreeStems.matchesCut "some-extremely-long-canon" longDigest (name "other"))
                    "The name differs"
        ]

        testList "The version segment under deps" [
            testCase "A bare @ ends the package path and carries no version yet"
            <| fun () ->
                Expect.equal
                    (TreeStems.splitDependency [ "morphir"; "_sdk"; "@"; "list" ])
                    (Ok
                        {
                            PackageDirectories = [ "morphir"; "_sdk" ]
                            Version = ""
                            ModuleDirectories = [ "list" ]
                        })
                    "Package, version, module"

            testCase "Only the bare @ is accepted, whatever follows it"
            <| fun () ->
                for segment in [ "@3.0.0"; "@v1"; "@latest" ] do
                    Expect.isError
                        (TreeStems.splitDependency [ "a"; segment; "b"; "c" ])
                        $"{segment} is a version, which the current layout does not have"

            testCase "A package path may hold another package's path as a prefix"
            <| fun () ->
                let first = TreeStems.splitDependency [ "a"; "@"; "b"; "c" ]
                let second = TreeStems.splitDependency [ "a"; "b"; "@"; "c" ]

                Expect.equal
                    first
                    (Ok
                        {
                            PackageDirectories = [ "a" ]
                            Version = ""
                            ModuleDirectories = [ "b"; "c" ]
                        })
                    "Module b/c of a"

                Expect.equal
                    second
                    (Ok
                        {
                            PackageDirectories = [ "a"; "b" ]
                            Version = ""
                            ModuleDirectories = [ "c" ]
                        })
                    "Module c of a/b"

            testCase "A path with no @ segment is refused"
            <| fun () ->
                Expect.isError (TreeStems.splitDependency [ "a"; "b" ]) "No version segment"

            testCase "A path whose package is empty is refused"
            <| fun () ->
                Expect.isError (TreeStems.splitDependency [ "@"; "x" ]) "No package"
                Expect.isError (TreeStems.splitDependency []) "Nothing at all"
        ]
    ]
