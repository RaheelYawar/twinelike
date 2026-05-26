using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(find: where-lambda, items...)</c>. Iterates the items, evaluates the
  /// lambda's <c>where</c> clause once per item, and returns a new array of
  /// the items that produced <c>true</c>. The full Harlowe surface accepts
  /// items either inline or spread from an array (<c>...$arr</c>); spread
  /// support is deferred to a later slice, so a single trailing array arg is
  /// auto-iterated for convenience.
  /// </summary>
  public class FindMacro : IMacro
  {
    public string Name => "find";
    public int MinArgs => 2;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError("(find:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      var items = LambdaArgs.ExpandItems(args, startIndex: 1);
      var matches = new List<HarloweValue>();

      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        var verdict = LambdaInvoker.EvalPredicate(lambda, item, i + 1, context);
        if (verdict.IsError) return verdict;
        if (verdict.AsBool) matches.Add(item);
      }

      return HarloweValue.OfArray(matches);
    }
  }
}
