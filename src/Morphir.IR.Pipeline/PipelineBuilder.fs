namespace Morphir.IR.Pipeline

/// <summary>
/// Computation expression builder for creating MorphirProcessor pipelines.
/// Provides a declarative, F#-idiomatic API for pipeline construction.
/// Pipelines are frozen (immutable) by default.
/// </summary>
/// <example>
/// <code>
/// let proc = pipeline {
///     parse irJsonParser
///     uses validateIRPlugin
///     uses optimizePlugin
///     stringify irJsonSerializer
/// }
/// // Automatically frozen - no freeze call needed!
/// </code>
/// </example>
type PipelineBuilder() =
    /// <summary>
    /// Yields an empty processor as the starting point.
    /// </summary>
    member _.Yield(_) = MorphirProcessor.empty

    /// <summary>
    /// Returns an empty processor for empty pipeline blocks.
    /// </summary>
    member _.Zero() = MorphirProcessor.empty

    /// <summary>
    /// Returns the processor, frozen by default.
    /// Pipelines are immutable unless explicitly marked as mutable.
    /// </summary>
    member _.Run(proc: MorphirProcessor) : MorphirProcessor =
        // Check if pipeline is marked as mutable
        let stayMutable =
            match MorphirProcessor.getDataAs<bool> "__stay_mutable__" proc with
            | Some true -> true
            | _ -> false

        if proc.Frozen then
            proc
        elif stayMutable then
            // Remove the marker and return unfrozen processor
            { proc with Data = proc.Data.Remove("__stay_mutable__") }
        else
            MorphirProcessor.freeze proc

    /// <summary>
    /// Adds a parser to the pipeline.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="parser">The parser to add</param>
    [<CustomOperation("parse")>]
    member _.Parse(proc: MorphirProcessor, parser: Parser): MorphirProcessor =
        MorphirProcessor.parse parser proc

    /// <summary>
    /// Adds a plugin to the pipeline.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="plugin">The plugin to add</param>
    [<CustomOperation("uses")>]
    member _.Uses(proc: MorphirProcessor, plugin: Plugin): MorphirProcessor =
        MorphirProcessor.plugin plugin proc

    /// <summary>
    /// Adds a compiler to the pipeline.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="compiler">The compiler to add</param>
    [<CustomOperation("stringify")>]
    member _.Stringify(proc: MorphirProcessor, compiler: Compiler): MorphirProcessor =
        MorphirProcessor.stringify compiler proc

    /// <summary>
    /// Freezes the processor, making it an immutable template.
    /// Note: Pipelines are frozen by default, so this operation is typically redundant.
    /// It's kept for backward compatibility and explicit freeze scenarios.
    /// </summary>
    /// <param name="proc">The current processor</param>
    [<CustomOperation("freeze")>]
    member _.Freeze(proc: MorphirProcessor): MorphirProcessor =
        MorphirProcessor.freeze proc

    /// <summary>
    /// Marks the pipeline as unfrozen, preventing auto-freezing.
    /// Use this when you need to continue modifying the pipeline after it's defined.
    /// </summary>
    /// <param name="proc">The current processor</param>
    [<CustomOperation("unfrozen")>]
    member _.Unfrozen(proc: MorphirProcessor): MorphirProcessor =
        MorphirProcessor.setData "__stay_mutable__" (box true) proc

    /// <summary>
    /// Sets data in the processor's data dictionary.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="key">The data key</param>
    /// <param name="value">The data value</param>
    [<CustomOperation("data")>]
    member _.Data(proc: MorphirProcessor, key: string, value: obj): MorphirProcessor =
        MorphirProcessor.setData key value proc

/// <summary>
/// Global pipeline builder instance.
/// </summary>
[<AutoOpen>]
module PipelineBuilderInstance =
    /// <summary>
    /// Creates a new pipeline using computation expression syntax.
    /// Pipelines are frozen (immutable) by default.
    /// </summary>
    /// <example>
    /// <code>
    /// // Frozen by default (immutable)
    /// let proc = pipeline {
    ///     parse irJsonParser
    ///     uses validateIRPlugin
    ///     stringify irJsonSerializer
    /// }
    ///
    /// // Explicitly unfrozen (opt-out of auto-freezing)
    /// let mutableProc = pipeline {
    ///     parse irJsonParser
    ///     uses validateIRPlugin
    ///     unfrozen
    /// }
    /// // Can continue modifying mutableProc
    /// let extended = mutableProc |> MorphirProcessor.plugin optimizePlugin
    ///
    /// // Unfreeze a frozen pipeline
    /// let unfrozenAfter = proc |> MorphirProcessor.unfreeze
    /// </code>
    /// </example>
    let pipeline = PipelineBuilder()
