using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(some-pass: where-lambda, items...)</c>. True iff at least one item
  /// satisfies the predicate. Short-circuits on the first match. Empty input
  /// is <c>false</c> (Harlowe's "no items, no match" rule — the mirror of
  /// <c>(all-pass:)</c>'s vacuous truth).
  /// </summary>
  public class SomePassMacro : IMacro
  {
    public string Name => "some-pass";
    public int MinArgs => 2;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError("(some-pass:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      var items = LambdaArgs.ExpandItems(args, startIndex: 1);

      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        var verdict = LambdaInvoker.EvalPredicate(lambda, item, context);
        if (verdict.IsError) return verdict;
        if (verdict.AsBool) return HarloweValue.OfBool(true);
      }

      return HarloweValue.OfBool(false);
    }
  }
}
