using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(altered: via-lambda, items...)</c>. Map operation: runs the lambda's
  /// <c>via</c> clause once per item and returns an array of transformed
  /// values. The lambda must carry a <c>via</c> clause; a <c>where</c>-only
  /// lambda errors out in-prose. Auto-iterates a single trailing array arg
  /// (see <see cref="FindMacro"/> for the rationale).
  /// </summary>
  public class AlteredMacro : IMacro
  {
    public string Name => "altered";
    public int MinArgs => 2;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError("(altered:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      var items = LambdaArgs.ExpandItems(args, startIndex: 1);
      var transformed = new List<HarloweValue>(items.Count);

      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        var mapped = LambdaInvoker.EvalTransform(lambda, item, i + 1, context);
        if (mapped.IsError) return mapped;
        transformed.Add(mapped);
      }

      return HarloweValue.OfArray(transformed);
    }
  }
}
