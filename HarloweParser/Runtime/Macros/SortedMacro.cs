using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(sorted: [via-lambda], values...)</c>. Returns a new array of the
  /// given values in ascending order — numbers (numerically) ahead of strings
  /// (ordinally), matching the mixed-value example in reference Harlowe's
  /// <c>ts/macrolib/datastructures.ts</c>: <c>(sorted: ...$a)</c> over
  /// <c>(a:'A','C','E','G',2,1)</c> produces <c>(a:1,2,"A","C","E","G")</c>.
  /// Zero values produce an empty array (reference's "As of 3.3.0" note).
  ///
  /// <para>
  /// An optional leading <c>via</c> lambda translates each value into the
  /// number-or-string key it sorts by (<c>(sorted: via its name, ...$maps)</c>),
  /// so any value kind is sortable through a lambda; without one, only
  /// numbers and strings are accepted. Equal-keyed values keep their given
  /// order — reference documents the lambda sort as stable.
  /// </para>
  ///
  /// <para>
  /// Deliberate divergence: string keys order ordinally rather than by
  /// reference's locale-collated alphanumeric NaturalSort, for the
  /// deterministic, culture-independent results a cross-platform engine
  /// consumer needs. A side-effect: reference stringifies numbers into that
  /// same sort, so a numeric string interleaves with numbers there
  /// (<c>(sorted: "1", 2)</c> keeps "1" first); ours keeps the kinds apart —
  /// every number sorts before every string.
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
    public int MinArgs => 0;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      // Optional leading key-lambda. Reference rejects anything but a pure
      // `via` lambda here ("must be a 'via' lambda" in datastructures.ts).
      LambdaValue lambda = null;
      int startIndex = 0;
      if (args.Count > 0 && args[0].Kind == HarloweValueKind.Lambda)
      {
        lambda = args[0].AsLambda;
        var node = lambda == null ? null : lambda.Node;
        if (node == null || node.ViaClause == null || node.WhereClause != null
            || node.WhenClause != null || node.MakingName != null)
          return HarloweValue.OfError("the optional lambda given to (sorted:) must be a 'via' lambda");
        startIndex = 1;
      }

      var items = LambdaArgs.ExpandItems(args, startIndex);
      if (items.Count == 0)
        return HarloweValue.OfArray(new List<HarloweValue>());

      // One sort key per item: the item itself, or the via-lambda's product.
      var keys = new HarloweValue[items.Count];
      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        var key = lambda == null ? item : LambdaInvoker.EvalTransform(lambda, item, i + 1, context);
        if (key.IsError) return key;
        if (key.Kind != HarloweValueKind.Number && key.Kind != HarloweValueKind.String)
          return HarloweValue.OfError(lambda == null
            ? $"if (sorted:) isn't given a 'via' lambda, it must be given only numbers and strings, got {key.Kind}"
            : $"the 'via' lambda given to (sorted:) must produce a number or string for every value, got {key.Kind}");
        keys[i] = key;
      }

      // Sort indices rather than values, tie-breaking on the original index:
      // equal-keyed values keep their given order without needing a stable
      // sort algorithm under the (unstable) BCL sort.
      var order = new int[items.Count];
      for (int i = 0; i < order.Length; i++) order[i] = i;
      System.Array.Sort(order, (a, b) =>
      {
        int c = CompareKeys(keys[a], keys[b]);
        return c != 0 ? c : a.CompareTo(b);
      });

      var sorted = new List<HarloweValue>(items.Count);
      for (int i = 0; i < order.Length; i++) sorted.Add(items[order[i]]);
      return HarloweValue.OfArray(sorted);
    }

    /// <summary>
    /// Ascending key order: every number before every string, numbers by
    /// value, strings by ordinal comparison.
    /// </summary>
    private static int CompareKeys(HarloweValue a, HarloweValue b)
    {
      bool aIsNumber = a.Kind == HarloweValueKind.Number;
      bool bIsNumber = b.Kind == HarloweValueKind.Number;
      if (aIsNumber != bIsNumber) return aIsNumber ? -1 : 1;
      if (aIsNumber) return a.AsNumber.CompareTo(b.AsNumber);
      return string.CompareOrdinal(a.AsString, b.AsString);
    }
  }
}
