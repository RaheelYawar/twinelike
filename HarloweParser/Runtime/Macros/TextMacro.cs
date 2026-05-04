using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text: 5)</c> → <c>"5"</c>. Coerces its single argument to its
  /// <see cref="HarloweValue.ToHarloweString"/> form and returns the result
  /// as a <see cref="HarloweValueKind.String"/>. Useful for forcing string
  /// concatenation: <c>"You have " + (text: $hp) + " HP"</c>.
  /// </summary>
  public class TextMacro : IMacro
  {
    public string Name => "text";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      return HarloweValue.OfString(v.ToHarloweString());
    }
  }
}
