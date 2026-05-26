using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(rotated-to: where-lambda, items...)</c>. Rotates the sequence so the
  /// first item matching the lambda's <c>where</c> clause becomes the first
  /// element; items before it wrap around to the end. The lambda must carry a
  /// <c>where</c> clause; a <c>via</c>-only lambda errors out in-prose.
  ///
  /// <para>
  /// If no item matches, the sequence is returned unchanged — a forgiving
  /// no-op rather than an error, consistent with the library's in-prose
  /// tolerance. Auto-iterates a single trailing array arg (see
  /// <see cref="FindMacro"/> for the rationale).
  /// </para>
  /// </summary>
  public class RotatedToMacro : IMacro
  {
    public string Name => "rotated-to";
    public int MinArgs => 2;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError("(rotated-to:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      var items = LambdaArgs.ExpandItems(args, startIndex: 1);

      int pivot = -1;
      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.IsError) return item;
        var verdict = LambdaInvoker.EvalPredicate(lambda, item, i + 1, context);
        if (verdict.IsError) return verdict;
        if (verdict.AsBool)
        {
          pivot = i;
          break;
        }
      }

      if (pivot <= 0)
      {
        // No match, or the match is already first — nothing to rotate.
        return HarloweValue.OfArray(new List<HarloweValue>(items));
      }

      var rotated = new List<HarloweValue>(items.Count);
      for (int i = pivot; i < items.Count; i++) rotated.Add(items[i]);
      for (int i = 0; i < pivot; i++) rotated.Add(items[i]);
      return HarloweValue.OfArray(rotated);
    }
  }
}
