// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The manager command line: historical recipe planning and doctor, plus explicit local developer setup,
/// selection and installed execution. Publisher-authenticated production acquisition remains unqualified.
/// </summary>
module Morphirup.Cli

open System
open System.IO
open System.Threading.Tasks
open Morphir.Toolchain

/// What a run may touch: its streams, the working directory and the platform it runs on. Tests supply their own.
type Io = {
    Out: Stream
    Error: TextWriter
    CurrentDirectory: string
    Platform: Platform option
}

/// <summary>Exit codes (manager result page): 0 and 1 follow the result's outcome, and 2 is a command line that cannot
/// be parsed, with no result. 3, an internal failure with no result, is provisional: the contract has no exit code for
/// it.</summary>
module ExitCode =
    /// A planned or succeeded result.
    let ok = 0
    /// A failed result: a document was refused or a check failed.
    let failed = 1
    /// A command line that cannot be parsed, or this build's own sizes file unreadable; no result document is written.
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

    member _.Combine(first: Result<unit, Usage>, rest: unit -> Result<'T, Usage>) =
        Result.bind rest first

let private invocation = Builder()

let private helpText =
    """morphirup: the standalone Morphir toolchain manager. Local candidate commands require explicit local-developer
provenance. Publisher-authenticated production acquisition remains unqualified. Recipe setup below is plan-only.

Usage:
  morphirup --version
  morphirup setup [--home <absolute>] [--dry-run | --yes]
  morphirup setup --candidate <json> --local-archive <zip> --home <absolute> --local-developer (--dry-run | --yes)
  morphirup setup --candidate <json> --local-archive <zip> --local-bundle <json> [--home <absolute>] --local-developer (--dry-run | --yes)
  morphirup select --generation <generation-sha256> --home <absolute> [--dry-run]
  morphirup show --home <absolute> [--explain] [--json]
  morphirup run --home <absolute> -- <Morphir arguments>
  morphirup setup (--plan | --dry-run) --recipe <file> --repository-id <sha256> --artifact-sizes <file> [request]
                  [options]
  morphirup doctor [--project <dir>] [--boundary <dir>] [--request <file>] [--lock <file>] [--platform <rid>]
                   [--recipe <file> --repository-id <sha256>]

Request, one of:
  --project <dir>         (provisional) Start discovery here instead of the current directory: the nearest
                          .morphir/toolchain.toml in it or a parent, up to and including the boundary
  --boundary <dir>        (provisional) The discovery boundary; by default the root of the start path
  --request <file>        (provisional) Read this request file
  --implementation <id> --deployment <id> --preset <id> (--channel <channel> | --version <version>)

Local setup retains the full official self-contained folder and probes its installed version. It does not select
a default. Select verifies the complete installed inventory; use is its alias. Both dry runs validate without
creating Home, locks, stages, receipts or pointers, and execute no candidate code. Run needs only installed state.

Recipe setup options:
  --plan                  Required, or --dry-run: report the plan. Without either, or with --yes, setup refuses
  --dry-run               The same as --plan: the same plan, result and exit code, and nothing created, changed or
                          removed. Both may be given; neither permits --yes
  --platform <rid>        Plan for this platform instead of the current one
  --expect-plan <sha256>  Refuse a plan whose planSha256 differs: plan_changed, exit 1, nothing changed
  --recipe <file>         (provisional) The recipe document; nothing authenticates it in this build
  --repository-id <sha256> (provisional) The repository the recipe and its artifacts come from
  --recipe-target <path>  (provisional) The recipe's target path; default toolchains/recipes/<preset>/<version>.json
  --artifact-sizes <file> (provisional) JSON object: target path -> {"downloadBytes": n, "installedBytes": n}
  --json                  Accepted; JSON is the only output format of this build

doctor options:
  --lock <file>           (provisional) Read this lock file; by default the one beside the request, given or found
  --recipe <file>, --repository-id <sha256>  Also check the lock against this recipe and repository

Exit codes: 0 planned or succeeded, 1 failed (one result document on stdout either way), 2 for a command line that
cannot be parsed, with nothing on stdout. Provisional: 3 for an internal error, with nothing on stdout. A request, lock
or recipe that cannot be read is document_unavailable (exit 1). Files are read only when they are regular files, up to
1 MiB for a request, lock or sizes file and 16 MiB for a recipe (provisional bounds); a sizes file that cannot be read
is an invocation error (exit 2).
"""

/// Parsed options: flags and single-valued options, each at most once.
type private Options =
    {
        Flags: Set<string>
        Values: Map<string, string>
    }

    member this.Has flag = this.Flags.Contains flag
    member this.TryGet name = this.Values.TryFind name

/// <summary>Parses options. An option's value may not itself start with <c>--</c>, so a forgotten value is reported
/// instead of swallowing the next option; a file named <c>--x</c> is given as <c>./--x</c>.</summary>
let private parse
    (known: string list)
    (flags: string list)
    (args: string list)
    : Result<Options, Usage> =
    let rec go (options: Options) rest =
        match rest with
        | [] -> Ok options
        | arg :: tail when List.contains arg flags ->
            if options.Flags.Contains arg then
                usage $"{arg} is given twice"
            else
                go
                    { options with
                        Flags = options.Flags.Add arg
                    }
                    tail
        | arg :: value :: tail when
            List.contains arg known
            && not (value.StartsWith("--", StringComparison.Ordinal))
            ->
            if options.Values.ContainsKey arg then
                usage $"{arg} is given twice"
            else
                go
                    { options with
                        Values = options.Values.Add(arg, value)
                    }
                    tail
        | arg :: _ when List.contains arg known -> usage $"{arg} needs a value"
        | arg :: _ -> usage $"unknown option {arg}"

    go
        {
            Flags = Set.empty
            Values = Map.empty
        }
        args

/// <summary>
/// Reads a file the way repository content must be read, or says why it cannot: the path, after following links, must be a regular file
/// (not a directory, a device or a pipe), and at most <paramref name="limit"/> bytes are read. Windows device paths and
/// Unix device trees are refused by name; anything else that does not open within five seconds, such as a FIFO, is
/// refused as not a regular file.
/// </summary>
/// <summary>Why a file cannot be read, in morphirup's own words. Exception messages are not used: with
/// UseSystemResourceKeys they are .NET resource keys such as IO_SharingViolation_File, which mean nothing to a
/// user.</summary>
let private unreadable (error: exn) =
    // Windows reports a file another process holds open as ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33).
    let code =
        error.HResult
        &&& 0xFFFF

    match error with
    | :? FileNotFoundException
    | :? DirectoryNotFoundException -> "does not exist"
    | :? PathTooLongException -> "cannot be read: its path is too long for this system"
    | :? UnauthorizedAccessException -> "cannot be read: access is denied"
    | :? IOException when
        code = 32
        || code = 33
        ->
        "cannot be read: it is in use by another process"
    | :? IOException -> $"cannot be read: input or output failed (error 0x{error.HResult:X8})"
    | _ -> $"cannot be read ({error.GetType().Name})"

let private readFile (io: Io) (limit: int64) (path: string) : Result<byte array, string> =
    let full = Path.GetFullPath(path, io.CurrentDirectory)
    io.Error.WriteLine $"morphirup: reading {full}"

    // Device trees, by name: .NET does not mark a Unix device file with FileAttributes.Device. A link is checked by
    // the path it resolves to as well, so a link to /dev/null is refused like /dev/null.
    let isDeviceName (candidate: string) =
        candidate.StartsWith(@"\\.\", StringComparison.Ordinal)
        || (not (OperatingSystem.IsWindows())
            && [ "/dev/"; "/proc/"; "/sys/" ]
               |> List.exists (fun prefix -> candidate.StartsWith(prefix, StringComparison.Ordinal)))

    // The path as written is checked too. Off Windows, GetFullPath turns \\.\pipe\x into a file name under the current
    // directory, and a path written as a Windows device is refused by name on every system, not looked up.
    if
        isDeviceName path
        || isDeviceName full
    then
        Error $"{full} is not a regular file"
    else
        try
            let info = FileInfo full

            let target =
                if
                    info.Exists
                    && not (isNull info.LinkTarget)
                then
                    match info.ResolveLinkTarget true with
                    | :? FileInfo as linked -> linked
                    | _ -> info
                else
                    info

            if
                Directory.Exists full
                || Directory.Exists target.FullName
            then
                Error $"{full} is a directory, not a file"
            elif not target.Exists then
                Error $"{full} does not exist"
            elif
                isDeviceName target.FullName
                || target.Attributes.HasFlag FileAttributes.Device
            then
                Error $"{full} is not a regular file"
            else
                // Opening a FIFO blocks until a writer comes, so the open has five seconds. After that each read has
                // thirty: a slow file that keeps sending data is read in full, and one that stops is refused.
                let opening =
                    Task.Run<FileStream>(
                        Func<FileStream>(fun () ->
                            new FileStream(
                                target.FullName,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read
                            )
                        )
                    )

                if not (opening.Wait(TimeSpan.FromSeconds 5.0)) then
                    // A late open still gets its stream closed.
                    opening.ContinueWith(fun (opened: Task<FileStream>) ->
                        if opened.IsCompletedSuccessfully then
                            opened.Result.Dispose()
                    )
                    |> ignore

                    Error $"{full} is not a regular file: it did not open within five seconds"
                else
                    use stream = opening.Result
                    use output = new MemoryStream()
                    let chunk = Array.zeroCreate<byte> 81920
                    let mutable outcome = None

                    // At most limit + 1 bytes are read, so an endless file stops one byte past the limit.
                    while outcome.IsNone do
                        if output.Length > limit then
                            outcome <-
                                Some(
                                    Error
                                        $"{full} is larger than the provisional limit of {limit} bytes"
                                )
                        else
                            let wanted =
                                int (
                                    min
                                        (int64 chunk.Length)
                                        (limit + 1L
                                         - output.Length)
                                )

                            let reading =
                                Task.Run<int>(Func<int>(fun () -> stream.Read(chunk, 0, wanted)))

                            if not (reading.Wait(TimeSpan.FromSeconds 30.0)) then
                                outcome <-
                                    Some(
                                        Error
                                            $"{full} is not a regular file: it sent no data for thirty seconds"
                                    )
                            elif reading.Result = 0 then
                                outcome <- Some(Ok(output.ToArray()))
                            else
                                output.Write(chunk, 0, reading.Result)

                    outcome.Value
        with
        | :? AggregateException as error when
            (error.InnerException :? IOException
             || error.InnerException :? UnauthorizedAccessException)
            ->
            Error $"{full} {unreadable (nonNull error.InnerException)}"
        | :? IOException
        | :? UnauthorizedAccessException as error -> Error $"{full} {unreadable error}"

let private platformOption (io: Io) (options: Options) : Result<Platform, Usage> =
    match options.TryGet "--platform", io.Platform with
    | Some text, _ ->
        match Platform.tryParse text with
        | Ok platform -> Ok platform
        | Error _ -> usage $"--platform {text} is not one of the eight platform ids"
    | None, Some platform -> Ok platform
    | None, None ->
        usage "this machine is not one of the eight supported platforms; pass --platform"

let private validatedOption
    (name: string)
    (parse: string -> Result<'T, ReaderCode>)
    (options: Options)
    : Result<'T option, Usage> =
    match options.TryGet name with
    | None -> Ok None
    | Some text ->
        match parse text with
        | Ok value -> Ok(Some value)
        | Error _ -> usage $"{name} {text} is not valid"

let private requiredOption name parse options (why: string) : Result<'T, Usage> =
    validatedOption name parse options
    |> Result.bind (
        function
        | Some value -> Ok value
        | None -> usage $"{name} is required {why}"
    )

/// <summary>A full path as path segments: its root, then each directory name, split at this operating system's
/// separators only. On Linux and macOS a backslash is an ordinary character of a name.</summary>
let private segments (full: string) =
    let root = nonNull (Path.GetPathRoot full)

    root
    :: (full
           .Substring(root.Length)
           .Split(
               [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |],
               StringSplitOptions.RemoveEmptyEntries
           )
        |> List.ofArray)

let private pathOf (segments: string list) = Path.Combine(Array.ofList segments)

/// <summary>Whether an entry exists at the path in any form: a file, a directory, a link (a dangling one too), or an
/// entry whose directory cannot be listed. Discovery stops at it, and the reader then refuses anything that is not a
/// readable regular file as <c>document_unavailable</c>, so an ancestor's request is never used in its place.</summary>
let private present (path: string) =
    let info = FileInfo path

    if
        info.Exists
        || Directory.Exists path
        || not (isNull info.LinkTarget)
    then
        true
    else
        let parent = nonNull (Path.GetDirectoryName path)

        Directory.Exists parent
        && (try
                Directory.EnumerateFileSystemEntries(parent, Path.GetFileName path)
                |> Seq.isEmpty
                |> not
            with
            | :? UnauthorizedAccessException
            | :? IOException -> true)

/// <summary>Finds the project request by discovery (toolchain request page): from <c>--project</c>, or the working
/// directory, up through its parents to the discovery boundary, <c>--boundary</c> or by default the root of the start
/// path, as morphir's own configuration discovery does. The result is the request path, or why there is none.</summary>
let private discover (io: Io) (options: Options) : Result<string, string> =
    let start =
        Path.GetFullPath(defaultArg (options.TryGet "--project") ".", io.CurrentDirectory)

    let boundary =
        match options.TryGet "--boundary" with
        | Some given -> Path.GetFullPath(given, io.CurrentDirectory)
        | None -> nonNull (Path.GetPathRoot start)

    let startSegments = segments start
    let boundarySegments = segments boundary
    let names = Discovery.currentPathCase

    match
        Discovery.requestIn
            names
            boundarySegments
            startSegments
            (pathOf
             >> present)
    with
    | Some found -> Ok(pathOf found)
    | None when not (Discovery.within names boundarySegments startSegments) ->
        Error $"{start} is outside the discovery boundary {boundary}, so it has no project request"
    | None ->
        Error
            $"no .morphir/toolchain.toml was found in {start} or its parents up to the discovery boundary {boundary}"

/// The request bytes, from a file, from discovery or from the selector options, or why the request cannot be read.
let private requestBytes (io: Io) (options: Options) : Result<Result<byte array, string>, Usage> =
    let selectors = [ "--implementation"; "--deployment"; "--preset"; "--channel"; "--version" ]

    let given =
        selectors
        |> List.filter (
            options.TryGet
            >> Option.isSome
        )

    let project =
        options.TryGet "--project"
        |> Option.isSome

    match options.TryGet "--request", given with
    | Some _, _ :: _ -> usage "give either --request or the selector options, not both"
    | Some _, [] when project -> usage "give either --request or --project, not both"
    | Some file, [] -> Ok(readFile io Limits.request file)
    | None, [] ->
        Ok(
            discover io options
            |> Result.bind (readFile io Limits.request)
        )
    | None, _ :: _ when project -> usage "give either --project or the selector options, not both"
    | None, _ :: _ -> invocation {
        let required name =
            requiredOption name Token.tryParse options "with the selector options"

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
            Ok(
                RequestDocument.write
                    {
                        Implementation = implementation
                        Deployment = deployment
                        Release = release
                    }
                    preset
                    None
            )
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
let private artifactSizes
    (io: Io)
    (options: Options)
    : Result<TargetPath -> ArtifactSize option, Usage> =
    match options.TryGet "--artifact-sizes" with
    | None -> Ok(fun _ -> None)
    | Some file -> invocation {
        // The sizes file is this build's own input, not a contract document, so it cannot be document_unavailable.
        let! bytes =
            readFile io Limits.sizes file
            |> Result.mapError Usage

        let number (target: string) (fields: (string * Value) list) name =
            match
                fields
                |> List.tryFind (
                    fst
                    >> (=) name
                )
            with
            | Some(_, Value.Number spelling) ->
                match
                    UInt64.TryParse(
                        spelling,
                        Globalization.NumberStyles.None,
                        Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, n -> Ok n
                | false, _ ->
                    usage $"--artifact-sizes: {target}.{name} is not a non-negative integer"
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
                                        map.Add(
                                            target,
                                            {
                                                DownloadBytes = download
                                                InstalledBytes = installed
                                            }
                                        )
                                    )
                                )
                            | _ -> usage $"--artifact-sizes: {target} is not an object"
                        )
                    )
                    (Ok Map.empty)
            | _ -> usage "--artifact-sizes is not a JSON object"

        return fun (target: TargetPath) -> entries.TryFind target.Text
      }

let private setup (io: Io) (args: string list) : Result<int, Usage> = invocation {
    let! options =
        parse
            [
                "--project"
                "--boundary"
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
                "--expect-plan"
            ]
            [ "--plan"; "--dry-run"; "--json"; "--yes" ]
            args

    // --dry-run is --plan under the name the maintainer's rule gives a run that changes nothing: this build only
    // plans, so the two take the one planning path, and neither lets --yes apply anything.
    if
        options.Has "--yes"
        || not (
            options.Has "--plan"
            || options.Has "--dry-run"
        )
    then
        return!
            usage
                "this build plans only: acquisition, installation and activation are not implemented yet; pass --plan or --dry-run"

    let! platform = platformOption io options

    let! repository =
        requiredOption
            "--repository-id"
            Sha256.tryParse
            options
            "until authenticated acquisition exists"

    let! expectPlan = validatedOption "--expect-plan" Sha256.tryParse options

    let! recipeFile =
        match options.TryGet "--recipe" with
        | Some file -> Ok file
        | None -> usage "--recipe <file> is required until authenticated acquisition exists"

    let recipeTargetRefusal () =
        failedWith io Command.Setup [
            Diagnostics.ofReaderRefusal
                DocumentKind.Recipe
                {
                    Code = ReaderCode.NonPortablePath
                    Path = DocumentPath.root
                }
        ]

    // The recipe target path is checked before anything is read.
    match
        options.TryGet "--recipe-target"
        |> Option.map TargetPath.tryParse
    with
    | Some(Error _) -> return recipeTargetRefusal ()
    | given ->
        let! requestBytes = requestBytes io options
        let recipeBytes = readFile io Limits.recipe recipeFile
        let! sizes = artifactSizes io options

        match requestBytes, recipeBytes with
        | Error _, _
        | _, Error _ ->
            return
                failedWith io Command.Setup [
                    match requestBytes with
                    | Error reason ->
                        Diagnostics.documentUnavailable
                            DocumentKind.Request
                            $"The request cannot be read: {reason}."
                    | Ok _ -> ()
                    match recipeBytes with
                    | Error reason ->
                        Diagnostics.documentUnavailable
                            DocumentKind.Recipe
                            $"The recipe cannot be read: {reason}."
                    | Ok _ -> ()
                ]
        | Ok requestBytes, Ok recipeBytes ->
            match RequestDocument.read requestBytes, RecipeDocument.read recipeBytes with
            | Error refusal, _ ->
                return
                    failedWith io Command.Setup [
                        Diagnostics.ofReaderRefusal DocumentKind.Request refusal
                    ]
            | _, Error refusal ->
                return
                    failedWith io Command.Setup [
                        Diagnostics.ofReaderRefusal DocumentKind.Recipe refusal
                    ]
            | Ok request, Ok recipe ->
                let target =
                    match given with
                    | Some(Ok target) -> Ok target
                    | Some(Error code) -> Error code
                    | None ->
                        TargetPath.tryParse
                            $"toolchains/recipes/{recipe.Preset.Text}/{recipe.Version.Text}.json"

                match target, Bindings.recipeProvidersInRepository recipe repository with
                | Error _, _ -> return recipeTargetRefusal ()
                | _, Error refusal ->
                    return failedWith io Command.Setup [ Diagnostics.ofBindingRefusal refusal ]
                | Ok recipeTarget, Ok() ->
                    io.Error.WriteLine
                        $"morphirup: planning {Platform.name platform} from recipe {recipe.Preset.Text} {recipe.Version.Text}"

                    let planned =
                        Planning.plan
                            {
                                Recipe = recipe
                                RecipeRepository = repository
                                RecipeTarget = recipeTarget
                                Request = request
                                Platform = platform
                                Sizes = sizes
                                // Provisional: planning from a local recipe file has no authenticated metadata yet.
                                MetadataRefresh = MetadataRefresh.Required
                            }

                    match planned with
                    | Error(PlanFailure.Unresolved failure) ->
                        return
                            failedWith io Command.Setup [ Diagnostics.ofResolutionFailure failure ]
                    | Error(PlanFailure.SizeUnknown target) ->
                        return!
                            usage
                                $"the size of {target.Text} is unknown; give it in --artifact-sizes until authenticated metadata provides it"
                    | Ok(digested, _) ->
                        match expectPlan with
                        | Some expected when
                            expected
                            <> digested.Sha256
                            ->
                            return
                                failedWith io Command.Setup [
                                    Diagnostics.planChanged expected digested.Sha256
                                ]
                        | Some _
                        | None -> return emit io Command.Setup Outcome.Planned (Some digested) []
}

let private doctor (io: Io) (args: string list) : Result<int, Usage> = invocation {
    let! options =
        parse
            [
                "--project"
                "--boundary"
                "--request"
                "--lock"
                "--recipe"
                "--repository-id"
                "--platform"
            ]
            [ "--json" ]
            args

    if
        options.TryGet "--request"
        |> Option.isSome
        && options.TryGet "--project"
           |> Option.isSome
    then
        return! usage "give either --request or --project, not both"

    let! platform = platformOption io options

    // The request: the one given, or the one discovery finds. The lock is beside that request.
    let request =
        match options.TryGet "--request" with
        | Some file -> Ok(Path.GetFullPath(file, io.CurrentDirectory))
        | None -> discover io options

    // A request given on the command line is read by the path as written, so that the reader sees a path written
    // as a Windows device and refuses it by name on every system. The full path is only for finding the lock.
    let requestBytes =
        match options.TryGet "--request" with
        | Some file -> readFile io Limits.request file
        | None ->
            request
            |> Result.bind (readFile io Limits.request)

    let lockBytes =
        match options.TryGet "--lock" with
        | Some file -> readFile io Limits.lock file
        | None ->
            request
            |> Result.mapError (fun reason ->
                $"the project lock is beside the project request, and {reason}"
            )
            |> Result.bind (fun request ->
                readFile
                    io
                    Limits.lock
                    (Path.Combine(nonNull (Path.GetDirectoryName request), "toolchain.lock.json"))
            )

    let! recipe =
        match options.TryGet "--recipe" with
        | None -> Ok None
        | Some file -> invocation {
            let! repository =
                requiredOption
                    "--repository-id"
                    Sha256.tryParse
                    options
                    "with --recipe, naming the repository the recipe was read from"

            let read =
                readFile io Limits.recipe file
                |> Result.mapError (fun reason ->
                    Diagnostics.documentUnavailable
                        DocumentKind.Recipe
                        $"The recipe cannot be read: {reason}."
                )
                |> Result.bind (
                    RecipeDocument.read
                    >> Result.mapError (Diagnostics.ofReaderRefusal DocumentKind.Recipe)
                )

            return Some(read, repository)
          }

    let readDocument kind (bytes: Result<byte array, string>) read =
        bytes
        |> Result.mapError (fun reason ->
            Diagnostics.documentUnavailable
                kind
                $"The {DocumentKind.name kind} cannot be read: {reason}."
        )
        |> Result.bind (
            read
            >> Result.mapError (Diagnostics.ofReaderRefusal kind)
        )

    let request = readDocument DocumentKind.Request requestBytes RequestDocument.read
    let lock = readDocument DocumentKind.Lock lockBytes LockDocument.read

    let diagnostics = [
        match request with
        | Error diagnostic -> diagnostic
        | Ok _ -> ()

        match lock with
        | Error diagnostic -> diagnostic
        | Ok _ -> ()

        // A refused recipe is reported whether or not the lock reads.
        match recipe with
        | Some(Error diagnostic, _) -> diagnostic
        | Some(Ok _, _)
        | None -> ()

        match lock, requestBytes with
        | Error _, _ -> ()
        | Ok lock, requestBytes ->
            // The lock binds the request bytes as read, even a request that does not parse.
            match
                requestBytes
                |> Result.map (Bindings.lockBindsRequest lock)
            with
            | Ok(Error refusal) -> Diagnostics.ofBindingRefusal refusal
            | Ok(Ok())
            | Error _ -> ()

            match Bindings.lockedClosure lock platform with
            | Error refusal -> Diagnostics.ofBindingRefusal refusal
            | Ok _ -> ()

            match recipe with
            | Some(Ok recipe, repository) ->
                match Bindings.lockMatchesRecipe lock recipe repository with
                | Error refusal -> Diagnostics.ofBindingRefusal refusal
                | Ok() -> ()
            | Some(Error _, _)
            | None -> ()
    ]

    let outcome =
        if
            diagnostics
            |> List.exists (fun d -> d.Severity = Severity.Error)
        then
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
                let bytes =
                    Text.Encoding.UTF8.GetBytes(
                        VersionInfo.Version
                        + "\n"
                    )

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

let private isLocalInvocation =
    function
    | ("select" | "use" | "show" | "run") :: _ -> true
    | "setup" :: rest ->
        rest
        |> List.exists (fun arg ->
            List.contains arg [ "--candidate"; "--local-archive"; "--local-bundle"; "--local-developer" ]
        )
    | _ -> false

let private isReleaseSetup = function
    | "setup" :: rest -> not(rest |> List.exists(fun arg -> List.contains arg ["--recipe";"--candidate";"--local-archive";"--local-bundle";"--local-developer"]))
    | _ -> false

/// Local executable composition uses the actual Windows process platform, including x64 emulation.
let executionPlatform os architecture platform args =
    if
        os = Runtime.InteropServices.OSPlatform.Windows
        && (isLocalInvocation args || isReleaseSetup args)
    then
        match architecture with
        | Runtime.InteropServices.Architecture.X64 -> Some Platform.WinX64
        | Runtime.InteropServices.Architecture.Arm64 -> Some Platform.WinArm64
        | _ -> platform
    else
        platform

/// Actual explicit local developer journey. Historical recipe planning still uses its original route.
let private runCoreAsync
    (io: Io)
    input
    (args: string list)
    (releaseTransport: Morphir.Toolchain.IO.IAcquisitionTransport option)
    (ct: System.Threading.CancellationToken)
    : Task<int> =
    task {
        let local = isLocalInvocation args

        if isReleaseSetup args then
            let emit status candidate generation diagnostic =
                let bytes = Value.Object [
                    "$schema",Value.String(ComposedDocuments.schemaFor "0.1.0-preview.3" "result")
                    "command",Value.String "setup"; "status",Value.String status; "origin",Value.String "release-checksum"
                    match candidate with Some(hash: Sha256) -> "candidateSha256",Value.String hash.Text | None -> ()
                    match generation with
                    | Some(hash: Sha256) ->
                        "generationSha256",Value.String hash.Text
                        "receiptPath",Value.String(LocalCandidateDocument.receiptPath hash)
                        "selectionPath",Value.String LocalCandidateDocument.selectionPath
                    | None -> ()
                    match diagnostic with
                    | Some(code,message) -> "diagnostic",Value.Object ["code",Value.String code;"message",Value.String message]
                    | None -> () ] |> JsonText.toBytes
                io.Out.Write(bytes,0,bytes.Length)
            try
                let parsed = parse ["--home"] ["--yes";"--dry-run";"--json"] args.Tail |> function Ok value -> value | Error(Usage message) -> raise (ArgumentException message)
                if parsed.Has "--yes" && parsed.Has "--dry-run" then raise (ArgumentException "Choose --yes or --dry-run.")
                let platform =
                    match io.Platform with
                    | Some platform when Platform.isWindows platform -> platform
                    | _ -> LocalCandidate.refuse "setup.platform_mismatch" "Release setup requires an actual Windows x64 or ARM64 manager process."
                let home = parsed.TryGet "--home" |> Option.defaultWith ComposedInstallation.defaultHome |> LocalCandidate.homePath
                let version = ComposedInstallation.releaseVersion io.CurrentDirectory VersionInfo.Version
                let execute transport = ComposedInstallation.setupWithTransportAsync platform home version transport (not(parsed.Has "--yes")) ct
                let! candidate,generation =
                    match releaseTransport with
                    | Some transport -> execute transport
                    | None -> task {
                        use transport = new AcquisitionDelivery.ReleaseTransport()
                        return! execute transport
                      }
                io.Error.WriteLine "morphirup: release-checksum delivery, NOT publisher-authenticated."
                if not(parsed.Has "--yes") then io.Error.WriteLine "Run morphirup setup --yes to install and select this toolchain."
                emit (if generation.IsSome then "installed" else "planned") (Some candidate) generation None
                return 0
            with
            | :? ArgumentException as error -> io.Error.WriteLine $"morphirup: {error.Message}"; return 2
            | :? OperationCanceledException -> io.Error.WriteLine "morphirup: setup cancelled."; return 130
            | error ->
                let code,message =
                    match error with
                    | LocalCandidate.Refused(code,message) -> code,message
                    | Morphir.Extensions.Installation.InstallationException failure -> failure.Code,failure.Message
                    | _ -> "setup.refused",error.Message
                emit "refused" None None (Some(code,message))
                io.Error.WriteLine "morphirup: release-checksum delivery, NOT publisher-authenticated."
                return 1
        elif not local then
            return run io args
        else
            let command = if args.Head = "use" then "select" else args.Head
            let composedLocal = command="setup" && List.contains "--local-bundle" args
            let mutable localGeneration: Sha256 option = None

            let emit status identity diagnostic =
                let bytes =
                    if not composedLocal then LocalCandidate.result command status identity diagnostic
                    else Value.Object [
                        "$schema",Value.String(ComposedDocuments.schemaFor "0.1.0-preview.3" "result")
                        "command",Value.String command;"status",Value.String status;"origin",Value.String "local-developer"
                        match identity with Some(hash: Sha256) -> "candidateSha256",Value.String hash.Text | None -> ()
                        match localGeneration with
                        | Some hash ->
                            "generationSha256",Value.String hash.Text
                            "receiptPath",Value.String(LocalCandidateDocument.receiptPath hash)
                        | None -> ()
                        match diagnostic with
                        | Some(code,message) -> "diagnostic",Value.Object ["code",Value.String code;"message",Value.String message]
                        | None -> ()] |> JsonText.toBytes
                io.Out.Write(bytes, 0, bytes.Length)
                io.Out.Flush()

            let options known flags rest =
                match parse known flags rest with
                | Ok options -> options
                | Error(Usage message) -> raise (ArgumentException message)

            let required name (options: Options) =
                options.TryGet name
                |> Option.defaultWith (fun () ->
                    raise (
                        ArgumentException(
                            name
                            + " is required"
                        )
                    )
                )

            let absolute path =
                Path.GetFullPath(path, io.CurrentDirectory)

            let home (options: Options) =
                options.TryGet "--home" |> Option.defaultWith ComposedInstallation.defaultHome
                |> LocalCandidate.homePath

            try
                ct.ThrowIfCancellationRequested()

                if
                    command
                    <> "show"
                    && not(io.Platform |> Option.exists Platform.isWindows)
                then
                    LocalCandidate.refuse
                        "local.platform_mismatch"
                        "Local execution requires an actual Windows x64 or ARM64 manager process."

                match command with
                | "setup" ->
                    let options =
                        options
                            [ "--candidate"; "--local-archive"; "--local-bundle"; "--home" ]
                            [ "--local-developer"; "--yes"; "--dry-run"; "--json" ]
                            args.Tail

                    if options.Has "--yes" = options.Has "--dry-run" then
                        raise (ArgumentException "Exactly one of --yes and --dry-run is required.")

                    if not (options.Has "--local-developer") then
                        LocalCandidate.refuse
                            "local.consent_required"
                            "Explicit --local-developer consent is required."

                    let home = home options

                    if composedLocal then
                        let! candidate,generation =
                            ComposedInstallation.setupLocalAsync
                                (Option.get io.Platform)
                                home
                                (required "--candidate" options |> absolute)
                                (required "--local-archive" options |> absolute)
                                (required "--local-bundle" options |> absolute)
                                (options.Has "--dry-run")
                                ct
                        localGeneration <- generation
                        emit (if options.Has "--dry-run" then "planned" else "installed") (Some candidate) None
                        io.Error.WriteLine "morphirup: local-developer input, NOT publisher-authenticated. Select the completed generation explicitly."
                        return 0
                    else
                        let raw =
                            LocalCandidate.readBytes
                                LocalInstallation.documentBound
                                (required "--candidate" options
                                 |> absolute)

                        let candidate =
                            LocalCandidateDocument.readCandidate raw
                            |> LocalCandidate.reader

                        let platform =
                            io.Platform
                            |> Option.defaultWith (fun () ->
                                LocalCandidate.refuse
                                    "local.platform_mismatch"
                                    "The current platform is unsupported."
                            )

                        match
                            LocalCandidatePlanning.plan
                                platform
                                (Some LocalConsent.LocalDeveloper)
                                LocalInstallation.maximumArchive
                                candidate
                        with
                        | Error refusal ->
                            LocalCandidate.refuse "local.candidate_refused" (string refusal)
                        | Ok _ -> ()

                        let dryRun = options.Has "--dry-run"

                        do!
                            LocalInstallation.setupAsync
                                home
                                raw
                                candidate
                                (required "--local-archive" options
                                 |> absolute)
                                dryRun
                                ct

                        emit
                            (if dryRun then "planned" else "installed")
                            (Some candidate.ManifestSha256)
                            None

                        return 0
                | "select" ->
                    let options =
                        options [ "--generation"; "--home" ] [ "--dry-run"; "--json" ] args.Tail

                    let identity =
                        required "--generation" options
                        |> Sha256.tryParse
                        |> function
                            | Ok value -> value
                            | Error _ ->
                                raise (
                                    ArgumentException "--generation must be a lowercase SHA-256."
                                )

                    let selectedHome = home options
                    let generationPath = Path.Combine(selectedHome,LocalCandidateDocument.generationPath identity,"generation.json")
                    if File.Exists generationPath then
                        let! installed = ComposedInstallation.selectAsync (Option.get io.Platform) selectedHome identity (options.Has "--dry-run") ct
                        let manifest = installed.Manifest |> Option.defaultWith(fun () -> LocalCandidate.refuse "setup.incomplete_generation" "A complete generation is required.")
                        let result = Value.Object [
                            "$schema",Value.String(ComposedDocuments.schemaFor installed.Family "result")
                            "command",Value.String "select";"status",Value.String(if options.Has "--dry-run" then "planned" else "selected")
                            "origin",Value.String installed.Candidate.Origin.WireValue
                            "candidateSha256",Value.String installed.Candidate.Host.ManifestSha256.Text
                            "generationSha256",Value.String manifest.Identity.Text
                            "receiptPath",Value.String(LocalCandidateDocument.receiptPath manifest.Identity)
                            "selectionPath",Value.String LocalCandidateDocument.selectionPath] |> JsonText.toBytes
                        io.Out.Write(result,0,result.Length)
                        io.Error.WriteLine($"morphirup: {installed.Candidate.Origin.WireValue} delivery, NOT publisher-authenticated.")
                        return 0
                    else
                      if io.Platform <> Some Platform.WinX64 then LocalCandidate.refuse "local.platform_mismatch" "Historical local selection requires a win-x64 manager process."
                      let! installed =
                        LocalInstallation.selectAsync
                            selectedHome
                            identity
                            (options.Has "--dry-run")
                            ct

                      emit
                        (if options.Has "--dry-run" then "planned" else "selected")
                        (Some installed.Candidate.ManifestSha256)
                        None

                      return 0
                | "show" ->
                    let options = options [ "--home" ] [ "--explain"; "--json" ] args.Tail
                    if ComposedInstallation.isSelected(home options) then
                        let! snapshot = ComposedInstallation.selected (home options) ct
                        let manifest = snapshot.Manifest |> Option.defaultWith(fun () -> LocalCandidate.refuse "setup.incomplete_generation" "No completed generation.")
                        let bytes = Value.Object [
                            "$schema",Value.String(ComposedDocuments.schemaFor snapshot.Family "result")
                            "command",Value.String "show";"status",Value.String "shown";"origin",Value.String snapshot.Candidate.Origin.WireValue
                            "candidateSha256",Value.String snapshot.Candidate.Host.ManifestSha256.Text;"generationSha256",Value.String manifest.Identity.Text
                            "receiptPath",Value.String(LocalCandidateDocument.receiptPath manifest.Identity);"selectionPath",Value.String LocalCandidateDocument.selectionPath] |> JsonText.toBytes
                        io.Out.Write(bytes,0,bytes.Length)
                        io.Error.WriteLine($"morphirup: {snapshot.Candidate.Origin.WireValue} delivery, NOT publisher-authenticated.")
                        return 0
                    else
                      let installed = LocalInstallation.selected (home options) ct

                      if options.Has "--explain" then
                        io.Error.WriteLine
                            $"morphirup: local-developer {installed.Candidate.Host.Version.Text}, candidate {installed.Candidate.ManifestSha256.Text}, archive {installed.Candidate.ArchiveSha256.Text}"

                        let executable =
                            Path.Combine(
                                home options,
                                (LocalCandidateDocument.installationPath installed.Candidate
                                 + "/"
                                 + installed.Candidate.EntryPoint.Target.Text)
                                    .Replace('/', Path.DirectorySeparatorChar)
                            )

                        io.Error.WriteLine $"morphirup: absolute executable {executable}"

                      emit "shown" (Some installed.Candidate.ManifestSha256) None
                      return 0
                | "run" ->
                    let before, after =
                        args.Tail
                        |> List.splitAt (
                            args.Tail
                            |> List.tryFindIndex ((=) "--")
                            |> Option.defaultWith (fun () ->
                                raise (ArgumentException "run requires -- before host arguments.")
                            )
                        )

                    let options = options [ "--home" ] [] before

                    let runner =
                        if ComposedInstallation.isSelected(home options) then ComposedInstallation.runAsync (Option.get io.Platform)
                        else
                            if io.Platform <> Some Platform.WinX64 then LocalCandidate.refuse "local.platform_mismatch" "Historical local execution requires a win-x64 manager process."
                            LocalInstallation.runAsync
                    return!
                        runner
                            (home options)
                            after.Tail
                            io.CurrentDirectory
                            input
                            io.Out
                            io.Error
                            ct
                | _ -> return 2
            with
            | :? ArgumentException as error ->
                io.Error.WriteLine(
                    "morphirup: "
                    + error.Message
                )

                return 2
            | :? OperationCanceledException ->
                io.Error.WriteLine "morphirup: local operation cancelled."
                return 130
            | error ->
                let code, message =
                    match error with
                    | LocalCandidate.Refused(code, message) -> code, message
                    | Morphir.Extensions.Installation.InstallationException failure ->
                        "local."
                        + failure.Code,
                        failure.Message
                    | :? IOException ->
                        "local.io_refused",
                        "Local input or installed state could not be safely opened."
                    | :? UnauthorizedAccessException ->
                        "local.access_refused", "Local input or installed state was not accessible."
                    | :? Text.Json.JsonException ->
                        "local.launch_structure", "A managed launch document was malformed."
                    | _ -> "local.operation_failed", error.Message

                if command = "run" then
                    io.Error.WriteLine($"morphirup: {code}: {message}")
                else
                    emit "refused" None (Some(code, message))

                return 1
    }

/// Embedding boundary for a caller-owned, cancellation-cooperative input stream.
let runAsync io input args ct =
    runCoreAsync io (LocalProcess.Input.Redirect input) args None ct

/// Test composition replaces transport only, with the same acquisition, installation and publication path.
let internal runWithTransportAsync io input args transport ct =
    runCoreAsync io (LocalProcess.Input.Redirect input) args (Some transport) ct

/// The executable delegates its actual standard-input handle directly to the child, with no parent input pump.
let internal runWithInheritedInputAsync io args ct =
    runCoreAsync io LocalProcess.Input.Inherit args None ct
