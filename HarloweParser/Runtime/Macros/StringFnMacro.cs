using System;
using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// A single-argument <c>String → String</c> macro wrapping one transform:
  /// <c>(uppercase:)</c>, <c>(lowercase:)</c>, <c>(str-reversed:)</c>/
  /// <c>(string-reversed:)</c>, <c>(upperfirst:)</c>, and <c>(lowerfirst:)</c>
  /// (reference <c>ts/macrolib/values.ts</c>). One String in, one String out;
  /// the transform is supplied at construction (mirrors <see cref="MathMacro"/>
  /// for numbers).
  /// </summary>
  public class StringFnMacro : IMacro
  {
    private readonly string _name;
    private readonly Func<string, string> _fn;

    /// <param name="name">The registered macro name (e.g. <c>uppercase</c>).</param>
    /// <param name="fn">The string→string transform.</param>
    public StringFnMacro(string name, Func<string, string> fn) { _name = name; _fn = fn; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a String, got {v.Kind}");
      return HarloweValue.OfString(_fn(v.AsString));
    }

    /// <summary>Reverse a string by Unicode code point (surrogate-pair safe).</summary>
    public static string Reverse(string s)
    {
      var cps = CodePoints.Split(s);
      var sb = new StringBuilder(s.Length);
      for (int i = cps.Count - 1; i >= 0; i--) sb.Append(cps[i]);
      return sb.ToString();
    }

    /// <summary>
    /// Case only the <em>first alphanumeric</em> code point (leaving every other
    /// character as-is); a digit is a no-op. <paramref name="toUpper"/> picks
    /// upper vs lower. Deliberately first-alphanumeric-only — diverging from
    /// reference, which title-cases the whole first word — which is more
    /// intuitive for names/acronyms (<c>"McDonald"</c> stays <c>"McDonald"</c>),
    /// while still matching reference's <c>(upperfirst:"4ever")→"4ever"</c> (a
    /// leading digit can't change case). <c>char.IsLetterOrDigit</c> is
    /// full-Unicode where reference's <c>anyRealLetter</c> is ASCII-Latin-centric
    /// — a documented divergence (cf. <c>(sorted:)</c> ordinal ordering).
    /// </summary>
    public static string CaseFirst(string s, bool toUpper)
    {
      var cps = CodePoints.Split(s);
      for (int i = 0; i < cps.Count; i++)
      {
        if (!char.IsLetterOrDigit(cps[i], 0)) continue;
        cps[i] = toUpper ? cps[i].ToUpperInvariant() : cps[i].ToLowerInvariant();
        break;
      }
      var sb = new StringBuilder(s.Length);
      for (int i = 0; i < cps.Count; i++) sb.Append(cps[i]);
      return sb.ToString();
    }
  }
}
