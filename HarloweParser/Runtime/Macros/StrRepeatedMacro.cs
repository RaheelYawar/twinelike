using System;
using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(str-repeated: 5, "Fool! ")</c> → <c>"Fool! Fool! Fool! Fool! Fool! "</c>.
  /// Repeats a string <c>count</c> times (reference <c>ts/macrolib/values.ts</c>;
  /// registered as <c>str-repeated</c> + <c>string-repeated</c>). The count must
  /// be a non-negative whole number; the <em>empty-string check runs first</em>,
  /// so <c>(str-repeated: 0, "")</c> errors rather than returning <c>""</c>, while
  /// <c>(str-repeated: 0, "x")</c> returns <c>""</c>.
  /// </summary>
  public class StrRepeatedMacro : IMacro
  {
    private readonly string _name;

    public StrRepeatedMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 2;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var cv = args[0];
      if (cv.IsError) return cv;
      if (cv.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"({_name}:) requires a Number count, got {cv.Kind}");
      double cd = cv.AsNumber;
      if (double.IsNaN(cd) || cd != Math.Floor(cd) || cd < 0)
        return HarloweValue.OfError(
          $"({_name}:) needs a non-negative whole number of repetitions; got {HarloweValue.FormatNumber(cd)}");

      var sv = args[1];
      if (sv.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a String, got {sv.Kind}");
      string s = sv.AsString;
      // Empty-string check first, unconditionally (so count 0 + "" still errors).
      if (s.Length == 0)
        return HarloweValue.OfError("I can't repeat an empty string.");

      // Gate the result size before any int arithmetic: (int)cd on an
      // out-of-range double is an unspecified conversion, and s.Length * count
      // in int can overflow to a negative StringBuilder capacity — an exception,
      // breaching the no-throw contract. The double product is exact this far
      // below 2^53, and an infinite cd compares > the ceiling.
      if (cd * s.Length > MaxResultLength)
        return HarloweValue.OfError(
          $"({_name}:) can't produce a string longer than {MaxResultLength} characters.");

      int count = (int)cd;
      var sb = new StringBuilder(s.Length * count);
      for (int i = 0; i < count; i++) sb.Append(s);
      return HarloweValue.OfString(sb.ToString());
    }

    // Result-length ceiling: JS engines cap strings near 2^29 code units (V8's
    // 2^29 - 24), the limit reference Harlowe's str.repeat inherits — and it
    // keeps the capacity arithmetic above safely inside int range.
    private const long MaxResultLength = (1L << 29) - 24;
  }
}
