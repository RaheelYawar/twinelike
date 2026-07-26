using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(words: "god-king Torment's peril")</c> → <c>(a: "god-king",
  /// "Torment's", "peril")</c>. Splits a string into an Array of its
  /// whitespace-separated words (reference <c>ts/macrolib/values.ts</c>:
  /// <c>split(realWhitespace+).filter(Boolean)</c>); an empty or whitespace-only
  /// string yields an empty Array.
  ///
  /// <para>Splits on <see cref="CodePoints.IsRealWhitespace"/> — reference's
  /// <c>realWhitespace</c> class exactly, shared with the <c>whitespace</c>
  /// datatype. Notably <em>not</em> .NET <c>char.IsWhiteSpace</c>, which would
  /// additionally split on U+0085 (NEL) and U+1680 (Ogham space mark).</para>
  /// </summary>
  public class WordsMacro : IMacro
  {
    public string Name => "words";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(words:) requires a String, got {v.Kind}");

      string s = v.AsString;
      var items = new List<HarloweValue>();
      int start = -1;
      for (int i = 0; i < s.Length; i++)
      {
        if (CodePoints.IsRealWhitespace(s[i]))
        {
          if (start >= 0) { items.Add(HarloweValue.OfString(s.Substring(start, i - start))); start = -1; }
        }
        else if (start < 0) start = i;
      }
      if (start >= 0) items.Add(HarloweValue.OfString(s.Substring(start)));
      return HarloweValue.OfArray(items);
    }
  }
}
