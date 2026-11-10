// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The <c>morphirup</c> command line for this slice: <c>--version</c>, plan-only <c>setup --plan</c> and the project
/// checks of <c>doctor</c>, all over local files. Every command writes exactly one manager result document to standard
/// output and progress to standard error. Nothing here downloads, installs, probes or activates anything.
/// </summary>
module Morphirup.Cli

open System
open System.IO
open Morphir.Toolchain

/// What a run may touch: its arguments, its streams, the working directory and the platform it runs on. Tests supply
/// their own.
type Io =
    { Out: Stream
      Error: TextWriter
      CurrentDirectory: string
      Platform: Platform option }

/// Exit codes, as AGENTS.md defines them for CLI tools.
module ExitCode =
    /// A planned or succeeded result.
    let ok = 0
    /// A failed result: a document was refused or a check failed.
    let failed = 1
    /// An invocation or file-system error; no result document is written.
    let usage = 2

exception private UsageError of string

let private usage message = raise (UsageError message)

let private helpText =
    """morphirup: the standalone Morphir toolchain manager (this build reads documents and plans only)

Usage:
  morphirup --version
  morphirup setup --plan --recipe <file> --repository-id <sha256> [request] [options]
  morphirup doctor [--project <dir>] [--request <file>] [--lock <file>] [--recipe <file>] [--platform <rid>]

Request, one of:
  --project <dir>         Read <dir>/.morphir/toolchain.toml (default: the current directory)
  --request <file>        Read this request file
  --implementation <id> --deployment <id> --preset <id> (--channel <channel> | --version <version>)

setup options:
  --plan                  Required: report the plan; acquisition and installation are not implemented yet
  --platform <rid>        Plan for this platform instead of the current one
  --expect-plan <sha256>  Refuse a plan whose planSha256 differs
  --recipe-target <path>  The recipe's target path (default: toolchains/recipes/<preset>/<version>.json)
  --artifact-sizes <file> JSON object of target path -> {"downloadBytes", "installedBytes"}
  --json                  Accepted; JSON is the only output format of this build

Provisional for this slice, until authenticated acquisition exists: --recipe, --repository-id, --recipe-target,
--artifact-sizes, --request and --lock.
"""

/// Parsed options: flags and single-valued options, each at most once.
type private Options =
    { Flags: Set<string>
      Values: Map<string, string> }

    member this.Has flag = this.Flags.Contains flag
    member this.TryGet name = this.Values.TryFind name

let private parse (known: string list) (flags: string list) (args: string list) =
    let rec go (options: Options) rest =
        match rest with
        | [] -> options
        | arg :: tail when List.contains arg flags ->
            if options.Flags.Contains arg then usage $"{arg} is given twice"
            go { options with Flags = options.Flags.Add arg } tail
        | arg :: value :: tail when List.contains arg known ->
            if options.Values.ContainsKey arg then usage $"{arg} is given twice"
            go { options with Values = options.Values.Add(arg, value) } tail
        | [ arg ] when List.contains arg known -> usage $"{arg} needs a value"
        | arg :: _ -> usage $"unknown option {arg}"

    go { Flags = Set.empty; Values = Map.empty } args

let private readFile (io: Io) (path: string) =
    let full = Path.GetFullPath(path, io.CurrentDirectory)

    try
        io.Error.WriteLine $"morphirup: reading {full}"
        File.ReadAllBytes full
    with
    | :? FileNotFoundException
    | :? DirectoryNotFoundException -> usage $"{full} does not exist"
    | :? UnauthorizedAccessException
    | :? IOException as error -> usage $"{full} cannot be read: {error.Message}"

let private platformOption (io: Io) (options: Options) =
    match options.TryGet "--platform" with
    | Some text ->
        match Platform.tryParse text with
        | Ok platform -> platform
        | Error _ -> usage $"--platform {text} is not one of the eight platform ids"
    | None ->
        match io.Platform with
        | Some platform -> platform
        | None -> usage "this machine is not one of the eight supported platforms; pass --platform"

let private validatedOption (name: string) (parse: string -> Result<'T, ReaderCode>) (options: Options) =
    options.TryGet name
    |> Option.map (fun text ->
        match parse text with
        | Ok value -> value
        | Error _ -> usage $"{name} {text} is not valid")

let private projectFile (io: Io) (options: Options) (relative: string) =
    let project = defaultArg (options.TryGet "--project") "."
    Path.Combine(Path.GetFullPath(project, io.CurrentDirectory), ".morphir", relative)

/// The request bytes, from a file or from the selector options.
let private requestBytes (io: Io) (options: Options) =
    let selectors = [ "--implementation"; "--deployment"; "--preset"; "--channel"; "--version" ]
    let given = selectors |> List.filter (options.TryGet >> Option.isSome)

    match options.TryGet "--request", given with
    | Some _, _ :: _ -> usage "give either --request or the selector options, not both"
    | Some file, [] ->
        if options.TryGet "--project" |> Option.isSome then usage "give either --request or --project, not both"
        readFile io file
    | None, [] -> readFile io (projectFile io options "toolchain.toml")
    | None, _ ->
        if options.TryGet "--project" |> Option.isSome then usage "give either --project or the selector options, not both"

        let required name =
            match validatedOption name Token.tryParse options with
            | Some token -> token
            | None -> usage $"{name} is required with the selector options"

        let release =
            match validatedOption "--channel" Channel.tryParse options, validatedOption "--version" SemVer.tryParse options with
            | Some channel, None -> HostRelease.Channel channel
            | None, Some version -> HostRelease.Exact version
            | _ -> usage "give exactly one of --channel and --version"

        let host =
            { Implementation = required "--implementation"
              Deployment = required "--deployment"
              Release = release }

        RequestDocument.write host (required "--preset") None

let private writeResult (io: Io) (result: ManagerResult) =
    let bytes = ResultDocument.write result
    io.Out.Write(bytes, 0, bytes.Length)
    io.Out.Flush()

    match result.Outcome with
    | Outcome.Failed -> ExitCode.failed
    | Outcome.Planned
    | Outcome.Succeeded -> ExitCode.ok

let private emit (io: Io) command outcome plan diagnostics =
    match ResultDocument.create command outcome plan None diagnostics with
    | Ok result -> writeResult io result
    | Error reason -> failwith $"morphirup built an inconsistent result: {reason}"

let private failedWith (io: Io) command diagnostics =
    emit io command Outcome.Failed None diagnostics

/// The provisional artifact-size file: a JSON object from target path to its download and installed bytes.
let private artifactSizes (io: Io) (options: Options) : TargetPath -> ArtifactSize option =
    match options.TryGet "--artifact-sizes" with
    | None -> fun _ -> None
    | Some file ->
        let bytes = readFile io file

        let entries =
            match Syntax.parseJson bytes with
            | Ok(Value.Object members) ->
                members
                |> List.map (fun (target, value) ->
                    let number name =
                        match value with
                        | Value.Object fields ->
                            match fields |> List.tryFind (fst >> (=) name) with
                            | Some(_, Value.Number spelling) ->
                                match UInt64.TryParse(spelling, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                                | true, n -> n
                                | false, _ -> usage $"--artifact-sizes: {target}.{name} is not a non-negative integer"
                            | _ -> usage $"--artifact-sizes: {target} has no {name}"
                        | _ -> usage $"--artifact-sizes: {target} is not an object"

                    target,
                    { DownloadBytes = number "downloadBytes"
                      InstalledBytes = number "installedBytes" })
                |> Map.ofList
            | _ -> usage "--artifact-sizes is not a JSON object"

        fun target -> entries.TryFind target.Text

let private setup (io: Io) (args: string list) =
    let options =
        parse
            [ "--project"
              "--request"
              "--implementation"
              "--deployment"
              "--preset"
              "--channel"
              "--version"
              "--platform"
              "--recipe"
              "--repository-id"
              "--recipe-target"
              "--artifact-sizes"
              "--expect-plan" ]
            [ "--plan"; "--json"; "--yes" ]
            args

    if options.Has "--yes" || not (options.Has "--plan") then
        usage "this build plans only: acquisition, installation and activation are not implemented yet; pass --plan"

    let platform = platformOption io options

    let repository =
        match validatedOption "--repository-id" Sha256.tryParse options with
        | Some id -> id
        | None -> usage "--repository-id <sha256> is required until authenticated acquisition exists"

    let expectPlan = validatedOption "--expect-plan" Sha256.tryParse options

    let recipeFile =
        match options.TryGet "--recipe" with
        | Some file -> file
        | None -> usage "--recipe <file> is required until authenticated acquisition exists"

    let request = RequestDocument.read (requestBytes io options)
    let recipe = RecipeDocument.read (readFile io recipeFile)

    match request, recipe with
    | Error refusal, _ -> failedWith io Command.Setup [ Diagnostics.ofReaderRefusal DocumentKind.Request refusal ]
    | _, Error refusal -> failedWith io Command.Setup [ Diagnostics.ofReaderRefusal DocumentKind.Recipe refusal ]
    | Ok request, Ok recipe ->
        let recipeTarget =
            match options.TryGet "--recipe-target" with
            | Some text ->
                match TargetPath.tryParse text with
                | Ok target -> target
                | Error _ -> usage $"--recipe-target {text} is not a portable target path"
            | None ->
                match TargetPath.tryParse $"toolchains/recipes/{recipe.Preset.Text}/{recipe.Version.Text}.json" with
                | Ok target -> target
                | Error _ -> usage "the recipe's preset and version do not make a portable target path; pass --recipe-target"

        io.Error.WriteLine $"morphirup: planning {Platform.name platform} from recipe {recipe.Preset.Text} {recipe.Version.Text}"

        let planned =
            Planning.plan
                { Recipe = recipe
                  RecipeRepository = repository
                  RecipeTarget = recipeTarget
                  Request = request
                  Platform = platform
                  Sizes = artifactSizes io options
                  MetadataRefresh = MetadataRefresh.Required }

        match planned with
        | Error(PlanFailure.Unresolved failure) -> failedWith io Command.Setup [ Diagnostics.ofResolutionFailure failure ]
        | Error(PlanFailure.SizeUnknown target) ->
            usage $"the size of {target.Text} is unknown; give it in --artifact-sizes until authenticated metadata provides it"
        | Ok(digested, _) ->
            match expectPlan with
            | Some expected when expected <> digested.Sha256 ->
                let refusal =
                    { Code = ReaderCode.InconsistentDocument
                      Path = DocumentPath.root.Child "planSha256" }

                let diagnostic =
                    { Diagnostics.ofReaderRefusal DocumentKind.Result refusal with
                        Message =
                            match NonBlankText.tryParse $"The plan's digest is {digested.Sha256.Text}, not the expected {expected.Text}. Nothing was changed." with
                            | Ok message -> message
                            | Error _ -> failwith "unreachable" }

                emit io Command.Setup Outcome.Failed (Some digested) [ diagnostic ]
            | _ -> emit io Command.Setup Outcome.Planned (Some digested) []

let private doctor (io: Io) (args: string list) =
    let options =
        parse [ "--project"; "--request"; "--lock"; "--recipe"; "--platform" ] [ "--json" ] args

    let platform = platformOption io options

    let requestFile =
        defaultArg (options.TryGet "--request") (projectFile io options "toolchain.toml")

    let lockFile =
        defaultArg (options.TryGet "--lock") (projectFile io options "toolchain.lock.json")

    let requestBytes = readFile io requestFile
    let lockBytes = readFile io lockFile
    let request = RequestDocument.read requestBytes
    let lock = LockDocument.read lockBytes
    let recipe = options.TryGet "--recipe" |> Option.map (readFile io >> RecipeDocument.read)

    let diagnostics =
        [ match request with
          | Error refusal -> Diagnostics.ofReaderRefusal DocumentKind.Request refusal
          | Ok _ -> ()
          match lock with
          | Error refusal -> Diagnostics.ofReaderRefusal DocumentKind.Lock refusal
          | Ok lock ->
              match Bindings.lockBindsRequest lock requestBytes with
              | Error refusal -> Diagnostics.ofBindingRefusal refusal
              | Ok() -> ()

              match Bindings.lockedClosure lock platform with
              | Error refusal -> Diagnostics.ofBindingRefusal refusal
              | Ok _ -> ()

              match recipe with
              | Some(Error refusal) -> Diagnostics.ofReaderRefusal DocumentKind.Recipe refusal
              | Some(Ok recipe) ->
                  match Bindings.lockMatchesRecipe lock recipe with
                  | Error refusal -> Diagnostics.ofBindingRefusal refusal
                  | Ok() -> ()
              | None -> () ]

    let outcome =
        if diagnostics |> List.exists (fun d -> d.Severity = Severity.Error) then
            Outcome.Failed
        else
            Outcome.Succeeded

    emit io Command.Doctor outcome None diagnostics

/// Runs one command line and returns its exit code. Usage and file errors write a message to standard error and no
/// result document.
let run (io: Io) (args: string list) : int =
    try
        match args with
        | [ "--version" ] ->
            let bytes = Text.Encoding.UTF8.GetBytes(VersionInfo.Version + "\n")
            io.Out.Write(bytes, 0, bytes.Length)
            io.Out.Flush()
            ExitCode.ok
        | [ "--help" ]
        | [ "-h" ] ->
            io.Error.Write helpText
            ExitCode.ok
        | "setup" :: rest -> setup io rest
        | "doctor" :: rest -> doctor io rest
        | [] -> usage "no command given"
        | command :: _ -> usage $"{command} is not a command of this build"
    with UsageError message ->
        io.Error.WriteLine $"morphirup: {message}"
        io.Error.WriteLine "Run morphirup --help for the commands of this build."
        ExitCode.usage
