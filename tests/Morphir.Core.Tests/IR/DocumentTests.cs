using System.Collections.Immutable;

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

    #region Char Tests

    [Test]
    public async Task Char_Should_Store_Char_Value()
    {
        var doc = new Document.Char('a');
        await Assert.That(doc.Value).IsEqualTo('a');
    }

    [Test]
    public async Task Char_Should_Store_Special_Characters()
    {
        var specialChars = new[] { '\n', '\t', '\r', '\\', '"', '\'' };
        foreach (var ch in specialChars)
        {
            var doc = new Document.Char(ch);
            await Assert.That(doc.Value).IsEqualTo(ch);
        }
    }

    [Test]
    public async Task Char_Should_Store_Unicode_Characters()
    {
        var doc1 = new Document.Char('€');
        var doc2 = new Document.Char('日');
        var doc3 = new Document.Char('א');

        using (Assert.Multiple())
        {
            await Assert.That(doc1.Value).IsEqualTo('€');
            await Assert.That(doc2.Value).IsEqualTo('日');
            await Assert.That(doc3.Value).IsEqualTo('א');
        }
    }

    [Test]
    public async Task Char_Chr_Factory_Should_Create_Char()
    {
        var doc = Document.Chr('x');
        using (Assert.Multiple())
        {
            await Assert.That(doc).IsTypeOf<Document.Char>();
            await Assert.That(doc.Value).IsEqualTo('x');
        }
    }

    [Test]
    public async Task Char_Should_Support_Equality()
    {
        var doc1 = new Document.Char('a');
        var doc2 = new Document.Char('a');
        var doc3 = new Document.Char('b');

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
    public async Task Char_Should_Be_DocumentValue()
    {
        Document.Char doc = new Document.Char('a');
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
        // Create: [null, true, "hello", 'x', 42, 3.14, [], {}]
        var array = new Document.Array(ImmutableList.Create<Document>(
            Document.NullDoc,
            Document.True,
            Document.Str("hello"),
            Document.Chr('x'),
            new Document.Integer(42),
            new Document.Number(3.14m),
            new Document.Array(ImmutableList<Document>.Empty),
            Document.EmptyDoc
        ));

        using (Assert.Multiple())
        {
            await Assert.That(array.Items.Count).IsEqualTo(8);
            await Assert.That(array.Items[0]).IsTypeOf<Document.Null>();
            await Assert.That(array.Items[1]).IsTypeOf<Document.Boolean>();
            await Assert.That(array.Items[2]).IsTypeOf<Document.String>();
            await Assert.That(array.Items[3]).IsTypeOf<Document.Char>();
            await Assert.That(array.Items[4]).IsTypeOf<Document.Integer>();
            await Assert.That(array.Items[5]).IsTypeOf<Document.Number>();
            await Assert.That(array.Items[6]).IsTypeOf<Document.Array>();
            await Assert.That(array.Items[7]).IsTypeOf<Document.Object>();
        }
    }

    #endregion
}
