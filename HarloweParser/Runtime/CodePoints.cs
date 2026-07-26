using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Unicode code-point helpers for the code-point string model reference
  /// Harlowe uses (JS <c>[...str]</c>): a surrogate-pair (astral) character is
  /// one position, not two. Shared by <see cref="ExpressionEvaluator"/>'s string
  /// length/indexing and the string-utility macros (<c>(substring:)</c>,
  /// <c>(str-reversed:)</c>, <c>(upperfirst:)</c>/<c>(lowerfirst:)</c>) so the
  /// counting/splitting logic lives in one place.
  /// </summary>
  internal static class CodePoints
  {
    /// <summary>Counts Unicode code points (surrogate pairs as one).</summary>
    public static int Count(string s)
    {
      if (string.IsNullOrEmpty(s)) return 0;
      int count = 0;
      for (int p = 0; p < s.Length; p++)
      {
        if (char.IsHighSurrogate(s[p]) && p + 1 < s.Length && char.IsLowSurrogate(s[p + 1])) p++;
        count++;
      }
      return count;
    }

    /// <summary>
    /// Splits <paramref name="s"/> into a list of single-code-point strings —
    /// each entry is one character (a surrogate pair stays a single two-char
    /// entry). A lone surrogate is kept as its own one-char entry.
    /// </summary>
    public static List<string> Split(string s)
    {
      if (string.IsNullOrEmpty(s)) return new List<string>();
      var list = new List<string>(s.Length);
      for (int p = 0; p < s.Length; p++)
      {
        bool pair = char.IsHighSurrogate(s[p]) && p + 1 < s.Length && char.IsLowSurrogate(s[p + 1]);
        list.Add(pair ? s.Substring(p, 2) : s[p].ToString());
        if (pair) p++;
      }
      return list;
    }

    /// <summary>
    /// Reference's <c>realWhitespace</c> class in <c>ts/utils.ts</c>: "all forms
    /// of Unicode 6 whitespace ... except Ogham space mark" - space, the five
    /// ASCII whitespace controls, U+00A0, U+2000 through U+200A, U+2028, U+2029,
    /// U+202F, U+205F, U+3000. Spelled out rather than deferring to
    /// <see cref="char.IsWhiteSpace"/>, which also accepts U+1680 OGHAM SPACE
    /// MARK and U+0085 NEL.
    ///
    /// <para>The exotic code points are compared as numbers, not char literals:
    /// U+2028 and U+2029 are line terminators to the C# lexer, so writing them
    /// literally would truncate this file.</para>
    ///
    /// <para>Shared by the <c>whitespace</c> datatype and <c>(words:)</c>, the
    /// two places reference consults this class, so the two can't drift.</para>
    /// </summary>
    public static bool IsRealWhitespace(char c)
    {
      if (c == ' ' || c == '\n' || c == '\r' || c == '\f' || c == '\t' || c == '\v') return true;
      int u = c;
      return u == 0x00a0 || (u >= 0x2000 && u <= 0x200a)
        || u == 0x2028 || u == 0x2029 || u == 0x202f || u == 0x205f || u == 0x3000;
    }

    /// <summary>
    /// Returns <paramref name="s"/> with the code point at 1-based
    /// <paramref name="index"/> replaced by <paramref name="replacement"/>
    /// (which may be any length — the caller enforces Harlowe's
    /// one-code-point rule for single-position assignment). Used by property
    /// assignment's string-character writes. Callers bounds-check first; an
    /// out-of-range index returns <paramref name="s"/> unchanged rather than
    /// throwing, per the runtime's no-throw policy.
    /// </summary>
    public static string ReplaceAt(string s, int index, string replacement)
    {
      if (string.IsNullOrEmpty(s)) return s;
      int seen = 0;
      for (int p = 0; p < s.Length; p++)
      {
        bool pair = char.IsHighSurrogate(s[p]) && p + 1 < s.Length && char.IsLowSurrogate(s[p + 1]);
        if (++seen == index)
          return s.Substring(0, p) + replacement + s.Substring(p + (pair ? 2 : 1));
        if (pair) p++;
      }
      return s;
    }
  }
}
