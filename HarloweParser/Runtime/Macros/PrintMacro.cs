using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(print: $value)</c>. Coerces its single argument to its
  /// <see cref="HarloweValue.ToHarloweString"/> form and returns it as a
  /// <see cref="HarloweValueKind.String"/>. The body renderer writes that
  /// string into the output stream.
  /// </summary>
  public class PrintMacro : IMacro
  {
    public string Name => "print";
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
