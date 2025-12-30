namespace Morphir.IR.Pipeline

/// <summary>
/// Computation expression builder for creating MorphirProcessor pipelines.
/// Provides a declarative, F#-idiomatic API for pipeline construction.
/// </summary>
/// <example>
/// <code>
/// let proc = pipeline {
///     parse irJsonParser
///     uses validateIRPlugin
///     uses optimizePlugin
///     stringify irJsonSerializer
///     freeze
/// }
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
    /// </summary>
    /// <example>
    /// <code>
    /// let proc = pipeline {
    ///     parse irJsonParser
    ///     uses validateIRPlugin
    ///     stringify irJsonSerializer
    ///     freeze
    /// }
    /// </code>
    /// </example>
    let pipeline = PipelineBuilder()
