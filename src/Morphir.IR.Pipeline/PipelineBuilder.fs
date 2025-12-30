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
        if proc.Frozen then proc
        else MorphirProcessor.freeze proc

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
    /// let proc = pipeline {
    ///     parse irJsonParser
    ///     uses validateIRPlugin
    ///     stringify irJsonSerializer
    /// }
    /// // Automatically frozen!
    /// </code>
    /// </example>
    let pipeline = PipelineBuilder()
