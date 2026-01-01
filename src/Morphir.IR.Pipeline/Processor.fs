namespace Morphir.IR.Pipeline

open System.Collections.Immutable

/// <summary>
/// Parser function that converts a VFile into an IR tree.
/// Returns Result with IRNode on success or error message on failure.
/// </summary>
type Parser = VFile -> Result<obj, string>

/// <summary>
/// Compiler function that converts an IR tree and VFile into an updated VFile.
/// Typically serializes the IR tree to the file's content or external format.
/// </summary>
type Compiler = obj -> VFile -> VFile

/// <summary>
/// Plugin record representing a transformation plugin.
/// Plugins can configure the processor and transform IR nodes.
/// </summary>
type Plugin =
    {
        /// <summary>Plugin name for identification</summary>
        Name: string
        /// <summary>Configure the processor (e.g., add additional plugins, set data)</summary>
        Configure: MorphirProcessor -> MorphirProcessor
        /// <summary>Transform an IR node and file, returning updated node and file</summary>
        Transform: obj -> VFile -> (obj option * VFile)
    }

/// <summary>
/// Immutable processor that orchestrates the transformation pipeline.
/// Inspired by unified.js processor pattern.
/// Supports frozen/unfrozen states for safe template sharing.
/// </summary>
and MorphirProcessor =
    {
        /// <summary>List of parsers to execute in parse phase</summary>
        Parsers: Parser list
        /// <summary>List of plugins to execute in transform phase</summary>
        Plugins: Plugin list
        /// <summary>List of compilers to execute in stringify phase</summary>
        Compilers: Compiler list
        /// <summary>Whether this processor is frozen (immutable template)</summary>
        Frozen: bool
        /// <summary>Shared data dictionary for processor configuration</summary>
        Data: ImmutableDictionary<string, obj>
    }

[<RequireQualifiedAccess>]
module MorphirProcessor =
    /// <summary>
    /// Creates an empty processor with no parsers, plugins, or compilers.
    /// </summary>
    let empty: MorphirProcessor =
        {
            Parsers = []
            Plugins = []
            Compilers = []
            Frozen = false
            Data = ImmutableDictionary.Empty
        }

    /// <summary>
    /// Adds a parser to the processor.
    /// If the processor is frozen, creates an unfrozen copy before adding.
    /// </summary>
    /// <param name="parser">The parser to add</param>
    /// <param name="processor">The processor to update</param>
    let parse (parser: Parser) (processor: MorphirProcessor): MorphirProcessor =
        if processor.Frozen then
            // Create unfrozen copy
            { processor with
                Parsers = processor.Parsers @ [ parser ]
                Frozen = false
            }
        else
            { processor with Parsers = processor.Parsers @ [ parser ] }

    /// <summary>
    /// Adds a plugin to the processor.
    /// If the processor is frozen, creates an unfrozen copy before adding.
    /// Executes the plugin's Configure method to allow plugin self-configuration.
    /// </summary>
    /// <param name="plugin">The plugin to add</param>
    /// <param name="processor">The processor to update</param>
    let plugin (plugin: Plugin) (processor: MorphirProcessor): MorphirProcessor =
        let processorWithPlugin =
            if processor.Frozen then
                // Create unfrozen copy
                { processor with
                    Plugins = processor.Plugins @ [ plugin ]
                    Frozen = false
                }
            else
                { processor with Plugins = processor.Plugins @ [ plugin ] }

        // Allow plugin to configure the processor
        plugin.Configure processorWithPlugin

    /// <summary>
    /// Adds a compiler to the processor.
    /// If the processor is frozen, creates an unfrozen copy before adding.
    /// </summary>
    /// <param name="compiler">The compiler to add</param>
    /// <param name="processor">The processor to update</param>
    let stringify (compiler: Compiler) (processor: MorphirProcessor): MorphirProcessor =
        if processor.Frozen then
            // Create unfrozen copy
            { processor with
                Compilers = processor.Compilers @ [ compiler ]
                Frozen = false
            }
        else
            { processor with Compilers = processor.Compilers @ [ compiler ] }

    /// <summary>
    /// Freezes the processor, making it an immutable template.
    /// Frozen processors create unfrozen copies when modified.
    /// </summary>
    /// <param name="processor">The processor to freeze</param>
    let freeze (processor: MorphirProcessor): MorphirProcessor =
        { processor with Frozen = true }

    /// <summary>
    /// Unfreezes the processor, making it mutable.
    /// This is useful when you want to continue modifying a pipeline that was frozen by default.
    /// </summary>
    /// <param name="processor">The processor to unfreeze</param>
    let unfreeze (processor: MorphirProcessor): MorphirProcessor =
        { processor with Frozen = false }

    /// <summary>
    /// Checks if the processor is frozen.
    /// </summary>
    /// <param name="processor">The processor to check</param>
    let isFrozen (processor: MorphirProcessor): bool = processor.Frozen

    /// <summary>
    /// Sets a data value in the processor's data dictionary.
    /// </summary>
    /// <param name="key">The data key</param>
    /// <param name="value">The data value</param>
    /// <param name="processor">The processor to update</param>
    let setData (key: string) (value: obj) (processor: MorphirProcessor): MorphirProcessor =
        if processor.Frozen then
            { processor with
                Data = processor.Data.SetItem(key, value)
                Frozen = false
            }
        else
            { processor with Data = processor.Data.SetItem(key, value) }

    /// <summary>
    /// Gets a data value from the processor's data dictionary.
    /// </summary>
    /// <param name="key">The data key</param>
    /// <param name="processor">The processor to query</param>
    let getData (key: string) (processor: MorphirProcessor): obj option =
        match processor.Data.TryGetValue(key) with
        | true, value -> Some value
        | false, _ -> None

    /// <summary>
    /// Gets a typed data value from the processor's data dictionary.
    /// </summary>
    /// <param name="key">The data key</param>
    /// <param name="processor">The processor to query</param>
    let getDataAs<'T> (key: string) (processor: MorphirProcessor): 'T option =
        match processor.Data.TryGetValue(key) with
        | true, value ->
            match value with
            | :? 'T as typed -> Some typed
            | _ -> None
        | false, _ -> None

    /// <summary>
    /// Executes the parse phase, running all parsers sequentially until one succeeds.
    /// </summary>
    /// <param name="file">The input file</param>
    /// <param name="processor">The processor with parsers</param>
    let private runParsePhase (file: VFile) (processor: MorphirProcessor): VFile =
        match processor.Parsers with
        | [] ->
            // No parsers, return file as-is
            file
        | parsers ->
            // Try parsers in order until one succeeds
            let rec tryParsers remainingParsers currentFile =
                match remainingParsers with
                | [] ->
                    // All parsers failed, add error
                    currentFile
                    |> VFile.error "No parser succeeded" None
                | parser :: rest ->
                    match parser currentFile with
                    | Result.Ok content ->
                        // Parser succeeded, update file content
                        { currentFile with Content = Some content }
                    | Result.Error errorMsg ->
                        // Parser failed, try next parser
                        let updatedFile =
                            currentFile
                            |> VFile.warn (sprintf "Parser failed: %s" errorMsg) None

                        tryParsers rest updatedFile

            tryParsers parsers file

    /// <summary>
    /// Executes the transform phase, running all plugins sequentially.
    /// </summary>
    /// <param name="file">The file with IR content</param>
    /// <param name="processor">The processor with plugins</param>
    let private runTransformPhase (file: VFile) (processor: MorphirProcessor): VFile =
        match file.Content with
        | None ->
            // No content to transform (parse phase failed)
            file
        | Some initialNode ->
            // Run plugins sequentially, threading node and file through chain
            let rec runPlugins remainingPlugins currentNode (currentFile: VFile) =
                match remainingPlugins with
                | [] ->
                    // All plugins complete, update file content with final node
                    ({ currentFile with Content = Some currentNode }: VFile)
                | plugin :: rest ->
                    let (transformedNode, updatedFile) = plugin.Transform currentNode currentFile

                    match transformedNode with
                    | Some node ->
                        // Plugin returned transformed node, continue with next plugin
                        runPlugins rest node updatedFile
                    | None ->
                        // Plugin removed node (e.g., validation failed), stop transform phase
                        // Update file content to None and return
                        ({ updatedFile with Content = None }: VFile)

            runPlugins processor.Plugins initialNode file

    /// <summary>
    /// Executes the stringify phase, running all compilers sequentially.
    /// </summary>
    /// <param name="file">The file with transformed IR content</param>
    /// <param name="processor">The processor with compilers</param>
    let private runStringifyPhase (file: VFile) (processor: MorphirProcessor): VFile =
        match file.Content with
        | None ->
            // No content to stringify (parse or transform phase failed)
            file
        | Some node ->
            // Run compilers sequentially, threading file through chain
            let rec runCompilers remainingCompilers currentFile =
                match remainingCompilers with
                | [] -> currentFile
                | compiler :: rest ->
                    let updatedFile = compiler node currentFile
                    runCompilers rest updatedFile

            runCompilers processor.Compilers file

    /// <summary>
    /// Processes a file through the complete three-phase pipeline.
    /// Phases: Parse -> Transform -> Stringify
    /// </summary>
    /// <param name="file">The input file</param>
    /// <param name="processor">The processor to execute</param>
    let processFile (file: VFile) (processor: MorphirProcessor): VFile =
        file
        |> fun f -> runParsePhase f processor
        |> fun f -> runTransformPhase f processor
        |> fun f -> runStringifyPhase f processor

    /// <summary>
    /// Processes a file path through the complete three-phase pipeline.
    /// Creates a VFile from the path and processes it.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <param name="processor">The processor to execute</param>
    let processPath (path: string) (processor: MorphirProcessor): VFile =
        let file = VFile.fromPath path
        processFile file processor
