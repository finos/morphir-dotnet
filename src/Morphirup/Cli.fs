// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The <c>morphirup</c> command line for this slice: <c>--version</c>, plan-only <c>setup --plan</c> and the project
/// checks of <c>doctor</c>, all over local files. A command that runs writes exactly one manager result document to
/// standard output and its progress to standard error. Nothing here downloads, installs, probes or activates anything.
/// </summary>
module Morphirup.Cli

open System
open System.IO
open System.Threading.Tasks
open Morphir.Toolchain

/// What a run may touch: its streams, the working directory and the platform it runs on. Tests supply their own.
type Io =
    { Out: Stream
      Error: TextWriter
      CurrentDirectory: string
      Platform: Platform option }

/// Exit codes. 0 and 1 follow the result's outcome; 2 and 3 write no result document (provisional: the result contract
/// has no code for an invocation error or an internal failure).
module ExitCode =
    /// A planned or succeeded result.
    let ok = 0
    /// A failed result: a document was refused or a check failed.
    let failed = 1
    /// An invocation or file error; no result document is written.
    let usage = 2
    /// An internal error in morphirup; no result document is written.
    let internalError = 3

/// The provisional bounds on what morphirup reads, until the contract states them.
module Limits =
    let request = 1L <<< 20
    let lock = 1L <<< 20
    let recipe = 16L <<< 20
    let sizes = 1L <<< 20

/// An invocation or file error, reported on standard error with exit code 2.
type private Usage = Usage of string

let private usage message : Result<'T, Usage> = Error(Usage message)

type private Builder() =
    member _.Return value : Result<'T, Usage> = Ok value
    member _.ReturnFrom(value: Result<'T, Usage>) = value
    member _.Bind(value: Result<'T, Usage>, next) = Result.bind next value
    member _.Zero() : Result<unit, Usage> = Ok()
    member _.Delay(body: unit -> Result<'T, Usage>) = body
    member _.Run(body: unit -> Result<'T, Usage>) = body ()
    member _.Combine(first: Result<unit, Usage>, rest: unit -> Result<'T, Usage>) = Result.bind rest first

let private invocation = Builder()

let private helpText =
    """morphirup: the standalone Morphir toolchain manager. This build reads documents and plans only: it downloads,
installs and activates nothing.

Usage:
  morphirup --version
  morphirup setup --plan --recipe <file> --repository-id <sha256> --artifact-sizes <file> [request] [options]
  morphirup doctor [--project <dir>] [--request <file>] [--lock <file>] [--platform <rid>]
                   [--recipe <file> --repository-id <sha256>]

Request, one of:
  --project <dir>         (provisional) Read <dir>/.morphir/toolchain.toml; default the current directory, no discovery
  --request <file>        (provisional) Read this request file
  --implementation <id> --deployment <id> --preset <id> (--channel <channel> | --version <version>)

setup options:
  --plan                  Required: report the plan. Without it, or with --yes, setup refuses
  --platform <rid>        Plan for this platform instead of the current one
  --expect-plan <sha256>  Refuse a plan whose planSha256 differs (exit 1; the diagnostic is provisional)
  --recipe <file>         (provisional) The recipe document; nothing authenticates it in this build
  --repository-id <sha256> (provisional) The repository the recipe and its artifacts come from
  --recipe-target <path>  (provisional) The recipe's target path; default toolchains/recipes/<preset>/<version>.json
  --artifact-sizes <file> (provisional) JSON object: target path -> {"downloadBytes": n, "installedBytes": n}
  --json                  Accepted; JSON is the only output format of this build

doctor options:
  --lock <file>           (provisional) Read this lock file; default <project>/.morphir/toolchain.lock.json
  --recipe <file>, --repository-id <sha256>  Also check the lock against this recipe and repository

Exit codes: 0 planned or succeeded, 1 failed (one result document on stdout either way). Provisional: 2 for an
invocation or file error and 3 for an internal error, with nothing on stdout. Files are read only when they are regular
files, up to 1 MiB for a request, lock or sizes file and 16 MiB for a recipe (provisional bounds).
"""

/// Parsed options: flags and single-valued options, each at most once.
type private Options =
    { Flags: Set<string>
      Values: Map<string, string> }

    member this.Has flag = this.Flags.Contains flag
    member this.TryGet name = this.Values.TryFind name

/// <summary>Parses options. An option's value may not itself start with <c>--</c>, so a forgotten value is reported
/// instead of swallowing the next option; a file named <c>--x</c> is given as <c>./--x</c>.</summary>
let private parse (known: string list) (flags: string list) (args: string list) : Result<Options, Usage> =
    let rec go (options: Options) rest =
        match rest with
        | [] -> Ok options
        | arg :: tail when List.contains arg flags ->
            if options.Flags.Contains arg then usage $"{arg} is given twice"
            else go { options with Flags = options.Flags.Add arg } tail
        | arg :: value :: tail when List.contains arg known && not (value.StartsWith("--", StringComparison.Ordinal)) ->
            if options.Values.ContainsKey arg then usage $"{arg} is given twice"
            else go { options with Values = options.Values.Add(arg, value) } tail
        | arg :: _ when List.contains arg known -> usage $"{arg} needs a value"
        | arg :: _ -> usage $"unknown option {arg}"

    go { Flags = Set.empty; Values = Map.empty } args

/// <summary>
/// Reads a file the way repository content must be read: the path, after following links, must be a regular file
/// (not a directory, a device or a pipe), and at most <paramref name="limit"/> bytes are read. Windows device paths and
/// Unix device trees are refused by name; anything else that does not open within five seconds, such as a FIFO, is
/// refused as not a regular file.
/// </summary>
let private readFile (io: Io) (limit: int64) (path: string) : Result<byte array, Usage> =
    let full = Path.GetFullPath(path, io.CurrentDirectory)
    io.Error.WriteLine $"morphirup: reading {full}"

    let deviceName =
        full.StartsWith(@"\\.\", StringComparison.Ordinal)
        || (not (OperatingSystem.IsWindows())
            && [ "/dev/"; "/proc/"; "/sys/" ] |> List.exists (fun prefix -> full.StartsWith(prefix, StringComparison.Ordinal)))

    if deviceName then
        usage $"{full} is not a regular file"
    else
        try
            let info = FileInfo full

            let target =
                if info.Exists && not (isNull info.LinkTarget) then
                    match info.ResolveLinkTarget true with
                    | :? FileInfo as linked -> linked
                    | _ -> info
                else
                    info

            if Directory.Exists full || Directory.Exists target.FullName then
                usage $"{full} is a directory, not a file"
            elif not target.Exists then
                usage $"{full} does not exist"
            elif target.Attributes.HasFlag FileAttributes.Device then
                usage $"{full} is not a regular file"
            else
                // At most limit + 1 bytes are read, so an endless file stops one byte past the limit.
                let read =
                    Task.Run<byte array>(
                        Func<byte array>(fun () ->
                            use stream = new FileStream(target.FullName, FileMode.Open, FileAccess.Read, FileShare.Read)
                            use output = new MemoryStream()
                            let chunk = Array.zeroCreate<byte> 81920
                            let mutable reading = true

                            while reading && output.Length <= limit do
                                let wanted = int (min (int64 chunk.Length) (limit + 1L - output.Length))
                                let count = stream.Read(chunk, 0, wanted)
                                if count = 0 then reading <- false else output.Write(chunk, 0, count)

                            output.ToArray())
                    )

                if not (read.Wait(TimeSpan.FromSeconds 5.0)) then
                    usage $"{full} is not a regular file: it did not open within five seconds"
                else
                    let bytes = read.Result

                    if int64 bytes.Length > limit then
                        usage $"{full} is larger than the provisional limit of {limit} bytes"
                    else
                        Ok bytes
        with
        | :? AggregateException as error when (error.InnerException :? IOException || error.InnerException :? UnauthorizedAccessException) ->
            let inner = error.InnerException |> Option.ofObj |> Option.map _.Message |> Option.defaultValue error.Message
            usage $"{full} cannot be read: {inner}"
        | :? IOException
        | :? UnauthorizedAccessException as error -> usage $"{full} cannot be read: {error.Message}"

let private platformOption (io: Io) (options: Options) : Result<Platform, Usage> =
    match options.TryGet "--platform", io.Platform with
    | Some text, _ ->
        match Platform.tryParse text with
        | Ok platform -> Ok platform
        | Error _ -> usage $"--platform {text} is not one of the eight platform ids"
    | None, Some platform -> Ok platform
    | None, None -> usage "this machine is not one of the eight supported platforms; pass --platform"

let private validatedOption (name: string) (parse: string -> Result<'T, ReaderCode>) (options: Options) : Result<'T option, Usage> =
    match options.TryGet name with
    | None -> Ok None
    | Some text ->
        match parse text with
        | Ok value -> Ok(Some value)
        | Error _ -> usage $"{name} {text} is not valid"

let private requiredOption name parse options (why: string) : Result<'T, Usage> =
    validatedOption name parse options
    |> Result.bind (function
        | Some value -> Ok value
        | None -> usage $"{name} is required {why}")

let private projectFile (io: Io) (options: Options) (relative: string) =
    let project = defaultArg (options.TryGet "--project") "."
    Path.Combine(Path.GetFullPath(project, io.CurrentDirectory), ".morphir", relative)

/// The request bytes, from a file or from the selector options.
let private requestBytes (io: Io) (options: Options) : Result<byte array, Usage> =
    let selectors = [ "--implementation"; "--deployment"; "--preset"; "--channel"; "--version" ]
    let given = selectors |> List.filter (options.TryGet >> Option.isSome)
    let project = options.TryGet "--project" |> Option.isSome

    match options.TryGet "--request", given with
    | Some _, _ :: _ -> usage "give either --request or the selector options, not both"
    | Some _, [] when project -> usage "give either --request or --project, not both"
    | Some file, [] -> readFile io Limits.request file
    | None, [] -> readFile io Limits.request (projectFile io options "toolchain.toml")
    | None, _ :: _ when project -> usage "give either --project or the selector options, not both"
    | None, _ :: _ ->
        invocation {
            let required name = requiredOption name Token.tryParse options "with the selector options"
            let! implementation = required "--implementation"
            let! deployment = required "--deployment"
            let! preset = required "--preset"
            let! channel = validatedOption "--channel" Channel.tryParse options
            let! version = validatedOption "--version" SemVer.tryParse options

            let! release =
                match channel, version with
                | Some channel, None -> Ok(HostRelease.Channel channel)
                | None, Some version -> Ok(HostRelease.Exact version)
                | Some _, Some _
                | None, None -> usage "give exactly one of --channel and --version"

            return
                RequestDocument.write
                    { Implementation = implementation
                      Deployment = deployment
                      Release = release }
                    preset
                    None
        }

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
let private artifactSizes (io: Io) (options: Options) : Result<TargetPath -> ArtifactSize option, Usage> =
    match options.TryGet "--artifact-sizes" with
    | None -> Ok(fun _ -> None)
    | Some file ->
        invocation {
            let! bytes = readFile io Limits.sizes file

            let number (target: string) (fields: (string * Value) list) name =
                match fields |> List.tryFind (fst >> (=) name) with
                | Some(_, Value.Number spelling) ->
                    match UInt64.TryParse(spelling, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                    | true, n -> Ok n
                    | false, _ -> usage $"--artifact-sizes: {target}.{name} is not a non-negative integer"
                | _ -> usage $"--artifact-sizes: {target} has no {name}"

            let! entries =
                match Syntax.parseJson bytes with
                | Ok(Value.Object members) ->
                    members
                    |> List.fold
                        (fun (state: Result<Map<string, ArtifactSize>, Usage>) (target, value) ->
                            state
                            |> Result.bind (fun map ->
                                match value with
                                | Value.Object fields ->
                                    number target fields "downloadBytes"
                                    |> Result.bind (fun download ->
                                        number target fields "installedBytes"
                                        |> Result.map (fun installed ->
                                            map.Add(target, { DownloadBytes = download; InstalledBytes = installed })))
                                | _ -> usage $"--artifact-sizes: {target} is not an object"))
                        (Ok Map.empty)
                | _ -> usage "--artifact-sizes is not a JSON object"

            return fun (target: TargetPath) -> entries.TryFind target.Text
        }

/// The diagnostic for a plan that differs from --expect-plan (provisional: the contract has no code for it).
let private expectPlanMismatch (expected: Sha256) (actual: Sha256) =
    let refusal =
        { Code = ReaderCode.InconsistentDocument
          Path = DocumentPath.root.Child "planSha256" }

    let message =
        $"The plan's digest is {actual.Text}, not the expected {expected.Text}. Nothing was changed. Provisional: the contract has no code for a plan that differs from --expect-plan, so this build reports inconsistent_document at planSha256."

    match NonBlankText.tryParse message with
    | Ok text -> { Diagnostics.ofReaderRefusal DocumentKind.Result refusal with Message = text }
    | Error _ -> Diagnostics.ofReaderRefusal DocumentKind.Result refusal

let private setup (io: Io) (args: string list) : Result<int, Usage> =
    invocation {
        let! options =
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
            return! usage "this build plans only: acquisition, installation and activation are not implemented yet; pass --plan"

        let! platform = platformOption io options
        let! repository = requiredOption "--repository-id" Sha256.tryParse options "until authenticated acquisition exists"
        let! expectPlan = validatedOption "--expect-plan" Sha256.tryParse options

        let! recipeFile =
            match options.TryGet "--recipe" with
            | Some file -> Ok file
            | None -> usage "--recipe <file> is required until authenticated acquisition exists"

        let recipeTargetRefusal () =
            failedWith io Command.Setup [ Diagnostics.ofReaderRefusal DocumentKind.Recipe { Code = ReaderCode.NonPortablePath; Path = DocumentPath.root } ]

        // The recipe target path is checked before anything is read.
        match options.TryGet "--recipe-target" |> Option.map TargetPath.tryParse with
        | Some(Error _) -> return recipeTargetRefusal ()
        | given ->
            let! requestBytes = requestBytes io options
            let! recipeBytes = readFile io Limits.recipe recipeFile
            let! sizes = artifactSizes io options

            match RequestDocument.read requestBytes, RecipeDocument.read recipeBytes with
            | Error refusal, _ -> return failedWith io Command.Setup [ Diagnostics.ofReaderRefusal DocumentKind.Request refusal ]
            | _, Error refusal -> return failedWith io Command.Setup [ Diagnostics.ofReaderRefusal DocumentKind.Recipe refusal ]
            | Ok request, Ok recipe ->
                let target =
                    match given with
                    | Some(Ok target) -> Ok target
                    | Some(Error code) -> Error code
                    | None -> TargetPath.tryParse $"toolchains/recipes/{recipe.Preset.Text}/{recipe.Version.Text}.json"

                match target, Bindings.recipeProvidersInRepository recipe repository with
                | Error _, _ -> return recipeTargetRefusal ()
                | _, Error refusal -> return failedWith io Command.Setup [ Diagnostics.ofBindingRefusal refusal ]
                | Ok recipeTarget, Ok() ->
                    io.Error.WriteLine $"morphirup: planning {Platform.name platform} from recipe {recipe.Preset.Text} {recipe.Version.Text}"

                    let planned =
                        Planning.plan
                            { Recipe = recipe
                              RecipeRepository = repository
                              RecipeTarget = recipeTarget
                              Request = request
                              Platform = platform
                              Sizes = sizes
                              // Provisional: planning from a local recipe file has no authenticated metadata yet.
                              MetadataRefresh = MetadataRefresh.Required }

                    match planned with
                    | Error(PlanFailure.Unresolved failure) ->
                        return failedWith io Command.Setup [ Diagnostics.ofResolutionFailure failure ]
                    | Error(PlanFailure.SizeUnknown target) ->
                        return!
                            usage $"the size of {target.Text} is unknown; give it in --artifact-sizes until authenticated metadata provides it"
                    | Ok(digested, _) ->
                        match expectPlan with
                        | Some expected when expected <> digested.Sha256 ->
                            return emit io Command.Setup Outcome.Failed (Some digested) [ expectPlanMismatch expected digested.Sha256 ]
                        | Some _
                        | None -> return emit io Command.Setup Outcome.Planned (Some digested) []
    }

let private doctor (io: Io) (args: string list) : Result<int, Usage> =
    invocation {
        let! options =
            parse [ "--project"; "--request"; "--lock"; "--recipe"; "--repository-id"; "--platform" ] [ "--json" ] args

        let! platform = platformOption io options
        let requestFile = defaultArg (options.TryGet "--request") (projectFile io options "toolchain.toml")
        let lockFile = defaultArg (options.TryGet "--lock") (projectFile io options "toolchain.lock.json")
        let! requestBytes = readFile io Limits.request requestFile
        let! lockBytes = readFile io Limits.lock lockFile

        let! recipe =
            match options.TryGet "--recipe" with
            | None -> Ok None
            | Some file ->
                invocation {
                    let! repository = requiredOption "--repository-id" Sha256.tryParse options "with --recipe, naming the repository the recipe was read from"
                    let! bytes = readFile io Limits.recipe file
                    return Some(RecipeDocument.read bytes, repository)
                }

        let request = RequestDocument.read requestBytes
        let lock = LockDocument.read lockBytes

        let diagnostics =
            [ match request with
              | Error refusal -> Diagnostics.ofReaderRefusal DocumentKind.Request refusal
              | Ok _ -> ()

              match lock with
              | Error refusal -> Diagnostics.ofReaderRefusal DocumentKind.Lock refusal
              | Ok _ -> ()

              // A refused recipe is reported whether or not the lock reads.
              match recipe with
              | Some(Error refusal, _) -> Diagnostics.ofReaderRefusal DocumentKind.Recipe refusal
              | Some(Ok _, _)
              | None -> ()

              match lock with
              | Error _ -> ()
              | Ok lock ->
                  match Bindings.lockBindsRequest lock requestBytes with
                  | Error refusal -> Diagnostics.ofBindingRefusal refusal
                  | Ok() -> ()

                  match Bindings.lockedClosure lock platform with
                  | Error refusal -> Diagnostics.ofBindingRefusal refusal
                  | Ok _ -> ()

                  match recipe with
                  | Some(Ok recipe, repository) ->
                      match Bindings.lockMatchesRecipe lock recipe repository with
                      | Error refusal -> Diagnostics.ofBindingRefusal refusal
                      | Ok() -> ()
                  | Some(Error _, _)
                  | None -> () ]

        let outcome =
            if diagnostics |> List.exists (fun d -> d.Severity = Severity.Error) then
                Outcome.Failed
            else
                Outcome.Succeeded

        return emit io Command.Doctor outcome None diagnostics
    }

/// Runs one command line and returns its exit code. Invocation and file errors write a message to standard error and
/// no result document (exit 2); an unexpected failure is reported on standard error as an internal error (exit 3).
let run (io: Io) (args: string list) : int =
    let outcome =
        try
            match args with
            | [ "--version" ] ->
                let bytes = Text.Encoding.UTF8.GetBytes(VersionInfo.Version + "\n")
                io.Out.Write(bytes, 0, bytes.Length)
                io.Out.Flush()
                Ok ExitCode.ok
            | [ "--help" ]
            | [ "-h" ] ->
                io.Error.Write helpText
                Ok ExitCode.ok
            | "setup" :: rest -> setup io rest
            | "doctor" :: rest -> doctor io rest
            | [] -> usage "no command given"
            | command :: _ -> usage $"{command} is not a command of this build"
            |> Choice1Of2
        with error ->
            Choice2Of2 error

    match outcome with
    | Choice1Of2(Ok code) -> code
    | Choice1Of2(Error(Usage message)) ->
        io.Error.WriteLine $"morphirup: {message}"
        io.Error.WriteLine "Run morphirup --help for the commands of this build."
        ExitCode.usage
    | Choice2Of2 error ->
        io.Error.WriteLine $"morphirup: internal error: {error.GetType().Name}: {error.Message}"
        ExitCode.internalError
