namespace Morphir.IR.Pipeline

open System.Collections.Immutable

/// <summary>
/// Parser function that converts a MorphirFile into an IR tree.
/// Returns Result with IRNode on success or error message on failure.
/// </summary>
type Parser = MorphirFile -> Result<obj, string>

/// <summary>
/// Compiler function that converts an IR tree and MorphirFile into an updated MorphirFile.
/// Typically serializes the IR tree to the file's content or external format.
/// </summary>
type Compiler = obj -> MorphirFile -> MorphirFile

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
        Transform: obj -> MorphirFile -> (obj option * MorphirFile)
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
