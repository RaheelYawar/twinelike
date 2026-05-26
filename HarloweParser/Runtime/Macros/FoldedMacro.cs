using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(folded: fold-lambda, items...)</c>. Reduce operation: threads a
  /// running accumulator through the items, evaluating the lambda's
  /// <c>via</c> clause once per item. The lambda must carry both a
  /// <c>making</c> accumulator parameter and a <c>via</c> body; a
  /// <c>where</c>-only or plain <c>via</c> lambda errors out in-prose.
  ///
  /// <para>
  /// Per Harlowe's documented behaviour the initial accumulator is the first
  /// item, so the fold runs from the second item onward and an empty input
  /// has no starting value — that surfaces as an in-prose error. A single
  /// item folds to itself. Auto-iterates a single trailing array arg (see
  /// <see cref="FindMacro"/> for the rationale).
  /// </para>
  /// </summary>
  public class FoldedMacro : IMacro
  {
    public string Name => "folded";
    public int MinArgs => 2;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError("(folded:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      var items = LambdaArgs.ExpandItems(args, startIndex: 1);
      if (items.Count == 0)
        return HarloweValue.OfError("(folded:) needs at least one item to fold");

      var acc = items[0];
      if (acc.IsError) return acc;

      for (int i = 1; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        // pos is the 1-indexed position in the original arg list. items[0]
        // is the initial accumulator (no lambda call), so the first call
        // here lands on items[1] with pos=2, matching reference.
        acc = LambdaInvoker.EvalFold(lambda, acc, item, i + 1, context);
        if (acc.IsError) return acc;
      }

      return acc;
    }
  }
}
