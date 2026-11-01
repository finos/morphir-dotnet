module Morphir.IR.Pipeline.Tests.FileTreeTests

open System.IO
open Expecto
open Morphir.IR.Pipeline

/// Keys of a file map, in map order.
let private mapKeys (fileMap: Map<string, VFile>) =
    fileMap |> Map.toList |> List.map fst

/// Runs a test in a new temporary directory and removes the directory afterwards.
let private withTempDir (run: string -> unit) =
    let temp = Directory.CreateTempSubdirectory("morphir-filetree-").FullName

    try
        run temp
    finally
        Directory.Delete(temp, true)

[<Tests>]
let treeConfigTests =
    testList "TreeConfig" [
        test "empty should create config with all None values" {
            let config = TreeConfig.empty
            
            Expect.isNone config.Name "name should be None"
            Expect.isNone config.Version "version should be None"
            Expect.isNone config.OutputFormat "output format should be None"
            Expect.equal (config.Extensions.Count) 0 "extensions should be empty"
        }

        test "create should set name and version" {
            let config = TreeConfig.create "MyProject" "1.0.0"
            
            Expect.equal config.Name (Some "MyProject") "name should be set"
            Expect.equal config.Version (Some "1.0.0") "version should be set"
            Expect.isNone config.OutputFormat "output format should be None"
        }
    ]

[<Tests>]
let vfileTreeConstructorTests =
    testList "VFileTree Constructors" [
        test "empty should create tree with default values" {
            let tree = VFileTree.empty
            
            Expect.equal tree.Path "." "path should be '.'"
            Expect.isEmpty tree.Content "content should be empty"
            Expect.equal (tree.Metadata.Count) 0 "metadata should be empty"
            Expect.equal tree.Config TreeConfig.empty "config should be empty"
        }

        test "create should set path and config" {
            let config = TreeConfig.create "MyProject" "1.0.0"
            let tree = VFileTree.create "/project" config
            
            Expect.equal tree.Path "/project" "path should be '/project'"
            Expect.isEmpty tree.Content "content should be empty"
            Expect.equal tree.Config config "config should match"
        }

        test "fromFiles should build flat tree from file list" {
            let files = [
                VFile.create "file1.fs" "code1"
                VFile.create "file2.fs" "code2"
            ]
            
            let tree = VFileTree.fromFiles files
            
            Expect.equal (VFileTree.fileCount tree) 2 "should have 2 files"
            Expect.equal (VFileTree.directoryCount tree) 0 "should have 0 directories"
        }
    ]

[<Tests>]
let vfileTreeMutationTests =
    testList "VFileTree Mutations" [
        test "addFile should add file to tree" {
            let file = VFile.create "test.fs" "let x = 42"
            let tree = VFileTree.empty |> VFileTree.addFile file

            Expect.equal (VFileTree.fileCount tree) 1 "should have 1 file"
            Expect.hasLength (VFileTree.allFiles tree) 1 "allFiles should have 1 element"
            
            let allFiles = VFileTree.allFiles tree
            Expect.equal allFiles.[0] file "file should match"
        }

        test "addDirectory should add subdirectory to tree" {
            let subTree = VFileTree.create "subdir" TreeConfig.empty
            let tree = VFileTree.empty |> VFileTree.addDirectory subTree

            Expect.equal (VFileTree.directoryCount tree) 1 "should have 1 directory"
        }

        test "addFile multiple times should accumulate files" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            let file3 = VFile.create "file3.fs" "code3"
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2
                |> VFileTree.addFile file3

            Expect.equal (VFileTree.fileCount tree) 3 "should have 3 files"
        }

        test "nested directories should count correctly" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            
            let subSubTree =
                VFileTree.create "subsubdir" TreeConfig.empty
                |> VFileTree.addFile file2
            
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addDirectory subSubTree
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree

            Expect.equal (VFileTree.fileCount tree) 2 "should have 2 files total"
            Expect.equal (VFileTree.directoryCount tree) 2 "should have 2 directories total"
        }
    ]

[<Tests>]
let vfileTreeQueryTests =
    testList "VFileTree Queries" [
        test "allFiles should flatten nested tree" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            let file3 = VFile.create "file3.fs" "code3"
            
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile file2
                |> VFileTree.addFile file3
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree

            let allFiles = VFileTree.allFiles tree
            
            Expect.hasLength allFiles 3 "should have 3 files total"
        }

        test "findFile should find file by path" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2

            let found = VFileTree.findFile "file2.fs" tree
            
            Expect.isSome found "should find file2.fs"
            Expect.equal found (Some file2) "found file should match file2"
        }

        test "findFile should return None for missing file" {
            let file1 = VFile.create "file1.fs" "code1"
            
            let tree = VFileTree.empty |> VFileTree.addFile file1

            let found = VFileTree.findFile "missing.fs" tree
            
            Expect.isNone found "should not find missing file"
        }

        test "findFile should find file in nested directory" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "nested.fs" "code2"
            
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile file2
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree

            let found = VFileTree.findFile "nested.fs" tree
            
            Expect.isSome found "should find nested.fs"
            Expect.equal found (Some file2) "found file should match file2"
        }

        test "hasErrors should return false for tree with no errors" {
            let file1 = VFile.create "file1.fs" "code1" |> VFile.info "Info message"
            let file2 = VFile.create "file2.fs" "code2" |> VFile.warn "Warning" None
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2

            Expect.isFalse (VFileTree.hasErrors tree) "should not have errors"
        }

        test "hasErrors should return true for tree with errors" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2" |> VFile.error "Error" None
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2

            Expect.isTrue (VFileTree.hasErrors tree) "should have errors"
        }

        test "hasErrors should return true for nested errors" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2" |> VFile.fail "Fatal" None
            
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile file2
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree

            Expect.isTrue (VFileTree.hasErrors tree) "should have errors from nested file"
        }

        test "collectMessages should gather all messages" {
            let file1 =
                VFile.create "file1.fs" "code1"
                |> VFile.info "Info 1"
                |> VFile.warn "Warning 1" None
            
            let file2 =
                VFile.create "file2.fs" "code2"
                |> VFile.error "Error 1" None
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2

            let messages = VFileTree.collectMessages tree
            
            Expect.hasLength messages 3 "should have 3 messages total"
        }
    ]

[<Tests>]
let vfileTreeTransformationTests =
    testList "VFileTree Transformations" [
        test "map should transform all files" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2

            let transformed = tree |> VFileTree.map (fun f -> f |> VFile.info "Processed")

            let allFiles = VFileTree.allFiles transformed
            Expect.all allFiles (fun f -> f.Messages.Length = 1) "all files should have 1 message"
        }

        test "map should work with nested trees" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile file2
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree

            let transformed = tree |> VFileTree.map (fun f -> f |> VFile.info "Processed")

            let allFiles = VFileTree.allFiles transformed
            Expect.hasLength allFiles 2 "should have 2 files"
            Expect.all allFiles (fun f -> f.Messages.Length = 1) "all files should have 1 message"
        }

        test "filter should remove files that don't match predicate" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.txt" "text"
            let file3 = VFile.create "file3.fs" "code3"
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2
                |> VFileTree.addFile file3

            let filtered =
                tree
                |> VFileTree.filter (fun f ->
                    match f.Path with
                    | Some p -> p.EndsWith(".fs")
                    | None -> false)

            Expect.equal (VFileTree.fileCount filtered) 2 "should have 2 .fs files"
        }

        test "updateFile should update specific file by path" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2

            let updated =
                tree
                |> VFileTree.updateFile "file2.fs" (fun f -> f |> VFile.warn "Updated" None)

            let file2Updated = VFileTree.findFile "file2.fs" updated
            
            match file2Updated with
            | Some f -> Expect.hasLength f.Messages 1 "updated file should have 1 message"
            | None -> failtest "file2.fs should be found"
            
            let file1Unchanged = VFileTree.findFile "file1.fs" updated
            
            match file1Unchanged with
            | Some f -> Expect.isEmpty f.Messages "file1.fs should have no messages"
            | None -> failtest "file1.fs should be found"
        }
    ]

[<Tests>]
let vfileTreeConversionTests =
    testList "VFileTree Conversions" [
        test "toFileMap keys should be forward-slash paths relative to the tree root" {
            let subSubTree =
                VFileTree.create "deep" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "file3.fs" "code3")

            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "file2.fs" "code2")
                |> VFileTree.addDirectory subSubTree

            let tree =
                VFileTree.empty
                |> VFileTree.addFile (VFile.create "file1.fs" "code1")
                |> VFileTree.addDirectory subTree

            Expect.equal
                (mapKeys (VFileTree.toFileMap tree))
                [ "file1.fs"; "subdir/deep/file3.fs"; "subdir/file2.fs" ]
                "keys should be relative and use forward slashes"
        }

        test "toFileMap keys should exclude the root tree path" {
            let tree =
                VFileTree.create "/project/MyProject.fsproj" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "file1.fs" "code1")

            Expect.equal (mapKeys (VFileTree.toFileMap tree)) [ "file1.fs" ] "root path should not appear in keys"
        }

        test "toFileMap should normalize backslashes and dot segments in file paths" {
            let tree =
                VFileTree.empty
                |> VFileTree.addFile (VFile.create @".\src\.\a.fs" "code")

            Expect.equal (mapKeys (VFileTree.toFileMap tree)) [ "src/a.fs" ] "key should be normalized"
        }

        test "toFileMap should omit files that have no path" {
            let tree =
                VFileTree.empty
                |> VFileTree.addFile (VFile.fromContent "orphan")
                |> VFileTree.addFile (VFile.create "file1.fs" "code1")

            Expect.equal (mapKeys (VFileTree.toFileMap tree)) [ "file1.fs" ] "pathless file has no key"
        }

        test "toFileMap should keep rooted file paths rather than rebasing them" {
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "/abs/a.fs" "code")

            let tree = VFileTree.empty |> VFileTree.addDirectory subTree

            Expect.equal (mapKeys (VFileTree.toFileMap tree)) [ "/abs/a.fs" ] "rooted path should be kept"
        }

        test "fromFileMap should infer nested directories from key segments" {
            let fileMap =
                Map.ofList [
                    "README.md", VFile.create "README.md" "readme"
                    "src/Types.fs", VFile.create "src/Types.fs" "types"
                    "src/Domain/Orders.fs", VFile.create "src/Domain/Orders.fs" "orders"
                ]

            let tree = VFileTree.fromFileMap fileMap

            Expect.equal tree.Path "." "root path should be '.'"
            Expect.equal (VFileTree.fileCount tree) 3 "should keep all files"
            Expect.equal (VFileTree.directoryCount tree) 2 "should create src and src/Domain"

            match tree.Content with
            | [ File readme; Directory src ] ->
                Expect.equal readme.Path (Some "README.md") "root file comes first"
                Expect.equal src.Path "src" "first level directory"

                match src.Content with
                | [ File types; Directory domain ] ->
                    Expect.equal types.Path (Some "Types.fs") "file path should be relative to its directory"
                    Expect.equal domain.Path "Domain" "second level directory"

                    match domain.Content with
                    | [ File orders ] ->
                        Expect.equal orders.Path (Some "Orders.fs") "file path should be relative to its directory"
                    | other -> failtestf "unexpected Domain content: %A" other
                | other -> failtestf "unexpected src content: %A" other
            | other -> failtestf "unexpected root content: %A" other
        }

        test "fromFileMap should split keys on backslashes" {
            let tree =
                Map.ofList [ @"src\a.fs", VFile.fromContent "code" ]
                |> VFileTree.fromFileMap

            Expect.equal (mapKeys (VFileTree.toFileMap tree)) [ "src/a.fs" ] "backslash should separate directories"
            Expect.equal (VFileTree.directoryCount tree) 1 "should create src"
        }

        test "fromFileMap should round trip string content with toStringMap" {
            let tree =
                Map.ofList [
                    "a.fs", VFile.create "a.fs" "A"
                    "x/b.fs", VFile.create "x/b.fs" "B"
                    "x/y/c.fs", VFile.create "x/y/c.fs" "C"
                ]
                |> VFileTree.fromFileMap

            Expect.equal
                (VFileTree.toStringMap tree)
                (Map.ofList [ "a.fs", "A"; "x/b.fs", "B"; "x/y/c.fs", "C" ])
                "keys and content should survive the round trip"
        }

        test "fromFileMap should use the key as the location of a pathless file" {
            let tree =
                Map.ofList [ "x/b.fs", VFile.fromContent "B" ]
                |> VFileTree.fromFileMap

            Expect.equal (VFileTree.toStringMap tree) (Map.ofList [ "x/b.fs", "B" ]) "key should give the location"
        }

        test "fromFileMap should prefer the key over a different VFile path" {
            let tree =
                Map.ofList [ "x/b.fs", VFile.create "other/name.fs" "B" ]
                |> VFileTree.fromFileMap

            Expect.equal (VFileTree.toStringMap tree) (Map.ofList [ "x/b.fs", "B" ]) "key should win"
        }

        test "fromFileMap should keep rooted keys at the top of the tree" {
            let tree =
                Map.ofList [ "/abs/a.fs", VFile.fromContent "A" ]
                |> VFileTree.fromFileMap

            Expect.equal (VFileTree.directoryCount tree) 0 "rooted key should not become directories"
            Expect.equal (VFileTree.toStringMap tree) (Map.ofList [ "/abs/a.fs", "A" ]) "rooted key should survive"
        }

        test "fromFileMap should record the relocation in file history and keep diagnostics" {
            let original =
                VFile.create "src/a.fs" "code"
                |> VFile.warn "careful" None
                |> VFile.setData "key" (box "value")

            let tree = Map.ofList [ "src/a.fs", original ] |> VFileTree.fromFileMap

            match VFileTree.allFiles tree with
            | [ file ] ->
                Expect.equal file.Path (Some "a.fs") "path should be relative to src"
                Expect.equal file.History [ "src/a.fs"; "a.fs" ] "history should record the move"
                Expect.hasLength file.Messages 1 "messages should be kept"
                Expect.equal (VFile.getData "key" file) (Some (box "value")) "data should be kept"
            | other -> failtestf "expected one file, got %A" other
        }

        test "fromFileMap should leave a file unchanged when its key names no location" {
            let file = VFile.create "a.fs" "A"
            let tree = Map.ofList [ ".", file ] |> VFileTree.fromFileMap

            Expect.equal (VFileTree.allFiles tree) [ file ] "file should stay at the top with its own path"
        }

        test "fromFileMap should build an empty tree from an empty map" {
            let tree = VFileTree.fromFileMap Map.empty

            Expect.isEmpty tree.Content "content should be empty"
        }

        test "toStringMap should flatten nested files to exact keys and content" {
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "file2.fs" "code2")

            let tree =
                VFileTree.empty
                |> VFileTree.addFile (VFile.create "file1.fs" "code1")
                |> VFileTree.addDirectory subTree

            Expect.equal
                (VFileTree.toStringMap tree)
                (Map.ofList [ "file1.fs", "code1"; "subdir/file2.fs", "code2" ])
                "should match the morphir-elm Dict FilePath FileContent shape"
        }

        test "toStringMap should stringify non-string content and default missing content to empty" {
            let tree =
                VFileTree.empty
                |> VFileTree.addFile (VFile.create "number.txt" 42)
                |> VFileTree.addFile (VFile.fromPath "missing.txt")

            Expect.equal
                (VFileTree.toStringMap tree)
                (Map.ofList [ "missing.txt", ""; "number.txt", "42" ])
                "content should be converted to text"
        }
    ]

[<Tests>]
let vfileTreePathAddressingTests =
    /// A tree holding two files named a.fs, with the nested one first.
    let treeWithTwoSameNamedFiles =
        let subTree =
            VFileTree.create "subdir" TreeConfig.empty
            |> VFileTree.addFile (VFile.create "a.fs" "nested")

        VFileTree.empty
        |> VFileTree.addDirectory subTree
        |> VFileTree.addFile (VFile.create "a.fs" "top")

    let contentOf (file: VFile option) =
        file |> Option.bind (fun f -> f.Content) |> Option.map string

    testList "VFileTree Path Addressing" [
        test "findFile should accept a path qualified by its directories" {
            Expect.equal
                (treeWithTwoSameNamedFiles |> VFileTree.findFile "subdir/a.fs" |> contentOf)
                (Some "nested")
                "should find the nested file"
        }

        test "findFile should accept backslashes in a qualified path" {
            Expect.equal
                (treeWithTwoSameNamedFiles |> VFileTree.findFile @"subdir\a.fs" |> contentOf)
                (Some "nested")
                "should normalize the lookup path"
        }

        test "findFile should prefer the exact location over a same-named nested file" {
            Expect.equal
                (treeWithTwoSameNamedFiles |> VFileTree.findFile "a.fs" |> contentOf)
                (Some "top")
                "should find the top-level file"
        }

        test "findFile should find files by flat map key after fromFileMap" {
            let tree =
                Map.ofList [ "src/Domain/Orders.fs", VFile.create "src/Domain/Orders.fs" "orders" ]
                |> VFileTree.fromFileMap

            Expect.equal
                (tree |> VFileTree.findFile "src/Domain/Orders.fs" |> contentOf)
                (Some "orders")
                "should find by the original key"
        }

        test "updateFile should accept a path qualified by its directories" {
            let updated =
                treeWithTwoSameNamedFiles
                |> VFileTree.updateFile "subdir/a.fs" (VFile.info "touched")

            let messages =
                updated
                |> VFileTree.toFileMap
                |> Map.map (fun _ file -> file.Messages.Length)

            Expect.equal messages (Map.ofList [ "a.fs", 0; "subdir/a.fs", 1 ]) "only the nested file should change"
        }

        test "updateFile should change only the file findFile would return" {
            let updated =
                treeWithTwoSameNamedFiles
                |> VFileTree.updateFile "a.fs" (VFile.info "touched")

            let messages =
                updated
                |> VFileTree.toFileMap
                |> Map.map (fun _ file -> file.Messages.Length)

            Expect.equal messages (Map.ofList [ "a.fs", 1; "subdir/a.fs", 0 ]) "only the top-level file should change"
        }

        test "updateFile should find a nested file by its own path" {
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "nested.fs" "code")

            let updated =
                VFileTree.empty
                |> VFileTree.addDirectory subTree
                |> VFileTree.updateFile "nested.fs" (VFile.info "touched")

            let messages = updated |> VFileTree.collectMessages

            Expect.hasLength messages 1 "nested file should be updated"
        }

        test "updateFile should leave the tree unchanged for an unknown path" {
            let updated =
                treeWithTwoSameNamedFiles
                |> VFileTree.updateFile "missing.fs" (VFile.info "touched")

            Expect.isEmpty (VFileTree.collectMessages updated) "no file should change"
        }

        test "filter should apply to nested files and keep their directory" {
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile (VFile.create "keep.fs" "code")
                |> VFileTree.addFile (VFile.create "drop.txt" "text")

            let filtered =
                VFileTree.empty
                |> VFileTree.addDirectory subTree
                |> VFileTree.filter (fun f -> f.Path = Some "keep.fs")

            Expect.equal (mapKeys (VFileTree.toFileMap filtered)) [ "subdir/keep.fs" ] "only keep.fs should remain"
            Expect.equal (VFileTree.directoryCount filtered) 1 "directory should remain"
        }
    ]

[<Tests>]
let vfileTreeWriteToDiskTests =
    testList "VFileTree writeToDisk" [
        test "should write nested files under the output directory" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let subTree =
                    VFileTree.create "subdir" TreeConfig.empty
                    |> VFileTree.addFile (VFile.create "file2.fs" "code2")

                let result =
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.create "file1.fs" "code1")
                    |> VFileTree.addDirectory subTree
                    |> VFileTree.writeToDisk output

                Expect.equal result (Ok ()) "write should succeed"
                Expect.equal (File.ReadAllText(Path.Combine(output, "file1.fs"))) "code1" "top-level file"
                Expect.equal (File.ReadAllText(Path.Combine(output, "subdir", "file2.fs"))) "code2" "nested file")
        }

        test "should not create a directory for a relative root path" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let result =
                    VFileTree.create "generated" TreeConfig.empty
                    |> VFileTree.addFile (VFile.create "file1.fs" "code1")
                    |> VFileTree.writeToDisk output

                Expect.equal result (Ok ()) "write should succeed"
                Expect.isTrue (File.Exists(Path.Combine(output, "file1.fs"))) "file should be directly under output")
        }

        test "should stay under the output directory when the root path is absolute" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")
                let absoluteRoot = Path.Combine(temp, "source")

                let result =
                    VFileTree.create absoluteRoot TreeConfig.empty
                    |> VFileTree.addFile (VFile.create "file1.fs" "code1")
                    |> VFileTree.writeToDisk output

                Expect.equal result (Ok ()) "write should succeed"
                Expect.isTrue (File.Exists(Path.Combine(output, "file1.fs"))) "file should be under output"
                Expect.isFalse (Directory.Exists absoluteRoot) "root path should not be written to")
        }

        test "should write an empty file when content is missing" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let result =
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.fromPath "empty.txt")
                    |> VFileTree.writeToDisk output

                Expect.equal result (Ok ()) "write should succeed"
                Expect.equal (File.ReadAllText(Path.Combine(output, "empty.txt"))) "" "file should be empty")
        }

        test "should reject a parent-relative path and write nothing" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let result =
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.create "good.fs" "good")
                    |> VFileTree.addFile (VFile.create "../evil.fs" "evil")
                    |> VFileTree.writeToDisk output

                Expect.equal
                    result
                    (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.PathOutsideOutputDirectory "../evil.fs" ]))
                    "traversal should be rejected"

                Expect.isFalse (File.Exists(Path.Combine(temp, "evil.fs"))) "should not escape the output directory"
                Expect.isFalse (Directory.Exists output) "should not write anything")
        }

        test "should reject an absolute file path and write nothing" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")
                let outside = Path.Combine(temp, "outside.fs")

                let result =
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.create outside "evil")
                    |> VFileTree.writeToDisk output

                Expect.equal
                    result
                    (Result.Error (
                        TreeWriteError.InvalidTree [
                            TreeWriteProblem.PathOutsideOutputDirectory (outside.Replace('\\', '/'))
                        ]
                    ))
                    "absolute path should be rejected"

                Expect.isFalse (File.Exists outside) "should not write outside the output directory")
        }

        test "should reject two files with the same path and write nothing" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let subTree =
                    VFileTree.create "subdir" TreeConfig.empty
                    |> VFileTree.addFile (VFile.create "a.fs" "first")

                let result =
                    VFileTree.empty
                    |> VFileTree.addDirectory subTree
                    |> VFileTree.addFile (VFile.create "subdir/a.fs" "second")
                    |> VFileTree.writeToDisk output

                Expect.equal
                    result
                    (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.DuplicateLocation "subdir/a.fs" ]))
                    "duplicate should be rejected rather than losing a file"

                Expect.isFalse (Directory.Exists output) "should not write anything")
        }

        test "should reject a file with no path and write nothing" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let orphan = VFile.fromContent "orphan"

                let result =
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.create "good.fs" "good")
                    |> VFileTree.addFile orphan
                    |> VFileTree.writeToDisk output

                Expect.equal
                    result
                    (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.FileWithoutPath orphan ]))
                    "pathless file cannot be written"

                Expect.isFalse (Directory.Exists output) "should not write anything")
        }

        test "should reject a file whose path names no location" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let dot = VFile.create "." "dot"

                let subTree =
                    VFileTree.create "subdir" TreeConfig.empty
                    |> VFileTree.addFile dot

                let result =
                    VFileTree.empty
                    |> VFileTree.addDirectory subTree
                    |> VFileTree.writeToDisk output

                Expect.equal
                    result
                    (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.FileWithoutPath dot ]))
                    "a directory path cannot be written as a file"

                Expect.isFalse (Directory.Exists output) "should not write anything")
        }

        test "should report every problem in the tree" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")
                let orphan = VFile.fromContent "orphan"

                let result =
                    VFileTree.empty
                    |> VFileTree.addFile orphan
                    |> VFileTree.addFile (VFile.create "../evil.fs" "evil")
                    |> VFileTree.addFile (VFile.create "twice.fs" "first")
                    |> VFileTree.addFile (VFile.create "twice.fs" "second")
                    |> VFileTree.writeToDisk output

                Expect.equal
                    result
                    (Result.Error (
                        TreeWriteError.InvalidTree [
                            TreeWriteProblem.FileWithoutPath orphan
                            TreeWriteProblem.PathOutsideOutputDirectory "../evil.fs"
                            TreeWriteProblem.DuplicateLocation "twice.fs"
                        ]
                    ))
                    "all problems should be listed, not only the first")
        }

        test "should keep the cause when the output path is an existing file" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "taken")
                File.WriteAllText(output, "already here")

                let result =
                    VFileTree.empty
                    |> VFileTree.addFile (VFile.create "file1.fs" "code1")
                    |> VFileTree.writeToDisk output

                match result with
                | Result.Error (TreeWriteError.WriteFailed (outputDir, cause)) ->
                    Expect.equal outputDir output "should name the output directory"
                    Expect.isTrue (cause :? IOException) "should keep the I/O exception"
                | other -> failtestf "expected WriteFailed, got %A" other)
        }

        test "describe should name each invalid location and every problem" {
            let description =
                TreeWriteError.InvalidTree [
                    TreeWriteProblem.FileWithoutPath (VFile.fromContent "orphan")
                    TreeWriteProblem.PathOutsideOutputDirectory "../evil.fs"
                    TreeWriteProblem.DuplicateLocation "twice.fs"
                ]
                |> TreeWriteError.describe

            Expect.stringContains description "no path" "should mention the file without a path"
            Expect.stringContains description "../evil.fs" "should name the outside location"
            Expect.stringContains description "twice.fs" "should name the duplicate location"
        }

        test "describe should name the directory and cause of a write failure" {
            let description =
                TreeWriteError.WriteFailed ("out", IOException "disk full")
                |> TreeWriteError.describe

            Expect.stringContains description "out" "should name the directory"
            Expect.stringContains description "disk full" "should include the cause"
        }
    ]

[<Tests>]
let vfileTreeMetadataTests =
    testList "VFileTree Metadata" [
        test "setMetadata should store metadata value" {
            let tree =
                VFileTree.empty
                |> VFileTree.setMetadata "key1" (box "value1")

            let value = VFileTree.getMetadata "key1" tree
            
            Expect.isSome value "value should be Some"
            Expect.equal value (Some (box "value1")) "value should match"
        }

        test "getMetadata should return None for missing key" {
            let tree = VFileTree.empty

            let value = VFileTree.getMetadata "missing" tree
            
            Expect.isNone value "value should be None"
        }

        test "should support multiple metadata values" {
            let tree =
                VFileTree.empty
                |> VFileTree.setMetadata "string" (box "text")
                |> VFileTree.setMetadata "number" (box 123)
                |> VFileTree.setMetadata "flag" (box true)

            let str = VFileTree.getMetadata "string" tree
            let num = VFileTree.getMetadata "number" tree
            let flag = VFileTree.getMetadata "flag" tree
            
            Expect.isSome str "string should be Some"
            Expect.isSome num "number should be Some"
            Expect.isSome flag "flag should be Some"
        }
    ]

[<Tests>]
let vfileTreeStatisticsTests =
    testList "VFileTree Statistics" [
        test "statistics should count files and directories" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile file2
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree

            let stats = VFileTree.statistics tree
            
            Expect.equal stats.TotalFiles 2 "should have 2 files"
            Expect.equal stats.TotalDirectories 1 "should have 1 directory"
            Expect.equal stats.ErrorCount 0 "should have 0 errors"
            Expect.equal stats.WarningCount 0 "should have 0 warnings"
            Expect.equal stats.InfoCount 0 "should have 0 info messages"
        }

        test "statistics should count messages correctly" {
            let fileWithError =
                VFile.create "error.fs" "code"
                |> VFile.error "Test error" None

            let fileWithWarning =
                VFile.create "warning.fs" "code"
                |> VFile.warn "Test warning" None

            let fileWithInfo =
                VFile.create "info.fs" "code"
                |> VFile.info "Test info"

            let tree =
                VFileTree.empty
                |> VFileTree.addFile fileWithError
                |> VFileTree.addFile fileWithWarning
                |> VFileTree.addFile fileWithInfo

            let stats = VFileTree.statistics tree
            
            Expect.equal stats.TotalFiles 3 "should have 3 files"
            Expect.equal stats.ErrorCount 1 "should have 1 error"
            Expect.equal stats.WarningCount 1 "should have 1 warning"
            Expect.equal stats.InfoCount 1 "should have 1 info message"
        }

        test "statistics should count fatal errors as errors" {
            let fileWithFatal =
                VFile.create "fatal.fs" "code"
                |> VFile.fail "Fatal error" None

            let tree = VFileTree.empty |> VFileTree.addFile fileWithFatal

            let stats = VFileTree.statistics tree
            
            Expect.equal stats.ErrorCount 1 "fatal should count as error"
        }
    ]

[<Tests>]
let vfileTreeLoadFromDiskTests =
    let writeFile (root: string) (relativePath: string) (content: string) =
        let fullPath = Path.Combine(root, relativePath)
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)) |> ignore
        File.WriteAllText(fullPath, content)

    /// Creates a symbolic link, skipping the test where the OS does not allow it.
    let createLinkOrSkip (create: unit -> unit) =
        try
            create ()
        with
        | :? IOException
        | :? System.UnauthorizedAccessException -> skiptest "symbolic links are not available"

    let loaded (result: Result<VFileTree, TreeLoadError>) =
        match result with
        | Ok tree -> tree
        | Result.Error problem -> failtestf "expected a tree, got error: %A" problem

    testList "VFileTree loadFromDisk" [
        test "should load nested files as a hierarchy with files before directories" {
            withTempDir (fun temp ->
                writeFile temp "b.fs" "B"
                writeFile temp "a.fs" "A"
                writeFile temp "zeta/deep/d.fs" "D"
                writeFile temp "alpha/c.fs" "C"

                let tree = VFileTree.loadFromDisk temp |> loaded

                Expect.equal tree.Path temp "root path should be the directory given"

                match tree.Content with
                | [ File a; File b; Directory alpha; Directory zeta ] ->
                    Expect.equal a.Path (Some "a.fs") "files should be sorted by name"
                    Expect.equal b.Path (Some "b.fs") "files should be sorted by name"
                    Expect.equal alpha.Path "alpha" "directory path should be its name"
                    Expect.equal zeta.Path "zeta" "directories should be sorted by name"

                    match zeta.Content with
                    | [ Directory deep ] ->
                        Expect.equal deep.Path "deep" "nested directory path should be its name"

                        match deep.Content with
                        | [ File d ] -> Expect.equal d.Path (Some "d.fs") "nested file path should be its name"
                        | other -> failtestf "unexpected deep content: %A" other
                    | other -> failtestf "unexpected zeta content: %A" other
                | other -> failtestf "unexpected root content: %A" other)
        }

        test "should load file text as string content" {
            withTempDir (fun temp ->
                writeFile temp "top.fs" "top"
                writeFile temp "sub/nested.fs" "nested"

                let tree = VFileTree.loadFromDisk temp |> loaded

                Expect.equal
                    (VFileTree.toStringMap tree)
                    (Map.ofList [ "sub/nested.fs", "nested"; "top.fs", "top" ])
                    "keys should be relative to the directory"

                Expect.equal
                    (VFileTree.findFile "sub/nested.fs" tree |> Option.bind (fun f -> f.Content))
                    (Some (box "nested"))
                    "content should be a string")
        }

        test "should include hidden-style dotfiles" {
            withTempDir (fun temp ->
                writeFile temp ".gitignore" "bin/"

                let tree = VFileTree.loadFromDisk temp |> loaded

                Expect.equal (VFileTree.toStringMap tree) (Map.ofList [ ".gitignore", "bin/" ]) "dotfile should load")
        }

        test "should keep empty directories" {
            withTempDir (fun temp ->
                Directory.CreateDirectory(Path.Combine(temp, "empty")) |> ignore

                let tree = VFileTree.loadFromDisk temp |> loaded

                Expect.equal (VFileTree.directoryCount tree) 1 "empty directory should be in the tree"
                Expect.equal (VFileTree.fileCount tree) 0 "there are no files")
        }

        test "should return an error for a missing directory" {
            withTempDir (fun temp ->
                let missing = Path.Combine(temp, "missing")

                Expect.equal
                    (VFileTree.loadFromDisk missing)
                    (Result.Error (TreeLoadError.DirectoryNotFound missing))
                    "missing directory")
        }

        test "should return an error when the path is a file" {
            withTempDir (fun temp ->
                writeFile temp "a.fs" "A"
                let filePath = Path.Combine(temp, "a.fs")

                Expect.equal
                    (VFileTree.loadFromDisk filePath)
                    (Result.Error (TreeLoadError.DirectoryNotFound filePath))
                    "a file is not a directory")
        }

        test "describe should name the missing directory" {
            let description =
                TreeLoadError.DirectoryNotFound "some/dir" |> TreeLoadError.describe

            Expect.stringContains description "some/dir" "should name the directory"
        }

        test "describe should name the path and cause of a load failure" {
            let description =
                TreeLoadError.LoadFailed ("some/dir", IOException "access lost")
                |> TreeLoadError.describe

            Expect.stringContains description "some/dir" "should name the path"
            Expect.stringContains description "access lost" "should include the cause"
        }

        test "should round trip through writeToDisk keeping exact text" {
            withTempDir (fun temp ->
                let output = Path.Combine(temp, "out")

                let expected =
                    Map.ofList [
                        "a.fs", "line1\r\nline2\n"
                        "sub/deep/b.fs", "café 世界"
                        "sub/empty.txt", ""
                    ]

                let written =
                    expected
                    |> Map.map (fun key content -> VFile.create key content)
                    |> VFileTree.fromFileMap
                    |> VFileTree.writeToDisk output

                Expect.equal written (Ok ()) "write should succeed"

                Expect.equal
                    (VFileTree.loadFromDisk output |> loaded |> VFileTree.toStringMap)
                    expected
                    "keys and text should match")
        }

        test "should not follow a symbolic link to a directory" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "root")
                writeFile root "a.fs" "A"

                createLinkOrSkip (fun () ->
                    Directory.CreateSymbolicLink(Path.Combine(root, "loop"), root) |> ignore)

                let tree = VFileTree.loadFromDisk root |> loaded

                Expect.equal (VFileTree.toStringMap tree) (Map.ofList [ "a.fs", "A" ]) "the link should be skipped"
                Expect.equal (VFileTree.directoryCount tree) 0 "the link should not become a directory")
        }

        test "should return an error when a file cannot be read" {
            withTempDir (fun temp ->
                createLinkOrSkip (fun () ->
                    File.CreateSymbolicLink(Path.Combine(temp, "broken.fs"), Path.Combine(temp, "missing.fs"))
                    |> ignore)

                match VFileTree.loadFromDisk temp with
                | Result.Error (TreeLoadError.LoadFailed (path, cause)) ->
                    Expect.equal path temp "should name the directory being loaded"
                    Expect.isTrue (cause :? IOException) "should keep the I/O exception"
                | other -> failtestf "expected LoadFailed, got %A" other)
        }
    ]

[<Tests>]
let vfileTreeIntegrationTests =
    testList "VFileTree Integration" [
        test "should support complex tree operations" {
            let config = TreeConfig.create "TestProject" "1.0.0"
            
            let file1 =
                VFile.create "file1.fs" "code1"
                |> VFile.info "Processed file1"
            
            let file2 =
                VFile.create "file2.fs" "code2"
                |> VFile.warn "Warning in file2" None
            
            let file3 =
                VFile.create "file3.fs" "code3"
                |> VFile.info "Processed file3"
            
            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile file2
                |> VFileTree.addFile file3
            
            let tree =
                VFileTree.create "root" config
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree
                |> VFileTree.setMetadata "build" (box "debug")

            Expect.equal tree.Path "root" "path should be root"
            Expect.equal tree.Config config "config should match"
            Expect.equal (VFileTree.fileCount tree) 3 "should have 3 files"
            Expect.equal (VFileTree.directoryCount tree) 1 "should have 1 directory"
            Expect.isFalse (VFileTree.hasErrors tree) "should not have errors"
            
            let stats = VFileTree.statistics tree
            Expect.equal stats.TotalFiles 3 "stats should show 3 files"
            Expect.equal stats.WarningCount 1 "stats should show 1 warning"
            Expect.equal stats.InfoCount 2 "stats should show 2 info messages"
            
            let buildMetadata = VFileTree.getMetadata "build" tree
            Expect.equal buildMetadata (Some (box "debug")) "build metadata should be 'debug'"
        }
    ]
