/// The RFC 8785 (JCS) writer: the RFC's own examples, read from the pinned RFC text through FixtureCatalog, and the
/// UTF-8 measure that the compaction policy weighs sizes with.
module Morphir.Json.Tests.CanonicalJsonTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Morphir.Json
open Morphir.Models.TestSupport
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

        testList "The policy's form" [
            let sorted (_: string) = CanonicalJson.MemberOrder.Sorted

            /// The raw text of each number token of JSON text, in order.
            let numbers (bytes: byte array) =
                let mutable reader = Utf8JsonReader(ReadOnlySpan bytes)
                let lexemes = ResizeArray()

                while reader.Read() do
                    if reader.TokenType = JsonTokenType.Number then
                        lexemes.Add(Encoding.UTF8.GetString(reader.ValueSpan))

                List.ofSeq lexemes

            test "keeps each number's lexeme, where RFC 8785 rewrites it" {
                let sample = parse vectors.PrimitiveSample
                let source = numbers (Encoding.UTF8.GetBytes(sample.GetRawText()))
                Expect.sequenceEqual (numbers (CanonicalJson.writeSourceForm sorted sample)) source "the source lexemes"
                Expect.notEqual (numbers (CanonicalJson.write sample)) source "RFC 8785 writes the sample's numbers otherwise"
            }

            test "sorts members and escapes strings as RFC 8785 does" {
                let sample = parse vectors.SortingSample
                Expect.sequenceEqual (CanonicalJson.writeSourceForm sorted sample) (CanonicalJson.write sample) "a sample with no number"
            }

            test "keeps the order of an object it is told to" {
                let sample = parse vectors.SortingSample
                let kept = CanonicalJson.writeSourceForm (fun pointer -> if pointer = "" then CanonicalJson.MemberOrder.Kept else CanonicalJson.MemberOrder.Sorted) sample
                use document = JsonDocument.Parse kept
                let names = [ for property in document.RootElement.EnumerateObject() -> property.Name ]
                Expect.sequenceEqual names [ for property in sample.EnumerateObject() -> property.Name ] "the document's order"
            }

            test "names an object below the root by its JSON Pointer, and keeps order throughout when told to" {
                // The sorting sample inside the array /a~1b, inside an object that is sorted.
                let nested =
                    written (fun writer ->
                        writer.WriteStartObject()
                        writer.WriteNumber("z", 0)
                        writer.WriteStartArray "a/b"
                        writer.WriteRawValue vectors.SortingSample
                        writer.WriteEndArray()
                        writer.WriteEndObject()
                    )

                let order (wanted: string) (memberOrder: CanonicalJson.MemberOrder) (pointer: string) =
                    if pointer = wanted then memberOrder else CanonicalJson.MemberOrder.Sorted

                let namesOf (bytes: byte array) =
                    use document = JsonDocument.Parse bytes
                    let item = document.RootElement.GetProperty("a/b").[0]
                    [ for property in document.RootElement.EnumerateObject() -> property.Name ], [ for property in item.EnumerateObject() -> property.Name ]

                let documentOrder = [ for property in (parse vectors.SortingSample).EnumerateObject() -> property.Name ]

                for kept in [ namesOf (CanonicalJson.writeSourceForm (order "/a~1b/0" CanonicalJson.MemberOrder.Kept) nested)
                              namesOf (CanonicalJson.writeSourceForm (order "/a~1b" CanonicalJson.MemberOrder.KeptThroughout) nested) ] do
                    Expect.equal (fst kept) [ "a/b"; "z" ] "the root is sorted"
                    Expect.sequenceEqual (snd kept) documentOrder "the object inside keeps its order"
            }
        ]

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

        // The netstandard2.0 build checks the runtime it is loaded on; these tests run on net11.0, so they call the check
        // with each runtime's own FrameworkDescription and Environment.Version.
        testList "Canonical numbers need .NET Core 3.0 or later" [
            for description, version in [
                ".NET 11.0.0", "11.0.0"
                ".NET 5.0.17", "5.0.17"
                ".NET Core 3.1.32", "3.1.32"
                ".NET Core 3.0.3", "3.0.3"
            ] do
                yield test $"{description} writes canonical numbers" {
                    CanonicalJson.checkRuntime description (System.Version version)
                }

            for description, version in [
                ".NET Framework 4.8.9032.0", "4.0.30319.42000"
                ".NET Framework 4.6.2", "4.0.30319.42000"
                // .NET Core 2.x reports a 4.6 description and the version of .NET Framework 4.
                ".NET Core 4.6.27817.03", "4.0.30319.42000"
                "Mono 6.12.0.182 (2020-02/6051b710799)", "4.0.30319.42000"
                ".NET Native 2.2", "4.0.30319.42000"
            ] do
                yield test $"{description} is refused" {
                    let refusal =
                        Expect.throwsC
                            (fun () -> CanonicalJson.checkRuntime description (System.Version version))
                            id

                    Expect.isTrue (refusal :? PlatformNotSupportedException) "the refusal is PlatformNotSupportedException"
                    Expect.stringContains refusal.Message ".NET Core 3.0 or later" "the message names the runtimes that are supported"
                    Expect.stringContains refusal.Message description "the message names the runtime"
                }

            yield test "The runtime of these tests writes canonical numbers" {
                CanonicalJson.checkRuntime Runtime.InteropServices.RuntimeInformation.FrameworkDescription Environment.Version
            }
        ]

        // The parent's compaction policy fixes its expected bytes in canonical JSON, worked out by hand.
        testList "Each policy case's expected bytes are canonical JSON" [
            for policyCase in MorphirFileMother.policyCases ->
                test policyCase.Id {
                    let expected = MorphirFileMother.policyExpectedBytes policyCase.Id
                    Expect.sequenceEqual (CanonicalJson.write (parse (utf8 expected))) expected "the parent's bytes are their own canonical form"

                    if not policyCase.Compacted then
                        Expect.sequenceEqual
                            (CanonicalJson.write (MorphirFileMother.policyInput policyCase.Id))
                            expected
                            "a declined case is its input in canonical JSON"
                }
        ]
    ]
