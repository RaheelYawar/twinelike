using System;
using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// A single-argument maths macro wrapping one <c>System.Math</c> function:
  /// <c>(floor:)</c>, <c>(ceil:)</c>, <c>(trunc:)</c>, <c>(abs:)</c>, and
  /// <c>(sign:)</c> (reference <c>ts/macrolib/values.ts</c>, each registered as
  /// <c>[fn, Number]</c> over a <c>Math.*</c> call). One Number in, one Number
  /// out; the transform is supplied at construction.
  ///
  /// <para><c>(round:)</c> is deliberately not one of these — its half-up
  /// rounding needs a fractional-part comparison rather than a direct
  /// <c>Math.*</c> call, so it keeps its own <see cref="RoundMacro"/>.</para>
  /// </summary>
  public class MathMacro : IMacro
  {
    private readonly string _name;
    private readonly Func<double, double> _fn;

    /// <param name="name">The registered macro name (e.g. <c>floor</c>).</param>
    /// <param name="fn">The number→number transform (e.g. <c>Math.Floor</c>).</param>
    public MathMacro(string name, Func<double, double> fn) { _name = name; _fn = fn; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"({_name}:) requires a Number, got {v.Kind}");
      return HarloweValue.OfNumber(_fn(v.AsNumber));
    }

    /// <summary>
    /// JS-compatible <c>Math.sign</c>: returns -1 / 0 / 1, and NaN for NaN.
    /// <see cref="Math.Sign(double)"/> can't be used directly — it returns an
    /// <c>int</c> and throws <see cref="ArithmeticException"/> on NaN, which
    /// would violate the no-throw render policy.
    /// </summary>
    public static double Sign(double x) => double.IsNaN(x) ? double.NaN : Math.Sign(x);
  }
}
