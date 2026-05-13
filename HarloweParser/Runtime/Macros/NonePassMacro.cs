using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(none-pass: where-lambda, items...)</c>. True iff no item satisfies
  /// the predicate. Short-circuits to <c>false</c> on the first match. Empty
  /// input is <c>true</c> (no item means no failure).
  /// </summary>
  public class NonePassMacro : IMacro
  {
    public string Name => "none-pass";
    public int MinArgs => 2;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError("(none-pass:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      var items = LambdaArgs.ExpandItems(args, startIndex: 1);

      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        var verdict = LambdaInvoker.EvalPredicate(lambda, item, context);
        if (verdict.IsError) return verdict;
        if (verdict.AsBool) return HarloweValue.OfBool(false);
      }

      return HarloweValue.OfBool(true);
    }
  }
}
