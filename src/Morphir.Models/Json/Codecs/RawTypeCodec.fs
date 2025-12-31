namespace Morphir.Json.Codecs

open System.Text.Json
open Morphir.IR.Classic.Aliases
open Morphir.Json

/// <summary>
/// RawTypeCodec provides encoding and decoding for RawType (Type<unit>).
/// This is a specialized instance of the generic TypeCodec with unit attributes.
/// Types are encoded as JSON arrays: [tag, attributes, ...data]
/// For RawType, attributes are always unit (encoded as null).
/// </summary>
module RawTypeCodec =

    /// <summary>
    /// Encodes a RawType to a JsonElement using the specified options.
    /// This is a specialized encoder using TypeCodec.encodeWithOptions with unit attributes.
    /// </summary>
    let encodeWithOptions (options: MorphirJsonOptions) : Encoder<RawType> =
        TypeCodec.encodeWithOptions options Encode.unit

    /// <summary>
    /// Encodes a RawType to a JsonElement using default options (v3).
    /// This is a specialized encoder using TypeCodec.encode with unit attributes.
    /// </summary>
    let encode : Encoder<RawType> =
        TypeCodec.encode Encode.unit

    /// <summary>
    /// Decodes a JsonElement into a RawType using the specified options.
    /// This is a specialized decoder using TypeCodec.decodeWithOptions with unit attributes.
    /// </summary>
    let decodeWithOptions (options: MorphirJsonOptions) : Decoder<RawType> =
        TypeCodec.decodeWithOptions options Decode.unit

    /// <summary>
    /// Decodes a JsonElement into a RawType using default options (v3).
    /// This is a specialized decoder using TypeCodec.decode with unit attributes.
    /// </summary>
    let decode : Decoder<RawType> =
        TypeCodec.decode Decode.unit

    /// <summary>
    /// Convenience function to encode RawType to JSON string using the specified options.
    /// </summary>
    let encodeToStringWithOptions (options: MorphirJsonOptions) (typ: RawType) : string =
        let element = encodeWithOptions options typ
        element.GetRawText()

    /// <summary>
    /// Convenience function to encode RawType to JSON string using default options (v3).
    /// </summary>
    let encodeToString (typ: RawType) : string =
        let element = encode typ
        element.GetRawText()

    /// <summary>
    /// Convenience function to decode RawType from JSON string using the specified options.
    /// </summary>
    let decodeFromStringWithOptions (options: MorphirJsonOptions) (json: string) : Result<RawType, string> =
        try
            use doc = JsonDocument.Parse(json)
            decodeWithOptions options doc.RootElement
        with ex ->
            Error $"Failed to parse JSON: {ex.Message}"

    /// <summary>
    /// Convenience function to decode RawType from JSON string using default options (v3).
    /// </summary>
    let decodeFromString (json: string) : Result<RawType, string> =
        try
            use doc = JsonDocument.Parse(json)
            decode doc.RootElement
        with ex ->
            Error $"Failed to parse JSON: {ex.Message}"
