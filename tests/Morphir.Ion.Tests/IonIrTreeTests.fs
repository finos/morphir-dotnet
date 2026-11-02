/// Ion document trees, read from in-memory trees. Each scenario of the ion-ir-document-trees spec has a test
/// named after it. The expected values are the single-file datagrams that hold the same elements.
module Morphir.Ion.Tests.IonIrTreeTests

open System
open System.IO
open Expecto
open Morphir.IR
open Morphir.IR.V4
open Morphir.IR.Pipeline
open Morphir.Ion
open Morphir.Ion.Tests

let private limits = IonReadLimits.defaults

let private header (kind: string) (package: string) (version: string) =
    $"morphir::{{ionVersion: \"0.1.0-draft.1\", formatVersion: \"{version}\", kind: {kind}, packageName: \"{package}\", pathBudget: 4000}}"

let private libraryHeader package = header "library" package "4.0.0"
let private int = "\"morphir/SDK:basics#int\""

/// A tree from locations and the text of the files at them.
let private treeOf (files: (string * string) list) : VFileTree =
    files
    |> List.map (fun (location, text) -> location, VFile.ofText location text)
    |> Map.ofList
    |> VFileTree.fromFileMap

let private datagram (elements: string list) =
    String.concat
        "\n"
        (elements
         @ [ "morphir_footer::{}" ])

let private readSingle (text: string) =
    match IonIr.read limits (Fixtures.utf8 text) with
    | Ok document -> document
    | Result.Error error -> failtestf "The single file was refused: %A" error

let private readTree (files: (string * string) list) =
    match IonIrTree.read limits (treeOf files) with
    | Ok document -> document
    | Result.Error error -> failtestf "The tree was refused: %A" error

let private refusal (files: (string * string) list) : IonIrError =
    match IonIrTree.read limits (treeOf files) with
    | Ok _ -> failtest "Expected the tree to be refused."
    | Result.Error error -> error

let private expectCode (code: string) (path: string option) (error: IonIrError) =
    Expect.equal error.Code code $"The code: {error.Message}"
    Expect.equal error.Path path $"The path for {code}: {error.Message}"

let private expectSame (tree: IrDocument) (single: IrDocument) =
    match tree, single with
    | ClassicIr(treeVersion, a), ClassicIr(singleVersion, b) ->
        Expect.equal treeVersion singleVersion "The format version"

        Expect.equal
            (ClassicComparison.comparable a)
            (ClassicComparison.comparable b)
            "One classic value"
    | V4Ir(treeVersion, a), V4Ir(singleVersion, b) ->
        Expect.equal treeVersion singleVersion "The format version"
        Expect.equal a b "One v4 value"
    | a, b -> failtestf "The families differ: %A and %A" a b

let private nameOf (text: string) =
    match CanonicalNames.parseName text with
    | Ok name -> name
    | Result.Error message -> failtest message

let private pathOf (text: string) =
    match CanonicalNames.parsePath text with
    | Ok path -> path
    | Result.Error message -> failtest message

let private manifest = "manifest.ion"

let private baseFiles = [
    manifest,
    libraryHeader "example/finance"
    + "\npackage::spec::{name: \"other/dep\"}\n"
    "pkg/example/finance/eligibility/module.ion",
    "public::def::module::{doc: \"Credit eligibility.\"}\n"
    "pkg/example/finance/eligibility/score.type.ion",
    $"public::def::alias::type::{{typeExp: {int}}}\n"
]

let private v4Library (document: IrDocument) =
    match document with
    | V4Ir(_, Library(name, dependencies, definition)) -> name, dependencies, definition
    | other -> failtestf "Expected a v4 library, read %A" other

/// The names of a module's types, in document order.
let private typeNames (definition: PackageDefinition) (modulePath: string) =
    match OrderedMap.tryFind (ModulePath(pathOf modulePath)) definition.Modules with
    | Some found ->
        OrderedMap.keys found.Value.Types
        |> List.map CanonicalNames.nameToString
    | None -> failtestf "No module %s" modulePath

/// Counts the values in an Ion value, as the reader's node limit does.
let rec private nodes (value: IonValue) : int =
    match value.Data with
    | IonData.List items
    | IonData.Sexp items ->
        1
        + (items
           |> List.sumBy nodes)
    | IonData.Struct fields ->
        1
        + (fields
           |> List.sumBy (
               snd
               >> nodes
           ))
    | _ -> 1

[<Tests>]
let tests =
    testList "Ion document trees" [
        testList "The manifest identifies the tree" [
            testCase "Tree root"
            <| fun () ->
                let single =
                    readSingle (
                        datagram [
                            libraryHeader "example/finance"
                            "package::spec::{name: \"other/dep\"}"
                            $"public::def::module::{{name: \"eligibility\", doc: \"Credit eligibility.\", types: [public::def::alias::type::{{name: \"score\", typeExp: {int}}}]}}"
                        ]
                    )

                expectSame (readTree baseFiles) single

            testCase "Missing manifest"
            <| fun () ->
                expectCode
                    "missing_manifest"
                    None
                    (refusal (
                        baseFiles
                        |> List.filter (fun (l, _) ->
                            l
                            <> manifest
                        )
                    ))

                expectCode "missing_manifest" None (refusal [])

            testCase "A JSON or YAML manifest alone is not an Ion tree"
            <| fun () ->
                for other in [ "manifest.json"; "manifest.yaml"; "manifest.yml" ] do
                    let error = refusal [ other, "{}" ]
                    expectCode "missing_manifest" None error

            testCase "Two manifests"
            <| fun () ->
                for other in [ "manifest.json"; "manifest.yaml"; "manifest.yml" ] do
                    let error =
                        refusal (
                            (other, "{}")
                            :: baseFiles
                        )

                    Expect.equal error.Code "ambiguous_manifest" "The code"
                    Expect.stringContains error.Message "manifest.ion" "Names the Ion manifest"
                    Expect.stringContains error.Message other "Names the other manifest"

            testCase "Only the root's manifest counts"
            <| fun () ->
                // A manifest below the root is not the tree's manifest, and it is not a file of the tree either.
                let files =
                    ("docs/manifest.json", "{}")
                    :: baseFiles

                Expect.isOk (IonIrTree.read limits (treeOf files)) "No ambiguity"
        ]

        testList "A tree reads as the equivalent datagram" [
            testCase "Dependency with no modules"
            <| fun () ->
                let files = [
                    manifest,
                    libraryHeader "p"
                    + "\npackage::spec::{name: \"zeta/last\"}\npackage::spec::{name: \"alpha/first\"}\npackage::spec::{name: \"mid\"}\n"
                ]

                let _, dependencies, _ = v4Library (readTree files)

                Expect.equal
                    (OrderedMap.keys dependencies
                     |> List.map (fun (PackageName path) -> CanonicalNames.pathToString path))
                    [ "zeta/last"; "alpha/first"; "mid" ]
                    "Every dependency, in manifest order"

                for _, specification in OrderedMap.toList dependencies do
                    Expect.equal (OrderedMap.count specification.Modules) 0 "No modules"

            testCase "Dependencies keep manifest order with modules under deps"
            <| fun () ->
                let files = [
                    manifest,
                    libraryHeader "p"
                    + "\npackage::spec::{name: \"zeta\"}\npackage::spec::{name: \"alpha\"}\n"
                    "deps/alpha/@/m/module.ion", "module::spec::{}\n"
                    "deps/zeta/@/m/module.ion", "module::spec::{}\n"
                ]

                let _, dependencies, _ = v4Library (readTree files)

                Expect.equal
                    (OrderedMap.keys dependencies
                     |> List.map (fun (PackageName path) -> CanonicalNames.pathToString path))
                    [ "zeta"; "alpha" ]
                    "Manifest order, not path order"

            testCase "Footer in a tree"
            <| fun () ->
                let withFooter location extra =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l,
                            text
                            + extra
                        else
                            l, text
                    )

                for location in
                    [
                        manifest
                        "pkg/example/finance/eligibility/module.ion"
                        "pkg/example/finance/eligibility/score.type.ion"
                    ] do
                    expectCode
                        "invalid_document"
                        (Some location)
                        (refusal (withFooter location "morphir_footer::{}\n"))

            testCase
                "A module's children are read in file order, whatever order the tree lists them"
            <| fun () ->
                let files = [
                    manifest, libraryHeader "p"
                    "pkg/p/m/module.ion", "public::def::module::{}\n"
                    "pkg/p/m/b.type.ion", $"public::def::alias::type::{{typeExp: {int}}}\n"
                    "pkg/p/m/a.type.ion", $"public::def::alias::type::{{typeExp: {int}}}\n"
                    "pkg/p/m/c.value.ion", $"public::def::value::{{outputType: {int}, body: 1}}\n"
                ]

                // A flat tree, whose files carry their whole location and come in reverse order.
                let reversed =
                    files
                    |> List.rev
                    |> List.map (fun (location, text) -> VFile.ofText location text)
                    |> VFileTree.fromFiles

                let _, _, definition = v4Library (readTree files)
                Expect.equal (typeNames definition "m") [ "a"; "b" ] "Type files in path order"

                match IonIrTree.read limits reversed with
                | Ok document -> expectSame document (readTree files)
                | Result.Error error -> failtestf "%A" error

            testCase "A v3 library reads as the equivalent datagram"
            <| fun () ->
                let files = [
                    manifest,
                    header "library" "example/finance" "3.0.0"
                    + "\n"
                    "pkg/example/finance/eligibility/module.ion",
                    "public::def::module::{doc: \"Credit eligibility.\"}\n"
                    "pkg/example/finance/eligibility/score.type.ion",
                    $"public::def::alias::type::{{typeExp: {int}}}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            header "library" "example/finance" "3.0.0"
                            $"public::def::module::{{name: \"eligibility\", doc: \"Credit eligibility.\", types: [public::def::alias::type::{{name: \"score\", typeExp: {int}}}]}}"
                        ]
                    )

                expectSame (readTree files) single

            testCase "A v3 specs tree reads as the equivalent datagram"
            <| fun () ->
                let files = [
                    manifest,
                    header "specs" "my/pkg" "3.1.0"
                    + "\npackage::spec::{name: \"my/dep\"}\n"
                    "deps/my/dep/@/util/module.ion", "module::spec::{doc: \"Utilities.\"}\n"
                    "deps/my/dep/@/util/id.type.ion",
                    "public::spec::opaque::type::{doc: \"An identifier.\"}\n"
                    "pkg/my/pkg/basics/module.ion", "module::spec::{doc: \"Basics.\"}\n"
                    "pkg/my/pkg/basics/int.type.ion", "public::spec::opaque::type::{}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            header "specs" "my/pkg" "3.1.0"
                            "package::spec::{name: \"my/dep\", modules: [module::spec::{name: \"util\", doc: \"Utilities.\", types: [public::spec::opaque::type::{name: \"id\", doc: \"An identifier.\"}]}]}"
                            "module::spec::{name: \"basics\", doc: \"Basics.\", types: [public::spec::opaque::type::{name: \"int\"}]}"
                        ]
                    )

                expectSame (readTree files) single

            testCase "A v4 specs tree reads as the equivalent datagram"
            <| fun () ->
                let files = [
                    manifest,
                    header "specs" "my/pkg" "4.0.0"
                    + "\n"
                    "pkg/my/pkg/basics/module.ion", "module::spec::{doc: \"Basics.\"}\n"
                    "pkg/my/pkg/basics/int.type.ion", "public::spec::opaque::type::{}\n"
                    "pkg/my/pkg/basics/now.value.ion", $"public::spec::value::{{output: {int}}}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            header "specs" "my/pkg" "4.0.0"
                            $"module::spec::{{name: \"basics\", doc: \"Basics.\", types: [public::spec::opaque::type::{{name: \"int\"}}], values: [public::spec::value::{{name: \"now\", output: {int}}}]}}"
                        ]
                    )

                expectSame (readTree files) single

            testCase "A v4 application tree reads as the equivalent datagram"
            <| fun () ->
                let files = [
                    manifest,
                    header "application" "example" "4.0.0"
                    + "\npackage::def::{name: \"my-org/shared\"}\npackage::def::{name: \"empty\"}\n"
                    "deps/my-org/shared/@/util/module.ion", "public::def::module::{}\n"
                    "deps/my-org/shared/@/util/identity.value.ion",
                    $"public::def::value::{{outputType: {int}, body: 1}}\n"
                    "deps/my-org/shared/@/secret/module.ion",
                    "private::def::module::{doc: \"Hidden.\"}\n"
                    "pkg/example/main/module.ion", "public::def::module::{}\n"
                    "pkg/example/main/run.value.ion",
                    $"public::def::value::{{outputType: {int}, body: 2}}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            header "application" "example" "4.0.0"
                            $"package::def::{{name: \"my-org/shared\", modules: [public::def::module::{{name: \"util\", values: [public::def::value::{{name: \"identity\", outputType: {int}, body: 1}}]}}, private::def::module::{{name: \"secret\", doc: \"Hidden.\"}}]}}"
                            "package::def::{name: \"empty\"}"
                            $"public::def::module::{{name: \"main\", values: [public::def::value::{{name: \"run\", outputType: {int}, body: 2}}]}}"
                        ]
                    )

                expectSame (readTree files) single

            testCase "Files outside pkg and deps are ignored"
            <| fun () ->
                let files =
                    [
                        "README.md", "not ion"
                        "docs/guide.md", "not ion"
                        "elsewhere/other.ion", "not ion either"
                        "notes.10n", "not ion either"
                    ]
                    @ baseFiles

                expectSame (readTree files) (readTree baseFiles)

            testCase "A dependency directory the manifest does not name is refused"
            <| fun () ->
                let files = [
                    manifest,
                    libraryHeader "p"
                    + "\npackage::spec::{name: \"listed\"}\n"
                    "deps/unlisted/@/m/module.ion", "module::spec::{}\n"
                ]

                let error = refusal files
                expectCode "invalid_tree_path" (Some "deps/unlisted") error
                Expect.stringContains error.Message "deps/unlisted" "Names the directory"

                let nested = [
                    manifest, libraryHeader "p"
                    "deps/other/dep/@/m/module.ion", "module::spec::{}\n"
                ]

                let nestedError = refusal nested
                expectCode "invalid_tree_path" (Some "deps/other/dep") nestedError
                Expect.stringContains nestedError.Message "deps/other/dep" "Names the directory"

            testCase "A manifest package's inline modules merge with the modules under deps"
            <| fun () ->
                let files = [
                    manifest,
                    libraryHeader "p"
                    + "\npackage::spec::{name: \"other/dep\", modules: [module::spec::{name: \"inline\", doc: \"Inline.\"}, module::spec::{name: \"util\", types: [public::spec::opaque::type::{name: \"a\"}]}]}\n"
                    "deps/other/dep/@/util/id.type.ion", "public::spec::opaque::type::{}\n"
                    "deps/other/dep/@/extra/module.ion", "module::spec::{doc: \"Extra.\"}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            libraryHeader "p"
                            "package::spec::{name: \"other/dep\", modules: [module::spec::{name: \"inline\", doc: \"Inline.\"}, module::spec::{name: \"util\", types: [public::spec::opaque::type::{name: \"a\"}, public::spec::opaque::type::{name: \"id\"}]}, module::spec::{name: \"extra\", doc: \"Extra.\"}]}"
                        ]
                    )

                expectSame (readTree files) single

            testCase "An application package's inline modules merge with the modules under deps"
            <| fun () ->
                let files = [
                    manifest,
                    header "application" "example" "4.0.0"
                    + "\npackage::def::{name: \"my-org/shared\", modules: [public::def::module::{name: \"inline\"}]}\n"
                    "deps/my-org/shared/@/util/module.ion", "public::def::module::{}\n"
                    "deps/my-org/shared/@/util/identity.value.ion",
                    $"public::def::value::{{outputType: {int}, body: 1}}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            header "application" "example" "4.0.0"
                            $"package::def::{{name: \"my-org/shared\", modules: [public::def::module::{{name: \"inline\"}}, public::def::module::{{name: \"util\", values: [public::def::value::{{name: \"identity\", outputType: {int}, body: 1}}]}}]}}"
                        ]
                    )

                expectSame (readTree files) single
        ]

        testList "The path supplies names" [
            testCase "Name omitted"
            <| fun () ->
                let name, _, definition = v4Library (readTree baseFiles)
                Expect.equal name (PackageName(pathOf "example/finance")) "The package"

                Expect.equal
                    (typeNames definition "eligibility")
                    [ "score" ]
                    "The type is score in module eligibility"

            testCase "Name conflict"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, $"public::def::alias::type::{{name: \"other\", typeExp: {int}}}\n"
                        else
                            l, text
                    )

                expectCode "path_name_mismatch" (Some location) (refusal files)

            testCase "A stated name that agrees with the path is accepted"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, $"public::def::alias::type::{{name: \"score\", typeExp: {int}}}\n"
                        else
                            l, text
                    )

                expectSame (readTree files) (readTree baseFiles)

            testCase
                "Names that agree by spelling but differ in how they mark an initialism are the same name"
            <| fun () ->
                let files = [
                    manifest, libraryHeader "p"
                    "pkg/p/m/module.ion", "public::def::module::{}\n"
                    "pkg/p/m/value-in-_usd.type.ion",
                    $"public::def::alias::type::{{name: \"value-in-u-s-d\", typeExp: {int}}}\n"
                ]

                let _, _, definition = v4Library (readTree files)
                Expect.equal (typeNames definition "m") [ "value-in-USD" ] "One name"

            testCase "A stated module that disagrees with the path"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, $"public::def::alias::type::{{module: \"other\", typeExp: {int}}}\n"
                        else
                            l, text
                    )

                expectCode "path_name_mismatch" (Some location) (refusal files)

            testCase "A stated module name that disagrees with the path"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/module.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, "public::def::module::{name: \"other\"}\n"
                        else
                            l, text
                    )

                expectCode "path_name_mismatch" (Some location) (refusal files)

            testCase "A stated package that disagrees with the path"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/module.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, "public::def::module::{package: \"other\"}\n"
                        else
                            l, text
                    )

                expectCode "path_name_mismatch" (Some location) (refusal files)

            testCase "Cut stem"
            <| fun () ->
                let canonical = "some-extremely-long-canonical-type-name-that-overflows"

                let files =
                    baseFiles
                    @ [
                        "pkg/example/finance/eligibility/some-extremely-long-canon__6d98776c.type.ion",
                        $"public::def::alias::type::{{name: \"{canonical}\", typeExp: {int}}}\n"
                    ]

                let _, _, definition = v4Library (readTree files)

                Expect.contains
                    (typeNames definition "eligibility")
                    canonical
                    "The stated name is used"

            testCase "Cut stem without a name"
            <| fun () ->
                let location =
                    "pkg/example/finance/eligibility/some-extremely-long-canon__6d98776c.type.ion"

                let files =
                    baseFiles
                    @ [ location, $"public::def::alias::type::{{typeExp: {int}}}\n" ]

                expectCode "path_name_mismatch" (Some location) (refusal files)

            testCase "Cut stem with the wrong digest"
            <| fun () ->
                let location =
                    "pkg/example/finance/eligibility/some-extremely-long-canon__6d98776c.type.ion"

                let files =
                    baseFiles
                    @ [
                        location,
                        $"public::def::alias::type::{{name: \"some-extremely-long-canonical-type-name-that-overflowz\", typeExp: {int}}}\n"
                    ]

                expectCode "path_name_mismatch" (Some location) (refusal files)

            testCase "Cut stem with the wrong prefix"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/other-prefix__6d98776c.type.ion"

                let files =
                    baseFiles
                    @ [
                        location,
                        $"public::def::alias::type::{{name: \"some-extremely-long-canonical-type-name-that-overflows\", typeExp: {int}}}\n"
                    ]

                expectCode "path_name_mismatch" (Some location) (refusal files)

            testCase "Names that look like tree files"
            <| fun () ->
                let files = [
                    manifest, libraryHeader "example"
                    "pkg/example/manifest/module.ion", "public::def::module::{}\n"
                    "pkg/example/manifest/manifest.type.ion",
                    $"public::def::alias::type::{{typeExp: {int}}}\n"
                    "pkg/example/manifest/module.type.ion",
                    $"public::def::alias::type::{{typeExp: {int}}}\n"
                    "pkg/example/manifest/con_.type.ion",
                    $"public::def::alias::type::{{typeExp: {int}}}\n"
                    "pkg/example/manifest/module/module.ion", "public::def::module::{}\n"
                    "pkg/example/manifest/module/manifest.type.ion",
                    $"public::def::alias::type::{{typeExp: {int}}}\n"
                ]

                let _, _, definition = v4Library (readTree files)

                Expect.equal
                    (OrderedMap.keys definition.Modules
                     |> List.map (fun (ModulePath path) -> CanonicalNames.pathToString path)
                     |> List.sort)
                    [ "manifest"; "manifest/module" ]
                    "Module manifest and module manifest/module"

                Expect.equal
                    (typeNames definition "manifest"
                     |> List.sort)
                    [ "con"; "manifest"; "module" ]
                    "Type manifest, type module and the escaped type con_ read as con"

                Expect.equal
                    (typeNames definition "manifest/module")
                    [ "manifest" ]
                    "A type in the nested module"

            testCase "Module file directly under the package"
            <| fun () ->
                let location = "pkg/example/finance/module.ion"

                let files =
                    baseFiles
                    @ [ location, "public::def::module::{}\n" ]

                expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "A type file directly under the package is refused too"
            <| fun () ->
                let location = "pkg/example/finance/orphan.type.ion"

                let files =
                    baseFiles
                    @ [ location, $"public::def::alias::type::{{typeExp: {int}}}\n" ]

                expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "A file outside the package's directory is refused"
            <| fun () ->
                let location = "pkg/elsewhere/module.ion"

                let files =
                    baseFiles
                    @ [ location, "public::def::module::{}\n" ]

                expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "A dependency file with no @ segment is refused"
            <| fun () ->
                let location = "deps/other/dep/util/module.ion"

                let files =
                    baseFiles
                    @ [ location, "module::spec::{}\n" ]

                expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "A dependency module file directly under the @ segment is refused"
            <| fun () ->
                let location = "deps/other/dep/@/module.ion"

                let files =
                    baseFiles
                    @ [ location, "module::spec::{}\n" ]

                expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "A directory stem the escape never writes is refused"
            <| fun () ->
                for bad in [ "Eligibility"; "a_b"; "foo_"; "x__12345678" ] do
                    let location = $"pkg/example/finance/{bad}/module.ion"

                    let files =
                        baseFiles
                        @ [ location, "public::def::module::{}\n" ]

                    expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "An unknown Ion file under pkg is refused"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/notes.ion"

                let files =
                    baseFiles
                    @ [ location, "{}\n" ]

                expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "A module directory with no module file is refused"
            <| fun () ->
                let location = "pkg/example/finance/other/thing.type.ion"

                let files =
                    baseFiles
                    @ [ location, $"public::def::alias::type::{{typeExp: {int}}}\n" ]

                let error = refusal files
                Expect.equal error.Code "invalid_document" "The code"
                Expect.equal error.Path (Some location) "Names the first file of the directory"

            testCase "A file under pkg or deps that does not fit the layout is refused"
            <| fun () ->
                let strays = [
                    "pkg/example/finance/eligibility/notes.md"
                    "pkg/example/finance/eligibility/stray.json"
                    "pkg/example/finance/eligibility/score.type.json"
                    "pkg/example/finance/eligibility/notes.txt"
                    "pkg/example/finance/eligibility/module.10n"
                    "pkg/example/finance/eligibility/extra.10n"
                    "pkg/README.txt"
                    "deps/other/dep/@/util/notes.md"
                    "deps/other/dep/@/util/module.10n"
                    "deps/README.md"
                ]

                for location in strays do
                    let files =
                        baseFiles
                        @ [ location, "not ion" ]

                    let error = refusal files
                    expectCode "invalid_tree_path" (Some location) error
                    Expect.stringContains error.Message "tree file" "Says what a tree file is"

            testCase "A path segment that is empty, . or .. is refused"
            <| fun () ->
                let locations = [
                    "pkg/./example/finance/eligibility/module.ion"
                    "pkg//example/finance/eligibility/module.ion"
                    "pkg/example/finance/eligibility/../eligibility/module.ion"
                    "../pkg/example/finance/eligibility/module.ion"
                    "docs/../README.md"
                    "./README.md"
                    "deps/other/dep/@/./util/module.ion"
                ]

                for location in locations do
                    // The tree is flat, so each file keeps the location it was given.
                    let tree =
                        [
                            VFile.ofText manifest (libraryHeader "example/finance")
                            VFile.ofText location "public::def::module::{}\n"
                        ]
                        |> VFileTree.fromFiles

                    match IonIrTree.read limits tree with
                    | Ok _ -> failtestf "%s should be refused" location
                    | Result.Error error -> expectCode "invalid_tree_path" (Some location) error

            testCase "A nested directory whose path is . adds no segment, as in VFileTree"
            <| fun () ->
                let flat = readTree baseFiles

                let nested =
                    let files =
                        baseFiles
                        |> List.map (fun (location, text) -> VFile.ofText location text)

                    VFileTree.empty
                    |> VFileTree.addDirectory (
                        files
                        |> List.fold
                            (fun directory file -> VFileTree.addFile file directory)
                            VFileTree.empty
                    )

                match IonIrTree.read limits nested with
                | Ok document -> expectSame document flat
                | Result.Error error -> failtestf "The tree was refused: %A" error

            testCase "A backslash is not a separator, so it fails the stem"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/a\\b.type.ion"

                // The tree is flat, so the file keeps the location it was given.
                let tree =
                    baseFiles
                    @ [ location, $"public::def::alias::type::{{typeExp: {int}}}\n" ]
                    |> List.map (fun (l, text) -> VFile.ofText l text)
                    |> VFileTree.fromFiles

                match IonIrTree.read limits tree with
                | Ok _ -> failtest "The file name is not a stem"
                | Result.Error error -> expectCode "invalid_tree_path" (Some location) error

            testCase "Only the bare @ may follow a dependency's package path"
            <| fun () ->
                for segment in [ "@1.0.0"; "@v2"; "@latest" ] do
                    let location = $"deps/other/dep/{segment}/util/module.ion"

                    let files =
                        baseFiles
                        @ [ location, "module::spec::{}\n" ]

                    expectCode "invalid_tree_path" (Some location) (refusal files)

            testCase "An element with a tag the reader does not know is refused"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/module.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, "public::def::module::{}\nmystery::thing::{}\n"
                        else
                            l, text
                    )

                expectCode "invalid_document" (Some location) (refusal files)
        ]

        testList "Module files follow their directory's rules" [
            testCase "Second own module"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/module.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, "public::def::module::{}\npublic::def::module::{}\n"
                        else
                            l, text
                    )

                expectCode "duplicate_name" (Some location) (refusal files)

            testCase "A second own module::spec in a specs tree"
            <| fun () ->
                let location = "pkg/my/pkg/basics/module.ion"

                let files = [
                    manifest, header "specs" "my/pkg" "4.0.0"
                    location, "module::spec::{}\nmodule::spec::{}\n"
                ]

                expectCode "duplicate_name" (Some location) (refusal files)

            testCase "Dependency fragments merge"
            <| fun () ->
                let files = [
                    manifest,
                    libraryHeader "p"
                    + "\npackage::spec::{name: \"other/dep\"}\n"
                    "deps/other/dep/@/util/module.ion",
                    "module::spec::{types: [public::spec::opaque::type::{name: \"a\"}]}\nmodule::spec::{doc: \"Utilities.\", values: [public::spec::value::{name: \"v\", output: \"morphir/SDK:basics#int\"}]}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            libraryHeader "p"
                            $"package::spec::{{name: \"other/dep\", modules: [module::spec::{{name: \"util\", doc: \"Utilities.\", types: [public::spec::opaque::type::{{name: \"a\"}}], values: [public::spec::value::{{name: \"v\", output: {int}}}]}}]}}"
                        ]
                    )

                expectSame (readTree files) single

            testCase "Dependency fragments, type files and a missing module file merge"
            <| fun () ->
                let files = [
                    manifest,
                    libraryHeader "p"
                    + "\npackage::spec::{name: \"other/dep\"}\n"
                    "deps/other/dep/@/util/id.type.ion", "public::spec::opaque::type::{}\n"
                ]

                let single =
                    readSingle (
                        datagram [
                            libraryHeader "p"
                            "package::spec::{name: \"other/dep\", modules: [module::spec::{name: \"util\", types: [public::spec::opaque::type::{name: \"id\"}]}]}"
                        ]
                    )

                expectSame (readTree files) single

            testCase "Member in two files"
            <| fun () ->
                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = "pkg/example/finance/eligibility/module.ion" then
                            l,
                            $"public::def::module::{{}}\npublic::def::alias::type::{{name: \"score\", typeExp: {int}}}\n"
                        else
                            l, text
                    )

                let error = refusal files
                Expect.equal error.Code "duplicate_name" "The code"

                Expect.equal
                    error.Path
                    (Some "pkg/example/finance/eligibility/score.type.ion")
                    "The second statement is the one refused"

                Expect.stringContains error.Message "score" "Names the type"

            testCase "A member stated by two dependency fragments"
            <| fun () ->
                let location = "deps/other/dep/@/util/module.ion"

                let files = [
                    manifest,
                    libraryHeader "p"
                    + "\npackage::spec::{name: \"other/dep\"}\n"
                    location,
                    "module::spec::{types: [public::spec::opaque::type::{name: \"a\"}]}\nmodule::spec::{types: [public::spec::opaque::type::{name: \"a\"}]}\n"
                ]

                expectCode "duplicate_name" (Some location) (refusal files)

            testCase "A value in a type file is refused"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, $"public::def::value::{{outputType: {int}, body: 1}}\n"
                        else
                            l, text
                    )

                expectCode "invalid_document" (Some location) (refusal files)

            testCase "Two elements in a type file are refused"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l,
                            $"public::def::alias::type::{{typeExp: {int}}}\npublic::def::alias::type::{{typeExp: {int}}}\n"
                        else
                            l, text
                    )

                expectCode "invalid_document" (Some location) (refusal files)

            testCase "An empty type file is refused"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) -> if l = location then l, "" else l, text)

                expectCode "invalid_document" (Some location) (refusal files)

            testCase "A manifest that does not start with the header is refused"
            <| fun () ->
                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = manifest then
                            l, "package::spec::{name: \"x\"}\n"
                        else
                            l, text
                    )

                expectCode "invalid_document" (Some manifest) (refusal files)

            testCase "A manifest holds only the header and dependencies"
            <| fun () ->
                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = manifest then
                            l,
                            text
                            + "public::def::module::{name: \"m\"}\n"
                        else
                            l, text
                    )

                expectCode "invalid_document" (Some manifest) (refusal files)
        ]

        testList "Errors name the failing file" [
            testCase "Malformed file"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/rate.value.ion"

                let files =
                    baseFiles
                    @ [ location, "public::def::value::{" ]

                expectCode "malformed_ion" (Some location) (refusal files)

            testCase "A problem inside a file is placed in that file"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, $"public::def::alias::type::{{typeExp: {int}, bogus: 1}}\n"
                        else
                            l, text
                    )

                let error = refusal files
                Expect.equal error.Code "unknown_member" "The code"
                Expect.equal error.Path (Some location) "The file"

                Expect.equal
                    error.Location
                    (Some "$[0].bogus")
                    "The place in that file, counting from the file's own first value"

            testCase "A problem in a module file's later element is placed in that element"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/module.ion"

                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l,
                            $"public::def::module::{{}}\npublic::def::alias::type::{{name: \"t\", typeExp: {int}, bogus: 1}}\n"
                        else
                            l, text
                    )

                let error = refusal files
                Expect.equal error.Code "unknown_member" "The code"
                Expect.equal error.Path (Some location) "The file"
                Expect.equal error.Location (Some "$[1].bogus") "The second value of the file"

            testCase "A problem in the manifest header"
            <| fun () ->
                let files =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = manifest then
                            l,
                            "morphir::{formatVersion: \"4.0.0\", kind: nonsense, packageName: \"p\"}\n"
                        else
                            l, text
                    )

                let error = refusal files
                Expect.equal error.Path (Some manifest) "The manifest"
                Expect.equal error.Location (Some "$[0].kind") "The header member"

            testCase "A problem in a nested file of an application dependency"
            <| fun () ->
                let location = "deps/my-org/shared/@/util/identity.value.ion"

                let files = [
                    manifest,
                    header "application" "example" "4.0.0"
                    + "\npackage::def::{name: \"my-org/shared\"}\n"
                    "deps/my-org/shared/@/util/module.ion", "public::def::module::{}\n"
                    location, $"public::def::value::{{outputType: {int}, body: 1, bogus: 1}}\n"
                ]

                let error = refusal files
                Expect.equal error.Code "unknown_member" "The code"
                Expect.equal error.Path (Some location) "The file"

            testCase
                "A problem in an inline member is placed in it when other files add to the list"
            <| fun () ->
                let location = "pkg/p/m/module.ion"

                let inlineTypes =
                    $"public::def::module::{{types: [public::def::alias::type::{{name: \"a\", typeExp: {int}}}, public::def::alias::type::{{name: \"b\", typeExp: {int}, bogus: 1}}]}}\n"

                let typeFiles = [
                    manifest, libraryHeader "p"
                    location, inlineTypes
                    "pkg/p/m/c.type.ion", $"public::def::alias::type::{{typeExp: {int}}}\n"
                ]

                let typeError = refusal typeFiles
                Expect.equal typeError.Code "unknown_member" "The code"
                Expect.equal typeError.Path (Some location) "The file"
                Expect.equal typeError.Location (Some "$[0].types[1].bogus") "The inline member"

                let inlineValues =
                    $"public::def::module::{{values: [public::def::value::{{name: \"v\", outputType: {int}, body: 1, bogus: 1}}]}}\n"

                let valueFiles = [
                    manifest, libraryHeader "p"
                    location, inlineValues
                    "pkg/p/m/w.value.ion", $"public::def::value::{{outputType: {int}, body: 1}}\n"
                ]

                let valueError = refusal valueFiles
                Expect.equal valueError.Path (Some location) "The file"
                Expect.equal valueError.Location (Some "$[0].values[0].bogus") "The inline member"

                // A member that another file adds is still placed in its own file.
                let addedFiles = [
                    manifest, libraryHeader "p"
                    location, inlineTypes.Replace(", bogus: 1", "")
                    "pkg/p/m/c.type.ion",
                    $"public::def::alias::type::{{typeExp: {int}, bogus: 1}}\n"
                ]

                let addedError = refusal addedFiles
                Expect.equal addedError.Path (Some "pkg/p/m/c.type.ion") "The added file"
                Expect.equal addedError.Location (Some "$[0].bogus") "The added member"

            testCase "A file that is deep for the caller's stack reads in a tree as it does alone"
            <| fun () ->
                let deepLimits = { limits with MaxDepth = 4_000 }
                let levels = 2_000

                let body =
                    String.replicate levels "(list "
                    + "1"
                    + String.replicate levels ")"

                let files = [
                    manifest, libraryHeader "p"
                    "pkg/p/m/module.ion", "public::def::module::{}\n"
                    "pkg/p/m/deep.value.ion",
                    $"public::def::value::{{outputType: {int}, body: {body}}}\n"
                ]

                // The deep file is read last, and the limit is the tree's exact size. A budget that the
                // short-stack attempt had spent would refuse the second attempt.
                let exact =
                    { deepLimits with
                        MaxDecodedBytes =
                            files
                            |> List.sumBy (fun (_, text) -> Text.Encoding.UTF8.GetByteCount text)
                    }

                let onSmallStack work =
                    let result = ref None

                    let thread =
                        Threading.Thread((fun () -> result.Value <- Some(work ())), 256 * 1024)

                    thread.Start()
                    thread.Join()
                    Option.get result.Value

                let tree = treeOf files

                let single =
                    onSmallStack (fun () ->
                        IonIr.read
                            deepLimits
                            (Fixtures.utf8 (
                                datagram [
                                    libraryHeader "p"
                                    $"public::def::module::{{name: \"m\", values: [public::def::value::{{name: \"deep\", outputType: {int}, body: {body}}}]}}"
                                ]
                            ))
                    )

                let inTree = onSmallStack (fun () -> IonIrTree.read exact tree)

                // The values are 2,000 levels deep, so they are not compared as a whole: a comparison recurses too.
                let valueNames document =
                    let _, _, definition = v4Library document

                    match OrderedMap.tryFind (ModulePath(pathOf "m")) definition.Modules with
                    | Some found ->
                        OrderedMap.keys found.Value.Values
                        |> List.map CanonicalNames.nameToString
                    | None -> failtest "No module m"

                match single, inTree with
                | Ok alone, Ok found ->
                    Expect.equal
                        (valueNames found)
                        [ "deep" ]
                        "The deep value is in the tree's module"

                    Expect.equal (valueNames found) (valueNames alone) "As the single file reads"
                | _, Result.Error error ->
                    failtestf "The tree was refused: %s %s" error.Code error.Message
                | Result.Error error, _ -> failtestf "The single file was refused: %s" error.Code

            testCase "Limit across files"
            <| fun () ->
                let files = [
                    manifest, libraryHeader "p"
                    "pkg/p/m/module.ion", "public::def::module::{}\n"
                    "pkg/p/m/a.type.ion", $"public::def::alias::type::{{typeExp: {int}}}\n"
                    "pkg/p/m/b.type.ion", $"public::def::alias::type::{{typeExp: {int}}}\n"
                ]

                let counts =
                    files
                    |> List.map (fun (_, text) ->
                        match IonReader.read limits (Fixtures.utf8 text) with
                        | Ok values ->
                            values
                            |> List.sumBy nodes
                        | Result.Error error -> failtestf "%A" error
                    )

                let largest = List.max counts
                let total = List.sum counts
                Expect.isLessThan largest total "Several files"

                // Each file fits the limit; together they do not.
                let tight = { limits with MaxNodes = largest }
                Expect.isOk (IonIrTree.read limits (treeOf files)) "The default limit holds"

                match IonIrTree.read tight (treeOf files) with
                | Ok _ -> failtest "The tree's total is over the limit"
                | Result.Error error ->
                    Expect.equal error.Code "node_limit_exceeded" "The tree's total counts"

            testCase "A byte limit also covers the whole tree"
            <| fun () ->
                let size (text: string) = Text.Encoding.UTF8.GetByteCount text

                let largest =
                    baseFiles
                    |> List.map (
                        snd
                        >> size
                    )
                    |> List.max

                let tight =
                    { limits with
                        MaxDecodedBytes = largest
                    }

                match IonIrTree.read tight (treeOf baseFiles) with
                | Ok _ -> failtest "The tree's total is over the limit"
                | Result.Error error -> Expect.equal error.Code "decoded_limit_exceeded" "The code"

            testCase "Lazy sources"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"
                let cause = IOException "the disk went away"

                let failing = VFile.ofStream location (StreamSource(fun () -> raise cause))

                let tree =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then l, failing else l, VFile.ofText l text
                    )
                    |> Map.ofList
                    |> VFileTree.fromFileMap

                match IonIrTree.readDetailed limits tree with
                | Ok _ -> failtest "The source failed"
                | Result.Error(SourceFailed(path, found)) ->
                    Expect.equal path location "The file"
                    Expect.isTrue (obj.ReferenceEquals(found, cause)) "The original cause"
                | Result.Error other -> failtestf "Expected a source failure, got %A" other

                match IonIrTree.read limits tree with
                | Ok _ -> failtest "The source failed"
                | Result.Error error ->
                    Expect.equal error.Code "file_read_failed" "The code"
                    Expect.equal error.Path (Some location) "The file"
                    Expect.stringContains error.Message "the disk went away" "The cause"

            testCase "A stream that fails while it is read"
            <| fun () ->
                let location = "pkg/example/finance/eligibility/score.type.ion"
                let cause = IOException "the stream broke"

                let broken =
                    { new Stream() with
                        member _.CanRead = true
                        member _.CanSeek = false
                        member _.CanWrite = false
                        member _.Length = raise (NotSupportedException())

                        member _.Position
                            with get () = raise (NotSupportedException())
                            and set _ = raise (NotSupportedException())

                        member _.Flush() = ()
                        member _.Read(_, _, _) = raise cause
                        member _.Seek(_, _) = raise (NotSupportedException())
                        member _.SetLength _ = raise (NotSupportedException())
                        member _.Write(_, _, _) = raise (NotSupportedException())
                    }

                let tree =
                    baseFiles
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, VFile.ofStream l (StreamSource(fun () -> broken))
                        else
                            l, VFile.ofText l text
                    )
                    |> Map.ofList
                    |> VFileTree.fromFileMap

                match IonIrTree.readDetailed limits tree with
                | Result.Error(SourceFailed(path, found)) ->
                    Expect.equal path location "The file"
                    Expect.isTrue (obj.ReferenceEquals(found, cause)) "The original cause"
                | other -> failtestf "Expected a source failure, got %A" other

            testCase "A source that is never needed is never opened"
            <| fun () ->
                let opened = ref 0
                let manifestLocation = manifest

                let counted location =
                    VFile.ofStream
                        location
                        (StreamSource(fun () ->
                            opened.Value <-
                                opened.Value
                                + 1

                            new MemoryStream([||]) :> Stream
                        ))

                let tree =
                    [
                        manifestLocation, VFile.ofText manifestLocation "morphir::{"
                        "pkg/example/finance/eligibility/module.ion",
                        counted "pkg/example/finance/eligibility/module.ion"
                        "pkg/example/finance/eligibility/score.type.ion",
                        counted "pkg/example/finance/eligibility/score.type.ion"
                    ]
                    |> Map.ofList
                    |> VFileTree.fromFileMap

                match IonIrTree.read limits tree with
                | Ok _ -> failtest "The manifest is malformed"
                | Result.Error error -> expectCode "malformed_ion" (Some manifestLocation) error

                Expect.equal opened.Value 0 "Nothing else was opened"

            testCase "A path problem in a dependency is found before any file is opened"
            <| fun () ->
                let opened = ref 0

                let tree =
                    [
                        manifest,
                        VFile.ofStream
                            manifest
                            (StreamSource(fun () ->
                                opened.Value <-
                                    opened.Value
                                    + 1

                                new MemoryStream([||]) :> Stream
                            ))
                        "deps/other/dep/util/module.ion",
                        VFile.ofText "module.ion" "module::spec::{}"
                    ]
                    |> Map.ofList
                    |> VFileTree.fromFileMap

                match IonIrTree.read limits tree with
                | Ok _ -> failtest "The path is misplaced"
                | Result.Error error ->
                    expectCode "invalid_tree_path" (Some "deps/other/dep/util/module.ion") error

                Expect.equal opened.Value 0 "No file was opened"

            testCase "A binary Ion file is read like a text one"
            <| fun () ->
                // The bytes are the Ion 1.0 binary marker and nothing after it, an empty datagram. A manifest with no
                // header is refused for that, which shows that the bytes were read as Ion and not as text.
                let binaryEmpty = ReadOnlyMemory<byte>([| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |])

                let tree =
                    [ manifest, VFile.ofBytes manifest binaryEmpty ]
                    |> Map.ofList
                    |> VFileTree.fromFileMap

                match IonIrTree.read limits tree with
                | Ok _ -> failtest "An empty manifest has no header"
                | Result.Error error -> expectCode "invalid_document" (Some manifest) error
        ]
    ]
