using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO;

/// <summary>JSON5 1.0 grammar, with duplicate rejection and bounded nesting for configuration documents.</summary>
internal sealed partial class Json5Reader(string text)
{
    private readonly List<Tuple<FSharpList<string>, SourcePosition>> _positions = [];
    private int _offset;
    private int _line = 1;
    private int _column = 1;

    internal static ParsedTree Parse(string text)
    {
        var parser = new Json5Reader(text);
        var value = parser.Value([], 0);
        parser.Trivia();
        if (!parser.End) throw parser.Error("Unexpected content after the document", []);
        return new ParsedTree(value, parser._positions);
    }

    private bool End => _offset >= text.Length;
    private char Current => End ? '\0' : text[_offset];
    private SourcePosition Position => new(_line, _column);

    private char Advance()
    {
        var value = text[_offset++];
        if (value is '\r' or '\u2028' or '\u2029') { _line++; _column = 1; }
        else if (value == '\n')
        {
            if (_offset < 2 || text[_offset - 2] != '\r') _line++;
            _column = 1;
        }
        else _column++;
        return value;
    }

    private ReaderException Error(string message, string[] path, string code = "config.parse_error") =>
        new(code, message, path, Position);

    private static bool White(char value) => value is '\t' or '\v' or '\f' or '\r' or '\n' or '\u2028' or '\u2029' or '\uFEFF'
        || char.GetUnicodeCategory(value) == UnicodeCategory.SpaceSeparator;

    private void Trivia()
    {
        while (!End)
        {
            if (White(Current)) { Advance(); continue; }
            if (Current != '/' || _offset + 1 >= text.Length) return;
            var next = text[_offset + 1];
            if (next == '/')
            {
                Advance(); Advance();
                while (!End && Current is not ('\r' or '\n' or '\u2028' or '\u2029')) Advance();
            }
            else if (next == '*')
            {
                Advance(); Advance();
                while (!End && !(Current == '*' && _offset + 1 < text.Length && text[_offset + 1] == '/')) Advance();
                if (End) throw Error("Unterminated block comment", []);
                Advance(); Advance();
            }
            else return;
        }
    }

    private void Expect(char expected, string[] path)
    {
        Trivia();
        if (End || Current != expected) throw Error($"Expected '{expected}'", path);
        Advance();
    }

    private ConfigValue Value(string[] path, int depth)
    {
        Trivia();
        if (depth > 128) throw Error("Configuration nesting exceeds 128 levels", path, "config.depth_limit");
        if (End) throw Error("Expected a value", path);
        _positions.Add(Tuple.Create(ReaderValues.List(path), Position));
        if (Current == '{') return Object(path, depth);
        if (Current == '[') return Array(path, depth);
        if (Current is '\'' or '"') return ConfigValue.NewString(String(path));
        var start = _offset;
        while (!End && !White(Current) && Current is not (',' or '}' or ']' or '/')) Advance();
        var token = text[start.._offset];
        return token switch
        {
            "true" => ConfigValue.NewBoolean(true),
            "false" => ConfigValue.NewBoolean(false),
            "null" => ConfigValue.Null,
            _ => Number(token, path)
        };
    }

    private ConfigValue Object(string[] path, int depth)
    {
        Advance(); Trivia();
        var values = new Dictionary<string, ConfigValue>(StringComparer.Ordinal);
        if (!End && Current == '}') { Advance(); return ReaderValues.Object(values); }
        while (true)
        {
            Trivia();
            var position = Position;
            var key = Current is '\'' or '"' ? String(path) : Identifier(path);
            var child = path.Append(key).ToArray();
            if (values.ContainsKey(key))
                throw new ReaderException("config.duplicate_key", $"Duplicate key '{key}'", child, position);
            Expect(':', child);
            values.Add(key, Value(child, depth + 1));
            Trivia();
            if (!End && Current == '}') { Advance(); break; }
            Expect(',', path); Trivia();
            if (!End && Current == '}') { Advance(); break; }
        }
        return ReaderValues.Object(values);
    }

    private ConfigValue Array(string[] path, int depth)
    {
        Advance(); Trivia();
        var values = new List<ConfigValue>();
        if (!End && Current == ']') { Advance(); return ConfigValue.NewArray(ReaderValues.List(values)); }
        while (true)
        {
            values.Add(Value(path.Append(values.Count.ToString(CultureInfo.InvariantCulture)).ToArray(), depth + 1));
            Trivia();
            if (!End && Current == ']') { Advance(); break; }
            Expect(',', path); Trivia();
            if (!End && Current == ']') { Advance(); break; }
        }
        return ConfigValue.NewArray(ReaderValues.List(values));
    }

    private static bool IdentifierStart(Rune value) => value.Value is '$' or '_'
        || Rune.GetUnicodeCategory(value) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;

    private static bool IdentifierPart(Rune value) => IdentifierStart(value) || value.Value is 0x200C or 0x200D
        || Rune.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation;

    private string Identifier(string[] path)
    {
        var builder = new StringBuilder();
        var first = true;
        while (!End)
        {
            Rune value;
            if (Current == '\\')
            {
                Advance();
                if (End || Advance() != 'u') throw Error("Identifier escape must use \\u followed by four hex digits", path);
                var character = Hex(4, path);
                if (!Rune.TryCreate(character, out value)) throw Error("Invalid Unicode identifier escape", path);
            }
            else
            {
                if (!Rune.TryGetRuneAt(text, _offset, out value)) break;
                if (!(first ? IdentifierStart(value) : IdentifierPart(value))) break;
                for (var i = 0; i < value.Utf16SequenceLength; i++) Advance();
            }
            if (!(first ? IdentifierStart(value) : IdentifierPart(value))) throw Error("Invalid identifier character", path);
            builder.Append(value.ToString());
            first = false;
        }
        if (first) throw Error("Expected an object key", path);
        return builder.ToString();
    }

    private char Hex(int count, string[] path)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            if (End || !Uri.IsHexDigit(Current)) throw Error($"Expected {count} hexadecimal digits", path);
            var digit = Advance();
            value = value * 16 + (digit <= '9' ? digit - '0' : char.ToUpperInvariant(digit) - 'A' + 10);
        }
        return (char)value;
    }

    private string String(string[] path)
    {
        var quote = Advance();
        var builder = new StringBuilder();
        while (!End)
        {
            var value = Advance();
            if (value == quote) return builder.ToString();
            if (value is '\r' or '\n') throw Error("Unescaped line break in string", path);
            if (value != '\\') { builder.Append(value); continue; }
            if (End) throw Error("Unterminated string escape", path);
            var escape = Advance();
            if (escape is '\r' or '\n' or '\u2028' or '\u2029')
            {
                if (escape == '\r' && !End && Current == '\n') Advance();
                continue;
            }
            if (escape is >= '1' and <= '9' || escape == '0' && !End && char.IsAsciiDigit(Current))
                throw Error("Decimal and octal string escapes are not JSON5", path);
            builder.Append(escape switch
            {
                'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', 'v' => '\v', '0' => '\0',
                'x' => Hex(2, path), 'u' => Hex(4, path), _ => escape
            });
        }
        throw Error("Unterminated string", path);
    }

    private ConfigValue Number(string token, string[] path)
    {
        if (!NumberGrammar().IsMatch(token)) throw Error($"Invalid JSON5 value '{token}'", path);
        var unsigned = token.TrimStart('+', '-');
        var sign = token.StartsWith('-') ? -1 : 1;
        if (unsigned == "Infinity") return ConfigValue.NewNumber(sign * double.PositiveInfinity);
        if (unsigned == "NaN") return ConfigValue.NewNumber(double.NaN);
        if (unsigned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var integer = BigInteger.Parse("0" + unsigned[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) * sign;
            return integer >= long.MinValue && integer <= long.MaxValue
                ? ConfigValue.NewInteger((long)integer) : ConfigValue.NewBigInteger(integer);
        }
        if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            return ConfigValue.NewInteger(value);
        if (!token.Contains('.') && !token.Contains('e', StringComparison.OrdinalIgnoreCase)
            && BigInteger.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var large))
            return ConfigValue.NewBigInteger(large);
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return ConfigValue.NewNumber(number);
        throw Error($"Invalid JSON5 number '{token}'", path);
    }

    [GeneratedRegex(@"^[+-]?(?:Infinity|NaN|0[xX][0-9a-fA-F]+|(?:(?:0|[1-9][0-9]*)(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberGrammar();
}
