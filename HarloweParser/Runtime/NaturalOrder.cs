using System;
using System.Collections.Generic;
using System.Globalization;

namespace Harlowe.Runtime
{
  /// <summary>
  /// The order reference runs tagged passages in: <c>NaturalSort("en")</c> from
  /// <c>ts/utils/naturalsort.ts</c>. Names split into runs of digits and other
  /// text, numbers compare by value and come before text, and text compares like
  /// an English collator: punctuation, then digits, then letters regardless of
  /// case, with lowercase first between otherwise equal strings.
  ///
  /// <para>The collation is written out rather than taken from the runtime,
  /// because a host's culture data varies and this order decides which
  /// <c>startup</c> passage defines a macro first. It covers ASCII exactly.
  /// Other letters sort after <c>z</c> by code point, where a real collator
  /// would put <c>é</c> beside <c>e</c>. Reference's date and hex forms aren't
  /// implemented.</para>
  /// </summary>
  internal static class NaturalOrder
  {
    // Root collation order of ASCII punctuation and symbols.
    private const string PunctuationOrder = "_-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$";

    public static int Compare(string left, string right)
    {
      var a = Chunks((left ?? string.Empty).Trim());
      var b = Chunks((right ?? string.Empty).Trim());
      int count = Math.Max(a.Count, b.Count);
      for (int i = 0; i < count; i++)
      {
        string x = i < a.Count ? a[i] : string.Empty;
        string y = i < b.Count ? b[i] : string.Empty;
        bool xIsNumber = ReadsAsNumber(x, out double xValue);
        bool yIsNumber = ReadsAsNumber(y, out double yValue);

        // JS isNaN: a string like "007" still counts as numeric here.
        bool xIsNaN = !xIsNumber && !LooksNumeric(x);
        bool yIsNaN = !yIsNumber && !LooksNumeric(y);
        if (xIsNaN != yIsNaN) return xIsNaN ? 1 : -1;

        int order;
        if (xIsNumber && yIsNumber)
        {
          order = xValue.CompareTo(yValue);
        }
        else if (xIsNumber != yIsNumber)
        {
          // Mixed types are compared as plain strings.
          order = string.CompareOrdinal(
            xIsNumber ? HarloweValue.FormatNumber(xValue) : x,
            yIsNumber ? HarloweValue.FormatNumber(yValue) : y);
        }
        else
        {
          order = Collate(x, y);
          if (order == 0) order = string.CompareOrdinal(x, y);
        }
        if (order != 0) return order < 0 ? -1 : 1;
      }
      return 0;
    }

    /// <summary>A whole name that's one number stays one chunk. Otherwise it splits into digit runs and the text between them.</summary>
    private static List<string> Chunks(string name)
    {
      var chunks = new List<string>();
      if (name.Length == 0) return chunks;
      if (IsWholeNumber(name))
      {
        chunks.Add(name);
        return chunks;
      }
      int start = 0;
      while (start < name.Length)
      {
        bool digits = IsDigit(name[start]);
        int end = start + 1;
        while (end < name.Length && IsDigit(name[end]) == digits) end++;
        chunks.Add(name.Substring(start, end - start));
        start = end;
      }
      return chunks;
    }

    /// <summary>Reference's whole-string number form, <c>^-?[0-9]+(\.?[0-9]*)[df]?e?[0-9]?$</c>.</summary>
    private static bool IsWholeNumber(string s)
    {
      int p = 0;
      if (s[p] == '-') p++;
      int digitsStart = p;
      while (p < s.Length && IsDigit(s[p])) p++;
      if (p == digitsStart) return false;
      if (p < s.Length && s[p] == '.') p++;
      while (p < s.Length && IsDigit(s[p])) p++;
      if (p < s.Length && (s[p] == 'd' || s[p] == 'f' || s[p] == 'D' || s[p] == 'F')) p++;
      if (p < s.Length && (s[p] == 'e' || s[p] == 'E')) p++;
      if (p < s.Length && IsDigit(s[p])) p++;
      return p == s.Length;
    }

    /// <summary>
    /// Reference's <c>!x.match(/^0/) &amp;&amp; parseFloat(x) || x || 0</c>: an empty chunk
    /// is 0, one with a leading zero stays text, and so does one that parses to 0.
    /// </summary>
    private static bool ReadsAsNumber(string chunk, out double value)
    {
      value = 0;
      if (chunk.Length == 0) return true;
      if (chunk[0] == '0') return false;
      int end = NumberPrefixLength(chunk);
      if (end == 0) return false;
      if (!double.TryParse(chunk.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
      return value != 0;
    }

    /// <summary>The length of the number <c>parseFloat</c> would read from the start of <paramref name="s"/>, or 0.</summary>
    private static int NumberPrefixLength(string s)
    {
      int p = 0;
      if (s[p] == '-' || s[p] == '+') p++;
      int digits = 0;
      while (p < s.Length && IsDigit(s[p])) { p++; digits++; }
      if (p < s.Length && s[p] == '.')
      {
        int q = p + 1;
        int fraction = 0;
        while (q < s.Length && IsDigit(s[q])) { q++; fraction++; }
        if (digits + fraction > 0) { p = q; digits += fraction; }
      }
      if (digits == 0) return 0;
      if (p < s.Length && (s[p] == 'e' || s[p] == 'E'))
      {
        int q = p + 1;
        if (q < s.Length && (s[q] == '-' || s[q] == '+')) q++;
        int exponent = q;
        while (q < s.Length && IsDigit(s[q])) q++;
        if (q > exponent) p = q;
      }
      return p;
    }

    private static bool LooksNumeric(string s)
    {
      string trimmed = s.Trim();
      return trimmed.Length == 0
        || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static int Collate(string x, string y)
    {
      int shared = Math.Min(x.Length, y.Length);
      for (int i = 0; i < shared; i++)
      {
        int difference = PrimaryWeight(x[i]) - PrimaryWeight(y[i]);
        if (difference != 0) return difference < 0 ? -1 : 1;
      }
      if (x.Length != y.Length) return x.Length < y.Length ? -1 : 1;
      for (int i = 0; i < shared; i++)
      {
        if (x[i] != y[i] && char.ToLowerInvariant(x[i]) == char.ToLowerInvariant(y[i]))
          return char.IsLower(x[i]) ? -1 : 1;
      }
      return 0;
    }

    private static int PrimaryWeight(char c)
    {
      if (char.IsWhiteSpace(c)) return 1;
      int punctuation = PunctuationOrder.IndexOf(c);
      if (punctuation >= 0) return 0x100 + punctuation;
      if (IsDigit(c)) return 0x200 + (c - '0');
      return 0x1000 + char.ToLowerInvariant(c);
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';
  }
}
