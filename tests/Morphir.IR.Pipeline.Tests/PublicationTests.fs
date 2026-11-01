module Morphir.IR.Pipeline.Tests.PublicationTests

open System
open System.IO
open System.Threading.Tasks
open Expecto
open Morphir.IR.Pipeline

let private withTempDir (run: string -> unit) =
    let temp = Directory.CreateTempSubdirectory("morphir-publish-").FullName

    try
        run temp
    finally
        Directory.Delete(temp, true)

/// Creates a symbolic link, skipping the test where the OS does not allow it.
let private createLinkOrSkip (create: unit -> unit) =
    try
        create ()
    with
    | :? IOException
    | :? UnauthorizedAccessException -> skiptest "symbolic links are not available"

let private plain root = { OutputRoot = root; Manifest = ManifestMode.NoManifest }
let private tracked root = { OutputRoot = root; Manifest = ManifestMode.Manifest "manifest.json" }

let private treeOf (files: (string * string) list) =
    files
    |> List.fold (fun tree (path, content) -> VFileTree.addFile (VFile.create path content) tree) VFileTree.empty

let private invalid problems : Result<PublishResult, TreeWriteError> =
    Result.Error (TreeWriteError.InvalidTree problems)

let private published (result: Result<PublishResult, TreeWriteError>) =
    match result with
    | Ok value -> value
    | Result.Error error -> failtestf "expected success, got %s" (TreeWriteError.describe error)

let private filesUnder (root: string) =
    Directory.GetFiles(root, "*", SearchOption.AllDirectories)
    |> Array.map (fun path -> Path.GetRelativePath(root, path).Replace('\\', '/'))
    |> Array.sort
    |> List.ofArray

[<Tests>]
let containmentTests =
    testList "Publication containment" [
        test "should write nested files under the root" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                let subTree =
                    VFileTree.create "sub" TreeConfig.empty
                    |> VFileTree.addFile (VFile.create "b.fs" "B")

                let tree = treeOf [ "a.fs", "A" ] |> VFileTree.addDirectory subTree

                published (VFileTree.publish (plain root) tree) |> ignore

                Expect.equal (filesUnder root) [ "a.fs"; "sub/b.fs" ] "exact file set"
                Expect.equal (File.ReadAllText(Path.Combine(root, "sub", "b.fs"))) "B" "content")
        }

        test "should normalize dot segments when building locations" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                published (VFileTree.publish (plain root) (treeOf [ "./src/./a.fs", "A" ])) |> ignore

                Expect.equal (filesUnder root) [ "src/a.fs" ] "should be published at the normalized location")
        }

        test "should reject parent traversal and write nothing" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                Expect.equal
                    (VFileTree.publish (plain root) (treeOf [ "good.fs", "G"; "../evil.fs", "E" ]))
                    (invalid [ TreeWriteProblem.PathOutsideOutputDirectory "../evil.fs" ])
                    "should name the location"

                Expect.isFalse (Directory.Exists root) "should write nothing")
        }

        test "should reject traversal hidden inside a path" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                Expect.equal
                    (VFileTree.publish (plain root) (treeOf [ "a/../../evil.fs", "E" ]))
                    (invalid [ TreeWriteProblem.PathOutsideOutputDirectory "a/../../evil.fs" ])
                    "should name the location")
        }

        test "should reject a rooted path and write nothing" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let outside = Path.Combine(temp, "outside.fs")

                Expect.equal
                    (VFileTree.publish (plain root) (treeOf [ outside, "E" ]))
                    (invalid [ TreeWriteProblem.PathOutsideOutputDirectory (outside.Replace('\\', '/')) ])
                    "should name the location"

                Expect.isFalse (File.Exists outside) "should not write outside the root")
        }

        test "should ignore the root tree's own absolute path" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let source = Path.Combine(temp, "source")

                let tree =
                    VFileTree.create source TreeConfig.empty
                    |> VFileTree.addFile (VFile.create "a.fs" "A")

                published (VFileTree.publish (plain root) tree) |> ignore

                Expect.equal (filesUnder root) [ "a.fs" ] "file should be directly under the root"
                Expect.isFalse (Directory.Exists source) "root path should not be written to")
        }

        test "should reject a symbolically linked subdirectory and write nothing" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let elsewhere = Path.Combine(temp, "elsewhere")
                Directory.CreateDirectory root |> ignore
                Directory.CreateDirectory elsewhere |> ignore

                createLinkOrSkip (fun () ->
                    Directory.CreateSymbolicLink(Path.Combine(root, "linked"), elsewhere) |> ignore)

                Expect.equal
                    (VFileTree.publish (plain root) (treeOf [ "linked/a.fs", "A" ]))
                    (invalid [ TreeWriteProblem.SymbolicLinkInPath "linked/a.fs" ])
                    "should name the location"

                Expect.isEmpty (Directory.GetFiles elsewhere) "should not write through the link")
        }

        test "should reject a symbolically linked output root and write nothing" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let elsewhere = Path.Combine(temp, "elsewhere")
                Directory.CreateDirectory elsewhere |> ignore

                createLinkOrSkip (fun () -> Directory.CreateSymbolicLink(root, elsewhere) |> ignore)

                match VFileTree.publish (plain root) (treeOf [ "a.fs", "A" ]) with
                | Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.SymbolicLinkInPath _ ]) -> ()
                | other -> failtestf "expected a symbolic link problem, got %A" other

                Expect.isEmpty (Directory.GetFiles elsewhere) "should not write through the link")
        }
    ]

[<Tests>]
let collisionTests =
    testList "Publication collisions" [
        test "should reject locations that differ only by case" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                match VFileTree.publish (plain root) (treeOf [ "Domain/Types.fs", "A"; "domain/types.fs", "B" ]) with
                | Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.DuplicateLocation location ]) ->
                    Expect.equal (location.ToUpperInvariant()) "DOMAIN/TYPES.FS" "should name the collision"
                | other -> failtestf "expected a duplicate, got %A" other

                Expect.isFalse (Directory.Exists root) "should write nothing")
        }

        test "should reject a pathless file and identify it" {
            withTempDir (fun temp ->
                let orphan = VFile.fromContent "orphan"

                Expect.equal
                    (VFileTree.publish (plain (Path.Combine(temp, "out"))) (VFileTree.empty |> VFileTree.addFile orphan))
                    (invalid [ TreeWriteProblem.FileWithoutPath orphan ])
                    "should identify the file")
        }

        test "should treat a path with no segments as pathless" {
            withTempDir (fun temp ->
                let dot = VFile.create "." "D"

                Expect.equal
                    (VFileTree.publish (plain (Path.Combine(temp, "out"))) (VFileTree.empty |> VFileTree.addFile dot))
                    (invalid [ TreeWriteProblem.FileWithoutPath dot ])
                    "should identify the file")
        }

        test "should reject a file at the manifest's location" {
            withTempDir (fun temp ->
                Expect.equal
                    (VFileTree.publish (tracked (Path.Combine(temp, "out"))) (treeOf [ "MANIFEST.json", "M" ]))
                    (invalid [ TreeWriteProblem.DuplicateLocation "MANIFEST.json" ])
                    "should not let a file overwrite the manifest")
        }

        test "should report every problem together" {
            withTempDir (fun temp ->
                let orphan = VFile.fromContent "orphan"

                let tree =
                    treeOf [ "../evil.fs", "E"; "twice.fs", "1"; "twice.fs", "2" ]
                    |> VFileTree.addFile orphan

                Expect.equal
                    (VFileTree.publish (plain (Path.Combine(temp, "out"))) tree)
                    (invalid [
                        TreeWriteProblem.FileWithoutPath orphan
                        TreeWriteProblem.PathOutsideOutputDirectory "../evil.fs"
                        TreeWriteProblem.DuplicateLocation "twice.fs"
                    ])
                    "all problems should be listed")
        }

        test "validatePublish should report the same problems and touch nothing" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                Expect.equal
                    (VFileTree.validatePublish (plain root) (treeOf [ "../evil.fs", "E" ]))
                    (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.PathOutsideOutputDirectory "../evil.fs" ]))
                    "should report the problem"

                Expect.equal (VFileTree.validatePublish (plain root) (treeOf [ "a.fs", "A" ])) (Ok ()) "a good tree passes"
                Expect.isFalse (Directory.Exists root) "should not create the root")
        }

        test "should refuse a tree that reports errors and keep existing output" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                published (VFileTree.publish (plain root) (treeOf [ "a.fs", "old" ])) |> ignore

                let failure = VMessage.create Error "generation failed"
                let failed = treeOf [ "a.fs", "new" ] |> VFileTree.report failure

                Expect.equal
                    (VFileTree.publish (plain root) failed)
                    (invalid [ TreeWriteProblem.TreeHasErrors [ failure ] ])
                    "should refuse"

                Expect.equal (File.ReadAllText(Path.Combine(root, "a.fs"))) "old" "existing output should be unchanged")
        }
    ]

[<Tests>]
let stagingTests =
    let stagingDirectories (root: string) = Directory.GetDirectories(root, ".stage-*")

    testList "Publication staging" [
        test "a source failing while staging should leave output unchanged and no staging directory" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                published (VFileTree.publish (plain root) (treeOf [ "a.fs", "old" ])) |> ignore

                let failure = IOException "gone"

                let tree =
                    treeOf [ "a.fs", "new" ]
                    |> VFileTree.addFile (VFile.create "b.bin" (StreamSource(fun () -> raise failure)))

                match VFileTree.publish (plain root) tree with
                | Result.Error (TreeWriteError.WriteFailed (outputRoot, cause)) ->
                    Expect.equal outputRoot root "should name the root"
                    Expect.isTrue (obj.ReferenceEquals(cause, failure)) "should keep the original exception"
                | other -> failtestf "expected a write failure, got %A" other

                Expect.equal (File.ReadAllText(Path.Combine(root, "a.fs"))) "old" "existing output should be unchanged"
                Expect.equal (filesUnder root) [ "a.fs" ] "should add nothing"
                Expect.isEmpty (stagingDirectories root) "should remove the staging directory")
        }

        test "success should leave nothing but the published files" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                published (VFileTree.publish (plain root) (treeOf [ "a.fs", "A" ])) |> ignore

                Expect.equal (filesUnder root) [ "a.fs" ] "no lock or staging files"
                Expect.isEmpty (stagingDirectories root) "no staging directory")
        }

        test "an output root that is a file should return a write failure" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "taken")
                File.WriteAllText(root, "already here")

                match VFileTree.publish (plain root) (treeOf [ "a.fs", "A" ]) with
                | Result.Error (TreeWriteError.WriteFailed (outputRoot, cause)) ->
                    Expect.equal outputRoot root "should name the root"
                    Expect.isTrue (cause :? IOException) "should keep the I/O exception"
                | other -> failtestf "expected a write failure, got %A" other)
        }

        test "concurrent publications should finish with one complete set" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let setA = treeOf [ for i in 1..20 -> $"f{i}.txt", "A" ]
                let setB = treeOf [ for i in 1..20 -> $"f{i}.txt", "B" ]

                for _ in 1..5 do
                    let tasks =
                        [ setA; setB ]
                        |> List.map (fun tree -> Task.Run(fun () -> VFileTree.publish (plain root) tree))

                    let results = tasks |> List.map (fun task -> task.Result)
                    results |> List.iter (published >> ignore)

                    let contents =
                        [ for i in 1..20 -> File.ReadAllText(Path.Combine(root, $"f{i}.txt")) ]
                        |> List.distinct

                    Expect.hasLength contents 1 "every file should come from one set"
                    Expect.isEmpty (stagingDirectories root) "no staging directory should remain")
        }
    ]

[<Tests>]
let repeatTests =
    testList "Publication repeats" [
        test "should report written files the first time and unchanged files after" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let tree = treeOf [ "a.fs", "A"; "b.fs", "B" ]

                let first = published (VFileTree.publish (plain root) tree)
                let second = published (VFileTree.publish (plain root) tree)

                Expect.equal first.Written [ "a.fs"; "b.fs" ] "first publication writes"
                Expect.isEmpty first.Unchanged "nothing to compare yet"
                Expect.isEmpty second.Written "second publication writes nothing"
                Expect.equal second.Unchanged [ "a.fs"; "b.fs" ] "every file is unchanged")
        }

        test "should not touch the timestamp of an identical file" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let tree = treeOf [ "a.fs", "A" ]
                published (VFileTree.publish (plain root) tree) |> ignore

                let file = Path.Combine(root, "a.fs")
                let earlier = DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                File.SetLastWriteTimeUtc(file, earlier)

                published (VFileTree.publish (plain root) tree) |> ignore

                Expect.equal (File.GetLastWriteTimeUtc file) earlier "should not rewrite the file")
        }

        test "should replace a file whose content changed, including at the same length" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                published (VFileTree.publish (plain root) (treeOf [ "a.fs", "AAAA" ])) |> ignore

                let result = published (VFileTree.publish (plain root) (treeOf [ "a.fs", "AAAB" ]))

                Expect.equal result.Written [ "a.fs" ] "should report the write"
                Expect.equal (File.ReadAllText(Path.Combine(root, "a.fs"))) "AAAB" "should replace the content")
        }

        test "should compare stream content without treating it as changed" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let source = StreamSource(fun () -> new MemoryStream("same"B) :> Stream)
                let tree = VFileTree.empty |> VFileTree.addFile (VFile.create "a.bin" source)

                published (VFileTree.publish (plain root) tree) |> ignore
                let second = published (VFileTree.publish (plain root) tree)

                Expect.equal second.Unchanged [ "a.bin" ] "identical stream content should be unchanged")
        }
    ]

[<Tests>]
let manifestTests =
    testList "Publication manifest" [
        test "should list locations in the declared order, across directories" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let order = [ "b/first.fs"; "a/second.fs"; "b/third.fs" ]

                let tree =
                    treeOf [ "a/second.fs", "2"; "b/first.fs", "1"; "b/third.fs", "3" ]
                    |> VFileTree.set VFileTree.declaredOrder order

                published (VFileTree.publish (tracked root) tree) |> ignore

                Expect.equal (VFileTree.readManifest (tracked root)) (Ok order) "manifest keeps the order")
        }

        test "should sort locations when no order is declared" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")

                published (VFileTree.publish (tracked root) (treeOf [ "b.fs", "B"; "a.fs", "A" ])) |> ignore

                Expect.equal (VFileTree.readManifest (tracked root)) (Ok [ "a.fs"; "b.fs" ]) "sorted")
        }

        test "readManifest should return nothing when there is no manifest" {
            withTempDir (fun temp ->
                Expect.equal (VFileTree.readManifest (tracked (Path.Combine(temp, "out")))) (Ok []) "no manifest yet")
        }

        test "should reject a declared order that does not match the tree" {
            withTempDir (fun temp ->
                let tree =
                    treeOf [ "a.fs", "A"; "b.fs", "B" ]
                    |> VFileTree.set VFileTree.declaredOrder [ "a.fs"; "c.fs" ]

                Expect.equal
                    (VFileTree.publish (tracked (Path.Combine(temp, "out"))) tree)
                    (invalid [ TreeWriteProblem.DeclaredOrderMismatch ([ "c.fs" ], [ "b.fs" ]) ])
                    "should name both differences")
        }

        test "should delete a file the previous manifest listed and the tree no longer has" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                published (VFileTree.publish (tracked root) (treeOf [ "a.fs", "A"; "old/b.fs", "B" ])) |> ignore

                let result = published (VFileTree.publish (tracked root) (treeOf [ "a.fs", "A" ]))

                Expect.equal result.Removed [ "old/b.fs" ] "should report the removal"
                Expect.isFalse (File.Exists(Path.Combine(root, "old", "b.fs"))) "should delete the file"
                Expect.equal (VFileTree.readManifest (tracked root)) (Ok [ "a.fs" ]) "manifest should omit it")
        }

        test "should leave a file no manifest ever listed" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                Directory.CreateDirectory root |> ignore
                File.WriteAllText(Path.Combine(root, "mine.txt"), "keep me")

                published (VFileTree.publish (tracked root) (treeOf [ "a.fs", "A" ])) |> ignore
                published (VFileTree.publish (tracked root) (treeOf [ "b.fs", "B" ])) |> ignore

                Expect.equal (File.ReadAllText(Path.Combine(root, "mine.txt"))) "keep me" "unowned file should stay")
        }

        test "should reject a tampered manifest entry and delete nothing" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let outside = Path.Combine(temp, "outside.fs")
                File.WriteAllText(outside, "keep me")
                Directory.CreateDirectory root |> ignore
                File.WriteAllText(Path.Combine(root, "manifest.json"), """["../outside.fs"]""")

                Expect.equal
                    (VFileTree.publish (tracked root) (treeOf [ "a.fs", "A" ]))
                    (invalid [ TreeWriteProblem.InvalidManifestEntry "../outside.fs" ])
                    "should name the entry"

                Expect.isTrue (File.Exists outside) "should delete nothing outside the root"
                Expect.isFalse (File.Exists(Path.Combine(root, "a.fs"))) "should write nothing")
        }

        test "readManifest should reject a tampered entry" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                Directory.CreateDirectory root |> ignore
                File.WriteAllText(Path.Combine(root, "manifest.json"), """["./a.fs"]""")

                Expect.equal
                    (VFileTree.readManifest (tracked root))
                    (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.InvalidManifestEntry "./a.fs" ]))
                    "dot segments are not allowed in a manifest")
        }

        test "should not delete through a directory that became a symbolic link" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let elsewhere = Path.Combine(temp, "elsewhere")
                let external = Path.Combine(elsewhere, "old.fs")
                Directory.CreateDirectory root |> ignore
                Directory.CreateDirectory elsewhere |> ignore
                File.WriteAllText(external, "keep me")
                File.WriteAllText(Path.Combine(root, "manifest.json"), """["linked/old.fs"]""")

                createLinkOrSkip (fun () ->
                    Directory.CreateSymbolicLink(Path.Combine(root, "linked"), elsewhere) |> ignore)

                Expect.equal
                    (VFileTree.publish (tracked root) VFileTree.empty)
                    (invalid [ TreeWriteProblem.SymbolicLinkInPath "linked/old.fs" ])
                    "should name the entry"

                Expect.isTrue (File.Exists external) "should not delete outside the root")
        }

        test "readManifest should reject an entry behind a symbolic link" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                let elsewhere = Path.Combine(temp, "elsewhere")
                Directory.CreateDirectory root |> ignore
                Directory.CreateDirectory elsewhere |> ignore
                File.WriteAllText(Path.Combine(root, "manifest.json"), """["linked/old.fs"]""")

                createLinkOrSkip (fun () ->
                    Directory.CreateSymbolicLink(Path.Combine(root, "linked"), elsewhere) |> ignore)

                Expect.equal
                    (VFileTree.readManifest (tracked root))
                    (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.SymbolicLinkInPath "linked/old.fs" ]))
                    "should not hand back a path that leads outside the root")
        }

        testList "should reject a manifest name that is not a plain file name" [
            for label, name in
                [
                    "a parent path", "../manifest.json"
                    "a nested path", "sub/manifest.json"
                    "a backslash path", @"sub\manifest.json"
                    "a dot", "."
                    "two dots", ".."
                    "an empty name", ""
                    "a rooted path", "/manifest.json"
                ] do
                test label {
                    withTempDir (fun temp ->
                        let root = Path.Combine(temp, "out")
                        let options = { OutputRoot = root; Manifest = ManifestMode.Manifest name }

                        Expect.equal
                            (VFileTree.publish options (treeOf [ "a.fs", "A" ]))
                            (invalid [ TreeWriteProblem.InvalidManifestName name ])
                            "publish should name the problem"

                        Expect.equal
                            (VFileTree.readManifest options)
                            (Result.Error (TreeWriteError.InvalidTree [ TreeWriteProblem.InvalidManifestName name ]))
                            "readManifest should name the problem"

                        Expect.isFalse (Directory.Exists root) "should write nothing"
                        Expect.isEmpty (Directory.GetFiles temp) "should write nothing beside the root")
                }
        ]

        test "an absolute manifest name should not overwrite the file it names" {
            withTempDir (fun temp ->
                let external = Path.Combine(temp, "external.json")
                File.WriteAllText(external, "keep me")

                let options = { OutputRoot = Path.Combine(temp, "out"); Manifest = ManifestMode.Manifest external }

                Expect.equal
                    (VFileTree.publish options (treeOf [ "a.fs", "A" ]))
                    (invalid [ TreeWriteProblem.InvalidManifestName external ])
                    "should name the problem"

                Expect.equal (File.ReadAllText external) "keep me" "should leave the file alone")
        }

        test "a malformed manifest should return a write failure" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                Directory.CreateDirectory root |> ignore
                File.WriteAllText(Path.Combine(root, "manifest.json"), "not json")

                match VFileTree.readManifest (tracked root) with
                | Result.Error (TreeWriteError.WriteFailed _) -> ()
                | other -> failtestf "expected a write failure, got %A" other)
        }

        test "no-manifest publication should not delete or record anything" {
            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                published (VFileTree.publish (plain root) (treeOf [ "a.fs", "A" ])) |> ignore

                let result = published (VFileTree.publish (plain root) (treeOf [ "b.fs", "B" ]))

                Expect.isEmpty result.Removed "nothing is owned"
                Expect.equal (filesUnder root) [ "a.fs"; "b.fs" ] "earlier file stays and no manifest is written")
        }

        test "describe should explain each new problem" {
            let describe problem = TreeWriteError.describe (TreeWriteError.InvalidTree [ problem ])

            Expect.stringContains (describe (TreeWriteProblem.SymbolicLinkInPath "x/a.fs")) "x/a.fs" "link"
            Expect.stringContains (describe (TreeWriteProblem.InvalidManifestEntry "../a.fs")) "../a.fs" "entry"
            Expect.stringContains (describe (TreeWriteProblem.InvalidManifestName "../m.json")) "../m.json" "name"
            Expect.stringContains (describe (TreeWriteProblem.TreeHasErrors [ VMessage.create Error "boom" ])) "boom" "errors"

            let mismatch = describe (TreeWriteProblem.DeclaredOrderMismatch ([ "extra.fs" ], [ "absent.fs" ]))
            Expect.stringContains mismatch "extra.fs" "unexpected"
            Expect.stringContains mismatch "absent.fs" "missing"
        }
    ]
