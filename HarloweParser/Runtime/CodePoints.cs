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
  }
}
