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

      // Validate bounds inside the runtime-error contract: a NaN/infinity bound
      // would poison the draw, and a fractional bound must truncate toward zero
      // (reference's parseInt coercion) rather than bias the range. Out-of-Int32
      // bounds are rejected so the range arithmetic below stays well-defined.
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

      // Scale a [0,1) draw into [lo, hi] inclusive, matching reference's
      // ~~(State.random() * (to - from + 1)) + from (ts/macrolib/values.ts). The
      // range and offset are computed in double/long, never a narrowing (int)
      // cast: a span wider than int.MaxValue — e.g. (random: -2000000000,
      // 2000000000) — must not overflow, and (int) of a product >= 2^31 is
      // unspecified in C# (unlike JS ~~'s mod-2^32 wrap). lo + offset is in
      // [lo, hi], so it fits long (and int), and OfNumber stores it as a double.
      var rng = context.Rng ?? new MulberryRng();
      double range = (double)hi - lo + 1.0;
      return HarloweValue.OfNumber(lo + (long)(rng.NextDouble() * range));
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
