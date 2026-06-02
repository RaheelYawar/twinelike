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

      // Validate bounds inside the runtime-error contract: a NaN/infinity or
      // a fractional / out-of-Int32-range bound would otherwise either throw
      // (Random.Next on int.MaxValue + 1 overflows) or silently truncate via
      // the (int) cast.
      int lo, hi;
      if (args.Count == 1)
      {
        lo = 0;
        if (!TryAsBound(args[0].AsNumber, out hi))
          return HarloweValue.OfError("(random:) requires a finite number argument");
      }
      else
      {
        if (!TryAsBound(args[0].AsNumber, out lo) || !TryAsBound(args[1].AsNumber, out hi))
          return HarloweValue.OfError("(random:) requires finite number arguments");
      }
      if (lo > hi) { int tmp = lo; lo = hi; hi = tmp; }

      // Random.Next(lo, hi + 1) overflows on hi == int.MaxValue. Detect and
      // surface as an in-prose error rather than letting OverflowException
      // escape the runtime contract.
      if (hi == int.MaxValue)
        return HarloweValue.OfError("(random:) upper bound is too large");

      var rng = context.Rng ?? new Random();
      return HarloweValue.OfNumber(rng.Next(lo, hi + 1));
    }

    /// <summary>
    /// Coerce <paramref name="d"/> to an Int32 bound. A fractional value is
    /// truncated toward zero — matching reference Harlowe's <c>parseInt</c>
    /// argument coercion, so <c>(random: 1.5, 6.5)</c> behaves like
    /// <c>(random: 1, 6)</c>. Returns false only on NaN/infinity or a value
    /// outside Int32 range, so the <c>(int)</c> cast a successful return
    /// precedes is always safe.
    /// </summary>
    private static bool TryAsBound(double d, out int result)
    {
      result = 0;
      if (double.IsNaN(d) || double.IsInfinity(d)) return false;
      d = Math.Truncate(d);
      if (d < int.MinValue || d > int.MaxValue) return false;
      result = (int)d;
      return true;
    }
  }
}
