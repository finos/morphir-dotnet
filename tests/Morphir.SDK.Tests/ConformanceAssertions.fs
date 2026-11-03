module Morphir.SDK.Tests.ConformanceAssertions

open System
open System.Collections
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Numerics
open System.Text.Json
open Microsoft.FSharp.Reflection
open Expecto

// Observation and literal construction only. Every operation under test is a
// typed call in ConformanceCases.fs, authored from the frozen independent corpus.
let decimalInput (coefficient: string) (scale: int) =
    let mutable coefficient =
        BigInteger.Parse(coefficient, CultureInfo.InvariantCulture)

    let mutable scale = scale

    while scale > 0
          && (scale > 28
              || BigInteger.Abs coefficient > (1I <<< 96)
                                              - 1I)
          && coefficient % 10I = 0I do
        coefficient <-
            coefficient
            / 10I

        scale <- scale - 1

    if
        scale < 0
        || scale > 28
        || BigInteger.Abs coefficient > (1I <<< 96)
                                        - 1I
    then
        invalidArg "coefficient" "Fixture is outside the admitted Decimal96 profile"

    let magnitude = BigInteger.Abs coefficient

    let word shift =
        int (
            uint32 (
                (magnitude
                 >>> shift)
                &&& 4294967295I
            )
        )

    Decimal(word 0, word 32, word 64, coefficient.Sign < 0, byte scale)

let floatInput text =
    match text with
    | "NaN" -> Double.NaN
    | "+Infinity" -> Double.PositiveInfinity
    | "-Infinity" -> Double.NegativeInfinity
    | "+0" -> 0.0
    | "-0" -> BitConverter.Int64BitsToDouble(Int64.MinValue)
    | finite -> Double.Parse(finite, CultureInfo.InvariantCulture)

// Snapshots preserve IEEE zero signs/NaN classification recursively. The SDK's
// equality/hash invariants below separately use actual F# structural semantics.
let rec snapshot (value: obj) =
    if isNull value then
        "null"
    else
        let typ = value.GetType()

        match value with
        | :? double as number ->
            if Double.IsNaN number then
                "Float:NaN"
            else
                sprintf "Float:%016x" (BitConverter.DoubleToInt64Bits number)
        | :? decimal as number ->
            "Decimal:"
            + number.ToString("G29", CultureInfo.InvariantCulture)
        | :? string as text ->
            "String:"
            + JsonSerializer.Serialize text
        | :? char as character -> sprintf "Char:%04x" (int character)
        | :? int as number ->
            "Int:"
            + number.ToString(CultureInfo.InvariantCulture)
        | :? bool as flag -> sprintf "Bool:%b" flag
        | _ when FSharpType.IsTuple typ ->
            FSharpValue.GetTupleFields value
            |> Array.map snapshot
            |> String.concat ";"
            |> sprintf "Tuple(%s)"
        | _ when FSharpType.IsUnion typ ->
            let case, fields = FSharpValue.GetUnionFields(value, typ)

            fields
            |> Array.map snapshot
            |> String.concat ";"
            |> sprintf "%s(%s)" case.Name
        | :? IEnumerable as items ->
            items
            |> Seq.cast<obj>
            |> Seq.map (fun item ->
                let itemType = item.GetType()

                if
                    itemType.IsGenericType
                    && itemType.GetGenericTypeDefinition() = typedefof<KeyValuePair<_, _>>
                then
                    snapshot (itemType.GetProperty("Key").GetValue item)
                    + ":"
                    + snapshot (itemType.GetProperty("Value").GetValue item)
                else
                    snapshot item
            )
            |> String.concat ";"
            |> sprintf "%s[%s]" typ.FullName
        | _ when typ = typeof<unit> -> "Unit"
        | _ -> failwithf "No observation adapter for %s" typ.FullName

let normalizeError (error: exn) =
    match error with
    | :? OverflowException -> "Overflow"
    | :? DivideByZeroException -> "DivisionByZero"
    | :? ArithmeticException -> "NonFiniteConversion"
    | other -> sprintf "Unexpected:%s:%s" (other.GetType().FullName) other.Message

let private execution = Dictionary<string, bool>(StringComparer.Ordinal)

let check id (expected: Choice<'a, string>) (invoke: unit -> 'a) preserve callbacks invariants =
    let mutable passed = false

    try
        let observation =
            try
                Choice1Of2(invoke ())
            with error ->
                Choice2Of2 error

        preserve ()
        callbacks observation

        match expected, observation with
        | Choice1Of2 value, Choice1Of2 actual ->
            Expect.equal
                (snapshot (box actual))
                (snapshot (box value))
                (id
                 + " observation")

            invariants actual
        | Choice2Of2 code, Choice2Of2 error ->
            Expect.equal
                (normalizeError error)
                code
                (id
                 + " normalized error")
        | Choice1Of2 _, Choice2Of2 error -> failtestf "%s unexpectedly threw %O" id error
        | Choice2Of2 code, Choice1Of2 value -> failtestf "%s expected %s, got %A" id code value

        passed <- true
    finally
        execution.[id] <- passed

        match Environment.GetEnvironmentVariable "MORPHIR_FSHARP_CONFORMANCE_RESULTS" with
        | null
        | "" -> ()
        | path ->
            File.AppendAllText(
                path,
                JsonSerializer.Serialize({| caseId = id; passed = passed |})
                + Environment.NewLine
            )

let assertComplete (caseIds: string list) =
    let corpus =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "MorphirSDK", "cases.json")

    use document = JsonDocument.Parse(File.ReadAllText corpus)

    let cases =
        document.RootElement.GetProperty("cases").EnumerateArray()
        |> Seq.toArray

    let required =
        cases
        |> Array.filter (fun case -> case.GetProperty("classification").GetString() = "required")

    let requiredIds =
        required
        |> Array.map (fun case -> case.GetProperty("caseId").GetString())
        |> Set.ofArray

    Expect.equal (Set.ofList caseIds) requiredIds "Adapter exactly covers frozen required IDs"
    Expect.equal requiredIds.Count 207 "Frozen required count"

    Expect.equal
        (cases.Length
         - required.Length)
        8
        "Representation exclusions are separate, never passes"

    Expect.equal
        (execution.Keys
         |> Set.ofSeq)
        requiredIds
        "Every required ID actually executed"

    Expect.isTrue
        (execution.Values
         |> Seq.forall id)
        "Every executed required ID passed"
