using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(range: 1, 5)</c> → <c>(a: 1, 2, 3, 4, 5)</c>. An inclusive run of whole
  /// numbers, whichever way round the two are given. Reference's entry in
  /// <c>ts/macrolib/datastructures.ts</c>.
  ///
  /// <para>"Whole" is reference's <c>parseInt</c> signature check: the number's
  /// printed form has no decimal point. So <c>1e-7</c> passes, and the run then
  /// steps up by one from it, as it does there.</para>
  /// </summary>
  public class RangeMacro : IMacro
  {
    /// <summary>
    /// The longest range allowed. Real Harlowe fails with a stack error somewhere
    /// past 100,000 values, so this admits everything that works there and stops
    /// a huge range from exhausting the host's memory.
    /// </summary>
    internal const int MaxLength = 200000;

    public string Name => "range";
    public int MinArgs => 2;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      for (int i = 0; i < 2; i++)
      {
        if (!IsWhole(args[i]))
          return HarloweValue.OfError($"The (range:) macro's values should be whole numbers, not {args[i].ObjectName}.");
      }

      double a = args[0].AsNumber, b = args[1].AsNumber;
      if (a > b)
      {
        double swap = a;
        a = b;
        b = swap;
      }
      // Compared before any conversion, so an enormous or infinite span can't overflow.
      if (b - a >= MaxLength)
        return HarloweValue.OfError($"(range:) can't make an array of more than {MaxLength} numbers.");
      // A long range in a loop would otherwise be cheap to write and slow to run.
      if (!context.TrySpendOnSplice((long)(b - a) + 1))
        return HarloweValue.OfError("(range:) stopped: " + MacroContext.OverBudgetMessage);

      // Reference's loop, which keeps stepping while the distance left is above zero.
      var items = new List<HarloweValue>((int)(b - a) + 2) { HarloweValue.OfNumber(a) };
      double remaining = b - a;
      while (remaining-- > 0)
      {
        a += 1;
        items.Add(HarloweValue.OfNumber(a));
      }
      return HarloweValue.OfArray(items);
    }

    private static bool IsWhole(HarloweValue value)
      => value.Kind == HarloweValueKind.Number
        && !double.IsNaN(value.AsNumber)
        && HarloweValue.FormatNumber(value.AsNumber).IndexOf('.') < 0;
  }
}
