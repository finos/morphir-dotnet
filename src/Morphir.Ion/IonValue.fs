namespace Morphir.Ion

open System
open System.Globalization
open System.Numerics

/// <summary>The text of an Ion symbol. Symbol zero has no text, which is different from empty text.</summary>
[<RequireQualifiedAccess>]
type IonSymbol =
    | Text of string
    | Zero

/// <summary>The type named by an Ion typed null.</summary>
[<RequireQualifiedAccess>]
type IonNullType =
    | Null
    | Bool
    | Int
    | Float
    | Decimal
    | Timestamp
    | Symbol
    | String
    | Clob
    | Blob
    | List
    | Sexp
    | Struct

/// <summary>An Ion decimal: <c>Coefficient</c> times ten to the negative <c>Scale</c>.</summary>
/// <remarks>Trailing zeros are scale, so <c>1.50</c> and <c>1.5</c> are different values.</remarks>
type IonDecimal = {
    Coefficient: BigInteger
    Scale: int
    IsNegativeZero: bool
}

/// <summary>An Ion float. Equality compares bits, so NaN equals itself and the two zeros differ.</summary>
[<Struct; CustomEquality; NoComparison>]
type IonFloat =
    {
        Value: double
    }

    override this.Equals(other: obj) =
        match other with
        | :? IonFloat as other ->
            BitConverter.DoubleToInt64Bits this.Value = BitConverter.DoubleToInt64Bits other.Value
        | _ -> false

    override this.GetHashCode() =
        BitConverter.DoubleToInt64Bits(this.Value).GetHashCode()

/// <summary>
/// An Ion timestamp in canonical text. The text keeps the declared precision, including the number of
/// fraction digits, so <c>03.000</c> and <c>03</c> are different values.
/// </summary>
type IonTimestamp = { Text: string }

/// <summary>An Ion value and its annotations, in order.</summary>
[<StructuralEquality; NoComparison>]
type IonValue = {
    Annotations: IonSymbol list
    Data: IonData
}

/// <summary>The content of an Ion value. A struct keeps its fields in document order, including repeats.</summary>
and [<RequireQualifiedAccess; StructuralEquality; NoComparison>] IonData =
    | Null of IonNullType
    | Bool of bool
    | Int of BigInteger
    | Float of IonFloat
    | Decimal of IonDecimal
    | Timestamp of IonTimestamp
    | Symbol of IonSymbol
    | String of string
    | Clob of byte array
    | Blob of byte array
    | List of IonValue list
    | Sexp of IonValue list
    | Struct of (IonSymbol * IonValue) list

/// <summary>Limits applied while reading. The root value counts as depth one. Containers and scalars count as nodes; annotations and field names do not.</summary>
type IonReadLimits = {
    MaxDecodedBytes: int
    MaxDepth: int
    MaxNodes: int
}

/// <summary>Functions for working with read limits.</summary>
[<RequireQualifiedAccess>]
module IonReadLimits =
    /// <summary>Defaults for reading local files. MEP callers pass their negotiated limits instead.</summary>
    let defaults = {
        MaxDecodedBytes =
            256
            * 1024
            * 1024
        MaxDepth = 512
        MaxNodes = 50_000_000
    }

/// <summary>Why Ion could not be read.</summary>
[<RequireQualifiedAccess>]
type IonReadError =
    /// <summary>The input declares an Ion version other than 1.0.</summary>
    | UnsupportedIonVersion of message: string
    /// <summary>The input has more bytes than the limit.</summary>
    | DecodedLimitExceeded of limit: int
    /// <summary>The input nests deeper than the limit.</summary>
    | DepthLimitExceeded of limit: int
    /// <summary>The input holds more values than the limit.</summary>
    | NodeLimitExceeded of limit: int
    /// <summary>The input is not valid Ion.</summary>
    | MalformedIon of message: string

/// <summary>Functions for working with read errors.</summary>
[<RequireQualifiedAccess>]
module IonReadError =
    /// <summary>The stable code for an error.</summary>
    let code error =
        match error with
        | IonReadError.UnsupportedIonVersion _ -> "unsupported_ion_version"
        | IonReadError.DecodedLimitExceeded _ -> "decoded_limit_exceeded"
        | IonReadError.DepthLimitExceeded _ -> "depth_limit_exceeded"
        | IonReadError.NodeLimitExceeded _ -> "node_limit_exceeded"
        | IonReadError.MalformedIon _ -> "malformed_ion"

    /// <summary>A readable description of an error.</summary>
    let describe error =
        match error with
        | IonReadError.UnsupportedIonVersion message -> message
        | IonReadError.DecodedLimitExceeded limit ->
            "The input is larger than the limit of "
            + limit.ToString(CultureInfo.InvariantCulture)
            + " bytes."
        | IonReadError.DepthLimitExceeded limit ->
            "The input nests deeper than the limit of "
            + limit.ToString(CultureInfo.InvariantCulture)
            + "."
        | IonReadError.NodeLimitExceeded limit ->
            "The input holds more values than the limit of "
            + limit.ToString(CultureInfo.InvariantCulture)
            + "."
        | IonReadError.MalformedIon message -> message
