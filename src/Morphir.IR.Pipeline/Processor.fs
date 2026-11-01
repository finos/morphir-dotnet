namespace Morphir.IR.Pipeline

/// <summary>
/// The details of malformed input reported by a parser.
/// </summary>
type ParseFailure =
    {
        /// <summary>The file the failure is about, if it differs from the input</summary>
        Path: string option
        /// <summary>Rule ID for automated tools</summary>
        RuleId: string option
        /// <summary>Logical location, such as a JSON path</summary>
        Location: string option
        /// <summary>Human-readable message text</summary>
        Message: string
        /// <summary>Source code position</summary>
        Position: SourceRange option
    }

/// <summary>
/// The reason a parser could not parse its input.
/// </summary>
[<RequireQualifiedAccess>]
type ParseError =
    /// <summary>The input is not in a format this parser handles.</summary>
    | UnsupportedInput of reason: string
    /// <summary>The input is in a format this parser handles, but its content is malformed.</summary>
    | InvalidInput of ParseFailure
    /// <summary>The parser could not finish, for example because reading the file failed.</summary>
    | Failed of path: string option * cause: exn

/// <summary>
/// Functions for working with parse errors.
/// </summary>
[<RequireQualifiedAccess>]
module ParseError =
    /// <summary>
    /// Creates an error for malformed input with only a message.
    /// </summary>
    /// <param name="message">The message text</param>
    let invalidInput (message: string): ParseError =
        ParseError.InvalidInput
            {
                Path = None
                RuleId = None
                Location = None
                Message = message
                Position = None
            }

    /// <summary>
    /// Describes an error in text suitable for showing to a person.
    /// </summary>
    /// <param name="error">The error to describe</param>
    let describe (error: ParseError): string =
        match error with
        | ParseError.UnsupportedInput reason -> "unsupported input: " + reason
        | ParseError.InvalidInput failure -> failure.Message
        | ParseError.Failed (_, cause) -> cause.Message

/// <summary>
/// Parser function that converts a file or directory into the model type.
/// </summary>
type Parser<'T> = TreeNode -> Result<'T, ParseError>

/// <summary>
/// Compiler function that converts a model into any number of output files.
/// It also receives the input file, including diagnostics and data added so far.
/// </summary>
type Compiler<'T> = 'T -> VFile -> VFileTree

/// <summary>
/// Plugin record representing a transformation plugin.
/// </summary>
type Plugin<'T> =
    {
        /// <summary>Plugin name for identification</summary>
        Name: string
        /// <summary>Adds settings to the processor's data when the plugin is added</summary>
        Configure: PropertyBag -> PropertyBag
        /// <summary>
        /// Transforms the model and file. Returning None rejects the model and stops the remaining
        /// plugins and compilers.
        /// </summary>
        Transform: 'T -> VFile -> ('T option * VFile)
    }

/// <summary>
/// Immutable processor that orchestrates the transformation pipeline.
/// Inspired by unified.js processor pattern.
/// Supports frozen/unfrozen states for safe template sharing.
/// </summary>
type MorphirProcessor<'T> =
    {
        /// <summary>Parsers, tried in order until one succeeds</summary>
        Parsers: Parser<'T> list
        /// <summary>List of plugins to execute in transform phase</summary>
        Plugins: Plugin<'T> list
        /// <summary>Compilers, whose outputs are merged in order</summary>
        Compilers: Compiler<'T> list
        /// <summary>Whether this processor is frozen (immutable template)</summary>
        Frozen: bool
        /// <summary>Whether a pipeline builder should leave this processor unfrozen when it finishes</summary>
        StayUnfrozen: bool
        /// <summary>Typed data for processor configuration</summary>
        Data: PropertyBag
    }

/// <summary>
/// Functions for creating parsers.
/// </summary>
[<RequireQualifiedAccess>]
module Parser =
    /// <summary>
    /// Creates a parser for single files. Given a directory, it reports unsupported input, so the
    /// next parser is tried.
    /// </summary>
    /// <param name="parse">Parses one file</param>
    let forFile (parse: VFile -> Result<'T, ParseError>): Parser<'T> =
        function
        | File file -> parse file
        | Directory tree -> Result.Error (ParseError.UnsupportedInput ("expected a file, got directory " + tree.Path))

    /// <summary>
    /// Creates a single-file parser for any type with a static <c>Parse: string -> T</c> member,
    /// checked at compile time. The file is read as text. An exception from <c>Parse</c> becomes
    /// invalid input, and a read failure becomes a failed parser.
    /// </summary>
    let inline ofParseable< ^T when ^T: (static member Parse: string -> ^T)> () : Parser< ^T > =
        forFile (fun file ->
            match VFile.readText file with
            | Result.Error (ContentError.ReadFailed (path, cause)) -> Result.Error (ParseError.Failed (path, cause))
            | Result.Ok text ->
                try
                    Result.Ok(( ^T: (static member Parse: string -> ^T) text))
                with ex ->
                    Result.Error (ParseError.invalidInput ex.Message))

/// <summary>
/// Functions for creating plugins.
/// </summary>
[<RequireQualifiedAccess>]
module Plugin =
    /// <summary>
    /// Creates a plugin with no configuration.
    /// </summary>
    /// <param name="name">The plugin name</param>
    /// <param name="transform">Transforms the model and file</param>
    let create (name: string) (transform: 'T -> VFile -> ('T option * VFile)): Plugin<'T> =
        { Name = name; Configure = id; Transform = transform }

    /// <summary>
    /// Runs a plugin on one part of a larger model, without casts.
    /// </summary>
    /// <param name="get">Reads the part from the larger model</param>
    /// <param name="set">Puts an updated part back into the larger model</param>
    /// <param name="plugin">The plugin to run on the part</param>
    let focus (get: 'Outer -> 'Inner) (set: 'Inner -> 'Outer -> 'Outer) (plugin: Plugin<'Inner>): Plugin<'Outer> =
        {
            Name = plugin.Name
            Configure = plugin.Configure
            Transform =
                fun outer file ->
                    let inner, updatedFile = plugin.Transform (get outer) file
                    inner |> Option.map (fun value -> set value outer), updatedFile
        }

/// <summary>
/// Functions for building and running processors.
/// </summary>
[<RequireQualifiedAccess>]
module MorphirProcessor =
    /// <summary>
    /// Creates an empty processor with no parsers, plugins, or compilers.
    /// </summary>
    let empty<'T> : MorphirProcessor<'T> =
        {
            Parsers = []
            Plugins = []
            Compilers = []
            Frozen = false
            StayUnfrozen = false
            Data = PropertyBag.empty
        }

    /// <summary>
    /// Adds a parser to the processor.
    /// If the processor is frozen, creates an unfrozen copy before adding.
    /// </summary>
    /// <param name="parser">The parser to add</param>
    /// <param name="processor">The processor to update</param>
    let parse (parser: Parser<'T>) (processor: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { processor with Parsers = processor.Parsers @ [ parser ]; Frozen = false }

    /// <summary>
    /// Adds a plugin to the processor and applies its configuration to the processor's data.
    /// If the processor is frozen, creates an unfrozen copy before adding.
    /// </summary>
    /// <param name="plugin">The plugin to add</param>
    /// <param name="processor">The processor to update</param>
    let plugin (plugin: Plugin<'T>) (processor: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { processor with
            Plugins = processor.Plugins @ [ plugin ]
            Data = plugin.Configure processor.Data
            Frozen = false
        }

    /// <summary>
    /// Adds a compiler to the processor.
    /// If the processor is frozen, creates an unfrozen copy before adding.
    /// </summary>
    /// <param name="compiler">The compiler to add</param>
    /// <param name="processor">The processor to update</param>
    let stringify (compiler: Compiler<'T>) (processor: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { processor with Compilers = processor.Compilers @ [ compiler ]; Frozen = false }

    /// <summary>
    /// Freezes the processor, making it an immutable template.
    /// Frozen processors create unfrozen copies when modified.
    /// </summary>
    /// <param name="processor">The processor to freeze</param>
    let freeze (processor: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { processor with Frozen = true }

    /// <summary>
    /// Unfreezes the processor, making it mutable.
    /// This is useful when you want to continue modifying a pipeline that was frozen by default.
    /// </summary>
    /// <param name="processor">The processor to unfreeze</param>
    let unfreeze (processor: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { processor with Frozen = false }

    /// <summary>
    /// Checks if the processor is frozen.
    /// </summary>
    /// <param name="processor">The processor to check</param>
    let isFrozen (processor: MorphirProcessor<'T>): bool = processor.Frozen

    /// <summary>
    /// Stores a typed value on the processor.
    /// If the processor is frozen, creates an unfrozen copy.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="value">The value</param>
    /// <param name="processor">The processor to update</param>
    let set (property: Property<'V>) (value: 'V) (processor: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { processor with Data = PropertyBag.set property value processor.Data; Frozen = false }

    /// <summary>
    /// Reads a typed value from the processor.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="processor">The processor to query</param>
    let tryGet (property: Property<'V>) (processor: MorphirProcessor<'T>): 'V option =
        PropertyBag.tryGet property processor.Data

    /// <summary>
    /// Removes a typed value from the processor.
    /// If the processor is frozen, creates an unfrozen copy.
    /// </summary>
    /// <param name="property">The property</param>
    /// <param name="processor">The processor to update</param>
    let remove (property: Property<'V>) (processor: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { processor with Data = PropertyBag.remove property processor.Data; Frozen = false }

    /// <summary>The file stages work on: the input file, or a stand-in describing a directory.</summary>
    let private workingFile (input: TreeNode): VFile =
        match input with
        | File file -> file
        | Directory tree -> { VFile.fromPath tree.Path with Data = tree.Metadata }

    /// <summary>Turns a parser failure into a message with the given severity.</summary>
    let private parseMessage (severity: MessageSeverity) (error: ParseError): VMessage =
        let message = { VMessage.create severity (ParseError.describe error) with Source = Some "parser" }

        match error with
        | ParseError.UnsupportedInput _ -> message
        | ParseError.Failed (path, _) -> { message with Path = path }
        | ParseError.InvalidInput failure ->
            { message with
                Path = failure.Path
                RuleId = failure.RuleId
                Location = failure.Location
                Position = failure.Position
            }

    /// <summary>
    /// Tries parsers in order. Failures are warnings if a parser succeeds. Otherwise malformed
    /// input and parser failures become errors, and a generic error is added only when no parser
    /// recognized the input.
    /// </summary>
    let private runParsePhase (input: TreeNode) (file: VFile) (parsers: Parser<'T> list): 'T option * VFile =
        let rec tryParsers (remaining: Parser<'T> list) (failures: ParseError list) =
            match remaining with
            | [] -> None, List.rev failures
            | parser :: rest ->
                match parser input with
                | Result.Ok model -> Some model, List.rev failures
                | Result.Error failure -> tryParsers rest (failure :: failures)

        let model, failures = tryParsers parsers []

        match model with
        | Some _ ->
            model, failures |> List.fold (fun current failure -> VFile.report (parseMessage Warning failure) current) file
        | None ->
            let isUnsupported =
                function
                | ParseError.UnsupportedInput _ -> true
                | ParseError.InvalidInput _
                | ParseError.Failed _ -> false

            let reported =
                failures
                |> List.fold
                    (fun current failure ->
                        let severity = if isUnsupported failure then Warning else Error
                        VFile.report (parseMessage severity failure) current)
                    file

            if List.forall isUnsupported failures then
                None, reported |> VFile.report { VMessage.create Error "No parser succeeded" with Source = Some "parser" }
            else
                None, reported

    /// <summary>
    /// Runs plugins in order. Plugins keep running after an error so one run can report several
    /// problems. A plugin returning None or a fatal message stops the rest.
    /// </summary>
    let private runPluginPhase (model: 'T) (file: VFile) (plugins: Plugin<'T> list): 'T option * VFile =
        plugins
        |> List.fold
            (fun (current, currentFile) plugin ->
                match current with
                | Some value when not (VFile.hasFatals currentFile) -> plugin.Transform value currentFile
                | _ -> current, currentFile)
            (Some model, file)

    /// <summary>
    /// Runs the processor on a file or directory, producing one model and a tree of output files.
    /// Failures appear as messages on the returned tree, which then has no files.
    /// </summary>
    /// <param name="input">The file or directory</param>
    /// <param name="processor">The processor to execute</param>
    let run (input: TreeNode) (processor: MorphirProcessor<'T>): VFileTree =
        let start = workingFile input
        let parsed, afterParse = runParsePhase input start processor.Parsers

        let transformed, finished =
            match parsed with
            | Some model -> runPluginPhase model afterParse processor.Plugins
            | None -> None, afterParse

        let compiled =
            match transformed with
            | Some model when not (VFile.hasErrors finished) ->
                processor.Compilers
                |> List.map (fun compiler -> compiler model finished)
                |> List.fold (fun tree output -> VFileTree.merge output tree) VFileTree.empty
            | _ -> VFileTree.empty

        let inputMessages =
            finished.Messages
            |> List.map (fun message ->
                match message.Path with
                | Some _ -> message
                | None -> { message with Path = finished.Path })

        { compiled with
            Path =
                if List.isEmpty compiled.Content then
                    finished.Path |> Option.defaultValue compiled.Path
                else
                    compiled.Path
            Messages = inputMessages @ compiled.Messages
            Metadata = PropertyBag.merge compiled.Metadata finished.Data
        }

    /// <summary>
    /// Runs the processor once for each file in a tree. Output stays in the directory of the file
    /// it came from. A failure in one file does not stop the others, and its messages reach the
    /// root of the returned tree, named by the file's location. Each run's metadata is copied onto
    /// that run's output files, keeping the values a file already has, so per-file data cannot
    /// collide.
    /// </summary>
    /// <param name="tree">The input tree</param>
    /// <param name="processor">The processor to execute</param>
    let runEachFile (tree: VFileTree) (processor: MorphirProcessor<'T>): VFileTree =
        let runFile (basePath: string) (file: VFile) =
            let result = run (File file) processor

            let location =
                match file.Path with
                | Some path when basePath <> "" -> Some(basePath + "/" + path.Replace('\\', '/'))
                | other -> other

            let messages =
                result.Messages
                |> List.map (fun message ->
                    if message.Path = file.Path then { message with Path = location } else message)

            let outputs =
                { result with Messages = [] }
                |> VFileTree.map (fun output -> { output with Data = PropertyBag.merge output.Data result.Metadata })

            outputs.Content, messages

        let rec walk (basePath: string) (current: VFileTree): VFileTree * VMessage list =
            let results =
                current.Content
                |> List.map (function
                    | File file -> runFile basePath file
                    | Directory subtree ->
                        let path =
                            match subtree.Path.Trim('/', '\\') with
                            | "."
                            | "" -> basePath
                            | name when basePath = "" -> name.Replace('\\', '/')
                            | name -> basePath + "/" + name.Replace('\\', '/')

                        let walked, messages = walk path subtree
                        [ Directory walked ], messages)

            { current with Content = results |> List.collect fst },
            current.Messages @ (results |> List.collect snd)

        let walked, messages = walk "" tree
        { walked with Messages = messages }

    /// <summary>
    /// Runs the processor on one file.
    /// </summary>
    /// <param name="file">The input file</param>
    /// <param name="processor">The processor to execute</param>
    let processFile (file: VFile) (processor: MorphirProcessor<'T>): VFileTree =
        run (File file) processor

    /// <summary>
    /// Runs the processor on a file on disk, which is opened only if a stage reads its content.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="processor">The processor to execute</param>
    let processPath (path: string) (processor: MorphirProcessor<'T>): VFileTree =
        processFile (VFile.fromDisk path) processor
