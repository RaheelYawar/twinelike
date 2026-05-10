using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Harlowe.Twee
{
  /// <summary>
  /// Hand-rolled counterpart to <see cref="JsonReader"/>. Emits the same
  /// closed object graph (string / double / bool / null /
  /// <see cref="Dictionary{TKey, TValue}"/> string-keyed /
  /// <see cref="List{T}"/> of object) as pretty-printed JSON with a
  /// two-space indent — matching Twine 2's <c>:: StoryData</c> emission style
  /// so cross-tool diffs stay scoped to actually-edited content.
  ///
  /// <para>Unsupported value types throw <see cref="HarloweParseException"/>
  /// rather than serializing the CLR type name, which keeps mistakes loud.
  /// Numbers use <see cref="CultureInfo.InvariantCulture"/> and emit as
  /// integers when whole-valued (so <c>1.0</c> emits as <c>1</c>) — matches
  /// what Twine 2 produces for fields like <c>zoom</c>.</para>
  /// </summary>
  public class JsonWriter
  {
    private const string Indent = "  ";

    /// <summary>Serialize <paramref name="value"/> as pretty-printed JSON. Top-level value can be any supported type.</summary>
    public string Write(object value)
    {
      var sb = new StringBuilder();
      WriteValue(sb, value, 0);
      return sb.ToString();
    }

    private void WriteValue(StringBuilder sb, object value, int depth)
    {
      if (value == null) { sb.Append("null"); return; }
      switch (value)
      {
        case string s: WriteString(sb, s); return;
        case bool b: sb.Append(b ? "true" : "false"); return;
        case double d: WriteNumber(sb, d); return;
        case int i: WriteNumber(sb, i); return;
        case long l: WriteNumber(sb, l); return;
        case Dictionary<string, object> dict: WriteObject(sb, dict, depth); return;
        case List<object> list: WriteArray(sb, list, depth); return;
        default:
          throw new HarloweParseException($"JsonWriter cannot serialize {value.GetType().Name}");
      }
    }

    private void WriteObject(StringBuilder sb, Dictionary<string, object> dict, int depth)
    {
      if (dict.Count == 0) { sb.Append("{}"); return; }
      sb.Append('{').Append('\n');
      int i = 0;
      foreach (var kv in dict)
      {
        AppendIndent(sb, depth + 1);
        WriteString(sb, kv.Key);
        sb.Append(": ");
        WriteValue(sb, kv.Value, depth + 1);
        if (i < dict.Count - 1) sb.Append(',');
        sb.Append('\n');
        i++;
      }
      AppendIndent(sb, depth);
      sb.Append('}');
    }

    private void WriteArray(StringBuilder sb, List<object> list, int depth)
    {
      if (list.Count == 0) { sb.Append("[]"); return; }
      sb.Append('[').Append('\n');
      for (int i = 0; i < list.Count; i++)
      {
        AppendIndent(sb, depth + 1);
        WriteValue(sb, list[i], depth + 1);
        if (i < list.Count - 1) sb.Append(',');
        sb.Append('\n');
      }
      AppendIndent(sb, depth);
      sb.Append(']');
    }

    /// <summary>
    /// Emits a JSON string with the standard escapes: backslash, double-quote,
    /// and the C-style control codes (<c>\b</c>/<c>\f</c>/<c>\n</c>/<c>\r</c>/<c>\t</c>).
    /// Other control characters fall back to <c>\uXXXX</c>. The forward slash
    /// is intentionally left unescaped — JSON allows it bare and it reads
    /// more naturally in IFIDs and URLs.
    /// </summary>
    private static void WriteString(StringBuilder sb, string s)
    {
      sb.Append('"');
      if (s != null)
      {
        for (int i = 0; i < s.Length; i++)
        {
          char c = s[i];
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
      }
      sb.Append('"');
    }

    /// <summary>
    /// Emits a number using invariant culture. Whole-valued doubles render
    /// without a trailing <c>.0</c> so <c>zoom: 1</c> round-trips byte-for-byte
    /// against Twine 2's output.
    /// </summary>
    private static void WriteNumber(StringBuilder sb, double d)
    {
      if (d == System.Math.Floor(d) && !double.IsInfinity(d) && d >= long.MinValue && d <= long.MaxValue)
        sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
      else
        sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteNumber(StringBuilder sb, long l)
      => sb.Append(l.ToString(CultureInfo.InvariantCulture));

    private static void AppendIndent(StringBuilder sb, int depth)
    {
      for (int i = 0; i < depth; i++) sb.Append(Indent);
    }
  }
}
