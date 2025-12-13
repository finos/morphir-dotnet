using System.Collections.Immutable;
using System.Text;

namespace Morphir.IR;

public class DocumentTests
{
    #region Boolean Tests

    [Test]
    public async Task Boolean_Should_Store_True_Value()
    {
        var doc = new Document.Boolean(true);
        await Assert.That(doc.Value).IsTrue();
    }

    [Test]
    public async Task Boolean_Should_Store_False_Value()
    {
        var doc = new Document.Boolean(false);
        await Assert.That(doc.Value).IsFalse();
    }

    [Test]
    public async Task Boolean_True_Static_Property_Should_Return_True()
    {
        var doc = Document.True;
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Value).IsTrue();
        }
    }

    [Test]
    public async Task Boolean_False_Static_Property_Should_Return_False()
    {
        var doc = Document.False;
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Value).IsFalse();
        }
    }

    [Test]
    public async Task Boolean_Bool_Factory_Should_Create_True()
    {
        var doc = Document.Bool(true);
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Value).IsTrue();
        }
    }

    [Test]
    public async Task Boolean_Bool_Factory_Should_Create_False()
    {
        var doc = Document.Bool(false);
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Value).IsFalse();
        }
    }

    [Test]
    public async Task Boolean_Should_Support_Equality()
    {
        var doc1 = new Document.Boolean(true);
        var doc2 = new Document.Boolean(true);
        var doc3 = new Document.Boolean(false);

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsNotEqualTo(doc3);
        }
    }

    #endregion

    #region Null Tests

    [Test]
    public async Task Null_Should_Create_Instance()
    {
        var doc = new Document.Null();
        await Assert.That(doc).IsNotNull();
    }

    [Test]
    public async Task Null_NullDoc_Static_Property_Should_Return_Null()
    {
        var doc = Document.NullDoc;
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Null>();
            await Assert.That(doc).IsNotNull();
        }
    }

    [Test]
    public async Task Null_Should_Support_Equality()
    {
        var doc1 = new Document.Null();
        var doc2 = new Document.Null();
        var doc3 = Document.NullDoc;

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsEqualTo(doc3);
            await Assert.That(doc2).IsEqualTo(doc3);
        }
    }

    [Test]
    public async Task Null_Should_Not_Equal_Other_Types()
    {
        Document nullDoc = Document.NullDoc;
        Document boolDoc = Document.False;
        Document intDoc = new Document.Integer(0);

        using (Assert.Multiple())
        {
            await Assert.That(nullDoc).IsNotEqualTo(boolDoc);
            await Assert.That(nullDoc).IsNotEqualTo(intDoc);
        }
    }

    [Test]
    public async Task Null_Should_Be_Usable_In_Arrays()
    {
        var items = ImmutableList.Create<Document>(
            Document.NullDoc,
            Document.True,
            Document.NullDoc
        );
        var doc = new Document.Array(items);

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(3);
            await Assert.That(doc.Items[0]).IsTypeOf<Document.Null>();
            await Assert.That(doc.Items[1]).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Items[2]).IsTypeOf<Document.Null>();
        }
    }

    [Test]
    public async Task Null_Should_Be_Usable_In_Objects()
    {
        var items = ImmutableDictionary<string, Document>.Empty
            .Add("value", Document.NullDoc)
            .Add("active", Document.True);
        var doc = new Document.Object(items);

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(2);
            await Assert.That(doc.Items["value"]).IsTypeOf<Document.Null>();
            await Assert.That(doc.Items["active"]).IsTypeOf<Document.Boolean>();
        }
    }

    #endregion

    #region String Tests

    [Test]
    public async Task String_Should_Store_String_Value()
    {
        var doc = new Document.String("hello");
        await Assert.That(doc.Value).IsEqualTo("hello");
    }

    [Test]
    public async Task String_Should_Store_Empty_String()
    {
        var doc = new Document.String("");
        await Assert.That(doc.Value).IsEqualTo("");
    }

    [Test]
    public async Task String_Should_Store_Multiline_String()
    {
        var value = "line1\nline2\nline3";
        var doc = new Document.String(value);
        await Assert.That(doc.Value).IsEqualTo(value);
    }

    [Test]
    public async Task String_Should_Store_Special_Characters()
    {
        var value = "Hello, \"World\"!\t\n";
        var doc = new Document.String(value);
        await Assert.That(doc.Value).IsEqualTo(value);
    }

    [Test]
    public async Task String_Str_Factory_Should_Create_String()
    {
        var doc = Document.Str("test");
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.String>();
            await Assert.That(doc.Value).IsEqualTo("test");
        }
    }

    [Test]
    public async Task String_Should_Support_Equality()
    {
        var doc1 = new Document.String("hello");
        var doc2 = new Document.String("hello");
        var doc3 = new Document.String("world");

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsNotEqualTo(doc3);
        }
    }

    #endregion

    #region Uri Tests

    [Test]
    public async Task Uri_Should_Store_Uri_Value()
    {
        var uri = new System.Uri("https://example.com");
        var doc = new Document.Uri(uri);
        await Assert.That(doc.Value).IsEqualTo(uri);
    }

    [Test]
    public async Task Uri_Should_Store_Complex_Uri()
    {
        var uri = new System.Uri("https://example.com:8080/path/to/resource?query=value&other=123#fragment");
        var doc = new Document.Uri(uri);
        await Assert.That(doc.Value).IsEqualTo(uri);
    }

    [Test]
    public async Task Uri_UriDoc_Factory_Should_Create_From_Uri()
    {
        var uri = new System.Uri("https://example.com");
        var doc = Document.UriDoc(uri);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Uri>();
            await Assert.That(doc.Value).IsEqualTo(uri);
        }
    }

    [Test]
    public async Task Uri_UriDoc_Factory_Should_Create_From_String()
    {
        var doc = Document.UriDoc("https://example.com");

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Uri>();
            await Assert.That(doc.Value.ToString()).IsEqualTo("https://example.com/");
        }
    }

    [Test]
    public async Task Uri_UriDoc_Factory_Should_Throw_On_Null_Uri()
    {
        await Assert.That(() => Document.UriDoc((System.Uri)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Uri_UriDoc_Factory_Should_Throw_On_Invalid_Uri()
    {
        await Assert.That(() => Document.UriDoc("not a valid uri")).Throws<UriFormatException>();
    }

    [Test]
    public async Task Uri_Should_Support_Equality()
    {
        var uri1 = new System.Uri("https://example.com");
        var uri2 = new System.Uri("https://example.com");
        var uri3 = new System.Uri("https://other.com");
        var doc1 = new Document.Uri(uri1);
        var doc2 = new Document.Uri(uri2);
        var doc3 = new Document.Uri(uri3);

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsNotEqualTo(doc3);
        }
    }

    #endregion

    #region Uuid Tests

    [Test]
    public async Task Uuid_Should_Store_Guid_Value()
    {
        var guid = Guid.NewGuid();
        var doc = new Document.Uuid(guid);
        await Assert.That(doc.Value).IsEqualTo(guid);
    }

    [Test]
    public async Task Uuid_Should_Store_Empty_Guid()
    {
        var guid = Guid.Empty;
        var doc = new Document.Uuid(guid);
        await Assert.That(doc.Value).IsEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Uuid_UuidDoc_Factory_Should_Create_From_Guid()
    {
        var guid = Guid.NewGuid();
        var doc = Document.UuidDoc(guid);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Uuid>();
            await Assert.That(doc.Value).IsEqualTo(guid);
        }
    }

    [Test]
    public async Task Uuid_UuidDoc_Factory_Should_Create_From_String()
    {
        var guidString = "550e8400-e29b-41d4-a716-446655440000";
        var doc = Document.UuidDoc(guidString);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Uuid>();
            await Assert.That(doc.Value).IsEqualTo(Guid.Parse(guidString));
        }
    }

    [Test]
    public async Task Uuid_UuidDoc_Factory_Should_Support_Different_Formats()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

        // Test different GUID string formats
        var doc1 = Document.UuidDoc("550e8400-e29b-41d4-a716-446655440000"); // D format (default)
        var doc2 = Document.UuidDoc("{550e8400-e29b-41d4-a716-446655440000}"); // B format
        var doc3 = Document.UuidDoc("550e8400e29b41d4a716446655440000"); // N format

        using (Assert.Multiple())
        {
            await Assert.That(doc1.Value).IsEqualTo(guid);
            await Assert.That(doc2.Value).IsEqualTo(guid);
            await Assert.That(doc3.Value).IsEqualTo(guid);
        }
    }

    [Test]
    public async Task Uuid_UuidDoc_Factory_Should_Throw_On_Invalid_String()
    {
        await Assert.That(() => Document.UuidDoc("not-a-uuid")).Throws<FormatException>();
    }

    [Test]
    public async Task Uuid_Should_Support_Equality()
    {
        var guid1 = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var guid2 = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var guid3 = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var doc1 = new Document.Uuid(guid1);
        var doc2 = new Document.Uuid(guid2);
        var doc3 = new Document.Uuid(guid3);

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsNotEqualTo(doc3);
        }
    }

    #endregion

    #region Bytes Tests

    [Test]
    public async Task Bytes_Should_Store_Byte_Array()
    {
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var doc = new Document.Bytes(bytes.ToImmutableArray());
        await Assert.That(doc.Value.ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task Bytes_Should_Store_Empty_Array()
    {
        var doc = new Document.Bytes(ImmutableArray<byte>.Empty);
        await Assert.That(doc.Value).IsEmpty();
    }

    [Test]
    public async Task Bytes_BytesDoc_Factory_Should_Create_From_ByteArray()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 };
        var doc = Document.BytesDoc(bytes);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Bytes>();
            await Assert.That(doc.Value.ToArray()).IsEquivalentTo(bytes);
        }
    }

    [Test]
    public async Task Bytes_BytesDoc_Factory_Should_Create_From_ImmutableArray()
    {
        var bytes = ImmutableArray.Create<byte>(0xAA, 0xBB, 0xCC);
        var doc = Document.BytesDoc(bytes);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Bytes>();
            await Assert.That(doc.Value).IsEquivalentTo(bytes);
        }
    }

    [Test]
    public async Task Bytes_BytesFromBase64_Factory_Should_Create_From_Base64()
    {
        var base64 = "SGVsbG8="; // "Hello" in base64
        var doc = Document.BytesFromBase64(base64);
        var expected = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Bytes>();
            await Assert.That(doc.Value.ToArray()).IsEquivalentTo(expected);
        }
    }

    [Test]
    public async Task Bytes_BytesFromBase64_Factory_Should_Throw_On_Invalid_Base64()
    {
        await Assert.That(() => Document.BytesFromBase64("not valid base64!@#")).Throws<FormatException>();
    }

    [Test]
    public async Task Bytes_Should_Support_Equality()
    {
        var bytes1 = ImmutableArray.Create<byte>(0x01, 0x02, 0x03);
        var bytes2 = ImmutableArray.Create<byte>(0x01, 0x02, 0x03);
        var bytes3 = ImmutableArray.Create<byte>(0x04, 0x05, 0x06);
        var doc1 = new Document.Bytes(bytes1);
        var doc2 = new Document.Bytes(bytes2);
        var doc3 = new Document.Bytes(bytes3);

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsNotEqualTo(doc3);
        }
    }

    #endregion

    #region Integer Tests

    [Test]
    public async Task Integer_Should_Store_Positive_Value()
    {
        var doc = new Document.Integer(42);
        await Assert.That(doc.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Integer_Should_Store_Negative_Value()
    {
        var doc = new Document.Integer(-100);
        await Assert.That(doc.Value).IsEqualTo(-100L);
    }

    [Test]
    public async Task Integer_Should_Store_Zero()
    {
        var doc = new Document.Integer(0);
        await Assert.That(doc.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task Integer_Should_Store_Large_Value()
    {
        var doc = new Document.Integer(long.MaxValue);
        await Assert.That(doc.Value).IsEqualTo(long.MaxValue);
    }

    [Test]
    public async Task Integer_Should_Support_Equality()
    {
        var doc1 = new Document.Integer(42);
        var doc2 = new Document.Integer(42);
        var doc3 = new Document.Integer(43);

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsNotEqualTo(doc3);
        }
    }

    #endregion

    #region Number Tests

    [Test]
    public async Task Number_Should_Store_Decimal_Value()
    {
        var doc = new Document.Number(3.14m);
        await Assert.That(doc.Value).IsEqualTo(3.14m);
    }

    [Test]
    public async Task Number_Should_Store_Negative_Decimal()
    {
        var doc = new Document.Number(-2.5m);
        await Assert.That(doc.Value).IsEqualTo(-2.5m);
    }

    [Test]
    public async Task Number_Should_Store_Zero_Decimal()
    {
        var doc = new Document.Number(0.0m);
        await Assert.That(doc.Value).IsEqualTo(0.0m);
    }

    [Test]
    public async Task Number_Should_Store_Large_Decimal()
    {
        var value = 123456789.987654321m;
        var doc = new Document.Number(value);
        await Assert.That(doc.Value).IsEqualTo(value);
    }

    [Test]
    public async Task Number_Should_Support_Equality()
    {
        var doc1 = new Document.Number(3.14m);
        var doc2 = new Document.Number(3.14m);
        var doc3 = new Document.Number(2.71m);

        using (Assert.Multiple())
        {
            await Assert.That(doc1).IsEqualTo(doc2);
            await Assert.That(doc1).IsNotEqualTo(doc3);
        }
    }

    #endregion

    #region Array Tests

    [Test]
    public async Task Array_Should_Store_Empty_Collection()
    {
        var doc = new Document.Array(ImmutableList<Document>.Empty);
        await Assert.That(doc.Items).IsEmpty();
    }

    [Test]
    public async Task Array_Should_Store_Single_Item()
    {
        var items = ImmutableList.Create<Document>(Document.True);
        var doc = new Document.Array(items);

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(1);
            await Assert.That(doc.Items[0]).IsEqualTo(Document.True);
        }
    }

    [Test]
    public async Task Array_Should_Store_Multiple_Items()
    {
        var items = ImmutableList.Create<Document>(
            Document.True,
            new Document.Integer(42),
            new Document.Number(3.14m)
        );
        var doc = new Document.Array(items);

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(3);
            await Assert.That(doc.Items[0]).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Items[1]).IsTypeOf<Document.Integer>();
            await Assert.That(doc.Items[2]).IsTypeOf<Document.Number>();
        }
    }

    [Test]
    public async Task Array_Should_Maintain_Order()
    {
        var items = ImmutableList.Create<Document>(
            new Document.Integer(1),
            new Document.Integer(2),
            new Document.Integer(3)
        );
        var doc = new Document.Array(items);

        using (Assert.Multiple())
        {
            await Assert.That(((Document.Integer)doc.Items[0]).Value).IsEqualTo(1L);
            await Assert.That(((Document.Integer)doc.Items[1]).Value).IsEqualTo(2L);
            await Assert.That(((Document.Integer)doc.Items[2]).Value).IsEqualTo(3L);
        }
    }

    [Test]
    public async Task Array_Should_Support_Nested_Arrays()
    {
        var innerArray = new Document.Array(ImmutableList.Create<Document>(Document.True));
        var outerArray = new Document.Array(ImmutableList.Create<Document>(innerArray));

        using (Assert.Multiple())
        {
            await Assert.That(outerArray.Items.Count).IsEqualTo(1);
            await Assert.That(outerArray.Items[0]).IsTypeOf<Document.Array>();
            var nested = (Document.Array)outerArray.Items[0];
            await Assert.That(nested.Items.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Array_Should_Support_Equality_With_Same_Order()
    {
        var items1 = ImmutableList.Create<Document>(Document.True, Document.False);
        var items2 = ImmutableList.Create<Document>(Document.True, Document.False);
        var doc1 = new Document.Array(items1);
        var doc2 = new Document.Array(items2);

        await Assert.That(doc1).IsEqualTo(doc2);
    }

    [Test]
    public async Task Array_Should_Not_Equal_With_Different_Order()
    {
        var items1 = ImmutableList.Create<Document>(Document.True, Document.False);
        var items2 = ImmutableList.Create<Document>(Document.False, Document.True);
        var doc1 = new Document.Array(items1);
        var doc2 = new Document.Array(items2);

        await Assert.That(doc1).IsNotEqualTo(doc2);
    }

    [Test]
    public async Task Array_Arr_Factory_Should_Create_Array()
    {
        var items = ImmutableList.Create<Document>(Document.True, Document.False);
        var doc = Document.Arr(items);

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Array>();
            await Assert.That(doc.Items.Count).IsEqualTo(2);
        }
    }

    #endregion

    #region Object Tests

    [Test]
    public async Task Object_Should_Store_Empty_Dictionary()
    {
        var doc = new Document.Object(ImmutableDictionary<string, Document>.Empty);
        await Assert.That(doc.Items).IsEmpty();
    }

    [Test]
    public async Task Object_Empty_Static_Property_Should_Return_Empty_Object()
    {
        var doc = Document.Object.Empty;
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Object>();
            await Assert.That(doc.Items).IsEmpty();
        }
    }

    [Test]
    public async Task Object_EmptyDoc_Static_Property_Should_Return_Empty_Object()
    {
        var doc = Document.EmptyDoc;
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Object>();
            await Assert.That(doc.Items).IsEmpty();
            await Assert.That(doc).IsEqualTo(Document.Object.Empty);
        }
    }

    [Test]
    public async Task Object_Should_Store_Single_Property()
    {
        var items = ImmutableDictionary<string, Document>.Empty.Add("key", Document.True);
        var doc = new Document.Object(items);

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(1);
            await Assert.That(doc.Items["key"]).IsEqualTo(Document.True);
        }
    }

    [Test]
    public async Task Object_Should_Store_Multiple_Properties()
    {
        var items = ImmutableDictionary<string, Document>.Empty
            .Add("bool", Document.True)
            .Add("int", new Document.Integer(42))
            .Add("num", new Document.Number(3.14m));
        var doc = new Document.Object(items);

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(3);
            await Assert.That(doc.Items["bool"]).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Items["int"]).IsTypeOf<Document.Integer>();
            await Assert.That(doc.Items["num"]).IsTypeOf<Document.Number>();
        }
    }

    [Test]
    public async Task Object_Should_Support_Nested_Objects()
    {
        var innerObj = new Document.Object(
            ImmutableDictionary<string, Document>.Empty.Add("inner", Document.True)
        );
        var outerObj = new Document.Object(
            ImmutableDictionary<string, Document>.Empty.Add("outer", innerObj)
        );

        using (Assert.Multiple())
        {
            await Assert.That(outerObj.Items.Count).IsEqualTo(1);
            await Assert.That(outerObj.Items["outer"]).IsTypeOf<Document.Object>();
            var nested = (Document.Object)outerObj.Items["outer"];
            await Assert.That(nested.Items["inner"]).IsEqualTo(Document.True);
        }
    }

    [Test]
    public async Task Object_Should_Support_Array_Properties()
    {
        var array = new Document.Array(ImmutableList.Create<Document>(Document.True));
        var obj = new Document.Object(
            ImmutableDictionary<string, Document>.Empty.Add("array", array)
        );

        using (Assert.Multiple())
        {
            await Assert.That(obj.Items["array"]).IsTypeOf<Document.Array>();
            var arr = (Document.Array)obj.Items["array"];
            await Assert.That(arr.Items.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Object_Should_Support_Equality()
    {
        var items1 = ImmutableDictionary<string, Document>.Empty
            .Add("a", Document.True)
            .Add("b", Document.False);
        var items2 = ImmutableDictionary<string, Document>.Empty
            .Add("a", Document.True)
            .Add("b", Document.False);
        var doc1 = new Document.Object(items1);
        var doc2 = new Document.Object(items2);

        await Assert.That(doc1).IsEqualTo(doc2);
    }

    [Test]
    public async Task Object_Should_Not_Equal_With_Different_Values()
    {
        var items1 = ImmutableDictionary<string, Document>.Empty.Add("key", Document.True);
        var items2 = ImmutableDictionary<string, Document>.Empty.Add("key", Document.False);
        var doc1 = new Document.Object(items1);
        var doc2 = new Document.Object(items2);

        await Assert.That(doc1).IsNotEqualTo(doc2);
    }

    [Test]
    public async Task Object_Obj_Factory_Should_Create_Empty_Object()
    {
        var doc = Document.Obj();

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Object>();
            await Assert.That(doc.Items).IsEmpty();
        }
    }

    [Test]
    public async Task Object_Obj_Factory_Should_Create_Single_Property()
    {
        var doc = Document.Obj(("key", Document.True));

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Object>();
            await Assert.That(doc.Items.Count).IsEqualTo(1);
            await Assert.That(doc.Items["key"]).IsEqualTo(Document.True);
        }
    }

    [Test]
    public async Task Object_Obj_Factory_Should_Create_Multiple_Properties()
    {
        var doc = Document.Obj(
            ("bool", Document.True),
            ("int", new Document.Integer(42)),
            ("num", new Document.Number(3.14m))
        );

        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Object>();
            await Assert.That(doc.Items.Count).IsEqualTo(3);
            await Assert.That(doc.Items["bool"]).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Items["int"]).IsTypeOf<Document.Integer>();
            await Assert.That(doc.Items["num"]).IsTypeOf<Document.Number>();
        }
    }

    [Test]
    public async Task Object_Obj_Factory_Should_Support_Nested_Objects()
    {
        var doc = Document.Obj(
            ("inner", Document.Obj(("value", Document.True))),
            ("outer", Document.False)
        );

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(2);
            await Assert.That(doc.Items["inner"]).IsTypeOf<Document.Object>();
            await Assert.That(doc.Items["outer"]).IsTypeOf<Document.Boolean>();

            var inner = (Document.Object)doc.Items["inner"];
            await Assert.That(inner.Items["value"]).IsEqualTo(Document.True);
        }
    }

    [Test]
    public async Task Object_Obj_Factory_Should_Support_All_Value_Types()
    {
        var doc = Document.Obj(
            ("null", Document.NullDoc),
            ("bool", Document.True),
            ("int", new Document.Integer(42)),
            ("num", new Document.Number(3.14m)),
            ("array", new Document.Array(ImmutableList<Document>.Empty)),
            ("obj", Document.EmptyDoc)
        );

        using (Assert.Multiple())
        {
            await Assert.That(doc.Items.Count).IsEqualTo(6);
            await Assert.That(doc.Items["null"]).IsTypeOf<Document.Null>();
            await Assert.That(doc.Items["bool"]).IsTypeOf<Document.Boolean>();
            await Assert.That(doc.Items["int"]).IsTypeOf<Document.Integer>();
            await Assert.That(doc.Items["num"]).IsTypeOf<Document.Number>();
            await Assert.That(doc.Items["array"]).IsTypeOf<Document.Array>();
            await Assert.That(doc.Items["obj"]).IsTypeOf<Document.Object>();
        }
    }

    [Test]
    public async Task Object_Obj_Factory_Should_Support_Equality()
    {
        var doc1 = Document.Obj(
            ("a", Document.True),
            ("b", Document.False)
        );
        var doc2 = Document.Obj(
            ("a", Document.True),
            ("b", Document.False)
        );

        await Assert.That(doc1).IsEqualTo(doc2);
    }

    #endregion

    #region Type Hierarchy Tests

    [Test]
    public async Task Null_Should_Be_DocumentValue()
    {
        Document.Null doc = new Document.Null();
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task Boolean_Should_Be_DocumentValue()
    {
        Document.Boolean doc = new Document.Boolean(true);
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task String_Should_Be_DocumentValue()
    {
        Document.String doc = new Document.String("test");
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task Integer_Should_Be_DocumentValue()
    {
        Document.Integer doc = new Document.Integer(42);
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task Number_Should_Be_DocumentValue()
    {
        Document.Number doc = new Document.Number(3.14m);
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task Uri_Should_Be_DocumentValue()
    {
        Document.Uri doc = Document.UriDoc("https://example.com");
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task Uuid_Should_Be_DocumentValue()
    {
        Document.Uuid doc = Document.UuidDoc(Guid.NewGuid());
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task Bytes_Should_Be_DocumentValue()
    {
        Document.Bytes doc = Document.BytesDoc(new byte[] { 0x01, 0x02 });
        Document.DocumentValue value = doc;
        await Assert.That(value).IsNotNull();
    }

    [Test]
    public async Task Array_Should_Be_Document()
    {
        Document.Array arr = new Document.Array(ImmutableList<Document>.Empty);
        Document doc = arr;
        await Assert.That(doc).IsNotNull();
    }

    [Test]
    public async Task Object_Should_Be_Document()
    {
        Document.Object obj = Document.Object.Empty;
        Document doc = obj;
        await Assert.That(doc).IsNotNull();
    }

    #endregion

    #region Complex Scenarios

    [Test]
    public async Task Should_Create_Complex_Nested_Document()
    {
        // Create a document like: { "user": { "name": "Alice", "age": 30, "active": true } }
        var userObj = Document.Obj(
            ("name", Document.Str("Alice")),
            ("age", new Document.Integer(30)),
            ("active", Document.True)
        );
        var rootObj = Document.Obj(("user", userObj));

        using (Assert.Multiple())
        {
            await Assert.That(rootObj.Items.Count).IsEqualTo(1);
            await Assert.That(rootObj.Items["user"]).IsTypeOf<Document.Object>();

            var user = (Document.Object)rootObj.Items["user"];
            await Assert.That(user.Items.Count).IsEqualTo(3);
            await Assert.That(((Document.String)user.Items["name"]).Value).IsEqualTo("Alice");
            await Assert.That(((Document.Integer)user.Items["age"]).Value).IsEqualTo(30L);
            await Assert.That(user.Items["active"]).IsEqualTo(Document.True);
        }
    }

    [Test]
    public async Task Should_Create_Array_Of_Objects()
    {
        // Create: [{ "id": 1 }, { "id": 2 }]
        var obj1 = new Document.Object(
            ImmutableDictionary<string, Document>.Empty.Add("id", new Document.Integer(1))
        );
        var obj2 = new Document.Object(
            ImmutableDictionary<string, Document>.Empty.Add("id", new Document.Integer(2))
        );
        var array = new Document.Array(ImmutableList.Create<Document>(obj1, obj2));

        using (Assert.Multiple())
        {
            await Assert.That(array.Items.Count).IsEqualTo(2);
            await Assert.That(array.Items[0]).IsTypeOf<Document.Object>();
            await Assert.That(array.Items[1]).IsTypeOf<Document.Object>();

            var first = (Document.Object)array.Items[0];
            var second = (Document.Object)array.Items[1];
            await Assert.That(((Document.Integer)first.Items["id"]).Value).IsEqualTo(1L);
            await Assert.That(((Document.Integer)second.Items["id"]).Value).IsEqualTo(2L);
        }
    }

    [Test]
    public async Task Should_Create_Mixed_Type_Array()
    {
        // Create: [null, true, "hello", uri, uuid, bytes, 42, 3.14, [], {}]
        var array = new Document.Array(ImmutableList.Create<Document>(
            Document.NullDoc,
            Document.True,
            Document.Str("hello"),
            Document.UriDoc("https://example.com"),
            Document.UuidDoc(Guid.Parse("550e8400-e29b-41d4-a716-446655440000")),
            Document.BytesDoc(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }),
            new Document.Integer(42),
            new Document.Number(3.14m),
            new Document.Array(ImmutableList<Document>.Empty),
            Document.EmptyDoc
        ));

        using (Assert.Multiple())
        {
            await Assert.That(array.Items.Count).IsEqualTo(10);
            await Assert.That(array.Items[0]).IsTypeOf<Document.Null>();
            await Assert.That(array.Items[1]).IsTypeOf<Document.Boolean>();
            await Assert.That(array.Items[2]).IsTypeOf<Document.String>();
            await Assert.That(array.Items[3]).IsTypeOf<Document.Uri>();
            await Assert.That(array.Items[4]).IsTypeOf<Document.Uuid>();
            await Assert.That(array.Items[5]).IsTypeOf<Document.Bytes>();
            await Assert.That(array.Items[6]).IsTypeOf<Document.Integer>();
            await Assert.That(array.Items[7]).IsTypeOf<Document.Number>();
            await Assert.That(array.Items[8]).IsTypeOf<Document.Array>();
            await Assert.That(array.Items[9]).IsTypeOf<Document.Object>();
        }
    }

    #endregion
}
