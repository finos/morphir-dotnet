/// The RFC 8785 (JCS) writer: the RFC's own examples, read from the pinned RFC text through FixtureCatalog, and the
/// UTF-8 measure that the compaction policy weighs sizes with.
module Morphir.Json.Tests.CanonicalJsonTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Morphir.Json
open Morphir.TestSupport.Fixtures

let private vectors = FixtureCatalog.Rfc8785.Vectors

let private parse (text: string) =
    use document = JsonDocument.Parse text
    document.RootElement.Clone()

let private written (write: Utf8JsonWriter -> unit) =
    use buffer = new MemoryStream()

    do
        use writer = new Utf8JsonWriter(buffer)
        write writer

    parse (Encoding.UTF8.GetString(buffer.ToArray()))

/// A JSON string value written by Utf8JsonWriter, which escapes non-ASCII text by default (é as \u00E9).
let private stringValue (value: string) =
    written (fun writer -> writer.WriteStringValue value)

/// An object with these members, in this order, as Utf8JsonWriter writes it (it does not refuse a repeated name).
let private members (pairs: (string * int) list) =
    written (fun writer ->
        writer.WriteStartObject()

        for name, value in pairs do
            writer.WriteNumber(name, value)

        writer.WriteEndObject())

/// A JSON token that Utf8JsonWriter refuses to write from a .NET value, written as raw JSON.
let private rawToken (token: string) =
    written (fun writer -> writer.WriteRawValue(token, true))

let private utf8 (bytes: byte array) = Encoding.UTF8.GetString bytes

let private refused (write: unit -> 'a) =
    Expect.throwsT<ArgumentException> (write >> ignore) "JCS refuses the value"

[<Tests>]
let tests =
    testList "Canonical JSON (RFC 8785)" [
        testList "Each number sample of Appendix B" [
            for sample in vectors.Numbers do
                match sample.Json with
                | null ->
                    test $"%016x{sample.Ieee754} ({sample.Comment}) is refused" {
                        refused (fun () -> CanonicalJson.number sample.Value)
                    }
                | json ->
                    test $"%016x{sample.Ieee754} is written as {json}" {
                        Expect.equal (CanonicalJson.number sample.Value) json "the ECMAScript form of the double"
                        Expect.equal (utf8 (CanonicalJson.write (parse json))) json "the canonical text is its own canonical form"
                    }
        ]

        test "The primitive sample is written as the bytes of the UTF-8 section" {
            let canonical = CanonicalJson.write (parse vectors.PrimitiveSample)
            Expect.sequenceEqual canonical vectors.PrimitiveSampleCanonical "the bytes of Section 3.2.4"
        }

        test "The sorting sample orders its properties by UTF-16 code units" {
            use document = JsonDocument.Parse(CanonicalJson.write (parse vectors.SortingSample))

            let values =
                [ for property in document.RootElement.EnumerateObject() -> property.Value.GetString() ]

            Expect.sequenceEqual values vectors.SortedValues "the order of Section 3.2.3"
        }

        test "Writing the same value twice gives the same bytes" {
            let value = parse vectors.PrimitiveSample
            Expect.sequenceEqual (CanonicalJson.write value) (CanonicalJson.write value) "JCS output is deterministic"
        }

        test "Non-ASCII content is measured as UTF-8" {
            Expect.equal (CanonicalJson.byteLength "é") 4 "é is two bytes, between two quotes"
            Expect.equal (CanonicalJson.byteLength "é" - CanonicalJson.byteLength "e") 1 "é is one byte longer than e"

            let canonical = CanonicalJson.write (stringValue "é")
            Expect.sequenceEqual canonical [| 0x22uy; 0xC3uy; 0xA9uy; 0x22uy |] "é is written unescaped, as its UTF-8 bytes"
            Expect.equal canonical.Length (CanonicalJson.byteLength "é") "the measure is the length of the written literal"
        }

        test "A string literal is measured with its quotes and escapes" {
            Expect.equal (CanonicalJson.byteLength "") 2 "the two quotes"
            Expect.equal (CanonicalJson.byteLength "a\"b") 6 "a quote is escaped as two bytes"
            Expect.equal (CanonicalJson.byteLength "\n") 4 "a newline is \\n"
            Expect.equal (CanonicalJson.byteLength "\u000f") 8 "another control character is \\u000f"
            Expect.equal (CanonicalJson.byteLength "/") 3 "a solidus is not escaped"
        }

        testList "Numbers take the ECMAScript form" [
            for value, expected in [ 1e21, "1e+21"; -0.0, "0"; 0.1, "0.1"; 1e-7, "1e-7"; 4.0, "4"; -1.5, "-1.5" ] ->
                test $"{expected}" { Expect.equal (CanonicalJson.number value) expected "the ECMAScript form" }
        ]

        test "A lone surrogate is refused" {
            let loneSurrogate = rawToken "\"\\udead\""
            refused (fun () -> CanonicalJson.write loneSurrogate)
            refused (fun () -> CanonicalJson.byteLength (string (char 0xD83D)))
        }

        test "A number out of the double range is refused" {
            let outOfRange = rawToken "1e400"
            refused (fun () -> CanonicalJson.write outOfRange)
        }

        test "An object with the same member twice is refused" {
            let twice = members [ "a", 1; "a", 2 ]
            refused (fun () -> CanonicalJson.write twice)
        }
    ]
