using System;
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
  /// <para>Whitespace is .NET <c>char.IsWhiteSpace</c> — a <em>near</em>-match for
  /// reference's <c>realWhitespace</c>, additionally splitting on U+0085 (NEL) and
  /// U+1680 (Ogham space mark). A documented, deterministic divergence.</para>
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

      var parts = v.AsString.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
      var items = new List<HarloweValue>(parts.Length);
      for (int i = 0; i < parts.Length; i++) items.Add(HarloweValue.OfString(parts[i]));
      return HarloweValue.OfArray(items);
    }
  }
}
