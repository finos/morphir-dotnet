using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Morphir.SDK.Conformance.Tests;

internal static class FixtureProfiles
{
    private static readonly BigInteger DecimalMaximum = BigInteger.Parse("79228162514264337593543950335", CultureInfo.InvariantCulture);

    internal static void Validate(JsonNode inventory, Dictionary<string, JsonNode> operations,
        Dictionary<string, JsonNode> cases, List<string> errors)
    {
        var callbacks = inventory["callbacks"]!.AsArray().ToDictionary(x => x!["id"]!.ToString(), x => TypeSpec.Parse(x!["type"]!.ToString()), StringComparer.Ordinal);
        var callbackOutcomes = inventory["callbacks"]!.AsArray().ToDictionary(x => x!["id"]!.ToString(), x => x!["errorOutcomes"]!.AsArray().Select(code => code!.ToString()).ToArray(), StringComparer.Ordinal);
        var profile = inventory["profiles"]!.AsArray().Single(x => x!["id"]!.ToString() == "dotnet-sdk-v1")!;
        var errorCodes = profile["errorCodes"]!.AsArray().Select(x => x!.ToString()).ToHashSet(StringComparer.Ordinal);
        var exclusionCodes = profile["exclusionCodes"]!.AsArray().Select(x => x!.ToString()).ToHashSet(StringComparer.Ordinal);
        if (profile["integer"]!["minimum"]!.ToString() != "-2147483648" || profile["integer"]!["maximum"]!.ToString() != "2147483647"
            || profile["decimal"]!["maximumScale"]!.GetValue<int>() != 28 || profile["decimal"]!["maximumCoefficient"]!.ToString() != DecimalMaximum.ToString(CultureInfo.InvariantCulture))
            errors.Add("inventory/profiles: dotnet-sdk-v1 numeric boundaries cannot change without a new contract");

        foreach (var (id, operation) in operations)
        {
            var variables = operation["typeParameters"]!.AsObject().ToDictionary(x => x.Key, x => x.Value!.ToString(), StringComparer.Ordinal);
            if (variables.Values.Any(x => x is not ("value" or "number" or "comparable"))) errors.Add($"{id}/typeParameters: unknown constraint");
            if (operation["profile"]!.ToString() != "dotnet-sdk-v1" || string.IsNullOrWhiteSpace(operation["meaning"]!.ToString()))
                errors.Add($"{id}/profile or meaning: incomplete operation contract");
            foreach (var signature in operation["parameters"]!.AsArray().Select(x => x!["type"]!.ToString()).Append(operation["result"]!.ToString()))
                TypeSpec.Parse(signature).Check(variables.Keys.ToHashSet(StringComparer.Ordinal), $"{id}/signature", errors);
        }

        foreach (var (id, entry) in cases)
        {
            if (!operations.TryGetValue(entry["operation"]!.ToString(), out var operation)) continue;
            var arguments = entry["arguments"]!.AsArray();
            var parameters = operation["parameters"]!.AsArray();
            var typeErrorsBefore = errors.Count;
            var typeArguments = entry["typeArguments"]!.AsObject().ToDictionary(x => x.Key, x => TypeSpec.ParseAt(x.Value!.ToString(), $"{id}/typeArguments/{x.Key}", errors), StringComparer.Ordinal);
            var variables = operation["typeParameters"]!.AsObject();
            if (!variables.Select(x => x.Key).ToHashSet(StringComparer.Ordinal).SetEquals(typeArguments.Keys))
            {
                errors.Add($"{id}/typeArguments: must bind exactly the signature's type parameters");
                continue;
            }
            foreach (var (name, type) in typeArguments)
            {
                type.Check([], $"{id}/typeArguments/{name}", errors);
                if (!type.IsValue) errors.Add($"{id}/typeArguments/{name}: callbacks are not arbitrary SDK value payloads");
                var constraint = variables[name]!.ToString();
                if (constraint == "number" && type.Name is not ("Int" or "Float") || constraint == "comparable" && !type.IsComparable)
                    errors.Add($"{id}/typeArguments/{name}: {type} does not satisfy {constraint}");
            }
            if (errors.Count != typeErrorsBefore) continue;
            if (arguments.Count != parameters.Count)
            {
                errors.Add($"{id}/arguments: expected {parameters.Count} arguments, got {arguments.Count}");
                continue;
            }
            var limits = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < arguments.Count; index++)
            {
                var declared = TypeSpec.Parse(parameters[index]!["type"]!.ToString());
                var type = declared.Bind(typeArguments);
                var comparable = declared.Name is not null && variables[declared.Name]?.ToString() == "comparable";
                Value(arguments[index]!, type, $"{id}/arguments/{index}", false, comparable, callbacks, limits, errors);
            }
            var resultType = TypeSpec.Parse(operation["result"]!.ToString()).Bind(typeArguments);
            if (entry["expected"]!["value"] is { } expected)
            {
                // A retained reference observation can expose the reason for exclusion.
                var result = entry["exclusion"]?["code"]?.ToString() == "IntegerNegativeExponent" ? TypeSpec.Parse("Float") : resultType;
                Value(expected, result, $"{id}/expected/value", true, false, callbacks, limits, errors);
            }
            else
            {
                var allowed = operation["errorOutcomes"]!.AsArray().Select(x => x!.ToString()).ToHashSet(StringComparer.Ordinal);
                if (operation["callbackErrors"]!.ToString() == "propagate")
                    foreach (var argument in arguments.Where(x => x!["tag"]!.ToString() == "callback"))
                        if (callbackOutcomes.TryGetValue(argument!["id"]!.ToString(), out var outcomes)) allowed.UnionWith(outcomes);
                var code = entry["expected"]!["error"]!["code"]!.ToString();
                if (!errorCodes.Contains(code) || !allowed.Contains(code)) errors.Add($"{id}/expected/error/code: unknown arithmetic or callback outcome");
            }

            var invariants = entry["invariants"]!;
            foreach (var index in invariants["preserveArguments"]?.AsArray() ?? [])
                if (index!.GetValue<int>() >= arguments.Count || arguments[index.GetValue<int>()]!["tag"]!.ToString() == "callback")
                    errors.Add($"{id}/invariants/preserveArguments: index must identify a value argument");
            foreach (var field in new[] { "equalTo", "hashEqualTo" })
            {
                if (invariants[field] is not { } value) continue;
                Value(value, resultType, $"{id}/invariants/{field}", false, false, callbacks, limits, errors);
                if (entry["expected"]!["value"] is not { } result || !Equivalent(result, value))
                    errors.Add($"{id}/invariants/{field}: invariant must agree with the canonical expected value");
            }
            if (invariants["hashEqualTo"] is not null && invariants["equalTo"] is null)
                errors.Add($"{id}/invariants/hashEqualTo: require structural equality before hash equivalence");

            if (entry["classification"]!.ToString() == "required")
            {
                foreach (var limit in limits) errors.Add($"{id}/profile: value violates {limit} in Int32/Decimal96/text/comparable domain");
            }
            else
            {
                var code = entry["exclusion"]!["code"]!.ToString();
                if (code == "IntegerNegativeExponent" && entry["operation"]!.ToString() == "Morphir.SDK.Basics.power" && arguments[1]!["tag"]!.ToString() == "int" && Integer(arguments[1]!, "value") < 0)
                    limits.Add(code);
                if (code == "DecimalNonTerminating" && entry["operation"]!.ToString() == "Morphir.SDK.Decimal.div" && NonTerminating(arguments[0]!, arguments[1]!))
                    limits.Add(code);
                if (!exclusionCodes.Contains(code) || !limits.Contains(code)) errors.Add($"{id}/exclusion/code: exclusion must identify a demonstrated representation limit");
                if (entry["knownGaps"]!.AsArray().Count != 0) errors.Add($"{id}/knownGaps: an excluded observation cannot count as a covered runtime gap");
            }
        }
    }

    private static bool NonTerminating(JsonNode a, JsonNode b)
    {
        var denominator = BigInteger.Abs(Integer(b, "coefficient"));
        if (denominator.IsZero) return false;
        denominator /= BigInteger.GreatestCommonDivisor(BigInteger.Abs(Integer(a, "coefficient")), denominator);
        while (denominator % 2 == 0) denominator /= 2;
        while (denominator % 5 == 0) denominator /= 5;
        return denominator != 1;
    }

    private static bool Equivalent(JsonNode left, JsonNode right)
    {
        var a = left.DeepClone();
        var b = right.DeepClone();
        var hasNaN = false;
        Normalize(a, ref hasNaN);
        Normalize(b, ref hasNaN);
        return !hasNaN && JsonNode.DeepEquals(a, b);
    }

    // Invariant operands are construction values, so authored decimal scale may differ.
    private static void Normalize(JsonNode value, ref bool hasNaN)
    {
        if (value is JsonArray array)
        {
            foreach (var item in array) if (item is not null) Normalize(item, ref hasNaN);
        }
        else if (value is JsonObject obj)
        {
            foreach (var item in obj.Select(x => x.Value).ToArray()) if (item is not null) Normalize(item, ref hasNaN);
            if (obj["tag"]?.ToString() == "decimal")
            {
                var coefficient = Integer(obj, "coefficient");
                var scale = obj["scale"]!.GetValue<int>();
                while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
                obj["coefficient"] = coefficient.ToString(CultureInfo.InvariantCulture);
                obj["scale"] = scale;
            }
            else if (obj["tag"]?.ToString() == "float")
            {
                var number = Floating(obj);
                hasNaN |= double.IsNaN(number);
                obj["value"] = number == 0 ? "+0" : number.ToString("R", CultureInfo.InvariantCulture);
            }
        }
    }

    private static void Value(JsonNode value, TypeSpec type, string path, bool canonical, bool comparable,
        Dictionary<string, TypeSpec> callbacks, HashSet<string> limits, List<string> errors)
    {
        var tag = value["tag"]!.ToString();
        var matches = type.Name switch
        {
            "Int" => tag == "int",
            "Float" => tag == "float",
            "Decimal" => tag == "decimal",
            "String" => tag == "string",
            "Char" => tag == "char",
            "Bool" => tag == "bool",
            "Unit" => tag == "unit",
            "Order" => tag == "order",
            "List" => tag == "list",
            "Tuple" => tag == "tuple",
            "Dict" => tag == "dict",
            "Set" => tag == "set",
            "Maybe" => tag is "just" or "nothing",
            "Result" => tag is "ok" or "err",
            "Fn" => tag == "callback",
            _ => false,
        };
        if (!matches)
        {
            errors.Add($"{path}: tag {tag} does not match {type}");
            return;
        }
        switch (tag)
        {
            case "int":
                if (Integer(value, "value") < int.MinValue || Integer(value, "value") > int.MaxValue) limits.Add("IntegerOutOfRange");
                break;
            case "float":
                var text = value["value"]!.ToString();
                if (text is not ("NaN" or "+Infinity" or "-Infinity" or "+0" or "-0") && (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number == 0))
                    errors.Add($"{path}/value: finite binary64 text required; encode exceptional values and signed zero explicitly");
                if (comparable && text is "NaN" or "+Infinity" or "-Infinity") limits.Add("NonFiniteComparable");
                break;
            case "decimal":
                var coefficient = Integer(value, "coefficient");
                var scale = value["scale"]!.GetValue<int>();
                if (canonical && scale > 0 && coefficient % 10 == 0) errors.Add($"{path}: decimal projection must be canonical");
                while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
                if (scale > 28) limits.Add("DecimalScaleOutOfRange");
                if (BigInteger.Abs(coefficient) > DecimalMaximum) limits.Add("DecimalCoefficientOutOfRange");
                break;
            case "char":
                var character = value["value"]!.ToString();
                if (character.Length != 1 || char.IsSurrogate(character[0])) limits.Add("CharOutOfRange");
                break;
            case "string":
                var content = value["value"]!.ToString();
                for (var index = 0; index < content.Length; index++)
                {
                    if (!char.IsSurrogate(content[index])) continue;
                    if (!char.IsHighSurrogate(content[index]) || index + 1 == content.Length || !char.IsLowSurrogate(content[++index])) limits.Add("InvalidUtf16");
                }
                break;
            case "callback":
                var callback = value["id"]!.ToString();
                if (!callbacks.TryGetValue(callback, out var signature)) errors.Add($"{path}/callback: unknown callback ID {callback}");
                else if (signature.ToString() != type.ToString()) errors.Add($"{path}/callback: {callback} has type {signature}, expected {type}");
                break;
            case "just":
            case "ok":
            case "err":
                Value(value["value"]!, type.Arguments[tag == "ok" ? 1 : 0], path + "/value", canonical, false, callbacks, limits, errors);
                break;
            case "list":
            case "set":
            case "tuple":
                var items = value["items"]!.AsArray();
                if (tag == "tuple" && items.Count != type.Arguments.Count) { errors.Add($"{path}/items: tuple arity differs from {type}"); break; }
                for (var index = 0; index < items.Count; index++)
                    Value(items[index]!, type.Arguments[tag == "tuple" ? index : 0], $"{path}/items/{index}", canonical, comparable || tag == "set", callbacks, limits, errors);
                if (tag == "set") Ordered(items.Select(x => x!).ToArray(), path + "/items", errors);
                break;
            case "dict":
                var entries = value["entries"]!.AsArray();
                for (var index = 0; index < entries.Count; index++)
                {
                    Value(entries[index]![0]!, type.Arguments[0], $"{path}/entries/{index}/0", canonical, true, callbacks, limits, errors);
                    Value(entries[index]![1]!, type.Arguments[1], $"{path}/entries/{index}/1", canonical, false, callbacks, limits, errors);
                }
                Ordered(entries.Select(x => x![0]!).ToArray(), path + "/entries", errors);
                break;
        }
    }

    private static void Ordered(JsonNode[] values, string path, List<string> errors)
    {
        for (var index = 1; index < values.Length; index++)
            if (Compare(values[index - 1], values[index]) >= 0) errors.Add($"{path}/{index}: keys/elements must be strictly ascending and unique");
    }

    // Comparison here validates canonical data order only. It is not an SDK oracle.
    private static int Compare(JsonNode left, JsonNode right)
    {
        var tag = left["tag"]!.ToString();
        if (tag != right["tag"]!.ToString()) throw new FormatException("Ordered observations require homogeneous comparable values.");
        switch (tag)
        {
            case "int": return Integer(left, "value").CompareTo(Integer(right, "value"));
            case "float": return Floating(left).CompareTo(Floating(right));
            case "string": case "char": return string.CompareOrdinal(left["value"]!.ToString(), right["value"]!.ToString());
            case "list":
            case "tuple":
                var a = left["items"]!.AsArray();
                var b = right["items"]!.AsArray();
                for (var index = 0; index < Math.Min(a.Count, b.Count); index++)
                {
                    var comparison = Compare(a[index]!, b[index]!);
                    if (comparison != 0) return comparison;
                }
                return a.Count.CompareTo(b.Count);
            default: throw new FormatException($"{tag} is not an admitted comparable observation.");
        }
    }

    private static double Floating(JsonNode value) => value["value"]!.ToString() switch
    {
        "+0" or "-0" => 0,
        "NaN" => double.NaN,
        "+Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        var text => double.Parse(text, CultureInfo.InvariantCulture),
    };

    private static BigInteger Integer(JsonNode value, string field) => BigInteger.Parse(value[field]!.ToString(), CultureInfo.InvariantCulture);
}

internal sealed record TypeSpec(string Name, IReadOnlyList<TypeSpec> Arguments)
{
    internal bool IsValue => Name != "Fn" && Arguments.All(x => x.IsValue);
    internal bool IsComparable => Name is "Int" or "Float" or "String" or "Char" || Name is "List" or "Tuple" && Arguments.All(x => x.IsComparable);

    internal static TypeSpec ParseAt(string text, string path, List<string> errors)
    {
        try { return Parse(text); }
        catch (FormatException ex)
        {
            errors.Add($"{path}: {ex.Message}");
            return new TypeSpec("Invalid", []);
        }
    }

    internal static TypeSpec Parse(string text)
    {
        var opening = text.IndexOf('<');
        if (opening < 0) return new TypeSpec(text, []);
        if (!text.EndsWith('>')) throw new FormatException($"Invalid type expression {text}");
        var parts = new List<TypeSpec>();
        var depth = 0;
        var start = opening + 1;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '<') depth++;
            if (text[index] == '>') depth--;
            if (text[index] == ',' && depth == 0 || index == text.Length - 1)
            {
                parts.Add(Parse(text[start..index]));
                start = index + 1;
            }
        }
        return new TypeSpec(text[..opening], parts);
    }

    internal TypeSpec Bind(Dictionary<string, TypeSpec> arguments) => arguments.TryGetValue(Name, out var type) ? type : new(Name, Arguments.Select(x => x.Bind(arguments)).ToArray());

    internal void Check(HashSet<string> variables, string path, List<string> errors)
    {
        var arity = Name switch
        {
            "Int" or "Float" or "Decimal" or "String" or "Char" or "Bool" or "Unit" or "Order" => 0,
            "List" or "Set" or "Maybe" => 1,
            "Dict" or "Result" or "Fn" => 2,
            "Tuple" => Arguments.Count is 2 or 3 ? Arguments.Count : -1,
            _ => variables.Contains(Name) ? 0 : -1,
        };
        if (arity != Arguments.Count) errors.Add($"{path}: unknown or invalid type {this}");
        if (Name == "Set" && Arguments.Count == 1 && !Arguments[0].IsComparable && !variables.Contains(Arguments[0].Name) || Name == "Dict" && Arguments.Count == 2 && !Arguments[0].IsComparable && !variables.Contains(Arguments[0].Name))
            errors.Add($"{path}: key/element type must be comparable");
        foreach (var argument in Arguments) argument.Check(variables, path, errors);
    }

    public override string ToString() => Arguments.Count == 0 ? Name : $"{Name}<{string.Join(',', Arguments)}>";
}
