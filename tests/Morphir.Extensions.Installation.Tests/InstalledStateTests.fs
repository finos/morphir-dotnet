// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.InstalledStateTests

open System.IO
open System.Text
open System.Threading
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open IoTestSupport

[<Tests>]
let tests =
    testList "InstalledState" [
        test "shared package readers coexist and exclude an exclusive collector" {
            use sandbox = TestSandbox.Create()
            use root = SafeDirectory.Open(home sandbox, false)
            use first = root.NamedLease("tree.lease", false, false)
            use second = root.NamedLease("tree.lease", false, false)

            Expect.throwsT<FileLeaseBusyException>
                (fun () -> use writer = root.NamedLease("tree.lease", true, false) in ())
                "busy collector"
        }
        test "exclusive collector can acquire after the final reader closes" {
            use sandbox = TestSandbox.Create()
            use root = SafeDirectory.Open(home sandbox, false)

            do
                use reader = root.NamedLease("tree.lease", false, false)
                ()

            use writer = root.NamedLease("tree.lease", true, false)
            Expect.isTrue writer.CanRead "owned exclusive descriptor"
        }
        testTask "old catalog refuses before recovery and preserves journal" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            Directory.CreateDirectory(Path.Combine(root, "catalog"))
            |> ignore

            Directory.CreateDirectory(Path.Combine(root, "locks", "extensions"))
            |> ignore

            let catalog = Path.Combine(root, "catalog", "extensions.json")

            let journal =
                Path.Combine(root, "locks", "extensions", "org.example.backend.json.transaction")

            File.WriteAllText(catalog, "{\"version\":1,\"extensions\":[]}")

            let bytes =
                Encoding.UTF8.GetBytes
                    "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/journal.schema.json\",\"nextLock\":null,\"nextCatalog\":null}"

            File.WriteAllBytes(journal, bytes)
            let! result = InstalledState.CheckCompatibilityAsync(root, CancellationToken.None)
            refused "extension.old_state" result
            Expect.isTrue (File.Exists catalog) "old catalog untouched"
            Expect.sequenceEqual (File.ReadAllBytes journal) bytes "journal untouched"
        }
    ]
