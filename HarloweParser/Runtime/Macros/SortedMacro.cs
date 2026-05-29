using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(sorted: values...)</c>. Returns a new array of the given values in
  /// ascending order — numerically for numbers, ordinally for strings.
  ///
  /// <para>
  /// Divergences from reference Harlowe (tracked under the <c>(sorted:)</c>
  /// entry in <c>MACRO-DIVERGENCES.md</c>): reference sorts a mix of numbers
  /// and strings (numbers first) and accepts an optional leading <c>via</c>
  /// key-lambda; ours rejects mixed-kind input with an in-prose error and
  /// implements no lambda form — key-based sorting is done by composing with
  /// <c>(altered:)</c>. String ordering is ordinal rather than reference's
  /// alphanumeric/locale order; that one is deliberate, for the deterministic,
  /// culture-independent results a cross-platform engine consumer needs.
  /// </para>
  ///
  /// <para>
  /// A single trailing array arg is auto-iterated (see <see cref="FindMacro"/>
  /// for the rationale), so <c>(sorted: (a: 3, 1, 2))</c> sorts the array's
  /// contents and an empty array sorts to an empty array.
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
