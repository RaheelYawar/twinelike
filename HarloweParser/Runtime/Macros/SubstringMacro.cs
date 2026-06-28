using System;
using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(substring: "growl", 3, 5)</c> → <c>"owl"</c>. A code-point substring
  /// between two <em>inclusive</em> 1-based positions, mirroring reference
  /// Harlowe's <c>subset()</c> (<c>ts/utils/operationutils.ts</c>; the macro is
  /// registered in <c>ts/macrolib/values.ts</c> as <c>[String, parseInt, parseInt]</c>).
  ///
  /// <para>Negative positions count from the end (<c>-1</c> = last); a descending
  /// range (<c>b &lt; a</c>) is swapped; fractional positions truncate toward
  /// zero; a <c>0</c> or NaN position is an error. The slice is reference's
  /// <c>slice(a &gt; 0 ? a-1 : a, b)</c> over the code-point list, so an
  /// out-of-range start yields <c>""</c> rather than a clamped character.</para>
  /// </summary>
  public class SubstringMacro : IMacro
  {
    public string Name => "substring";
    public int MinArgs => 3;
    public int MaxArgs => 3;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var sv = args[0];
      if (sv.IsError) return sv;
      if (sv.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(substring:) requires a String first argument, got {sv.Kind}");
      if (args[1].Kind != HarloweValueKind.Number || args[2].Kind != HarloweValueKind.Number)
        return HarloweValue.OfError("(substring:) requires two Number positions.");

      // parseInt: truncate toward zero; NaN/Infinity coerce to NaN.
      double a = TruncTowardZero(args[1].AsNumber);
      double b = TruncTowardZero(args[2].AsNumber);

      // reference subset(): `!a || !b` rejects 0 or NaN. Report the offending one
      // (`${a && b}`: a when a is the falsy one, otherwise b).
      bool aBad = double.IsNaN(a) || a == 0;
      bool bBad = double.IsNaN(b) || b == 0;
      if (aBad || bBad)
      {
        double bad = aBad ? a : b;
        string badStr = double.IsNaN(bad) ? "NaN" : HarloweValue.FormatNumber(bad);
        return HarloweValue.OfError($"The substring index value must not be {badStr}.");
      }

      var cps = CodePoints.Split(sv.AsString);
      int count = cps.Count;

      // Negative → from the end, clamped toward the start (reference's max(0, len+i+1)).
      if (a < 0) a = Math.Max(0, count + a + 1);
      if (b < 0) b = Math.Max(0, count + b + 1);
      if (a > b) { double t = a; a = b; b = t; }

      // reference slice(a > 0 ? a-1 : a, b), clamped to [0, count]; empty when
      // start >= end. Stay in double until clamped so huge positions can't
      // overflow the int cast.
      double startD = a > 0 ? a - 1 : a;
      int start = (int)Math.Max(0, Math.Min(count, startD));
      int end = (int)Math.Max(0, Math.Min(count, b));
      if (start >= end) return HarloweValue.OfString(string.Empty);

      var sb = new StringBuilder();
      for (int i = start; i < end; i++) sb.Append(cps[i]);
      return HarloweValue.OfString(sb.ToString());
    }

    private static double TruncTowardZero(double x) =>
      (double.IsNaN(x) || double.IsInfinity(x)) ? double.NaN : Math.Truncate(x);
  }
}
