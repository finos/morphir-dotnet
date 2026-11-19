// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

// Installs the dotnet/skills plugins that .claude/settings.json enables, for Claude Code and Codex.
//
//   dotnet fsi scripts/setup-dotnet-skills.fsx          # install what is missing
//   dotnet fsi scripts/setup-dotnet-skills.fsx --check  # exit 1 if a plugin is missing
//
// .claude/settings.json is the one list of plugins. Claude Code reads it directly.
// Codex has no project-level plugin list: each developer installs the plugins in ~/.codex,
// and this script does that. A tool that is not on the PATH is skipped.

open System
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text.Json

let marketplace = "dotnet-agent-skills"

/// The folders of dotnet/skills that Codex needs. A full clone is about 600 MB and
/// does not complete in the 30 seconds that Codex allows.
let codexSparsePaths = [ ".agents/plugins"; "plugins" ]

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let check = fsi.CommandLineArgs |> Array.contains "--check"
let onWindows = RuntimeInformation.IsOSPlatform OSPlatform.Windows

/// Runs a tool from the repository root. Returns None when the tool is not on the PATH.
let run (tool: string) (args: string list) : (int * string) option =
    // On Windows the tools are .cmd shims, which only cmd.exe can start.
    let file, arguments =
        if onWindows then
            "cmd.exe", "/c" :: tool :: args
        else
            tool, args

    let info =
        ProcessStartInfo(file, WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false)

    for argument in arguments do
        info.ArgumentList.Add argument

    try
        use proc = Process.Start info
        let output = proc.StandardOutput.ReadToEnd()
        proc.WaitForExit()
        Some(proc.ExitCode, output)
    with :? Win32Exception ->
        None

let isOnPath (tool: string) =
    match run tool [ "--version" ] with
    | Some(0, _) -> true
    | _ -> false

/// Parses JSON output. Returns None when the output is not JSON, for example an error message.
let tryParse (json: string) =
    try
        Some(JsonDocument.Parse(json).RootElement)
    with :? JsonException ->
        None

let field (name: string) (element: JsonElement) =
    match element.TryGetProperty name with
    | true, value -> Some value
    | _ -> None

let isTrue (name: string) (element: JsonElement) =
    field name element
    |> Option.exists (fun value -> value.ValueKind = JsonValueKind.True)

let hasText (name: string) (text: string) (element: JsonElement) =
    field name element |> Option.exists (fun value -> value.GetString() = text)

let settings =
    JsonDocument
        .Parse(File.ReadAllText(Path.Combine(root, ".claude", "settings.json")))
        .RootElement

/// Plugin ids such as "dotnet-msbuild@dotnet-agent-skills".
let wanted =
    [ for plugin in settings.GetProperty("enabledPlugins").EnumerateObject() do
          if
              plugin.Name.EndsWith("@" + marketplace)
              && plugin.Value.ValueKind = JsonValueKind.True
          then
              yield plugin.Name ]

/// The GitHub repository of the marketplace, "dotnet/skills".
let repository =
    settings
        .GetProperty("extraKnownMarketplaces")
        .GetProperty(marketplace)
        .GetProperty("source")
        .GetProperty("repo")
        .GetString()

let claudeEnabled () =
    match run "claude" [ "plugin"; "list"; "--json" ] |> Option.bind (snd >> tryParse) with
    | Some plugins when plugins.ValueKind = JsonValueKind.Array ->
        set
            [ for plugin in plugins.EnumerateArray() do
                  if isTrue "enabled" plugin then
                      match field "id" plugin with
                      | Some id -> yield id.GetString()
                      | None -> () ]
    | _ -> Set.empty

let codexEnabled () =
    // The command fails when the marketplace is not configured: no plugin is installed then.
    match
        run "codex" [ "plugin"; "list"; "--marketplace"; marketplace; "--json" ]
        |> Option.bind (snd >> tryParse)
        |> Option.bind (field "installed")
    with
    | Some plugins ->
        set
            [ for plugin in plugins.EnumerateArray() do
                  if isTrue "enabled" plugin then
                      match field "pluginId" plugin with
                      | Some id -> yield id.GetString()
                      | None -> () ]
    | None -> Set.empty

/// The dotnet-msbuild plugin declares the `binlog` MCP server.
let codexHasBinlog () =
    match run "codex" [ "mcp"; "list"; "--json" ] |> Option.bind (snd >> tryParse) with
    | Some servers when servers.ValueKind = JsonValueKind.Array ->
        servers.EnumerateArray()
        |> Seq.exists (fun server -> hasText "name" "binlog" server && isTrue "enabled" server)
    | _ -> false

let mutable failed = false

let must (tool: string) (args: string list) =
    match run tool args with
    | Some(0, _) -> ()
    | _ ->
        let command = String.Join(" ", tool :: args)
        eprintfn $"error: `{command}` failed"
        failed <- true

let report (tool: string) (enabled: Set<string>) =
    for plugin in wanted do
        if enabled.Contains plugin then
            eprintfn $"ok       {tool, -7} {plugin}"
        else
            eprintfn $"missing  {tool, -7} {plugin}"
            failed <- true

let setUp (tool: string) (enabled: unit -> Set<string>) (install: string list -> unit) =
    if not (isOnPath tool) then
        eprintfn $"skip     {tool, -7} not on the PATH"
    else
        let before = enabled ()
        let missing = wanted |> List.filter (fun plugin -> not (before.Contains plugin))

        if check || missing.IsEmpty then
            report tool before
        else
            install missing
            report tool (enabled ())

setUp "claude" claudeEnabled (fun missing ->
    must "claude" [ "plugin"; "marketplace"; "add"; repository; "--scope"; "project" ]

    for plugin in missing do
        must "claude" [ "plugin"; "install"; plugin; "--scope"; "project" ])

setUp "codex" codexEnabled (fun missing ->
    must
        "codex"
        ([ "plugin"; "marketplace"; "add"; repository ]
         @ (codexSparsePaths |> List.collect (fun path -> [ "--sparse"; path ])))

    for plugin in missing do
        must "codex" [ "plugin"; "add"; plugin ])

if isOnPath "codex" && wanted |> List.contains ("dotnet-msbuild@" + marketplace) then
    if codexHasBinlog () then
        eprintfn "ok       codex   binlog MCP server"
    else
        eprintfn "missing  codex   binlog MCP server"
        failed <- true

if failed then
    if check then
        eprintfn "Run `dotnet fsi scripts/setup-dotnet-skills.fsx` to install the missing plugins."

    exit 1
