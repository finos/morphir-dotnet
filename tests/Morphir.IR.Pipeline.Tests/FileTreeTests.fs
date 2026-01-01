module Morphir.IR.Pipeline.Tests.FileTreeTests

open Expecto
open Morphir.IR.Pipeline

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
        test "toFileMap should flatten tree to Map" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"

            let subTree =
                VFileTree.create "subdir" TreeConfig.empty
                |> VFileTree.addFile file2
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addDirectory subTree

            let fileMap = VFileTree.toFileMap tree
            
            Expect.isGreaterThanOrEqual (Map.count fileMap) 1 "should have at least 1 file"
        }

        test "fromFileMap should build tree from Map" {
            let fileMap =
                Map.empty
                |> Map.add "file1.fs" (VFile.create "file1.fs" "code1")
                |> Map.add "file2.fs" (VFile.create "file2.fs" "code2")

            let tree = VFileTree.fromFileMap fileMap
            
            Expect.equal (VFileTree.fileCount tree) 2 "should have 2 files"
        }

        test "toStringMap should flatten to string map" {
            let file1 = VFile.create "file1.fs" "code1"
            let file2 = VFile.create "file2.fs" "code2"
            
            let tree =
                VFileTree.empty
                |> VFileTree.addFile file1
                |> VFileTree.addFile file2

            let stringMap = VFileTree.toStringMap tree
            
            Expect.isGreaterThanOrEqual (Map.count stringMap) 1 "should have at least 1 entry"
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
