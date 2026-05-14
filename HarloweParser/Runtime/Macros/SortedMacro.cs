using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(sorted: values...)</c>. Returns a new array of the given values in
  /// ascending order — numerically for numbers, ordinally for strings. The
  /// input must be all numbers or all strings; a mixed-kind input, or any
  /// value that isn't a number or string, surfaces as an in-prose error.
  ///
  /// <para>
  /// Matches stock Harlowe 3.3.8: <c>(sorted:)</c> takes only values, no
  /// lambda. Key-based sorting is done by composing with <c>(altered:)</c>.
  /// A single trailing array arg is auto-iterated (see <see cref="FindMacro"/>
  /// for the rationale), so <c>(sorted: (a: 3, 1, 2))</c> sorts the array's
  /// contents and an empty array sorts to an empty array. String ordering is
  /// ordinal — deterministic and culture-independent, which a cross-platform
  /// engine consumer needs.
  /// </para>
  /// </summary>
  public class SortedMacro : IMacro
  {
    public string Name => "sorted";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var items = LambdaArgs.ExpandItems(args, startIndex: 0);
      if (items.Count == 0)
        return HarloweValue.OfArray(new List<HarloweValue>());

      var kind = items[0].Kind;
      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        if (item.Kind != kind)
          return HarloweValue.OfError("(sorted:) can't compare values of different types");
      }
      if (kind != HarloweValueKind.Number && kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(sorted:) only orders numbers or strings, got {kind}");

      var sorted = new List<HarloweValue>(items);
      if (kind == HarloweValueKind.Number)
        sorted.Sort((a, b) => a.AsNumber.CompareTo(b.AsNumber));
      else
        sorted.Sort((a, b) => string.CompareOrdinal(a.AsString, b.AsString));

      return HarloweValue.OfArray(sorted);
    }
  }
}
