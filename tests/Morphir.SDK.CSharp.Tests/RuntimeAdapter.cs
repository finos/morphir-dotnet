using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

/// <summary>Only translates frozen tags/callbacks to public runtime calls and observations.</summary>
internal static class RuntimeAdapter
{
    internal static JsonObject Corpus => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cases.json")))!.AsObject();

    internal static (string[] Executed, string[] Passed, string[] Excluded, string[] Failures) RunAll()
    {
        var executed = new System.Collections.Generic.List<string>();
        var passed = new System.Collections.Generic.List<string>();
        var excluded = new System.Collections.Generic.List<string>();
        var failures = new System.Collections.Generic.List<string>();
        foreach (var entry in Corpus["cases"]!.AsArray().Select(x => x!.AsObject()))
        {
            var id = entry["caseId"]!.ToString();
            if (entry["classification"]!.ToString() == "representation-excluded") { excluded.Add(id); continue; }
            executed.Add(id);
            try { Check(entry); passed.Add(id); }
            catch (Exception ex) { failures.Add($"{id}: {ex.Message}"); }
        }
        var required = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventory.json")))!["operations"]!
            .AsArray().SelectMany(x => x!["requiredCaseIds"]!.AsArray().Select(y => y!.ToString())).Order().ToArray();
        executed.Order().ToArray().ShouldBe(required);
        return (executed.ToArray(), passed.ToArray(), excluded.ToArray(), failures.ToArray());
    }

    private static void Check(JsonObject entry)
    {
        var args = entry["arguments"]!.AsArray().Select(x => Parse(x!)).ToArray();
        var preserved = entry["invariants"]!["preserveArguments"]?.AsArray().Select(x => x!.GetValue<int>()).ToArray() ?? [];
        var snapshots = preserved.ToDictionary(i => i, i => Observe(args[i]).ToJsonString());
        object? actual = null;
        string? error = null;
        try { actual = Execute(entry["operation"]!.ToString(), args); }
        catch (OverflowException) { error = "Overflow"; }
        catch (DivideByZeroException) { error = "DivisionByZero"; }
        catch (NonFiniteConversionException) { error = "NonFiniteConversion"; }
        finally
        {
            foreach (var (i, snapshot) in snapshots) Observe(args[i]).ToJsonString().ShouldBe(snapshot);
        }
        var expected = entry["expected"]!;
        if (expected["error"] is { } expectedError) { error.ShouldBe(expectedError["code"]!.ToString()); return; }
        error.ShouldBeNull();
        actual.ShouldNotBeNull();
        if (entry["operation"]!.ToString() is "Morphir.SDK.Decimal.add" or "Morphir.SDK.Decimal.sub" or "Morphir.SDK.Decimal.mul" or "Morphir.SDK.Decimal.div")
            AssertCanonicalDecimalResult(actual);
        JsonNode.DeepEquals(Normalize(expected["value"]!), Normalize(Observe(actual))).ShouldBeTrue(
            $"Expected {expected["value"]}, observed {Observe(actual)}");
        if (entry["invariants"]!["equalTo"] is { } equal) Basics.Equal(actual, Parse(equal)).ShouldBeTrue();
        if (entry["invariants"]!["hashEqualTo"] is { } hash)
        {
            var independentlyConstructed = Parse(hash);
            Basics.Equal(actual, independentlyConstructed).ShouldBeTrue();
            actual.GetHashCode().ShouldBe(independentlyConstructed.GetHashCode());
        }
    }

    internal static object Execute(string operation, object[] a)
    {
        var op = operation.Replace("Morphir.SDK.", "", StringComparison.Ordinal);
        var first = a.Length > 0 ? a[0] : Unit.Value;
        object Number(Func<int, int, int> ints, Func<double, double, double> floats) => first is int x ? (object)ints(x, (int)a[1]) : floats((double)first, (double)a[1]);
        var result = op switch
        {
            "Basics.add" => Number(Basics.Add, Basics.Add),
            "Basics.subtract" => Number(Basics.Subtract, Basics.Subtract),
            "Basics.multiply" => Number(Basics.Multiply, Basics.Multiply),
            "Basics.divide" => Basics.Divide((double)a[0], (double)a[1]),
            "Basics.integerDivide" => Basics.IntegerDivide((int)a[0], (int)a[1]),
            "Basics.modBy" => Basics.ModBy((int)a[0], (int)a[1]),
            "Basics.remainderBy" => Basics.RemainderBy((int)a[0], (int)a[1]),
            "Basics.power" => Number(Basics.Power, Basics.Power),
            "Basics.negate" => first is int n ? (object)Basics.Negate(n) : Basics.Negate((double)first),
            "Basics.abs" => first is int n ? (object)Basics.Abs(n) : Basics.Abs((double)first),
            "Basics.toFloat" => Basics.ToFloat((int)first),
            "Basics.round" => Basics.Round((double)first),
            "Basics.floor" => Basics.Floor((double)first),
            "Basics.ceiling" => Basics.Ceiling((double)first),
            "Basics.truncate" => Basics.Truncate((double)first),
            "Basics.isNaN" => Basics.IsNaN((double)first),
            "Basics.isInfinite" => Basics.IsInfinite((double)first),
            "Basics.compare" => Basics.Compare(first, a[1]),
            "Basics.lessThan" => Basics.LessThan(first, a[1]),
            "Basics.lessThanOrEqual" => Basics.LessThanOrEqual(first, a[1]),
            "Basics.greaterThan" => Basics.GreaterThan(first, a[1]),
            "Basics.greaterThanOrEqual" => Basics.GreaterThanOrEqual(first, a[1]),
            "Basics.min" => Basics.Min(first, a[1]),
            "Basics.max" => Basics.Max(first, a[1]),
            "Basics.clamp" => first is int n ? (object)Basics.Clamp(n, (int)a[1], (int)a[2]) : Basics.Clamp((double)first, (double)a[1], (double)a[2]),
            "Basics.equal" => Basics.Equal(first, a[1]),
            "Basics.notEqual" => Basics.NotEqual(first, a[1]),
            "Basics.LT" => Basics.LT,
            "Basics.EQ" => Basics.EQ,
            "Basics.GT" => Basics.GT,
            "List.singleton" => List<object>.Singleton(first),
            "List.cons" => ((List<object>)a[1]).Cons(first),
            "List.map" => ((List<object>)a[1]).Map((Func<object, object>)first),
            "List.append" => ((List<object>)first).Append((List<object>)a[1]),
            "List.head" => ((List<object>)first).Head(),
            "List.tail" => ((List<object>)first).Tail().Map<object>(x => x),
            "List.length" => ((List<object>)first).Length,
            "Maybe.Just" => Maybe<object>.Just(first),
            "Maybe.Nothing" => Maybe<object>.Nothing,
            "Maybe.map" => ((Maybe<object>)a[1]).Map((Func<object, object>)first),
            "Maybe.andThen" => ((Maybe<object>)a[1]).AndThen(x => (Maybe<object>)((Func<object, object>)first)(x)),
            "Result.Ok" => Result<object, object>.Ok(first),
            "Result.Err" => Result<object, object>.Err(first),
            "Result.map" => ((Result<object, object>)a[1]).Map((Func<object, object>)first),
            "Result.andThen" => ((Result<object, object>)a[1]).AndThen(x => (Result<object, object>)((Func<object, object>)first)(x)),
            "Dict.empty" => Dict<object, object>.Empty(MorphirComparers.Value),
            "Dict.singleton" => Dict<object, object>.Singleton(first, a[1], MorphirComparers.Value),
            "Dict.fromList" => Dict<object, object>.FromEnumerable(((List<object>)first).Select(x => ((object, object))x), MorphirComparers.Value),
            "Dict.get" => ((Dict<object, object>)a[1]).Get(first),
            "Dict.insert" => ((Dict<object, object>)a[2]).Insert(first, a[1]),
            "Dict.update" => ((Dict<object, object>)a[2]).Update(first, x => (Maybe<object>)((Func<object, object>)a[1])(x)),
            "Dict.remove" => ((Dict<object, object>)a[1]).Remove(first),
            "Dict.toList" => ((Dict<object, object>)first).ToList().Map<object>(x => x),
            "Dict.union" => ((Dict<object, object>)first).Union((Dict<object, object>)a[1]),
            "Set.empty" => Set<object>.Empty(MorphirComparers.Value),
            "Set.singleton" => Set<object>.Singleton(first, MorphirComparers.Value),
            "Set.fromList" => Set<object>.FromEnumerable((List<object>)first, MorphirComparers.Value),
            "Set.insert" => ((Set<object>)a[1]).Insert(first),
            "Set.remove" => ((Set<object>)a[1]).Remove(first),
            "Set.toList" => ((Set<object>)first).ToList(),
            "Set.union" => ((Set<object>)first).Union((Set<object>)a[1]),
            "Decimal.add" => Decimal.Add((decimal)first, (decimal)a[1]),
            "Decimal.sub" => Decimal.Sub((decimal)first, (decimal)a[1]),
            "Decimal.mul" => Decimal.Mul((decimal)first, (decimal)a[1]),
            "Decimal.div" => Decimal.Div((decimal)first, (decimal)a[1]).Map<object>(x => x),
            "Decimal.compare" => Decimal.Compare((decimal)first, (decimal)a[1]),
            "Decimal.toString" => Decimal.ToString((decimal)first),
            _ => throw new NotSupportedException($"Unmapped frozen operation {operation}")
        };
        return result;
    }

    internal static object Parse(JsonNode node)
    {
        string Text(string name) => node[name]!.ToString();
        object[] Items() => node["items"]!.AsArray().Select(x => Parse(x!)).ToArray();
        return Text("tag") switch
        {
            "int" => int.Parse(Text("value"), CultureInfo.InvariantCulture),
            "float" => Float(Text("value")),
            "decimal" => ParseDecimal(Text("coefficient"), node["scale"]!.GetValue<int>()),
            "string" => Text("value"),
            "char" => Text("value")[0],
            "bool" => node["value"]!.GetValue<bool>(),
            "unit" => Unit.Value,
            "order" => Enum.Parse<Order>(Text("value")),
            "nothing" => Maybe<object>.Nothing,
            "just" => Maybe<object>.Just(Parse(node["value"]!)),
            "ok" => Result<object, object>.Ok(Parse(node["value"]!)),
            "err" => Result<object, object>.Err(Parse(node["value"]!)),
            "list" => List<object>.FromEnumerable(Items()),
            "tuple" => Items() is var items && items.Length == 2 ? (items[0], items[1]) : (items[0], items[1], items[2]),
            "dict" => Dict<object, object>.FromEnumerable(node["entries"]!.AsArray().Select(x => (Parse(x![0]!), Parse(x[1]!))), MorphirComparers.Value),
            "set" => Set<object>.FromEnumerable(Items(), MorphirComparers.Value),
            "callback" => Callback(Text("id")),
            _ => throw new NotSupportedException($"Unmapped fixture tag {Text("tag")}")
        };
    }

    private static Func<object, object> Callback(string id) => id switch
    {
        "increment" => x => Basics.Add((int)x, 1),
        "showInt" => x => ((int)x).ToString(CultureInfo.InvariantCulture),
        "positiveMaybe" => x => (int)x > 0 ? Maybe<object>.Just(x) : Maybe<object>.Nothing,
        "positiveResult" => x => (int)x > 0 ? Result<object, object>.Ok(x) : Result<object, object>.Err("negative"),
        "setJustSeven" => _ => Maybe<object>.Just(7),
        "removeEntry" => _ => Maybe<object>.Nothing,
        "incrementMaybe" => x => ((Maybe<object>)x).Map<object>(v => Basics.Add((int)v, 1)),
        _ => throw new NotSupportedException($"Unmapped callback {id}")
    };

    private static decimal ParseDecimal(string coefficient, int scale)
    {
        var c = BigInteger.Parse(coefficient, CultureInfo.InvariantCulture);
        var maximum = (BigInteger.One << 96) - 1;
        while (scale > 0 && (scale > 28 || BigInteger.Abs(c) > maximum) && c % 10 == 0) { c /= 10; scale--; }
        if (scale > 28 || BigInteger.Abs(c) > maximum) throw new NotSupportedException("Fixture decimal is outside the exact runtime profile.");
        return new decimal((int)(uint)(BigInteger.Abs(c) & uint.MaxValue), (int)(uint)((BigInteger.Abs(c) >> 32) & uint.MaxValue),
            (int)(uint)((BigInteger.Abs(c) >> 64) & uint.MaxValue), c.Sign < 0, checked((byte)scale));
    }

    private static double Float(string text) => text switch
    {
        "NaN" => double.NaN,
        "+Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        "+0" => +0d,
        "-0" => BitConverter.Int64BitsToDouble(long.MinValue),
        _ => double.Parse(text, CultureInfo.InvariantCulture)
    };

    private static JsonNode Observe(object value)
    {
        JsonObject Tagged(string tag, string? text = null) => text is null ? new() { ["tag"] = tag } : new() { ["tag"] = tag, ["value"] = text };
        JsonObject Nested(string tag, object payload) => new() { ["tag"] = tag, ["value"] = Observe(payload) };
        return value switch
        {
            int x => Tagged("int", x.ToString(CultureInfo.InvariantCulture)),
            double x => Tagged("float", double.IsNaN(x) ? "NaN" : x == 0 ? BitConverter.DoubleToInt64Bits(x) < 0 ? "-0" : "+0" : double.IsPositiveInfinity(x) ? "+Infinity" : double.IsNegativeInfinity(x) ? "-Infinity" : x.ToString("R", CultureInfo.InvariantCulture)),
            decimal x => ObserveDecimal(x),
            string x => Tagged("string", x),
            char x => Tagged("char", x.ToString()),
            bool x => new JsonObject { ["tag"] = "bool", ["value"] = x },
            Unit => Tagged("unit"),
            Order x => Tagged("order", x.ToString()),
            Maybe<object> x => x.Match(v => Nested("just", v), () => Tagged("nothing")),
            Result<object, object> x => x.Match(e => Nested("err", e), v => Nested("ok", v)),
            List<object> x => new JsonObject { ["tag"] = "list", ["items"] = new JsonArray(x.Select(Observe).ToArray()) },
            ITuple x => new JsonObject { ["tag"] = "tuple", ["items"] = new JsonArray(Enumerable.Range(0, x.Length).Select(i => Observe(x[i]!)).ToArray()) },
            Dict<object, object> x => new JsonObject { ["tag"] = "dict", ["entries"] = new JsonArray(x.Select(p => (JsonNode)new JsonArray(Observe(p.Key), Observe(p.Value))).ToArray()) },
            Set<object> x => new JsonObject { ["tag"] = "set", ["items"] = new JsonArray(x.Select(Observe).ToArray()) },
            _ => throw new NotSupportedException($"Unmapped runtime projection {value.GetType()}")
        };
    }

    private static JsonObject ObserveDecimal(decimal x)
    {
        var bits = decimal.GetBits(x);
        var c = (BigInteger)(uint)bits[0] | (BigInteger)(uint)bits[1] << 32 | (BigInteger)(uint)bits[2] << 64;
        if (bits[3] < 0) c = -c;
        var scale = (bits[3] >> 16) & 255;
        while (scale > 0 && c % 10 == 0) { c /= 10; scale--; }
        return new() { ["tag"] = "decimal", ["coefficient"] = c.ToString(CultureInfo.InvariantCulture), ["scale"] = scale };
    }

    private static void AssertCanonicalDecimalResult(object value)
    {
        if (value is Maybe<object> optional)
        {
            optional.Match(x => { AssertCanonicalDecimalResult(x); return true; }, () => true);
            return;
        }
        var bits = decimal.GetBits((decimal)value);
        var coefficient = (BigInteger)(uint)bits[0] | (BigInteger)(uint)bits[1] << 32 | (BigInteger)(uint)bits[2] << 64;
        var scale = (bits[3] >> 16) & 255;
        if (coefficient.IsZero) bits[3].ShouldBe(0);
        else if (scale > 0) (coefficient % 10 == 0).ShouldBeFalse("Arithmetic result must have no redundant decimal scale.");
    }

    private static JsonNode Normalize(JsonNode node)
    {
        if (node is JsonObject o && o["tag"]?.ToString() == "float")
        {
            var x = Float(o["value"]!.ToString());
            return new JsonObject { ["tag"] = "float", ["value"] = double.IsNaN(x) ? "NaN" : BitConverter.DoubleToInt64Bits(x).ToString(CultureInfo.InvariantCulture) };
        }
        if (node is JsonObject obj) return new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Normalize(p.Value))));
        if (node is JsonArray array) return new JsonArray(array.Select(x => x is null ? null : Normalize(x)).ToArray());
        return node.DeepClone();
    }
}
