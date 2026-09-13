#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WorldGen.App.Serialization
{
    public sealed class JsonFormatException : FormatException
    {
        /// <summary>0-alapú karakterpozíció.</summary>
        public int Position { get; }

        /// <summary>1-alapú sor és oszlop.</summary>
        public int Line { get; }
        public int Column { get; }

        public JsonFormatException(string message, int position, int line, int column)
            : base(message + " (sor " + line.ToString(CultureInfo.InvariantCulture) + ", oszlop "
                + column.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Position = position;
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// Szigorú RFC 8259 JSON-olvasó (ND-106): nincs komment, nincs záró vessző,
    /// nincs NaN/Infinity, nincs duplikált kulcs. Egy kezdő U+FEFF (BOM) megengedett.
    /// </summary>
    public static class JsonParser
    {
        public const int DefaultMaxDepth = 64;

        public static JsonValue Parse(string text, int maxDepth = DefaultMaxDepth)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            var reader = new Reader(text, maxDepth);
            return reader.ParseDocument();
        }

        public static bool TryParse(string text, out JsonValue? value, out string? error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonFormatException ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
        }

        private sealed class Reader
        {
            private readonly string _s;
            private readonly int _maxDepth;
            private int _pos;
            private int _depth;

            public Reader(string s, int maxDepth)
            {
                _s = s;
                _maxDepth = maxDepth;
            }

            public JsonValue ParseDocument()
            {
                if (_s.Length > 0 && _s[0] == '﻿') _pos = 1;
                SkipWhitespace();
                var value = ParseValue();
                SkipWhitespace();
                if (_pos != _s.Length) throw Error("Váratlan tartalom a JSON-érték után");
                return value;
            }

            private JsonValue ParseValue()
            {
                if (_pos >= _s.Length) throw Error("Váratlan szövegvég, értéket vártam");
                char c = _s[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return JsonValue.FromString(ParseString());
                    case 't': ExpectLiteral("true"); return JsonValue.True;
                    case 'f': ExpectLiteral("false"); return JsonValue.False;
                    case 'n': ExpectLiteral("null"); return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("Érvénytelen karakter: '" + c + "'");
                }
            }

            private JsonValue ParseObject()
            {
                EnterNesting();
                _pos++; // {
                var obj = JsonValue.CreateObject();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _pos++;
                    _depth--;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("Idézőjeles kulcsot vártam");
                    int keyPos = _pos;
                    string key = ParseString();
                    if (!seen.Add(key)) throw ErrorAt("Duplikált kulcs: '" + key + "'", keyPos);
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("':' karaktert vártam");
                    _pos++;
                    SkipWhitespace();
                    obj.Set(key, ParseValue());
                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == '}')
                    {
                        _pos++;
                        break;
                    }
                    throw Error("',' vagy '}' karaktert vártam");
                }
                _depth--;
                return obj;
            }

            private JsonValue ParseArray()
            {
                EnterNesting();
                _pos++; // [
                var array = JsonValue.CreateArray();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    _depth--;
                    return array;
                }
                while (true)
                {
                    SkipWhitespace();
                    array.Add(ParseValue());
                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == ']')
                    {
                        _pos++;
                        break;
                    }
                    throw Error("',' vagy ']' karaktert vártam");
                }
                _depth--;
                return array;
            }

            private string ParseString()
            {
                _pos++; // nyitó "
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _s.Length) throw Error("Lezáratlan string");
                    char c = _s[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw ErrorAt("Vezérlőkarakter stringben (escape kell)", _pos - 1);
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (_pos >= _s.Length) throw Error("Lezáratlan escape");
                    char e = _s[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _s.Length) throw Error("Csonka \\u escape");
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                int h = HexValue(_s[_pos + i]);
                                if (h < 0) throw ErrorAt("Érvénytelen hexa számjegy a \\u escape-ben", _pos + i);
                                code = code * 16 + h;
                            }
                            _pos += 4;
                            sb.Append((char)code);
                            break;
                        default:
                            throw ErrorAt("Ismeretlen escape: \\" + e, _pos - 1);
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = _pos;
                if (Peek() == '-') _pos++;
                if (_pos >= _s.Length) throw Error("Csonka szám");
                if (_s[_pos] == '0')
                {
                    _pos++;
                }
                else if (_s[_pos] >= '1' && _s[_pos] <= '9')
                {
                    while (_pos < _s.Length && IsDigit(_s[_pos])) _pos++;
                }
                else
                {
                    throw Error("Számjegyet vártam");
                }

                if (_pos < _s.Length && _s[_pos] == '.')
                {
                    _pos++;
                    if (_pos >= _s.Length || !IsDigit(_s[_pos])) throw Error("Számjegyet vártam a tizedespont után");
                    while (_pos < _s.Length && IsDigit(_s[_pos])) _pos++;
                }

                if (_pos < _s.Length && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    _pos++;
                    if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
                    if (_pos >= _s.Length || !IsDigit(_s[_pos])) throw Error("Számjegyet vártam a kitevőben");
                    while (_pos < _s.Length && IsDigit(_s[_pos])) _pos++;
                }

                return JsonValue.FromValidatedNumberText(_s.Substring(start, _pos - start));
            }

            private void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(_s, _pos, literal, 0, literal.Length) != 0 || _pos + literal.Length > _s.Length)
                    throw Error("Érvénytelen literál, ezt vártam: " + literal);
                _pos += literal.Length;
            }

            private void EnterNesting()
            {
                _depth++;
                if (_depth > _maxDepth) throw Error("Túl mély egymásba ágyazás (max " + _maxDepth.ToString(CultureInfo.InvariantCulture) + ")");
            }

            private void SkipWhitespace()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
                    _pos++;
                }
            }

            private char Peek() => _pos < _s.Length ? _s[_pos] : '\0';

            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private JsonFormatException Error(string message) => ErrorAt(message, _pos);

            private JsonFormatException ErrorAt(string message, int position)
            {
                int line = 1;
                int column = 1;
                int end = Math.Min(position, _s.Length);
                for (int i = 0; i < end; i++)
                {
                    if (_s[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }
                return new JsonFormatException(message, position, line, column);
            }
        }
    }
}
