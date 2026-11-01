namespace Morphir.IR.Pipeline

/// <summary>
/// Computation expression builder for creating MorphirProcessor pipelines.
/// Provides a declarative, F#-idiomatic API for pipeline construction.
/// The model type is inferred from the stages. Pipelines are frozen (immutable) by default.
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
    member _.Yield(_) : MorphirProcessor<'T> = MorphirProcessor.empty

    /// <summary>
    /// Returns an empty processor for empty pipeline blocks.
    /// </summary>
    member _.Zero() : MorphirProcessor<'T> = MorphirProcessor.empty

    /// <summary>
    /// Returns the processor, frozen by default.
    /// Pipelines are immutable unless explicitly marked as mutable.
    /// </summary>
    member _.Run(proc: MorphirProcessor<'T>) : MorphirProcessor<'T> =
        if proc.Frozen then
            proc
        elif proc.StayUnfrozen then
            { proc with StayUnfrozen = false }
        else
            MorphirProcessor.freeze proc

    /// <summary>
    /// Adds a parser to the pipeline.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="parser">The parser to add</param>
    [<CustomOperation("parse")>]
    member _.Parse(proc: MorphirProcessor<'T>, parser: Parser<'T>): MorphirProcessor<'T> =
        MorphirProcessor.parse parser proc

    /// <summary>
    /// Adds a plugin to the pipeline.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="plugin">The plugin to add</param>
    [<CustomOperation("uses")>]
    member _.Uses(proc: MorphirProcessor<'T>, plugin: Plugin<'T>): MorphirProcessor<'T> =
        MorphirProcessor.plugin plugin proc

    /// <summary>
    /// Adds a compiler to the pipeline.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="compiler">The compiler to add</param>
    [<CustomOperation("stringify")>]
    member _.Stringify(proc: MorphirProcessor<'T>, compiler: Compiler<'T>): MorphirProcessor<'T> =
        MorphirProcessor.stringify compiler proc

    /// <summary>
    /// Freezes the processor, making it an immutable template.
    /// Note: Pipelines are frozen by default, so this operation is typically redundant.
    /// It's kept for backward compatibility and explicit freeze scenarios.
    /// </summary>
    /// <param name="proc">The current processor</param>
    [<CustomOperation("freeze")>]
    member _.Freeze(proc: MorphirProcessor<'T>): MorphirProcessor<'T> =
        MorphirProcessor.freeze proc

    /// <summary>
    /// Marks the pipeline as unfrozen, preventing auto-freezing.
    /// Use this when you need to continue modifying the pipeline after it's defined.
    /// </summary>
    /// <param name="proc">The current processor</param>
    [<CustomOperation("unfrozen")>]
    member _.Unfrozen(proc: MorphirProcessor<'T>): MorphirProcessor<'T> =
        { proc with StayUnfrozen = true }

    /// <summary>
    /// Stores a typed value on the processor.
    /// </summary>
    /// <param name="proc">The current processor</param>
    /// <param name="property">The property</param>
    /// <param name="value">The value</param>
    [<CustomOperation("data")>]
    member _.Data(proc: MorphirProcessor<'T>, property: Property<'V>, value: 'V): MorphirProcessor<'T> =
        MorphirProcessor.set property value proc

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
    /// // Explicitly immutable - PREFERRED for clarity
    /// let immutableProc = Pipeline.immutable {
    ///     parse irJsonParser
    ///     uses validatePlugin
    /// }
    ///
    /// // Explicitly mutable - PREFERRED syntax for mutable pipelines
    /// let mutableProc = Pipeline.mut {
    ///     parse irJsonParser
    ///     uses validatePlugin
    /// }
    /// // Can continue modifying
    /// let extended = mutableProc |> MorphirProcessor.plugin optimizePlugin
    ///
    /// // Alternative: using unfrozen keyword inside pipeline
    /// let alsoMutable = pipeline {
    ///     parse irJsonParser
    ///     unfrozen
    /// }
    ///
    /// // Alternative: unfreeze an existing frozen pipeline
    /// let unfrozenAfter = proc |> MorphirProcessor.unfreeze
    /// </code>
    /// </example>
    let pipeline = PipelineBuilder()

    /// <summary>
    /// Helper type for Pipeline.frozen { ... } syntax.
    /// Creates frozen (immutable) pipelines explicitly.
    /// </summary>
    type FrozenPipelineBuilder() =
        member _.Yield(_) : MorphirProcessor<'T> = MorphirProcessor.empty
        member _.Zero() : MorphirProcessor<'T> = MorphirProcessor.empty
        member _.Run(proc: MorphirProcessor<'T>) : MorphirProcessor<'T> =
            if proc.Frozen then proc else MorphirProcessor.freeze proc
        [<CustomOperation("parse")>]
        member _.Parse(proc: MorphirProcessor<'T>, parser: Parser<'T>) : MorphirProcessor<'T> = MorphirProcessor.parse parser proc
        [<CustomOperation("uses")>]
        member _.Uses(proc: MorphirProcessor<'T>, plugin: Plugin<'T>) : MorphirProcessor<'T> = MorphirProcessor.plugin plugin proc
        [<CustomOperation("stringify")>]
        member _.Stringify(proc: MorphirProcessor<'T>, compiler: Compiler<'T>) : MorphirProcessor<'T> = MorphirProcessor.stringify compiler proc
        [<CustomOperation("data")>]
        member _.Data(proc: MorphirProcessor<'T>, property: Property<'V>, value: 'V) : MorphirProcessor<'T> = MorphirProcessor.set property value proc

    /// <summary>
    /// Helper type for Pipeline.mut { ... } syntax.
    /// Creates unfrozen (mutable) pipelines that can be modified after creation.
    /// </summary>
    type MutablePipelineBuilder() =
        member _.Yield(_) : MorphirProcessor<'T> = MorphirProcessor.empty
        member _.Zero() : MorphirProcessor<'T> = MorphirProcessor.empty
        member _.Run(proc: MorphirProcessor<'T>) : MorphirProcessor<'T> =
            // Never auto-freeze - always return unfrozen
            if proc.Frozen then MorphirProcessor.unfreeze proc else proc
        [<CustomOperation("parse")>]
        member _.Parse(proc: MorphirProcessor<'T>, parser: Parser<'T>) : MorphirProcessor<'T> = MorphirProcessor.parse parser proc
        [<CustomOperation("uses")>]
        member _.Uses(proc: MorphirProcessor<'T>, plugin: Plugin<'T>) : MorphirProcessor<'T> = MorphirProcessor.plugin plugin proc
        [<CustomOperation("stringify")>]
        member _.Stringify(proc: MorphirProcessor<'T>, compiler: Compiler<'T>) : MorphirProcessor<'T> = MorphirProcessor.stringify compiler proc
        [<CustomOperation("data")>]
        member _.Data(proc: MorphirProcessor<'T>, property: Property<'V>, value: 'V) : MorphirProcessor<'T> = MorphirProcessor.set property value proc

    /// <summary>
    /// Module providing explicit pipeline builder variations.
    /// </summary>
    module Pipeline =
        /// <summary>
        /// Explicitly creates an immutable (frozen) pipeline.
        /// This is the default behavior but can be used for clarity.
        /// </summary>
        /// <example>
        /// <code>
        /// let proc = Pipeline.immutable {
        ///     parse irJsonParser
        ///     uses validatePlugin
        /// }
        /// </code>
        /// </example>
        let immutable = FrozenPipelineBuilder()

        /// <summary>
        /// Alias for 'immutable' - creates a frozen (immutable) pipeline.
        /// </summary>
        let frozen = immutable

        /// <summary>
        /// Explicitly creates a mutable (unfrozen) pipeline.
        /// Use when you need to continue modifying the pipeline after creation.
        /// Short for "mutable" to avoid keyword conflicts.
        /// </summary>
        /// <example>
        /// <code>
        /// let proc = Pipeline.mut {
        ///     parse irJsonParser
        ///     uses validatePlugin
        /// }
        /// let extended = proc |> MorphirProcessor.plugin optimizePlugin
        /// </code>
        /// </example>
        let mut = MutablePipelineBuilder()
