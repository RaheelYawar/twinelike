using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(opacity: 0.5)[hook]</c>. Returns a <see cref="Changer"/> that sets
  /// <see cref="StyleSpec.Opacity"/> on its wrapped hook. The argument must be
  /// in the range <c>0..1</c> inclusive; values outside that range produce an
  /// in-prose error.
  /// </summary>
  public class OpacityMacro : IMacro
  {
    public string Name => "opacity";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"(opacity:) requires a Number, got {arg.Kind}");
      var n = arg.AsNumber;
      if (n < 0.0 || n > 1.0)
        return HarloweValue.OfError($"(opacity:) requires a number between 0 and 1, got {HarloweValue.FormatNumber(n)}");
      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Opacity = n }));
    }
  }
}
