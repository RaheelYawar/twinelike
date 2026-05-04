using System.Collections.Generic;
using System.Globalization;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(num: "5")</c> → <c>5</c>. Parses a string as a number. Already-numeric
  /// input is returned unchanged. A non-string, non-number argument or an
  /// unparseable string returns an in-prose error.
  /// </summary>
  public class NumMacro : IMacro
  {
    public string Name => "num";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind == HarloweValueKind.Number) return v;
      if (v.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(num:) requires a String or Number, got {v.Kind}");
      if (!double.TryParse(v.AsString, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        return HarloweValue.OfError($"(num:) could not parse \"{v.AsString}\" as a number");
      return HarloweValue.OfNumber(n);
    }
  }
}
