module Finos.Morphir.Commands.Workspace
open System
open System.IO
open Argu
open Fake.IO
open Fake.IO
open Serilog

type Arguments =
    | [<AltCommandLine("-d");Inherit>]Workspace_Directory of string
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<ListArgs>
    | [<CliPrefix(CliPrefix.None)>] Restore of ParseResults<RestoreArgs>
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Workspace_Directory _ -> "The custom path to the workspace"
            | List _ -> "List items in the workspace"
            | Restore _ -> "Restore the workspace"
and RestoreArgs =
    | Repository of path:string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Repository _ -> "include an additional repository for model/package resolution"
and ListArgs =
    | [<MainCommand; Last>]Target of ListTarget
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Target _ -> "list the items in the workspace of the given target type"
and ListTarget =
    | Models = 1
    | Dependencies = 2

let run json (args:ParseResults<_>) =
    let workspaceDir =
        match args.TryGetResult Workspace_Directory with
        | None -> Shell.pwd() |> DirectoryInfo.ofPath
        | Some dir -> dir |> DirectoryInfo.ofPath

    match args.GetSubCommand() with
    | List args ->
        match args.GetResult ListArgs.Target with
        | ListTarget.Models ->
            Log.Information($"Listing models from: {workspaceDir.FullName}")
            Ok ()
        | _ -> Error ()
    | Restore _ ->
        Log.Information("TODO: Restore the workspace at: {WorkspaceDir}", workspaceDir.FullName)
        Ok ()
    | Workspace_Directory _ ->
        Error ()
