using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(all-pass: where-lambda, items...)</c>. True iff the lambda's
  /// <c>where</c> clause is <c>true</c> for every item. Empty input is
  /// vacuously true, matching Harlowe's documented behaviour. Short-circuits
  /// on the first false-or-error. Auto-iterates a single trailing array arg
  /// (see <see cref="FindMacro"/> for the rationale).
  /// </summary>
  public class AllPassMacro : IMacro
  {
    public string Name => "all-pass";
    public int MinArgs => 2;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError("(all-pass:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      var items = LambdaArgs.ExpandItems(args, startIndex: 1);

      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        var verdict = LambdaInvoker.EvalPredicate(lambda, item, i + 1, context);
        if (verdict.IsError) return verdict;
        if (!verdict.AsBool) return HarloweValue.OfBool(false);
      }

      return HarloweValue.OfBool(true);
    }
  }
}
