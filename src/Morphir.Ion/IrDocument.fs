namespace Morphir.Ion

open System.Text.Json
open Morphir.IR.Classic.Distribution

/// <summary>An IR distribution read from Ion, in the family its format version names.</summary>
type IrDocument =
    /// <summary>A classic IR (1.0.0 to 3.1.0). Attributes stay JSON values.</summary>
    | ClassicIr of formatVersion: string * Distribution<JsonElement, JsonElement>
    /// <summary>An IR v4 distribution.</summary>
    | V4Ir of formatVersion: string * Morphir.IR.V4.Distribution

/// <summary>Why the IR of an Ion document could not be read.</summary>
type IonIrError = {
    /// <summary>A stable code, such as <c>ir_version_mismatch</c> or a JSON reader rule.</summary>
    Code: string
    /// <summary>A readable description.</summary>
    Message: string
    /// <summary>Where in the Ion the problem is, as a JSON-style path such as <c>$[1].values[0]</c>, when the Ion value is at fault.</summary>
    Path: string option
    /// <summary>
    /// A second place, by the kind of error. When the JSON reader is at fault, it is the JSON location that reader
    /// reported. For an error in a file of an Ion document tree, <c>Path</c> is the file's location in the tree
    /// and this is the place in that file, such as <c>$[0].types[1].bogus</c>, counted from the file's own first value.
    /// </summary>
    Location: string option
}
