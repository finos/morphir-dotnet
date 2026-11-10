namespace Morphir.CodeGeneration.FSharp

/// Options shared by typed-model and legacy JSON generation.
type GenerationOptions =
    {
        /// Whether to run exact constant folding. Validation always runs.
        Optimize: bool
        /// <summary>
        /// Whether to format the source with Fantomas. True formats when the runtime can run Fantomas (not under
        /// Native AOT, where the text stays unformatted and the tree records <c>unavailable</c>). False writes the
        /// emitter's own text: LF line endings only and exactly one final LF, the same bytes on every OS, runtime
        /// and culture.
        /// </summary>
        Format: bool
    }

    /// Existing callers keep constant folding and formatting enabled.
    static member Default = { Optimize = true; Format = true }

    /// <summary>The options with <c>Optimize</c> set, for C# callers, which cannot use a record expression.</summary>
    member this.WithOptimize(optimize: bool) = { this with Optimize = optimize }

    /// <summary>The options with <c>Format</c> set, for C# callers, which cannot use a record expression.</summary>
    member this.WithFormat(format: bool) = { this with Format = format }

    override this.ToString() =
        $"""Optimize = {(if this.Optimize then "true" else "false")}; Format = {(if this.Format then "true" else "false")}"""

/// <summary>
/// What the F# generator did about formatting, as a generated tree records it (Transpiler.formatting) and C# reads it
/// (FSharpGeneration.TryGetFormatting). C# writes it as text through <see cref="WireValue"/>.
/// </summary>
[<RequireQualifiedAccess>]
type FSharpFormatting =
    /// Fantomas formatted the text.
    | Applied
    /// Formatting is off (GenerationOptions.Format is false): the emitter's own text, LF only, one final LF.
    | Off
    /// Formatting was asked for, but the runtime cannot run Fantomas (Native AOT): the emitter's own text.
    | Unavailable

    /// <summary>The text of the report member <c>formatting</c>: <c>applied</c>, <c>off</c> or <c>unavailable</c>.</summary>
    member this.WireValue =
        match this with
        | Applied -> "applied"
        | Off -> "off"
        | Unavailable -> "unavailable"

    override this.ToString() = this.WireValue

/// One generated file in the manifest.
type ManifestEntry = {
    /// The module's dotted path, which selects it for the Myriad host
    ModulePath: string
    /// The generated file's location in the tree
    Location: string
}

/// One module source and its path, as emission produces it.
type internal GeneratedModule = {
    ModulePath: string
    RelativePath: string
    Source: string
}

exception internal GenerationFailure of category: string * location: string * message: string
