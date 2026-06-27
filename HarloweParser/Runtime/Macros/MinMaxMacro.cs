using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(min: 2, -5, 7)</c> → <c>-5</c> and <c>(max: 2, -5, 7)</c> → <c>7</c>.
  /// One parameterized class for both maths macros (reference
  /// <c>ts/macrolib/values.ts</c>: <c>min: [min, rest(Number)]</c> /
  /// <c>max: [max, rest(Number)]</c>) — they differ only in the comparison
  /// direction.
  ///
  /// <para>Variadic (one or more numbers). A single trailing Array is
  /// auto-unpacked (the project's deferred-spread convention, shared with
  /// <c>(sorted:)</c>), so <c>(min: (a:3,1,2))</c> is the min of 3, 1, 2. Every
  /// value must be a Number; a non-number — at the top level or inside the
  /// unpacked array — is an error. Any NaN makes the result NaN (matching JS
  /// <c>Math.min</c>/<c>Math.max</c>), so the result never depends on argument
  /// order.</para>
  /// </summary>
  public class MinMaxMacro : IMacro
  {
    private readonly string _name;
    private readonly bool _max;

    /// <param name="name">The registered macro name (<c>min</c> or <c>max</c>).</param>
    /// <param name="max"><c>true</c> for <c>(max:)</c> (highest), <c>false</c> for <c>(min:)</c> (lowest).</param>
    public MinMaxMacro(string name, bool max) { _name = name; _max = max; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var items = LambdaArgs.ExpandItems(args, startIndex: 0);
      if (items.Count == 0)
        return HarloweValue.OfError($"({_name}:) needs at least one number.");

      double best = 0;
      bool have = false;
      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        if (item.Kind != HarloweValueKind.Number)
          return HarloweValue.OfError($"({_name}:) requires Number arguments, got {item.Kind}");
        double n = item.AsNumber;
        // Any NaN makes the whole result NaN (matching JS Math.min/Math.max).
        // Without this, the comparison fold below silently skips a non-first NaN
        // (every comparison against NaN is false), leaking an order-dependent result.
        if (double.IsNaN(n)) return HarloweValue.OfNumber(double.NaN);
        if (!have || (_max ? n > best : n < best)) best = n;
        have = true;
      }
      return HarloweValue.OfNumber(best);
    }
  }
}
