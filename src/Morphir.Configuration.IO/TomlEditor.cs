using System.Globalization;
using System.Text;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO;

/// <summary>A rendered edit. Failures always return the untouched input and a safe diagnostic code.</summary>
public sealed record TomlEditResult(bool Success, bool Found, bool Changed, string Text, string? ErrorCode);

/// <summary>Patches TOML source spans and verifies the complete typed result with the canonical reader.</summary>
public static class TomlEditor
{
    public static TomlEditResult Edit(string text, IReadOnlyList<string> path, ConfigValue? value)
    {
        var original = ConfigurationReader.ReadText("edit.toml", text);
        if (original.Content is not SourceContent.Available available)
            return new(false, false, false, text, "config.edit.invalid_toml");
        if (path.Count == 0)
            return new(false, false, false, text, "config.edit.invalid_path");
        for (var length = 1; length < path.Count; length++)
        {
            var ancestor = ConfigTree.tryFind(ListModule.OfSeq(path.Take(length)), available.Item.Values);
            if (ancestor is not null && ancestor.Value is not ConfigValue.Object)
                return new(false, false, false, text, "config.edit.path_shape");
        }
        value = value is null ? null : RawValue(value);
        var old = ConfigTree.tryFind(ListModule.OfSeq(path), available.Item.Raw);
        if (old is null && value is null)
            return new(true, false, false, text, null);
        if (old is not null && value is not null && Equivalent(old.Value, value))
            return new(true, true, false, text, null);
        try
        {
            var statements = Scan(text);
            var statement = statements.FirstOrDefault(s => !s.Header && s.Path.SequenceEqual(path));
            string rendered;
            if (statement is not null)
                rendered = value is null ? text[..statement.Start] + text[statement.End..]
                    : text[..statement.ValueStart] + Render(value) + text[statement.ValueEnd..];
            else if (statements.FirstOrDefault(s => !s.Header && s.Path.Length < path.Count && Prefix(s.Path, path)) is { } ancestor)
            {
                rendered = PatchInline(text, ancestor.ValueStart, ancestor.ValueEnd, path.Skip(ancestor.Path.Length).ToArray(), value);
            }
            else
            {
                rendered = text;
                foreach (var related in statements.Where(s => Prefix(path, s.Path)).OrderByDescending(s => s.Start))
                    rendered = rendered.Remove(related.Start, related.End - related.Start);
                if (value is not null) rendered = Insert(rendered, Scan(rendered), path, Render(value));
            }
            var expected = value is null ? Remove(available.Item.Raw, path, 0) : ConfigTree.set(ListModule.OfSeq(path), value, available.Item.Raw);
            var candidate = ConfigurationReader.ReadText("edit.toml", rendered);
            if (value is null && candidate.Content is SourceContent.Available removed)
            {
                for (var length = 1; length < path.Count; length++)
                {
                    var prefix = path.Take(length).ToArray();
                    var wanted = ConfigTree.tryFind(ListModule.OfSeq(prefix), expected);
                    if (wanted is null || !EmptySkeleton(wanted.Value) || ConfigTree.tryFind(ListModule.OfSeq(prefix), removed.Item.Raw) is not null) continue;
                    var current = Scan(rendered);
                    var parent = current.FirstOrDefault(s => !s.Header && s.Path.Length < prefix.Length && Prefix(s.Path, prefix));
                    rendered = parent is null ? Insert(rendered, current, prefix, Render(wanted.Value))
                        : PatchInline(rendered, parent.ValueStart, parent.ValueEnd, prefix.Skip(parent.Path.Length).ToArray(), wanted.Value);
                    candidate = ConfigurationReader.ReadText("edit.toml", rendered);
                    break;
                }
            }
            if (candidate.Content is not SourceContent.Available after || !Equivalent(after.Item.Raw, expected))
                return new(false, old is not null, false, text, "config.edit.verification");
            return new(true, old is not null, rendered != text, rendered, null);
        }
        catch (ArgumentException)
        {
            return new(false, old is not null, false, text, "config.edit.unsupported_value");
        }
    }

    private static ConfigValue RawValue(ConfigValue value) => value switch
    {
        ConfigValue.SecretReference reference => RawValue(ConfigValue.NewObject(reference.Item)),
        ConfigValue.Object fields => ReaderValues.Object(fields.Item.Select(pair => new KeyValuePair<string, ConfigValue>(pair.Key, RawValue(pair.Value)))),
        ConfigValue.Array array => ConfigValue.NewArray(ListModule.OfSeq(array.Item.Select(RawValue))),
        _ => value
    };

    private static bool Equivalent(ConfigValue left, ConfigValue right) => (left, right) switch
    {
        (ConfigValue.Number first, ConfigValue.Number second) => (double.IsNaN(first.Item) && double.IsNaN(second.Item))
            || BitConverter.DoubleToInt64Bits(first.Item) == BitConverter.DoubleToInt64Bits(second.Item),
        (ConfigValue.Object first, ConfigValue.Object second) => first.Item.Count == second.Item.Count
            && first.Item.All(pair => second.Item.TryGetValue(pair.Key, out var value) && Equivalent(pair.Value, value)),
        (ConfigValue.Array first, ConfigValue.Array second) => first.Item.Length == second.Item.Length
            && first.Item.Zip(second.Item).All(pair => Equivalent(pair.First, pair.Second)),
        _ => left.Equals(right)
    };

    private static bool EmptySkeleton(ConfigValue value) => value is ConfigValue.Object fields && fields.Item.All(pair => EmptySkeleton(pair.Value));

    private sealed record InlineField(string[] Path, int Start, int End, int ValueStart, int ValueEnd, int CommaBefore, int CommaAfter);

    private static string PatchInline(string text, int start, int end, IReadOnlyList<string> path, ConfigValue? value)
    {
        if (text[start] != '{') throw new ArgumentException("Cannot traverse this value.");
        var fields = InlineFields(text, start, end);
        var field = fields.FirstOrDefault(f => f.Path.SequenceEqual(path));
        if (field is not null)
        {
            if (value is not null) return text[..field.ValueStart] + Render(value) + text[field.ValueEnd..];
            var comma = field.CommaAfter >= 0 ? field.CommaAfter : field.CommaBefore;
            var rendered = text.Remove(field.Start, field.End - field.Start);
            if (comma >= 0) rendered = rendered.Remove(comma > field.Start ? comma - (field.End - field.Start) : comma, 1);
            return rendered;
        }
        var ancestor = fields.FirstOrDefault(f => f.Path.Length < path.Count && Prefix(f.Path, path));
        if (ancestor is not null)
            return PatchInline(text, ancestor.ValueStart, ancestor.ValueEnd, path.Skip(ancestor.Path.Length).ToArray(), value);
        var related = fields.Where(f => Prefix(path, f.Path)).ToArray();
        if (related.Length > 0)
        {
            // Remove one field at a time so comma ownership is recalculated from the current text.
            foreach (var item in related.OrderByDescending(f => f.Start))
            {
                var previousLength = text.Length;
                text = PatchInline(text, start, end, item.Path, null);
                end += text.Length - previousLength;
            }
            if (value is null) return text;
            fields = InlineFields(text, start, end);
        }
        if (value is null) return text;
        var insertion = fields.Count > 0 ? fields[^1].ValueEnd : start + 1;
        var separator = fields.Count > 0 ? ", " : " ";
        return text.Insert(insertion, separator + string.Join(".", path.Select(Key)) + " = " + Render(value));
    }

    private static List<InlineField> InlineFields(string text, int start, int end)
    {
        var fields = new List<InlineField>();
        var index = start + 1;
        var previousComma = -1;
        while (index < end)
        {
            SkipTrivia(text, ref index);
            if (index >= end || text[index] == '}') break;
            var keyStart = index;
            while (index < end && text[index] != '=')
            {
                if (text[index] is '\'' or '\"') SkipString(text, ref index);
                else index++;
            }
            var path = ParseKey(text[keyStart..index]);
            index++;
            SkipTrivia(text, ref index);
            var valueStart = index;
            var depth = 0;
            while (index < end)
            {
                var character = text[index];
                if (character is '\'' or '\"') SkipString(text, ref index);
                else if (character is '[' or '{') { depth++; index++; }
                else if (depth == 0 && character is ',' or '}') break;
                else if (character is ']' or '}') { depth--; index++; }
                else if (character == '#')
                    while (index < end && text[index] is not '\r' and not '\n') index++;
                else index++;
            }
            var valueEnd = index;
            while (valueEnd > valueStart && char.IsWhiteSpace(text[valueEnd - 1])) valueEnd--;
            var comma = index < end && text[index] == ',' ? index : -1;
            fields.Add(new(path, keyStart, valueEnd, valueStart, valueEnd, previousComma, comma));
            previousComma = comma;
            if (comma >= 0) index++;
        }
        return fields;
    }

    private static bool Prefix(IReadOnlyList<string> prefix, IReadOnlyList<string> path) =>
        prefix.Count <= path.Count && prefix.SequenceEqual(path.Take(prefix.Count));

    private static string Insert(string text, List<Statement> statements, IReadOnlyList<string> path, string value)
    {
        var table = statements.Where(s => s.Header && s.Path.Length < path.Count && Prefix(s.Path, path))
            .OrderByDescending(s => s.Path.Length).FirstOrDefault();
        var next = statements.FirstOrDefault(s => s.Header && s.Start > (table?.Start ?? -1));
        var last = statements.LastOrDefault(s => !s.Header && s.Start > (table?.Start ?? -1) && s.Start < (next?.Start ?? text.Length));
        var index = last is not null ? LineEnd(text, last.End)
            : table is not null ? LineEnd(text, table.End)
            : next is not null ? (text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0) : text.Length;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var before = index > 0 && text[index - 1] is not '\r' and not '\n' and not '\uFEFF' ? newline : "";
        var key = string.Join(".", path.Skip(table?.Path.Length ?? 0).Select(Key));
        return text.Insert(index, before + key + " = " + value + newline);
    }

    private static int LineEnd(string text, int index)
    {
        while (index < text.Length && text[index] is not '\r' and not '\n') index++;
        if (index < text.Length && text[index] == '\r') index++;
        if (index < text.Length && text[index] == '\n') index++;
        return index;
    }

    private static ConfigValue Remove(ConfigValue tree, IReadOnlyList<string> path, int index)
    {
        if (tree is not ConfigValue.Object fields) return tree;
        if (index == path.Count - 1)
            return ConfigValue.NewObject(fields.Item.Remove(path[index]));
        if (!fields.Item.TryGetValue(path[index], out var child)) return tree;
        return ConfigValue.NewObject(fields.Item.Add(path[index], Remove(child, path, index + 1)));
    }

    private sealed record Statement(string[] Path, int Start, int End, int ValueStart, int ValueEnd, bool Header);

    private static List<Statement> Scan(string text)
    {
        var result = new List<Statement>();
        string[] table = [];
        var index = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        while (index < text.Length)
        {
            SkipTrivia(text, ref index);
            if (index == text.Length) break;
            var start = index;
            if (text[index] == '[')
            {
                var array = index + 1 < text.Length && text[index + 1] == '[';
                index += array ? 2 : 1;
                var keyStart = index;
                while (index < text.Length && text[index] != ']')
                {
                    if (text[index] is '\'' or '"') SkipString(text, ref index);
                    else index++;
                }
                table = ParseKey(text[keyStart..index]);
                index += array ? 2 : 1;
                result.Add(new(table, start, index, index, index, true));
            }
            else
            {
                var keyStart = index;
                while (index < text.Length && text[index] != '=')
                {
                    if (text[index] is '\'' or '"') SkipString(text, ref index);
                    else index++;
                }
                var path = table.Concat(ParseKey(text[keyStart..index])).ToArray();
                index++;
                while (index < text.Length && text[index] is ' ' or '\t') index++;
                var valueStart = index;
                var depth = 0;
                while (index < text.Length)
                {
                    var character = text[index];
                    if (character is '\'' or '"') SkipString(text, ref index);
                    else if (character is '[' or '{') { depth++; index++; }
                    else if (character is ']' or '}') { depth--; index++; }
                    else if (character == '#')
                    {
                        if (depth == 0) break;
                        while (index < text.Length && text[index] is not '\r' and not '\n') index++;
                    }
                    else if (depth == 0 && character is '\r' or '\n') break;
                    else index++;
                }
                var valueEnd = index;
                while (valueEnd > valueStart && text[valueEnd - 1] is ' ' or '\t') valueEnd--;
                result.Add(new(path, start, valueEnd, valueStart, valueEnd, false));
            }
        }
        return result;
    }

    private static void SkipTrivia(string text, ref int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index])) index++;
            else if (text[index] == '#')
                while (index < text.Length && text[index] is not '\r' and not '\n') index++;
            else break;
        }
    }

    private static void SkipString(string text, ref int index)
    {
        var quote = text[index++];
        var triple = index + 1 < text.Length && text[index] == quote && text[index + 1] == quote;
        if (triple) index += 2;
        while (index < text.Length)
        {
            if (quote == '"' && text[index] == '\\') { index += 2; continue; }
            if (text[index] == quote)
            {
                if (!triple) { index++; return; }
                if (index + 2 < text.Length && text[index + 1] == quote && text[index + 2] == quote)
                {
                    index += 3;
                    for (var extra = 0; extra < 2 && index < text.Length && text[index] == quote; extra++) index++;
                    return;
                }
            }
            index++;
        }
    }

    private static string[] ParseKey(string key)
    {
        // Let the pinned reader decode TOML literal/basic key escapes and dotted segments.
        var tree = TomlReader.Parse(key + " = 0").Value;
        var result = new List<string>();
        while (tree is ConfigValue.Object fields && fields.Item.Count == 1)
        {
            var field = fields.Item.Single();
            result.Add(field.Key);
            tree = field.Value;
        }
        return result.ToArray();
    }

    private static string Render(ConfigValue value) => value switch
    {
        ConfigValue.String text => Quote(text.Item),
        ConfigValue.Boolean boolean => boolean.Item ? "true" : "false",
        ConfigValue.Integer integer => integer.Item.ToString(CultureInfo.InvariantCulture),
        ConfigValue.Number number when double.IsNaN(number.Item) => "nan",
        ConfigValue.Number number when double.IsPositiveInfinity(number.Item) => "+inf",
        ConfigValue.Number number when double.IsNegativeInfinity(number.Item) => "-inf",
        ConfigValue.Number number => RenderNumber(number.Item),
        ConfigValue.Array array => "[" + string.Join(", ", array.Item.Select(Render)) + "]",
        ConfigValue.Object fields => "{ " + string.Join(", ", fields.Item.Select(pair => Key(pair.Key) + " = " + Render(pair.Value))) + " }",
        ConfigValue.SecretReference fields => Render(ConfigValue.NewObject(fields.Item)),
        _ => throw new ArgumentException("Value cannot be represented by the accepted TOML profile.")
    };

    private static string RenderNumber(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
    }

    private static string Key(string key) => Quote(key);

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        foreach (var character in value)
        {
            result.Append(character switch
            {
                '\\' => "\\\\",
                '\"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                < ' ' or '\u007F' => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        }
        return result.Append('\"').ToString();
    }
}
