#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace WorldGen.App.Serialization
{
    /// <summary>
    /// JSON-író. Kimenete platformfüggetlen: LF sorvég, 2 szóközös behúzás,
    /// invariáns számformátum (a szám eredeti szövege). Nem-ASCII karaktert
    /// nyersen ír (a fájl UTF-8).
    /// </summary>
    public static class JsonWriter
    {
        public static string Write(JsonValue value, bool indented = true)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var sb = new StringBuilder();
            WriteValue(sb, value, indented, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, JsonValue value, bool indented, int depth)
        {
            switch (value.Kind)
            {
                case JsonValueKind.Null:
                    sb.Append("null");
                    break;
                case JsonValueKind.Boolean:
                    sb.Append(value.AsBoolean() ? "true" : "false");
                    break;
                case JsonValueKind.Number:
                    sb.Append(value.NumberText);
                    break;
                case JsonValueKind.String:
                    WriteString(sb, value.AsString());
                    break;
                case JsonValueKind.Array:
                    WriteArray(sb, value, indented, depth);
                    break;
                case JsonValueKind.Object:
                    WriteObject(sb, value, indented, depth);
                    break;
                default:
                    throw new InvalidOperationException("Ismeretlen JSON-típus: " + value.Kind);
            }
        }

        private static void WriteArray(StringBuilder sb, JsonValue array, bool indented, int depth)
        {
            var items = array.Items;
            if (items.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, indented, depth + 1);
                WriteValue(sb, items[i], indented, depth + 1);
            }
            NewLine(sb, indented, depth);
            sb.Append(']');
        }

        private static void WriteObject(StringBuilder sb, JsonValue obj, bool indented, int depth)
        {
            var members = obj.Members;
            if (members.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append('{');
            for (int i = 0; i < members.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, indented, depth + 1);
                WriteString(sb, members[i].Key);
                sb.Append(indented ? ": " : ":");
                WriteValue(sb, members[i].Value, indented, depth + 1);
            }
            NewLine(sb, indented, depth);
            sb.Append('}');
        }

        private static void NewLine(StringBuilder sb, bool indented, int depth)
        {
            if (!indented) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
