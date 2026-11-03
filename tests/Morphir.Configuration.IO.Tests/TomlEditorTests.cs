using FluentAssertions;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class TomlEditorTests
{
    [Test]
    public void SetPatchesOnlyTheValueAndRetainsCommentsAndCrLf()
    {
        const string text = "# owner\r\n[custom]\r\nname  = 'before' # keep this\r\nother = 42\r\n";
        var result = TomlEditor.Edit(text, ["custom", "name"], ConfigValue.NewString("after"));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Be("# owner\r\n[custom]\r\nname  = \"after\" # keep this\r\nother = 42\r\n");
    }
    [Test]
    public void UnsetRetainsTheEmptyParentAndTrailingComment()
    {
        const string text = "[custom]\nname = 'before' # retain this explanation\n";
        var result = TomlEditor.Edit(text, ["custom", "name"], null);
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Found.Should().BeTrue();
        result.Text.Should().Be("[custom]\n # retain this explanation\n");
        Read(result.Text).Should().Be(Read("[custom]\n"));
    }

    [Test]
    public void AddUsesTheExistingTableWithoutReformattingItsEntries()
    {
        const string text = "# head\n[custom] # table\nkeep = 1\n[other]\nkeep=2\n";
        var result = TomlEditor.Edit(text, ["custom", "a.b", "quote\"key"], ConfigValue.NewBoolean(false));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Found.Should().BeFalse();
        result.Text.Should().Be("# head\n[custom] # table\nkeep = 1\n\"a.b\".\"quote\\\"key\" = false\n[other]\nkeep=2\n");
    }

    [Test]
    public void MissingUnsetAndIdenticalSetReturnUnchangedText()
    {
        const string text = "custom = 'same' # retain quoting\n";
        var same = TomlEditor.Edit(text, ["custom"], ConfigValue.NewString("same"));
        same.Should().Be(new TomlEditResult(true, true, false, text, null));
        TomlEditor.Edit(text, ["missing"], null).Should().Be(new TomlEditResult(true, false, false, text, null));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void CompleteTableReplacementAndUnsetPreserveUnrelatedBytes(bool unset)
    {
        const string text = "# head\n[custom] # own table\na = 1\n[custom.nested]\nb = 2 # retain comment\n[other]\nkeep='original'\n";
        var result = TomlEditor.Edit(text, ["custom"], unset ? null : ((ConfigValue.Object)Read("[empty]\n")).Item["empty"]);
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Contain("# head\n").And.Contain(" # own table\n").And.Contain(" # retain comment\n").And.Contain("[other]\nkeep='original'\n");
        Read(result.Text).Should().Be(Read(unset ? "[other]\nkeep='original'\n" : "custom = {}\n[other]\nkeep='original'\n"));
    }

    [Test]
    [Arguments("set")]
    [Arguments("unset")]
    [Arguments("add")]
    public void InlineEditsPreserveUnrelatedFieldPresentation(string operation)
    {
        const string text = "custom = { a=1, nested = { name = 'old', keep=2 }, z=3 } # comment\n";
        var path = operation == "add" ? new[] { "custom", "nested", "new" } : ["custom", "nested", "name"];
        var result = TomlEditor.Edit(text, path, operation == "unset" ? null : ConfigValue.NewString("new"));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Contain("a=1").And.Contain("keep=2").And.Contain("z=3 } # comment\n");
        var expected = operation switch
        {
            "set" => "custom = { a=1, nested = { name = \"new\", keep=2 }, z=3 } # comment\n",
            "unset" => "custom = { a=1, nested = {  keep=2 }, z=3 } # comment\n",
            _ => "custom = { a=1, nested = { name = 'old', keep=2, \"new\" = \"new\" }, z=3 } # comment\n"
        };
        result.Text.Should().Be(expected);
    }

    [Test]
    [Arguments("custom.nested.name = 'old'\n")]
    [Arguments("[custom.nested]\nname='old'\n")]
    [Arguments("[custom]\nnested.name='old'\n")]
    [Arguments("custom = { nested.name = 'old' }\n")]
    public void UnsetPreservesImplicitEmptyParentObjects(string text)
    {
        var result = TomlEditor.Edit(text, ["custom", "nested", "name"], null);
        result.Success.Should().BeTrue(result.ErrorCode);
        Read(result.Text).Should().Be(Read("custom = { nested = {} }\n"));
    }

    [Test]
    public void ArrayOfTablesIsReplacedAsOneCompleteValue()
    {
        const string text = "# targets\n[[custom]] # first\nname = 'one'\n[custom.nested]\nflag = true\n[[custom]]\nname='two'\n[other]\nkeep = 7\n";
        var replacement = ((ConfigValue.Object)Read("custom = [{ name = 'replacement' }]\n")).Item["custom"];
        var result = TomlEditor.Edit(text, ["custom"], replacement);
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Contain("# targets\n").And.Contain(" # first\n").And.Contain("[other]\nkeep = 7\n");
        Read(result.Text).Should().Be(Read("custom = [{ name = 'replacement' }]\n[other]\nkeep = 7\n"));
    }

    [Test]
    public void UnrelatedMultilineStringsArraysDatesAndNonfiniteValuesRemainExact()
    {
        const string text = "multi = \"\"\"line # not comment\n[not-table]\nend\"\"\"\narray = [ 1, # retained\n 2 ]\ndate = 1979-05-27 07:32:00Z\nnan_value = -nan\ninf_value = +inf\n[custom]\nvalue='old'\n";
        var result = TomlEditor.Edit(text, ["custom", "value"], ConfigValue.NewString("new"));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Be(text.Replace("value='old'", "value=\"new\"", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("'literal.key' = 'old'\n")]
    [Arguments("\"literal.key\" = 'old'\n")]
    public void LiteralDottedKeysRetainTheirOriginalSpelling(string text)
    {
        var result = TomlEditor.Edit(text, ["literal.key"], ConfigValue.NewString("new"));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Be(text.Replace("'old'", "\"new\"", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("custom = 1\n")]
    [Arguments("custom = [1]\n")]
    [Arguments("custom = { env = 'TOKEN' }\n")]
    public void TraversalThroughScalarsArraysOrAtomicReferencesFailsSafely(string text)
    {
        var result = TomlEditor.Edit(text, ["custom", "child"], ConfigValue.NewString("value"));
        result.Should().Be(new TomlEditResult(false, false, false, text, "config.edit.path_shape"));
    }

    [Test]
    public void ChangingTheSignOfZeroIsAnObservableNumericEdit()
    {
        const string text = "custom = 0.0\n";
        var result = TomlEditor.Edit(text, ["custom"], ConfigValue.NewNumber(-0.0));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Changed.Should().BeTrue();
        var parsed = (ConfigValue.Number)((ConfigValue.Object)Read(result.Text)).Item["custom"];
        BitConverter.DoubleToInt64Bits(parsed.Item).Should().Be(long.MinValue);
    }

    [Test]
    public void AddedEntryKeepsTheFollowingHeaderCommentAndIndentationTogether()
    {
        const string text = "[custom]\nkeep=1\n\n# unrelated table\n  [other] # heading\nkeep = 2\n";
        var result = TomlEditor.Edit(text, ["custom", "new"], ConfigValue.NewInteger(3));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Contain("\n\n# unrelated table\n  [other] # heading\nkeep = 2\n");
    }

    [Test]
    public void CanonicalSecretReferencesRemainOpaqueAndCanBeWrittenWithoutEvaluation()
    {
        const string text = "custom = { env = 'DO_NOT_READ_THIS_ENV' }\n";
        var document = ((SourceContent.Available)ConfigurationReader.ReadText("fixture.toml", text).Content).Item;
        var reference = ((ConfigValue.Object)document.Values).Item["custom"];
        reference.Should().BeOfType<ConfigValue.SecretReference>();
        TomlEditor.Edit(text, ["custom"], reference).Should().Be(new TomlEditResult(true, true, false, text, null));
        var fresh = TomlEditor.Edit("", ["custom"], reference);
        fresh.Success.Should().BeTrue(fresh.ErrorCode);
        Read(fresh.Text).Should().Be(document.Raw);
    }

    [Test]
    public void TypedValuesRoundtripIncludingUnicodeCompositeAndFloatingPointEdges()
    {
        var values = new ConfigValue[]
        {
            ConfigValue.NewString("Unicode 🌍, \"quote\", backslash\\, newline\n\0\b\f\u007F"),
            ConfigValue.NewBoolean(false), ConfigValue.NewInteger(long.MinValue), ConfigValue.NewInteger(long.MaxValue),
            ConfigValue.NewNumber(2.0), ConfigValue.NewNumber(-0.0), ConfigValue.NewNumber(double.Epsilon),
            ConfigValue.NewNumber(double.MaxValue), ConfigValue.NewNumber(double.PositiveInfinity),
            ConfigValue.NewNumber(double.NegativeInfinity), ConfigValue.NewNumber(double.NaN),
            ((ConfigValue.Object)Read("custom = [{ quoted = 'text' }, [1, false], {}]\n")).Item["custom"],
            ((ConfigValue.Object)Read("custom = { 'a.b' = { empty = {}, array = [1, 2.0] } }\n")).Item["custom"]
        };
        foreach (var value in values)
        {
            var result = TomlEditor.Edit("# retained\n", ["custom"], value);
            result.Success.Should().BeTrue(result.ErrorCode);
            result.Text.Should().StartWith("# retained\n");
            var actual = ((ConfigValue.Object)Read(result.Text)).Item["custom"];
            if (value is ConfigValue.Number expectedNumber && actual is ConfigValue.Number actualNumber)
            {
                if (double.IsNaN(expectedNumber.Item)) double.IsNaN(actualNumber.Item).Should().BeTrue();
                else BitConverter.DoubleToInt64Bits(actualNumber.Item).Should().Be(BitConverter.DoubleToInt64Bits(expectedNumber.Item));
            }
            else actual.Should().Be(value);
            TomlEditor.Edit(result.Text, ["custom"], value).Changed.Should().BeFalse();
        }
    }

    [Test]
    [Arguments("custom = {a=1,\n b=2}\n")]
    [Arguments("custom = \"\\e\"\n")]
    [Arguments("custom = [\n")]
    public void MalformedAndToml11OnlySourcesAreRejectedWithoutChanges(string text)
    {
        ConfigurationReader.ReadText("fixture.toml", text).Content.Should().BeOfType<SourceContent.Invalid>();
        TomlEditor.Edit(text, ["custom"], ConfigValue.NewString("new"))
            .Should().Be(new TomlEditResult(false, false, false, text, "config.edit.invalid_toml"));
    }

    [Test]
    public void UnrepresentableNullsAndOutOfRangeIntegersFailWithoutChanges()
    {
        var nulls = ((SourceContent.Available)ConfigurationReader.ReadText("fixture.json5", "{custom:[null]}").Content).Item.Raw;
        var values = new[]
        {
            ConfigValue.Null, ((ConfigValue.Object)nulls).Item["custom"],
            ConfigValue.NewBigInteger(System.Numerics.BigInteger.Parse("9223372036854775808", System.Globalization.CultureInfo.InvariantCulture))
        };
        foreach (var value in values)
            TomlEditor.Edit("custom=1\n", ["custom"], value).Should().Be(new TomlEditResult(false, true, false, "custom=1\n", "config.edit.unsupported_value"));
        TomlEditor.Edit("custom=1\n", [], ConfigValue.NewString("x")).ErrorCode.Should().Be("config.edit.invalid_path");
    }

    [Test]
    public void BomAndDeepQuotedPathsRetainCanonicalReaderCompatibility()
    {
        const string bom = "\uFEFF# head\r\n[custom]\r\nvalue='old'\r\n";
        var result = TomlEditor.Edit(bom, ["custom", "value"], ConfigValue.NewString("new"));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Be(bom.Replace("'old'", "\"new\"", StringComparison.Ordinal));
        var path = Enumerable.Repeat("a.b", 126).Append("leaf").ToArray();
        var dotted = string.Join(".", path.Select(key => "\"" + key + "\""));
        var deep = TomlEditor.Edit(dotted + " = 'old'\n", path, ConfigValue.NewString("new"));
        deep.Success.Should().BeTrue(deep.ErrorCode);
        deep.Text.Should().Be(dotted + " = \"new\"\n");
    }

    [Test]
    public void PinnedTomlCorpusDocumentsRetainEveryUnrelatedByte()
    {
        using var corpus = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Configuration", "toml-corpus.json")));
        foreach (var fixture in corpus.RootElement.EnumerateArray())
        {
            var text = fixture.GetProperty("text").GetString()!;
            var result = TomlEditor.Edit(text, ["editing_qualification"], ConfigValue.NewBoolean(true));
            result.Success.Should().BeTrue(result.ErrorCode);
            result.Text.Should().EndWith(text);
            var removed = TomlEditor.Edit(result.Text, ["editing_qualification"], null);
            removed.Success.Should().BeTrue(removed.ErrorCode);
            Read(removed.Text).Should().Be(Read(text));
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void CompleteInlineDottedSubtreesReplaceOrRemoveAllTheirEntries(bool unset)
    {
        const string text = "custom = { a.b = 1, a.c = 2, keep = 'original' } # retain\n";
        var replacement = ConfigValue.NewObject(Microsoft.FSharp.Collections.MapModule.OfSeq(new[] { Tuple.Create("replacement", ConfigValue.NewBoolean(true)) }));
        var result = TomlEditor.Edit(text, ["custom", "a"], unset ? null : replacement);
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Contain("keep = 'original'").And.EndWith(" } # retain\n");
        var expected = unset ? "custom = { keep = 'original' }" : "custom = { a = { replacement = true }, keep = 'original' }";
        Read(result.Text).Should().Be(Read(expected));
    }

    [Test]
    public void InlineArrayCommentsAndPartialMultilineStringQuotesStayUntouched()
    {
        const string text = "custom = { items = [1, # keep array\n 2], keep = true }\ntext = \"\"\"single \" quote\nkept\"\"\"\n";
        var result = TomlEditor.Edit(text, ["custom", "keep"], ConfigValue.NewBoolean(false));
        result.Success.Should().BeTrue(result.ErrorCode);
        result.Text.Should().Be(text.Replace("keep = true", "keep = false", StringComparison.Ordinal));
    }

    [Test]
    public void StringControlEscapesAndUnrepresentableUnicodeAreVerified()
    {
        var controls = TomlEditor.Edit("value='old'\n", ["value"], ConfigValue.NewString("a\tb\rc\n"));
        controls.Success.Should().BeTrue(controls.ErrorCode);
        controls.Text.Should().Be("value=\"a\\tb\\rc\\n\"\n");
        const string original = "value='old' # unchanged\n";
        var invalid = TomlEditor.Edit(original, ["value"], ConfigValue.NewString("\uD800"));
        invalid.Success.Should().BeFalse();
        invalid.ErrorCode.Should().Be("config.edit.verification");
        invalid.Text.Should().Be(original);
    }

    private static ConfigValue Read(string text) => ((SourceContent.Available)ConfigurationReader.ReadText("fixture.toml", text).Content).Item.Raw;
}
