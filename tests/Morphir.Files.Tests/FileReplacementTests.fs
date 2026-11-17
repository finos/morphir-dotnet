/// A virus scanner or a search indexer opens a file just after it is written, without delete sharing. While it
/// holds the file, Windows refuses to replace it, and both files stay as they were. Replacement waits for such a
/// handle a bounded time and checks again before every further attempt. Other systems replace a file that another
/// handle holds open, so these tests run on Windows only.
module Morphir.Files.Tests.FileReplacementTests

open System
open System.Diagnostics
open System.IO
open System.Threading
open Expecto
open Morphir.Files

let private windowsOnly () =
    if not (OperatingSystem.IsWindows()) then
        skiptest "only Windows refuses to replace a file that another handle holds open"

/// The failure ReplaceFile reports for a sharing violation (ERROR_SHARING_VIOLATION).
let private held () = IOException("held", 0x80070020)

let private withTempDir (run: string -> unit) =
    let temp = Directory.CreateTempSubdirectory("morphir-replace-").FullName

    try
        run temp
    finally
        Directory.Delete(temp, true)

/// As a scanner does: it reads, and shares reading and writing but not deletion.
let private holdWithoutDeleteSharing (path: string) =
    new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)

/// Releases a handle from a thread of its own after a short time, so the replacement meets it first.
let private releaseLater (handle: IDisposable) =
    let thread =
        Thread(
            (fun () ->
                Thread.Sleep 150
                handle.Dispose()),
            IsBackground = true
        )

    thread.Start()
    thread

let private treeOf (content: string) =
    VFileTree.empty |> VFileTree.addFile (VFile.create "src/a.fs" content)

let private options root = { OutputRoot = root; Manifest = ManifestMode.Manifest "manifest.json" }

let private publishOk root tree =
    match VFileTree.publish (options root) tree with
    | Ok _ -> ()
    | Result.Error error -> failtest (TreeWriteError.describe error)

[<Tests>]
let tests =
    testList "File replacement" [
        test "waits while another handle holds the file and checks again before each further attempt" {
            windowsOnly ()
            let mutable attempts = 0
            let mutable checks = 0

            let replaced =
                FileReplacement.Replace(
                    Action(fun () ->
                        attempts <- attempts + 1
                        if attempts < 3 then raise (held ())),
                    Func<bool>(fun () ->
                        checks <- checks + 1
                        true)
                )

            Expect.isTrue replaced "the third attempt replaces the file"
            Expect.equal (attempts, checks) (3, 2) "one check before each of the two further attempts"
        }

        test "stops without replacing when the check after a wait fails" {
            windowsOnly ()
            let mutable attempts = 0

            let replaced =
                FileReplacement.Replace(
                    Action(fun () ->
                        attempts <- attempts + 1
                        raise (held ())),
                    Func<bool>(fun () -> false)
                )

            Expect.isFalse replaced "another writer changed what the caller checks"
            Expect.equal attempts 1 "no attempt after the failed check"
        }

        test "raises the error after the last attempt, within a bounded time" {
            windowsOnly ()
            let mutable attempts = 0
            let clock = Stopwatch.StartNew()

            Expect.throwsT<IOException>
                (fun () ->
                    FileReplacement.Replace(
                        Action(fun () ->
                            attempts <- attempts + 1
                            raise (held ())),
                        Func<bool>(fun () -> true)
                    )
                    |> ignore)
                "the handle stays open"

            Expect.equal attempts FileReplacement.Attempts "every attempt is made"
            Expect.isLessThan clock.Elapsed (TimeSpan.FromSeconds 5.0) "the waits add up to about 2.6 seconds"
        }

        test "does not try again after a failure that is not a held handle" {
            windowsOnly ()

            // ERROR_UNABLE_TO_MOVE_REPLACEMENT: ReplaceFile has changed the files.
            for failure in [ IOException("moved", 0x80070498) :> exn; FileNotFoundException("missing") :> exn ] do
                let mutable attempts = 0

                Expect.throws
                    (fun () ->
                        FileReplacement.Replace(
                            Action(fun () ->
                                attempts <- attempts + 1
                                raise failure),
                            Func<bool>(fun () -> true)
                        )
                        |> ignore)
                    failure.Message

                Expect.equal attempts 1 $"{failure.GetType().Name} is raised at once"
        }

        test "a real handle without delete sharing makes File.Replace and an overwriting File.Move report a held handle" {
            windowsOnly ()

            withTempDir (fun temp ->
                let target = Path.Combine(temp, "target")
                let source = Path.Combine(temp, "source")
                File.WriteAllText(target, "old")
                File.WriteAllText(source, "new")

                use _scanner = holdWithoutDeleteSharing target

                for name, replace in
                    [ "File.Replace", (fun () -> File.Replace(source, target, null))
                      "File.Move", (fun () -> File.Move(source, target, true)) ] do
                    let error =
                        try
                            replace ()
                            None
                        with error ->
                            Some error

                    match error with
                    | Some error -> Expect.isTrue (FileReplacement.IsHeldByAnotherHandle error) $"{name}: {error.GetType().Name} {error.Message}"
                    | None -> failtest $"{name} replaced a file that another handle holds"

                    Expect.equal (File.ReadAllText target, File.ReadAllText source) ("old", "new") $"{name} changed neither file")
        }

        test "publication waits for a short-lived handle on an output file" {
            windowsOnly ()

            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                publishOk root (treeOf "A1")
                let output = Path.Combine(root, "src", "a.fs")
                let release = releaseLater (holdWithoutDeleteSharing output)

                let result = VFileTree.publish (options root) (treeOf "A2")
                release.Join()

                match result with
                | Ok published -> Expect.equal published.Written [ "src/a.fs" ] "the output file is written"
                | Result.Error error -> failtest (TreeWriteError.describe error)

                Expect.equal (File.ReadAllText output) "A2" "the new content is in place")
        }

        test "publication gives up on a handle that stays open and keeps the old file" {
            windowsOnly ()

            withTempDir (fun temp ->
                let root = Path.Combine(temp, "out")
                publishOk root (treeOf "A1")
                let output = Path.Combine(root, "src", "a.fs")
                let clock = Stopwatch.StartNew()

                let result =
                    use _scanner = holdWithoutDeleteSharing output
                    VFileTree.publish (options root) (treeOf "A2")

                match result with
                | Result.Error (TreeWriteError.WriteFailed _) -> ()
                | other -> failtestf "expected a write failure, got %A" other

                Expect.isLessThan clock.Elapsed (TimeSpan.FromSeconds 5.0) "the wait is bounded"
                Expect.equal (File.ReadAllText output) "A1" "the old file stays"
                Expect.isEmpty (Directory.GetDirectories(root, ".stage-*")) "no staging directory remains")
        }
    ]
    |> testLabel "PlatformSpecific"
