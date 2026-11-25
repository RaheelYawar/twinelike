using System;
using System.Globalization;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A datamap's data name: a string or a number. Reference datamaps are
  /// JavaScript Maps, where the number <c>1</c> and the string <c>"1"</c> are
  /// different names, and numbers compare as <c>SameValueZero</c>, so <c>0</c>
  /// and <c>-0</c> are one name. A string converts implicitly, so
  /// <c>map["name"]</c> reads a string name.
  /// </summary>
  public readonly struct DataName : IEquatable<DataName>
  {
    // A string or a boxed double, in one reference, so a name is no bigger than
    // the string key it replaced. The datamap walks recurse once per level of
    // nesting, and a wider key widens every one of their frames.
    private readonly object _key;

    private DataName(object key)
    {
      _key = key;
    }

    /// <summary>True for a number name, false for a string name.</summary>
    public bool IsNumber => _key is double;

    /// <summary>The string name, or the number's printed form for a number name.</summary>
    public string Text => _key is double d ? HarloweValue.FormatNumber(d) : (string)_key ?? string.Empty;

    /// <summary>The number name. Zero for a string name.</summary>
    public double Number => _key is double d ? d : 0;

    public static DataName Of(string text) => new DataName(text ?? string.Empty);
    public static DataName Of(double number) => new DataName(number == 0 ? 0.0 : number);

    public static implicit operator DataName(string text) => Of(text);

    /// <summary>The name as a value: a String or a Number.</summary>
    public HarloweValue ToValue() => IsNumber ? HarloweValue.OfNumber(Number) : HarloweValue.OfString(Text);

    /// <summary>The name as Harlowe source: a string literal, or a number. Null for a number with no source form.</summary>
    internal string ToSource()
    {
      if (!(_key is double d)) return Twee.MarkupPrinter.StringLiteral(Text);
      if (double.IsNaN(d) || double.IsInfinity(d)) return null;
      return HarloweValue.FormatNumber(d);
    }

    /// <summary>Reference's <c>objectName</c> for this name, such as <c>the number 1</c>.</summary>
    internal string ObjectName => IsNumber ? "the number " + Text : "the string \"" + Text + "\"";

    /// <summary>
    /// The name of the other type that reference won't let share a datamap with
    /// this one: a string's JavaScript number conversion, or a number's printed
    /// form. False for a string that isn't a number, such as <c>"1st"</c>.
    /// </summary>
    internal bool TryGetCounterpart(out DataName counterpart)
    {
      if (IsNumber)
      {
        counterpart = Of(Text);
        return true;
      }
      if (JsNumbers.TryParse(Text, out double n))
      {
        counterpart = Of(n);
        return true;
      }
      counterpart = default(DataName);
      return false;
    }

    /// <summary>Orders numbers before strings, numbers by value and strings ordinally, so a printed datamap reads the same on every runtime.</summary>
    internal static int Compare(DataName a, DataName b)
    {
      if (a.IsNumber != b.IsNumber) return a.IsNumber ? -1 : 1;
      if (a.IsNumber) return a.Number.CompareTo(b.Number);
      return string.CompareOrdinal(a.Text, b.Text);
    }

    public bool Equals(DataName other)
    {
      if (_key is double a)
        return other._key is double b && (a == b || (double.IsNaN(a) && double.IsNaN(b)));
      return !(other._key is double) && string.Equals(Text, other.Text, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) => obj is DataName other && Equals(other);

    public override int GetHashCode()
    {
      if (_key is double d) return double.IsNaN(d) ? -1 : d.GetHashCode() * 31 + 7;
      return StringComparer.Ordinal.GetHashCode(Text);
    }

    public static bool operator ==(DataName a, DataName b) => a.Equals(b);
    public static bool operator !=(DataName a, DataName b) => !a.Equals(b);

    public override string ToString() => Text;
  }

  /// <summary>
  /// JavaScript's number conversion of a string, which reference uses for
  /// <c>(num:)</c> and for its datamap name checks: surrounding whitespace is
  /// ignored, an empty string is <c>0</c>, <c>Infinity</c> is accepted, and so
  /// are unsigned <c>0x</c>, <c>0o</c> and <c>0b</c> integers.
  /// </summary>
  internal static class JsNumbers
  {
    public static bool TryParse(string s, out double n)
    {
      n = 0;
      if (string.IsNullOrWhiteSpace(s)) return true;
      string t = s.Trim();
      if (t == "Infinity" || t == "+Infinity") { n = double.PositiveInfinity; return true; }
      if (t == "-Infinity") { n = double.NegativeInfinity; return true; }
      if (t.Length > 2 && t[0] == '0')
      {
        int radix = t[1] == 'x' || t[1] == 'X' ? 16 : t[1] == 'o' || t[1] == 'O' ? 8 : t[1] == 'b' || t[1] == 'B' ? 2 : 0;
        if (radix > 0) return TryParseRadix(t, radix, out n);
      }
      // .NET would also read its own culture symbols, which JavaScript doesn't.
      for (int i = 0; i < t.Length; i++)
      {
        char c = t[i];
        if (!((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')) return false;
      }
      return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n);
    }

    /// <summary>The digits after a <c>0x</c>-style prefix, in <paramref name="radix"/>. False for any other character.</summary>
    private static bool TryParseRadix(string t, int radix, out double n)
    {
      n = 0;
      for (int i = 2; i < t.Length; i++)
      {
        int digit = ColourValue.HexDigit(t[i]);
        if (digit < 0 || digit >= radix) { n = 0; return false; }
        n = n * radix + digit;
      }
      return true;
    }
  }
}
