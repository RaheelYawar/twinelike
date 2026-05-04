using System;
using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(random: 1, 6)</c> or <c>(random: 6)</c>. Returns a random integer in
  /// the inclusive range. With one argument <c>n</c>, the range is
  /// <c>[0, n]</c>; with two arguments it's <c>[a, b]</c>. Uses
  /// <see cref="MacroContext.Rng"/> so tests can seed for determinism.
  /// </summary>
  public class RandomMacro : IMacro
  {
    public string Name => "random";
    public int MinArgs => 1;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      for (int i = 0; i < args.Count; i++)
      {
        if (args[i].IsError) return args[i];
        if (args[i].Kind != HarloweValueKind.Number)
          return HarloweValue.OfError($"(random:) requires Number arguments, got {args[i].Kind}");
      }

      int lo, hi;
      if (args.Count == 1) { lo = 0; hi = (int)args[0].AsNumber; }
      else { lo = (int)args[0].AsNumber; hi = (int)args[1].AsNumber; }
      if (lo > hi) { int tmp = lo; lo = hi; hi = tmp; }

      var rng = context.Rng ?? new Random();
      return HarloweValue.OfNumber(rng.Next(lo, hi + 1));
    }
  }
}
