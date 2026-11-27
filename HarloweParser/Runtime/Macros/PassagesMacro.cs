using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(passages:)</c> or <c>(passages: where its tags contains "x")</c> → an
  /// array of every passage's <c>(passage:)</c> datamap, or those the lambda
  /// keeps, sorted by name in natural order. Every passage counts, including
  /// <c>startup</c> and <c>header</c> ones. Reference's entry in
  /// <c>ts/macrolib/datastructures.ts</c>, whose signature is
  /// <c>[optional(Lambda.TypeSignature(`where`))]</c>: an <c>each _p</c> lambda,
  /// with no clause, keeps everything.
  /// </summary>
  public class PassagesMacro : IMacro
  {
    public string Name => "passages";
    public int MinArgs => 0;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      LambdaValue lambda = null;
      if (args.Count == 1)
      {
        var node = args[0].Kind == HarloweValueKind.Lambda ? args[0].AsLambda.Node : null;
        if (node == null || node.ViaClause != null || node.WhenClause != null || node.MakingName != null)
          return HarloweValue.OfError($"The (passages:) macro's 1st value is {args[0].ObjectName}, but should be (optional) a \"where ...\" lambda.");
        lambda = args[0].AsLambda;
      }

      // Reference filters in story order, so `pos` counts every passage, and sorts after.
      var kept = new List<HarlowePassage>();
      var items = new List<HarloweValue>();
      int pos = 0;
      if (context.AllPassages != null)
        foreach (var passage in context.AllPassages())
        {
          pos++;
          var item = PassageMacro.Describe(passage);
          if (lambda != null && lambda.Node.WhereClause != null)
          {
            var verdict = LambdaInvoker.EvalPredicate(lambda, item, pos, context);
            if (verdict.IsError) return verdict;
            if (!verdict.AsBool) continue;
          }
          kept.Add(passage);
          items.Add(item);
        }

      var names = new string[kept.Count];
      for (int i = 0; i < names.Length; i++) names[i] = kept[i].Name;
      var order = NaturalOrder.SortedIndices(names);
      var result = new List<HarloweValue>(order.Length);
      for (int i = 0; i < order.Length; i++) result.Add(items[order[i]]);
      return HarloweValue.OfArray(result);
    }
  }
}
