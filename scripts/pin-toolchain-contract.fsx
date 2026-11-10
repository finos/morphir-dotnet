// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// Re-pins the toolchain acquisition contract fixtures in tests/Fixtures/Toolchain from a finos/morphir checkout.
//
//   dotnet fsi scripts/pin-toolchain-contract.fsx -- --source <finos/morphir checkout> --commit <sha> [--note <text>]
//
// The script reads each file with `git cat-file blob <commit>:<path>`, so the bytes are the committed blob, never a
// working-tree copy with rewritten line endings. It writes the new pin and its PROVENANCE.md (the repository, the
// full commit, each upstream path and each SHA-256) into a temporary folder beside the pin, and replaces the pin only
// when every file was fetched, so a failed fetch leaves the previous pin intact. --note adds a sentence about the
// commit, such as whether it is merged; without it, the provenance states only the commit. Run the
// Morphir.Toolchain.Tests suite afterwards: the provenance test checks the files against the table, and the manifest
// tests show what the new pin changed. Progress goes to stderr; nothing is written to stdout.

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text

let args = fsi.CommandLineArgs |> Array.skip 1

let option name =
    args
    |> Array.tryFindIndex ((=) name)
    |> Option.bind (fun index -> if index + 1 < args.Length then Some args[index + 1] else None)

let fail (message: string) =
    eprintfn "%s" message
    exit 2

let source =
    option "--source"
    |> Option.defaultWith (fun () -> fail "--source <finos/morphir checkout> is required")

let commitArgument =
    option "--commit"
    |> Option.defaultWith (fun () -> fail "--commit <sha> is required")

let note = option "--note"

let repositoryRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let pinRoot = Path.Combine(repositoryRoot, "tests", "Fixtures", "Toolchain")

let git (arguments: string list) =
    let start =
        ProcessStartInfo("git", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true)

    start.ArgumentList.Add "-C"
    start.ArgumentList.Add source

    for argument in arguments do
        start.ArgumentList.Add argument

    use proc = Process.Start start
    use buffer = new MemoryStream()
    let errors = proc.StandardError.ReadToEndAsync()
    proc.StandardOutput.BaseStream.CopyTo buffer
    proc.WaitForExit()

    if proc.ExitCode <> 0 then
        fail $"git {String.Join(' ', arguments)} failed: {errors.Result}"

    buffer.ToArray()

let text (bytes: byte array) = Encoding.UTF8.GetString(bytes).Trim()

let commit = git [ "rev-parse"; "--verify"; $"{commitArgument}^{{commit}}" ] |> text

// -z: NUL-separated and unquoted, so non-ASCII and unusual names arrive exactly as stored.
let listed prefix =
    git [ "ls-tree"; "-r"; "-z"; "--name-only"; commit; "--"; prefix ]
    |> Encoding.UTF8.GetString
    |> fun listing -> listing.Split('\000', StringSplitOptions.RemoveEmptyEntries)

// Local folder under tests/Fixtures/Toolchain -> upstream path prefix. Files keep their path below the prefix.
let trees =
    [ "fixtures/0.1.0-draft.1", "docs/spec/toolchain/fixtures/0.1.0-draft.1"
      "workflows/fsharp-compile-generate", "spec/toolchain/workflows/fsharp-compile-generate" ]

// Single files: local path -> upstream path.
let singles =
    [ "LICENSE", "LICENSE"
      "NOTICE", "NOTICE"
      for page in [ "index"; "toolset-recipe"; "toolchain-request"; "toolchain-lock"; "installation-receipt"; "manager-result"; "readiness-workflow" ] do
          $"pages/{page}.md", $"docs/spec/toolchain/{page}.md"
      for schema in [ "toolset-recipe"; "toolchain-request"; "toolchain-lock"; "toolchain-receipt"; "morphirup-result"; "toolchain-workflow-case" ] do
          $"schemas/{schema}-0.1.0-draft.1.schema.json", $"website/static/schemas/{schema}-0.1.0-draft.1.schema.json" ]

let files =
    [ yield! singles
      for local, upstream in trees do
          for path in listed upstream do
              local + path.Substring(upstream.Length), path ]
    |> List.sortBy (fun (local, _) -> local)

let staging = pinRoot + ".pinning"

if Directory.Exists staging then
    Directory.Delete(staging, true)

let rows =
    [ for local, upstream in files do
          let bytes = git [ "cat-file"; "blob"; $"{commit}:{upstream}" ]
          let target = Path.Combine(staging, local.Replace('/', Path.DirectorySeparatorChar))
          Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
          File.WriteAllBytes(target, bytes)
          let digest = SHA256.HashData bytes |> Convert.ToHexStringLower
          eprintfn "%s %s" digest local
          $"| `{local}` | `{upstream}` | `{digest}` |" ]

let pinNote = note |> Option.map (fun text -> [ ""; text ]) |> Option.defaultValue []

let provenance =
    String.Join(
        "\n",
        [ "# Toolchain acquisition contract fixtures"
          ""
          "Copied unchanged from the toolchain acquisition contracts of finos/morphir. They are the conformance suite for"
          "`Morphir.Toolchain` and `morphirup`: tests read the case manifests and must reproduce every outcome, code and path."
          "Do not edit these files by hand. `.gitattributes` keeps their bytes exact, and"
          "`tests/Morphir.Toolchain.Tests/ProvenanceTests.fs` checks every file against the table below."
          ""
          "Repository: https://github.com/finos/morphir"
          ""
          $"Commit: `{commit}`"
          yield! pinNote
          ""
          "To re-pin, check out the wanted commit of finos/morphir and run"
          ""
          "```powershell"
          "dotnet fsi scripts/pin-toolchain-contract.fsx -- --source <finos/morphir checkout> --commit <sha>"
          "```"
          ""
          "then run `Morphir.Toolchain.Tests`. The script replaces this folder and this file."
          ""
          "| Local file | Upstream path | SHA-256 |"
          "| --- | --- | --- |"
          yield! rows
          "" ]
    )

File.WriteAllText(Path.Combine(staging, "PROVENANCE.md"), provenance, UTF8Encoding(false))

// Every file is in the staging folder: replace the previous pin.
if Directory.Exists pinRoot then
    Directory.Delete(pinRoot, true)

Directory.Move(staging, pinRoot)
eprintfn "Pinned %d files from %s at %s into %s" files.Length source commit pinRoot
