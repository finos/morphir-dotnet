// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Okf.Tests.BundleTests

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Expecto
open Morphir.Files
open Morphir.Knowledge.Okf
open Morphir.TestSupport

module private UnixFixture =
    [<DllImport("libc", EntryPoint = "mkfifo")>]
    extern int mkfifo([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, uint32 mode)

module private BundleMother =
    let concept description body =
        "---\ntype: Metric\ndescription: " + description + "\n---\n" + body

    let index version body =
        "---\nokf_version: \"" + version + "\"\n---\n" + body

    let tree files =
        files |> List.map (fun (path, content) -> VFile.ofText path content) |> VFileTree.fromFiles

    let versioned version =
        tree [
            "index.md", index version "# Metrics\n\nOperating figures for this period.\n\n## Revenue\n\n- [Revenue](metrics/revenue.md) - Revenue recognized\n"
            "metrics/revenue.md", concept "Revenue recognized" "# Revenue\n"
        ]

    let plainReadme = tree [ "README.md", "# This is project documentation\n" ]

    let invalidMembers =
        let unreadable = StreamSource(fun () -> raise (IOException("test content cannot be opened")))

        tree [ "broken.md", "---\ntype: [\n---\n# Heading\n" ]
        |> VFileTree.addFile (VFile.ofStream "unreadable.md" unreadable)

    let brokenLink =
        tree [ "metrics/revenue.md", concept "Revenue recognized" "[Missing](missing.md)\n" ]

    let policyFindings =
        tree [
            "index.md", index "0.3" "# Metrics\n\n- [Revenue](metrics/revenue.md) - Old description\n"
            "metrics/revenue.md",
                "---\ntype: Metric\ndescription: Revenue recognized\nsources: [{id: verified, resource: https://example.org/}]\n---\nA claim.[^missing]\n\n[^missing]: One source.\n"
        ]

    let allTargets =
        tree [
            "metrics/revenue.md",
                concept "Revenue recognized" "[Relative](cost.md) [Root](/metrics/cost.md) [Asset](/asset.bin) [Unknown](missing.md) [Outside](https://example.org/)\n"
            "metrics/cost.md", concept "Cost" "# Cost\n"
            "asset.bin", "payload that should remain an asset"
        ]

    let malformedReserved =
        tree [
            "index.md", "# Metrics\n\nNo directory entries here.\n"
            "log.md", "# Changes\n\n## 2025-02-30\n\n- Changed revenue.\n"
        ]

    let containedTree root =
        let bundle = Directory.CreateDirectory(Path.Combine(root, "bundle")).FullName
        let external = Path.Combine(root, "outside.md")
        File.WriteAllText(external, "# Contents outside the bundle\n")
        File.WriteAllText(Path.Combine(bundle, "revenue.md"), concept "Revenue" "# Revenue\n")
        bundle, external

let private validate version policy tree =
    BundleValidation.validate version policy (KnowledgeBundle.FromTree tree)

let private findings (report: BundleValidationReport) =
    report.Findings |> Seq.map (fun finding -> finding.PathOrNull, finding.Code) |> Seq.toList

[<Tests>]
let bundleTests =
    testList "Knowledge bundle validation" [
        test "creates supported version literals" {
            Expect.equal (OkfVersion.Create "0.2") OkfVersion.V0_2 "accepts 0.2"
            Expect.equal (OkfVersion.Create "0.3") OkfVersion.V0_3 "accepts 0.3"
            Expect.throwsT<ArgumentException> (fun () -> OkfVersion.Create "9.1" |> ignore) "rejects unknown version literals"
        }

        test "honors declarations, defaults and explicit overrides" {
            let bundle = BundleMother.versioned "0.2"
            Expect.equal (validate None BundlePolicy.Standard bundle).VersionOrNull OkfVersion.V0_2 "reads root index"
            Expect.equal (validate None BundlePolicy.Morphir bundle).ExitCode 0 "allows an introductory heading and prose"
            Expect.equal (validate None BundlePolicy.Standard BundleMother.plainReadme).VersionOrNull OkfVersion.V0_3 "defaults to 0.3"
            Expect.equal
                (validate (Some OkfVersion.V0_3) BundlePolicy.Standard bundle).VersionOrNull
                OkfVersion.V0_3
                "an explicit version wins"
        }

        test "checks ordinary Markdown under the selected version" {
            let check version = validate (Some version) BundlePolicy.Standard BundleMother.plainReadme
            Expect.equal (check OkfVersion.V0_2).ExitCode 1 "0.2 expects concept frontmatter"
            Expect.equal (check OkfVersion.V0_3).ExitCode 0 "0.3 allows Markdown without frontmatter"
        }

        test "reports unknown declaration without claiming conformance" {
            let report = validate None BundlePolicy.Standard (BundleMother.versioned "9.1")
            Expect.equal report.DeclaredVersionOrNull "9.1" "preserves declaration"
            Expect.equal report.Conformance BundleConformance.Unverified "unknown format status"
            Expect.contains (findings report) ("index.md", "okf.unknown-version") "reports the version warning"
        }

        test "uses the default version for empty declarations" {
            for empty in [ "null"; "''"; "[]" ] do
                let tree = BundleMother.tree [ "index.md", $"---\nokf_version: {empty}\n---\n# Index\n\n- [External](https://example.org/)\n" ]
                let standard = validate None BundlePolicy.Standard tree
                let morphir = validate None BundlePolicy.Morphir tree
                Expect.equal standard.VersionOrNull OkfVersion.V0_3 "empty values select the default"
                Expect.equal standard.ExitCode 0 "missing optional fields are conformant"
                Expect.contains (findings morphir) ("index.md", "morphir.version-required") "Morphir requires a declaration"
        }

        test "interprets YAML scalar aliases in mapped fields" {
            let tree = BundleMother.tree [ "revenue.md", "---\nx-kind: &metric Metric\ntype: *metric\n---\n# Revenue\n" ]
            let bundle = KnowledgeBundle.FromTree tree
            let report = BundleValidation.validate (Some OkfVersion.V0_2) BundlePolicy.Standard bundle
            Expect.equal report.ExitCode 0 "the type field is the anchored nonempty string"
            let path = BundlePath.Create("revenue.md")
            Expect.equal (BundleValidation.resolveLink OkfVersion.V0_3 bundle path "#revenue") (BundleTarget.Concept path) "the document is a concept"
        }

        test "keeps malformed and unreadable members as independent findings" {
            let report = validate None BundlePolicy.Standard BundleMother.invalidMembers
            let errors = report.Findings |> Seq.filter (fun finding -> finding.Severity = DiagnosticSeverity.Error) |> Seq.toList
            Expect.equal (errors |> List.map _.PathOrNull |> List.distinct) [ "broken.md"; "unreadable.md" ] "both affected paths"
            Expect.isTrue (errors |> Seq.exists (fun item -> item.PathOrNull = "broken.md" && ValueOption.isSome item.Span)) "syntax range"
        }

        test "checks parser failures for a standalone document" {
            let parsed = OkfDocument.Parse("---\ntype: [\n---\n# Heading\n")
            let result = BundleValidation.validateDocument OkfVersion.V0_3 None parsed
            Expect.isTrue (result |> Seq.exists (fun finding -> finding.Severity = DiagnosticSeverity.Error && ValueOption.isSome finding.Span))
                "malformed frontmatter retains a ranged error without a bundle"
        }

        test "keeps conformance distinct from Morphir policy" {
            let report = validate None BundlePolicy.Morphir BundleMother.brokenLink
            Expect.equal report.Conformance BundleConformance.Conformant "OKF permits broken links and a missing index"
            Expect.equal report.ExitCode 1 "policy can fail the command"
            Expect.contains (findings report) ("metrics/revenue.md", "morphir.unresolved-link") "link policy"
            Expect.contains (report.Findings |> Seq.map _.Code) "morphir.index-required" "index policy"
        }

        test "checks authored index descriptions and source citations" {
            let report = validate None BundlePolicy.Morphir BundleMother.policyFindings
            Expect.contains (findings report) ("index.md", "morphir.index-description") "description policy"
            Expect.contains (findings report) ("metrics/revenue.md", "morphir.unknown-citation") "citation policy"
        }

        test "distinguishes both local forms, assets, missing references and external URLs" {
            let bundle = KnowledgeBundle.FromTree BundleMother.allTargets
            let source = BundlePath.Create("metrics/revenue.md")
            let resolve reference = BundleValidation.resolveLink OkfVersion.V0_3 bundle source reference
            let cost = BundleTarget.Concept(BundlePath.Create("metrics/cost.md"))
            Expect.equal (resolve "cost.md") cost "relative concept"
            Expect.equal (resolve "/metrics/cost.md") cost "rooted concept"
            Expect.equal (resolve "/asset.bin") (BundleTarget.Asset(BundlePath.Create("asset.bin"))) "asset"
            Expect.equal (resolve "missing.md") BundleTarget.Unresolved "unknown file"
            Expect.equal (resolve "https://example.org/") BundleTarget.External "external URL"
            Expect.equal (resolve "../../outside.md") BundleTarget.External "bundle escape"
        }

        test "checks reserved files when present" {
            let report = validate None BundlePolicy.Standard BundleMother.malformedReserved
            Expect.contains (findings report) ("index.md", "okf.invalid-index") "invalid index section"
            Expect.contains (findings report) ("log.md", "okf.invalid-log-date") "invalid log date"
        }

        test "applies the version-specific rule for log frontmatter" {
            let logs = BundleMother.tree [ "log.md", "---\ntype: Release History\n---\n# Changes\n\n## 2026-05-22\n\n- Changed the release notes.\n" ]
            Expect.equal (validate (Some OkfVersion.V0_2) BundlePolicy.Standard logs).ExitCode 0 "0.2 reserves the body structure"
            let current = validate (Some OkfVersion.V0_3) BundlePolicy.Standard logs
            Expect.contains (findings current) ("log.md", "okf.reserved-frontmatter") "0.3 explicitly disallows frontmatter"
        }
    ]

[<Tests>]
let physicalBundleTests =
    testList "Physical knowledge bundle" [
        testCase "does not attach enumeration cancellation to documents" (fun () ->
            use sandbox = TestSandbox.Create("knowledge-load-cancellation-")
            use cancellation = new CancellationTokenSource()
            File.WriteAllText(Path.Combine(sandbox.Work, "revenue.md"), BundleMother.concept "Revenue" "# Revenue\n")

            let bundle =
                match KnowledgeBundle.LoadDirectory(sandbox.Work, cancellation.Token) with
                | Ok loaded -> loaded
                | Result.Error error -> failtest error.Message

            cancellation.Cancel()
            let report = BundleValidation.validate None BundlePolicy.Standard bundle
            Expect.equal report.ExitCode 0 "a finished load remains independently usable")
        testCase "reports the same findings for a directory and a virtual file tree" (fun () ->
            use sandbox = TestSandbox.Create("knowledge-bundle-")
            let root = Directory.CreateDirectory(Path.Combine(sandbox.Work, "metrics")).FullName
            File.WriteAllText(Path.Combine(root, "revenue.md"), BundleMother.concept "Revenue recognized" "[Missing](missing.md)\n")

            let physical = BundleValidation.validateDirectory None BundlePolicy.Morphir sandbox.Work CancellationToken.None
            let virtualReport = validate None BundlePolicy.Morphir BundleMother.brokenLink
            Expect.equal (findings physical) (findings virtualReport) "same diagnostics in the same order")
        testCase "excludes a symlink to an outside member" (fun () ->
            use sandbox = TestSandbox.Create("knowledge-containment-")
            let root, outside = BundleMother.containedTree sandbox.Work

            try
                File.CreateSymbolicLink(Path.Combine(root, "linked.md"), outside) |> ignore
            with
            | :? IOException
            | :? UnauthorizedAccessException -> skiptest "symbolic links are not available"

            let report = BundleValidation.validateDirectory None BundlePolicy.Standard root CancellationToken.None
            Expect.equal (findings report) [ "linked.md", "okf.excluded-entry" ] "outside contents are never read")
        testCase "accepts a bundle beneath a directory alias outside its root" (fun () ->
            use sandbox = TestSandbox.Create("knowledge-root-alias-")
            let parent = Directory.CreateDirectory(Path.Combine(sandbox.Work, "actual")).FullName
            let bundle = Directory.CreateDirectory(Path.Combine(parent, "bundle")).FullName
            let alias = Path.Combine(sandbox.Work, "alias")
            File.WriteAllText(Path.Combine(bundle, "revenue.md"), BundleMother.concept "Revenue" "# Revenue\n")

            try
                Directory.CreateSymbolicLink(alias, parent) |> ignore
            with
            | :? IOException
            | :? UnauthorizedAccessException -> skiptest "symbolic links are not available"

            let report = BundleValidation.validateDirectory None BundlePolicy.Standard (Path.Combine(alias, "bundle")) CancellationToken.None
            Expect.equal report.ExitCode 0 "the caller can use an alias to name an ancestor of the bundle root")
        testCase "excludes named pipes from physical bundle entries on Linux" (fun () ->
            if not (OperatingSystem.IsLinux()) then skiptest "the fixture creates a Linux named pipe"
            use sandbox = TestSandbox.Create("knowledge-pipe-")
            let pipe = Path.Combine(sandbox.Work, "pipe.md")
            Expect.equal (UnixFixture.mkfifo(pipe, 0x180u)) 0 "creates an owner-only named pipe"

            match KnowledgeBundle.LoadDirectory(sandbox.Work, CancellationToken.None) with
            | Ok bundle -> Expect.equal bundle.Members.Length 0 "a named pipe is excluded before opening bundle members"
            | Result.Error error -> failtest error.Message)
    ]
    |> testLabel "PlatformSpecific"
