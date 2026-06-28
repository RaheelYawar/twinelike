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

      int count = (int)cd;
      var sb = new StringBuilder(s.Length * count);
      for (int i = 0; i < count; i++) sb.Append(s);
      return HarloweValue.OfString(sb.ToString());
    }
  }
}
