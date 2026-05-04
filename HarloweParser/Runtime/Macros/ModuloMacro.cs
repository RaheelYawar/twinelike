using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(modulo: 7, 3)</c> → <c>1</c>. Two-argument numeric modulo. Returns
  /// an error on a divisor of zero rather than throwing.
  /// </summary>
  public class ModuloMacro : IMacro
  {
    public string Name => "modulo";
    public int MinArgs => 2;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].IsError) return args[0];
      if (args[1].IsError) return args[1];
      if (args[0].Kind != HarloweValueKind.Number || args[1].Kind != HarloweValueKind.Number)
        return HarloweValue.OfError("(modulo:) requires two Number arguments");
      if (args[1].AsNumber == 0.0) return HarloweValue.OfError("(modulo:) divisor is zero");
      return HarloweValue.OfNumber(args[0].AsNumber % args[1].AsNumber);
    }
  }
}
