using System.Collections.Immutable;
using System.Text.Json;

namespace Morphir.IR.Codecs;

public class DocumentJsonConverterTests
{
    [Test]
    public async Task Should_Serialize_Null_Document()
    {
        var doc = Document.NullDoc;
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("null");
    }

    [Test]
    public async Task Should_Deserialize_Null_Document()
    {
        var json = "null";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        await Assert.That(doc).IsTypeOf<Document.Null>();
    }

    [Test]
    public async Task Should_Serialize_Boolean_True()
    {
        var doc = Document.True;
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("true");
    }

    [Test]
    public async Task Should_Serialize_Boolean_False()
    {
        var doc = Document.False;
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("false");
    }

    [Test]
    public async Task Should_Deserialize_Boolean_True()
    {
        var json = "true";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Boolean>();
            await Assert.That(((Document.Boolean)doc!).Value).IsTrue();
        }
    }

    [Test]
    public async Task Should_Serialize_String()
    {
        var doc = Document.Str("Hello, World!");
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("\"Hello, World!\"");
    }

    [Test]
    public async Task Should_Deserialize_String()
    {
        var json = "\"Hello, World!\"";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.String>();
            await Assert.That(((Document.String)doc!).Value).IsEqualTo("Hello, World!");
        }
    }

    [Test]
    public async Task Should_Preserve_Special_Characters_In_Strings()
    {
        var doc = Document.Str("Line 1\nLine 2\tTabbed");
        var json = MorphirJson.EncodeAsString(doc);
        var roundtrip = MorphirJson.DecodeFromString<Document>(json);

        await Assert.That(((Document.String)roundtrip!).Value).IsEqualTo("Line 1\nLine 2\tTabbed");
    }

    [Test]
    public async Task Should_Serialize_Integer()
    {
        var doc = new Document.Integer(42);
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("42");
    }

    [Test]
    public async Task Should_Deserialize_Integer()
    {
        var json = "42";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Integer>();
            await Assert.That(((Document.Integer)doc!).Value).IsEqualTo(42L);
        }
    }

    [Test]
    public async Task Should_Serialize_Number()
    {
        var doc = new Document.Number(3.14m);
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("3.14");
    }

    [Test]
    public async Task Should_Deserialize_Number()
    {
        var json = "3.14";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Number>();
            await Assert.That(((Document.Number)doc!).Value).IsEqualTo(3.14m);
        }
    }

    [Test]
    public async Task Should_Distinguish_Integer_From_Number()
    {
        var intJson = "42";
        var numJson = "42.0";

        var intDoc = MorphirJson.DecodeFromString<Document>(intJson);
        var numDoc = MorphirJson.DecodeFromString<Document>(numJson);

        using (Assert.Multiple())
        {
            await Assert.That(intDoc).IsTypeOf<Document.Integer>();
            await Assert.That(numDoc).IsTypeOf<Document.Number>();
        }
    }

    [Test]
    public async Task Should_Serialize_Uri_With_Tagged_Format()
    {
        var doc = Document.UriDoc("https://example.com");
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("{\"$uri\":\"https://example.com/\"}");
    }

    [Test]
    public async Task Should_Deserialize_Uri_From_Tagged_Format()
    {
        var json = "{\"$uri\":\"https://example.com\"}";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Uri>();
            await Assert.That(((Document.Uri)doc!).Value.ToString()).IsEqualTo("https://example.com/");
        }
    }

    [Test]
    public async Task Should_Serialize_Uuid_With_Tagged_Format()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var doc = Document.UuidDoc(guid);
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("{\"$uuid\":\"550e8400-e29b-41d4-a716-446655440000\"}");
    }

    [Test]
    public async Task Should_Deserialize_Uuid_From_Tagged_Format()
    {
        var json = "{\"$uuid\":\"550e8400-e29b-41d4-a716-446655440000\"}";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Uuid>();
            await Assert.That(((Document.Uuid)doc!).Value).IsEqualTo(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
        }
    }

    [Test]
    public async Task Should_Serialize_Bytes_With_Tagged_Format_Base64()
    {
        var doc = Document.BytesDoc(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }); // "Hello"
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("{\"$bytes\":\"SGVsbG8=\"}");
    }

    [Test]
    public async Task Should_Deserialize_Bytes_From_Tagged_Format()
    {
        var json = "{\"$bytes\":\"SGVsbG8=\"}";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Bytes>();
            await Assert.That(((Document.Bytes)doc!).Value.ToArray()).IsEquivalentTo(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F });
        }
    }

    [Test]
    public async Task Should_Serialize_Empty_Array()
    {
        var doc = new Document.Array(ImmutableList<Document>.Empty);
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("[]");
    }

    [Test]
    public async Task Should_Deserialize_Empty_Array()
    {
        var json = "[]";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Array>();
            await Assert.That(((Document.Array)doc!).Items).IsEmpty();
        }
    }

    [Test]
    public async Task Should_Serialize_Array_With_Mixed_Types()
    {
        var doc = new Document.Array(ImmutableList.Create<Document>(
            Document.NullDoc,
            Document.True,
            new Document.Integer(42),
            Document.Str("hello")
        ));
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("[null,true,42,\"hello\"]");
    }

    [Test]
    public async Task Should_Deserialize_Array_With_Mixed_Types()
    {
        var json = "[null,true,42,\"hello\"]";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Array>();
            var array = (Document.Array)doc!;
            await Assert.That(array.Items.Count).IsEqualTo(4);
            await Assert.That(array.Items[0]).IsTypeOf<Document.Null>();
            await Assert.That(array.Items[1]).IsTypeOf<Document.Boolean>();
            await Assert.That(array.Items[2]).IsTypeOf<Document.Integer>();
            await Assert.That(array.Items[3]).IsTypeOf<Document.String>();
        }
    }

    [Test]
    public async Task Should_Serialize_Empty_Object()
    {
        var doc = Document.EmptyDoc;
        var json = MorphirJson.EncodeAsString(doc);

        await Assert.That(json).IsEqualTo("{}");
    }

    [Test]
    public async Task Should_Deserialize_Empty_Object()
    {
        var json = "{}";
        var doc = MorphirJson.DecodeFromString<Document>(json);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Object>();
            await Assert.That(((Document.Object)doc!).Items).IsEmpty();
        }
    }

    [Test]
    public async Task Should_Serialize_Object_With_Properties()
    {
        var doc = Document.Obj(
            ("name", Document.Str("Alice")),
            ("age", new Document.Integer(30)),
            ("active", Document.True)
        );
        var json = MorphirJson.EncodeAsString(doc);

        // Note: Order may vary, so we deserialize and check
        var roundtrip = MorphirJson.DecodeFromString<Document>(json);
        await Assert.That(roundtrip).IsTypeOf<Document.Object>();
        var obj = (Document.Object)roundtrip!;
        await Assert.That(obj.Items.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Should_Throw_On_Invalid_Tagged_Format()
    {
        var json = "{\"$unknown\":\"value\"}";

        await Assert.That(() => MorphirJson.DecodeFromString<Document>(json)).Throws<JsonException>();
    }

    [Test]
    public async Task Should_Throw_On_Malformed_Uuid()
    {
        var json = "{\"$uuid\":\"not-a-uuid\"}";

        await Assert.That(() => MorphirJson.DecodeFromString<Document>(json)).Throws<FormatException>();
    }

    [Test]
    public async Task Should_Throw_On_Malformed_Base64()
    {
        var json = "{\"$bytes\":\"not-valid-base64!@#\"}";

        await Assert.That(() => MorphirJson.DecodeFromString<Document>(json)).Throws<FormatException>();
    }

    [Test]
    public async Task Should_Round_Trip_Nested_Structures()
    {
        var doc = Document.Obj(
            ("user", Document.Obj(
                ("name", Document.Str("Bob")),
                ("contact", Document.UriDoc("https://example.com"))
            )),
            ("tags", new Document.Array(ImmutableList.Create<Document>(
                Document.Str("important"),
                Document.Str("verified")
            )))
        );

        var json = MorphirJson.EncodeAsString(doc);
        var roundtrip = MorphirJson.DecodeFromString<Document>(json);

        await Assert.That(roundtrip).IsEqualTo(doc);
    }
}
