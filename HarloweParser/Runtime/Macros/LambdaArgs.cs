using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>Shared helper for macros that take a run of items.</summary>
  internal static class LambdaArgs
  {
    /// <summary>
    /// The items of a variadic macro: every argument from
    /// <paramref name="startIndex"/> on. A <c>...</c> spread has already been
    /// expanded by the evaluator, so an array that arrives here is one item,
    /// as in reference: <c>(for: each _x, $list)</c> loops once.
    /// </summary>
    public static List<HarloweValue> Items(List<HarloweValue> args, int startIndex)
      => args.GetRange(startIndex, args.Count - startIndex);
  }
}
