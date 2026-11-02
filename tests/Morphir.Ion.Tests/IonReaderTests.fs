module Morphir.Ion.Tests.IonReaderTests

open System
open System.Numerics
open System.Text.Json
open Expecto
open Morphir.Ion
open Morphir.Ion.Tests

let private richBinary = "mep/fixtures/rich-model.ionb"
let private richText = "mep/fixtures/rich-model.ion"

let private readWith limits (text: string) =
    IonReader.read limits (Fixtures.utf8 text)

let private read text = readWith IonReadLimits.defaults text

let private readBytes (data: byte array) =
    IonReader.read IonReadLimits.defaults (ReadOnlyMemory<byte> data)

let private single text =
    match read text with
    | Ok [ value ] -> value
    | other -> failtestf "Expected one value from %s, got %A" text other

let private text value = IonSymbol.Text value

let private plain data = { Annotations = []; Data = data }

let private errorCode result =
    match result with
    | Ok values -> failtestf "Expected an error, got %A" values
    | Error error -> IonReadError.code error

let private fields value =
    match value.Data with
    | IonData.Struct fields -> fields
    | other -> failtestf "Expected a struct, got %A" other

let private dec coefficient scale negativeZero =
    IonData.Decimal
        { Coefficient = BigInteger.Parse coefficient
          Scale = scale
          IsNegativeZero = negativeZero }

[<Tests>]
let encodings =
    testList
        "text and binary produce the same values"
        [ testCase "reference fixture in both encodings"
          <| fun () ->
              let binary = readBytes (Fixtures.bytes richBinary)
              let readable = readBytes (Fixtures.bytes richText)
              Expect.isOk binary "binary reads"
              Expect.equal binary readable "the two encodings give equal values"

          testCase "an escape above U+FFFF reads like its binary spelling"
          <| fun () ->
              let emoji = "\U0001F600"
              let binary = readBytes [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy; 0x84uy; 0xF0uy; 0x9Fuy; 0x98uy; 0x80uy |]
              Expect.equal binary (Ok [ plain (IonData.String emoji) ]) "the binary spelling"
              Expect.equal (read "\"\\U0001F600\"") binary "a double-quoted string"
              Expect.equal (read "'''\\U0001F600'''") binary "a triple-quoted string"
              Expect.equal (single "'\\U0001F600'").Data (IonData.Symbol(text emoji)) "a quoted symbol"

          testCase "unsupported text version marker"
          <| fun () -> Expect.equal (errorCode (read "$ion_1_1 {a:1}")) "unsupported_ion_version" "text marker"

          testCase "unsupported binary version marker"
          <| fun () ->
              Expect.equal
                  (errorCode (readBytes [| 0xE0uy; 0x02uy; 0x00uy; 0xEAuy; 0x0Fuy |]))
                  "unsupported_ion_version"
                  "binary marker"

          testCase "the Ion 1.0 text marker is accepted"
          <| fun () -> Expect.equal (read "$ion_1_0 1") (Ok [ plain (IonData.Int BigInteger.One) ]) "text marker"

          testCase "a stream reads like bytes"
          <| fun () ->
              let data = Fixtures.bytes richBinary
              use stream = new IO.MemoryStream(data)
              Expect.equal (IonReader.readStream IonReadLimits.defaults stream) (readBytes data) "same values"

          testCase "an oversized stream is refused"
          <| fun () ->
              use stream = new IO.MemoryStream(Fixtures.bytes richBinary)

              Expect.equal
                  (IonReader.readStream { IonReadLimits.defaults with MaxDecodedBytes = 10 } stream)
                  (Error(IonReadError.DecodedLimitExceeded 10))
                  "over the limit"

          testCase "memory from the middle of an array"
          <| fun () ->
              let data = Fixtures.bytes richBinary
              let padded = Array.concat [ [| 0uy; 0uy |]; data; [| 0uy |] ]

              Expect.equal
                  (IonReader.read IonReadLimits.defaults (ReadOnlyMemory<byte>(padded, 2, data.Length)))
                  (readBytes data)
                  "only the slice is read" ]

[<Tests>]
let distinctions =
    testList
        "values keep every Ion distinction"
        [ testCase "numeric exactness"
          <| fun () ->
              Expect.equal (single "9007199254740993").Data (IonData.Int(BigInteger.Parse "9007199254740993")) "int"

              Expect.equal
                  (single "18446744073709551616000001").Data
                  (IonData.Int(BigInteger.Parse "18446744073709551616000001"))
                  "big int"

              Expect.equal (single "123.4500").Data (dec "1234500" 4 false) "scale"
              Expect.equal (single "-0d0").Data (dec "0" 0 true) "negative zero"
              Expect.notEqual (single "1.50").Data (single "1.5").Data "trailing zeros are scale"

          testCase "ordered annotations"
          <| fun () ->
              let value = single "source_name::target_name::{}"
              Expect.equal value.Annotations [ text "source_name"; text "target_name" ] "order"

          testCase "timestamp precision"
          <| fun () ->
              let millis = single "2026-10-01T01:02:03.004-00:00"
              let whole = single "2026-10-01T01:02:03.000-00:00"
              let seconds = single "2026-10-01T01:02:03-00:00"

              Expect.equal millis.Data (IonData.Timestamp { Text = "2026-10-01T01:02:03.004-00:00" }) "three digits"
              Expect.equal seconds.Data (IonData.Timestamp { Text = "2026-10-01T01:02:03-00:00" }) "no fraction"
              Expect.notEqual whole.Data seconds.Data "zero digits still count"

          testCase "timestamp precisions and offsets"
          <| fun () ->
              for input, expected in
                  [ "2007T", "2007T"
                    "2007-01T", "2007-01T"
                    "2007-01-02T", "2007-01-02"
                    "2007-01-02T03:04Z", "2007-01-02T03:04Z"
                    "2007-01-02T03:04:05+01:30", "2007-01-02T03:04:05+01:30"
                    "2007-01-02T03:04:05.5-08:00", "2007-01-02T03:04:05.5-08:00" ] do
                  Expect.equal (single input).Data (IonData.Timestamp { Text = expected }) input

          testCase "symbol and string stay distinct"
          <| fun () ->
              Expect.equal (single "abc").Data (IonData.Symbol(text "abc")) "symbol"
              Expect.equal (single "\"abc\"").Data (IonData.String "abc") "string"
              Expect.notEqual (single "abc") (single "\"abc\"") "not equal"

          testCase "symbol zero has no text"
          <| fun () ->
              Expect.equal (single "$0").Data (IonData.Symbol IonSymbol.Zero) "symbol zero"
              Expect.notEqual (single "$0").Data (single "''").Data "different from empty text"

          testCase "typed nulls keep their type"
          <| fun () ->
              Expect.equal (single "null.int").Data (IonData.Null IonNullType.Int) "null.int"
              Expect.equal (single "null").Data (IonData.Null IonNullType.Null) "null"
              Expect.equal (single "null.struct").Data (IonData.Null IonNullType.Struct) "null.struct"

          testCase "floats compare by bits"
          <| fun () ->
              Expect.equal (single "nan") (single "nan") "NaN equals itself"
              Expect.notEqual (single "0e0") (single "-0e0") "the two zeros differ"

          testCase "lobs keep their bytes"
          <| fun () ->
              Expect.equal (single "{{AQID}}").Data (IonData.Blob [| 1uy; 2uy; 3uy |]) "blob"
              Expect.equal (single "{{\"abc\"}}").Data (IonData.Clob [| 97uy; 98uy; 99uy |]) "clob"

          testCase "containers"
          <| fun () ->
              Expect.equal
                  (single "(a [1, 2])").Data
                  (IonData.Sexp
                      [ plain (IonData.Symbol(text "a"))
                        plain (IonData.List [ plain (IonData.Int BigInteger.One); plain (IonData.Int(BigInteger 2)) ]) ])
                  "sexp holding a list"

          testCase "symbol escapes the draft lists"
          <| fun () ->
              Expect.equal
                  (single @"'\0\a\b\t\n\v\f\r\\\'\""'").Data
                  (IonData.Symbol(text "\000\a\b\t\n\v\f\r\\'\""))
                  "every escape" ]

[<Tests>]
let structs =
    testList
        "structs keep order and repeated fields"
        [ testCase "repeated field"
          <| fun () ->
              let value = single "{duplicate:1,duplicate:2}"

              Expect.equal
                  (fields value)
                  [ text "duplicate", plain (IonData.Int BigInteger.One)
                    text "duplicate", plain (IonData.Int(BigInteger 2)) ]
                  "both fields in order"

          testCase "document order"
          <| fun () ->
              Expect.equal
                  (fields (single "{b:1,a:2}") |> List.map fst)
                  [ text "b"; text "a" ]
                  "fields are not sorted" ]

[<Tests>]
let limits =
    let defaults = IonReadLimits.defaults

    testList
        "reading is bounded"
        [ testCase "byte limit"
          <| fun () ->
              Expect.equal
                  (readWith { defaults with MaxDecodedBytes = 3 } "[1, 2, 3]")
                  (Error(IonReadError.DecodedLimitExceeded 3))
                  "over"

              Expect.isOk (readWith { defaults with MaxDecodedBytes = 9 } "[1, 2, 3]") "exactly at the limit"

          testCase "the byte limit applies before parsing"
          <| fun () ->
              Expect.equal
                  (errorCode (readWith { defaults with MaxDecodedBytes = 3 } "((((((((((("))
                  "decoded_limit_exceeded"
                  "malformed input over the limit is never parsed"

          testCase "depth limit"
          <| fun () ->
              let deep = String('[', 5000) + String(']', 5000)
              Expect.equal (read deep) (Error(IonReadError.DepthLimitExceeded 512)) "5000 lists"

          testCase "depth counts the root as one"
          <| fun () ->
              Expect.isOk (readWith { defaults with MaxDepth = 1 } "1") "a scalar at the root"
              Expect.isOk (readWith { defaults with MaxDepth = 2 } "[1]") "a scalar inside a list"
              Expect.equal (errorCode (readWith { defaults with MaxDepth = 1 } "[1]")) "depth_limit_exceeded" "over"

          testCase "the default depth limit reads"
          <| fun () ->
              let deep = String('[', 511) + "1" + String(']', 511)
              Expect.isOk (read deep) "512 levels"

          testCase "node limit"
          <| fun () ->
              Expect.isOk (readWith { defaults with MaxNodes = 3 } "[1, 2]") "a container and two scalars"
              Expect.equal (errorCode (readWith { defaults with MaxNodes = 2 } "[1, 2]")) "node_limit_exceeded" "over"

          testCase "annotations and field names are not nodes"
          <| fun () -> Expect.isOk (readWith { defaults with MaxNodes = 2 } "a::b::c::{ field: 1 }") "two nodes" ]

[<Tests>]
let malformed =
    let code = "malformed_ion"

    testList
        "malformed input returns a typed error"
        [ testCase "truncated text"
          <| fun () -> Expect.equal (errorCode (read "morphir::{ ionVersion: \"x\"")) code "no partial value"

          testCase "truncated binary"
          <| fun () ->
              Expect.equal (errorCode (readBytes (Fixtures.bytes richBinary |> Array.take 100))) code "no partial value"

          testCase "trailing bytes"
          <| fun () ->
              let data = Array.append (Fixtures.bytes richBinary) [| 0xFFuy; 0xFFuy |]
              Expect.equal (errorCode (readBytes data)) code "no partial value"

          testCase "list items without commas"
          <| fun () -> Expect.equal (errorCode (read "[a b]")) code "Ion requires commas"

          testCase "invalid UTF-8 in binary"
          <| fun () ->
              Expect.equal
                  (errorCode (readBytes [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy; 0x82uy; 0xFFuy; 0xFEuy |]))
                  code
                  "no replacement characters"

          testCase "unknown symbol id"
          <| fun () -> Expect.equal (errorCode (read "$99")) code "no symbol table defines it"

          testCase "field name without a value"
          <| fun () -> Expect.equal (errorCode (read "{a}")) code "needs a colon"

          testCase "dangling annotation"
          <| fun () -> Expect.equal (errorCode (read "a::")) code "needs a value"

          testCase "no exception escapes for any truncation of the binary fixture"
          <| fun () ->
              let data = Fixtures.bytes richBinary

              // The fixture opens with a local symbol table, a wrapper whose length is a two-byte VarUInt.
              // A cut exactly after it leaves a complete document with no user values.
              let symbolTableEnd =
                  Expect.equal data[4] 0xEEuy "the wrapper's type descriptor"
                  7 + ((int data[5] <<< 7) ||| (int data[6] &&& 0x7F))

              for length in 5 .. data.Length - 1 do
                  if length = symbolTableEnd then
                      Expect.equal (readBytes (Array.take length data)) (Ok []) "only a symbol table"
                  else
                      Expect.equal (errorCode (readBytes (Array.take length data))) code $"{length} bytes"

          testCase "invalid UTF-8 in text"
          <| fun () ->
              Expect.equal
                  (errorCode (readBytes [| byte '"'; 0xFFuy; 0xFEuy; byte '"' |]))
                  code
                  "no replacement characters"

          testCase "an escape above U+10FFFF"
          <| fun () -> Expect.equal (errorCode (read "\"\\U00110000\"")) code "not a Unicode scalar"

          testCase "no exception escapes for any truncation of the text fixture"
          <| fun () ->
              let data = Fixtures.bytes richText

              for length in 0 .. data.Length - 1 do
                  readBytes (Array.take length data) |> ignore

          testCase "no exception escapes for any single corrupted binary byte"
          <| fun () ->
              let data = Fixtures.bytes richBinary

              for index in 0 .. data.Length - 1 do
                  let copy = Array.copy data
                  copy[index] <- copy[index] ^^^ 0xFFuy
                  readBytes copy |> ignore

          testCase "empty input has no values"
          <| fun () -> Expect.equal (readBytes [||]) (Ok []) "nothing to read" ]

[<Tests>]
let vendored =
    testList
        "vendored MEP corpus"
        [ testCase "every asset matches the corpus manifest"
          <| fun () ->
              use manifest = JsonDocument.Parse(Fixtures.text "mep/model-transport.cases.json")
              let assets = manifest.RootElement.GetProperty("assets").EnumerateArray() |> Seq.toList
              Expect.isNonEmpty assets "the manifest lists assets"

              for asset in assets do
                  let path = asset.GetProperty("path").GetString()
                  let data = Fixtures.bytes ("mep/" + path)
                  Expect.equal data.Length (asset.GetProperty("decodedByteLength").GetInt32()) path
                  Expect.equal (Fixtures.sha256 data) (asset.GetProperty("sha256").GetString()) path
                  Expect.equal (Convert.ToBase64String data) (asset.GetProperty("content").GetString()) path ]
