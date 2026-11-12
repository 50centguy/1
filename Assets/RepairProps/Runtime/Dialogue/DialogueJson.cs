using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace BorderRepair.RepairProps.Dialogue
{
    /// <summary>
    /// Writes the dialogue files the way they are kept in git: two-space indent, Chinese as is, and empty strings,
    /// empty arrays and nulls left out (JsonUtility reads a missing field as empty). Fields keep their declared order.
    /// Reading stays JsonUtility.FromJson.
    /// </summary>
    public static class DialogueJson
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Value(sb, value, 0);
            return sb.Append('\n').ToString();
        }

        static bool Skip(object v) =>
            v == null || (v is string s && s.Length == 0) || (v is ICollection c && c.Count == 0);

        static void Value(StringBuilder sb, object v, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: Str(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case IList list:
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        sb.Append(i == 0 ? "\n" : ",\n");
                        Indent(sb, depth + 1);
                        Value(sb, list[i], depth + 1);
                    }
                    sb.Append('\n'); Indent(sb, depth); sb.Append(']');
                    return;
            }
            sb.Append('{');
            bool first = true;
            foreach (var field in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var fv = field.GetValue(v);
                if (Skip(fv)) continue;
                sb.Append(first ? "\n" : ",\n"); first = false;
                Indent(sb, depth + 1);
                Str(sb, field.Name); sb.Append(": ");
                Value(sb, fv, depth + 1);
            }
            if (!first) { sb.Append('\n'); Indent(sb, depth); }
            sb.Append('}');
        }

        static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
