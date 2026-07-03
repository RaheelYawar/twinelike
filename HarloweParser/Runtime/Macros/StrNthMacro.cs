using System;
using System.Collections.Generic;
using System.Globalization;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(str-nth: 3)</c> → <c>"3rd"</c>. Converts a whole number to its English
  /// ordinal abbreviation (reference <c>ts/macrolib/values.ts</c>:
  /// <c>nth(parseInt(num))</c>; registered as <c>str-nth</c> + <c>string-nth</c>).
  /// The number truncates toward zero (reference's <c>parseInt</c>; its docs say
  /// "error on non-whole", but the code truncates — match the code). The sign is
  /// preserved (<c>-7 → "-7th"</c>) and the teen special-case (<c>11/12/13 → "th"</c>)
  /// is handled.
  /// </summary>
  public class StrNthMacro : IMacro
  {
    private readonly string _name;

    public StrNthMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"({_name}:) requires a Number, got {v.Kind}");
      double d = v.AsNumber;
      if (double.IsNaN(d) || double.IsInfinity(d))
        return HarloweValue.OfError($"({_name}:) requires a finite number.");

      // (long) of a double beyond long range is an unspecified conversion (and
      // differs between runtimes). 2^63 is the first double past the range on
      // both sides: (double)long.MaxValue rounds up to it, long.MinValue is exact.
      if (d >= 9223372036854775808.0 || d < -9223372036854775808.0)
        return HarloweValue.OfError($"({_name}:) can't ordinalise so large a number.");

      long n = (long)Math.Truncate(d);
      return HarloweValue.OfString(n.ToString(CultureInfo.InvariantCulture) + OrdinalSuffix(n));
    }

    /// <summary>The English ordinal suffix (<c>st</c>/<c>nd</c>/<c>rd</c>/<c>th</c>) for <paramref name="n"/>, sign-agnostic.</summary>
    private static string OrdinalSuffix(long n)
    {
      long abs = n < 0 ? -n : n;
      long lastTwo = abs % 100;
      if (lastTwo >= 11 && lastTwo <= 13) return "th";
      switch (abs % 10)
      {
        case 1: return "st";
        case 2: return "nd";
        case 3: return "rd";
        default: return "th";
      }
    }
  }
}
