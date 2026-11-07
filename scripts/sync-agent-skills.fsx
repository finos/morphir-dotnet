// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

// Copies the project-owned skills from their canonical folder, .agents/skills/<name>/,
// to the skill folders of the coding agents that do not read .agents/skills.
//
//   dotnet fsi scripts/sync-agent-skills.fsx          # write the copies
//   dotnet fsi scripts/sync-agent-skills.fsx --check  # exit 1 if a copy differs
//
// Readers of .agents/skills: Codex, GitHub Copilot, Cursor, Muse Code.
// Copies:                    .claude/skills (Claude Code), .grok/skills (Grok Build).
// OpenSpec writes its own skills to every folder; this script does not touch them.

open System
open System.IO

/// Skills that this repository owns. Edit them only under .agents/skills/<name>/.
let ownedSkills = [ "fsharp-csharp-interop" ]

/// Skill folders of the agents that do not read .agents/skills.
let mirrors = [ ".claude/skills"; ".grok/skills" ]

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let check = fsi.CommandLineArgs |> Array.contains "--check"

let filesUnder (dir: string) =
    if Directory.Exists dir then
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
        |> Seq.map (fun file -> Path.GetRelativePath(dir, file).Replace('\\', '/'))
        |> Set.ofSeq
    else
        Set.empty

let sameBytes (a: string) (b: string) =
    File.Exists a && File.Exists b && (File.ReadAllBytes a = File.ReadAllBytes b)

/// Returns the differences between the canonical skill and one copy.
let differences (source: string) (target: string) =
    let sourceFiles = filesUnder source
    let targetFiles = filesUnder target
    [ for file in Set.union sourceFiles targetFiles do
          match sourceFiles.Contains file, targetFiles.Contains file with
          | true, false -> yield $"missing  {file}"
          | false, true -> yield $"extra    {file}"
          | _ when not (sameBytes (Path.Combine(source, file)) (Path.Combine(target, file))) -> yield $"differs  {file}"
          | _ -> () ]

let copy (source: string) (target: string) =
    if Directory.Exists target then Directory.Delete(target, true)
    for file in filesUnder source do
        let destination = Path.Combine(target, file)
        Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
        File.Copy(Path.Combine(source, file), destination)

let mutable failed = false

for skill in ownedSkills do
    let source = Path.Combine(root, ".agents", "skills", skill)
    if not (File.Exists(Path.Combine(source, "SKILL.md"))) then
        eprintfn $"error: {source}/SKILL.md does not exist"
        failed <- true
    else
        for mirror in mirrors do
            let target = Path.Combine(root, mirror, skill)
            match differences source target with
            | [] -> eprintfn $"ok       {mirror}/{skill}"
            | found when check ->
                failed <- true
                eprintfn $"drift    {mirror}/{skill}"
                for line in found do eprintfn $"           {line}"
            | _ ->
                copy source target
                eprintfn $"synced   {mirror}/{skill}"

if failed then
    if check then eprintfn "Run: dotnet fsi scripts/sync-agent-skills.fsx"
    exit 1
