using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(cond: $cash >= 300, "stable", $cash >= 100, "skint", "broke")</c> → the
  /// value paired with the first true condition, else the final value.
  /// Reference's entry in <c>ts/macrolib/values.ts</c>, whose signature
  /// <c>[Boolean, Any, rest(Any)]</c> needs at least three values. Every value
  /// is evaluated first, as there, so it doesn't short-circuit. A condition
  /// that isn't a boolean is an error, and so is running out of values with no
  /// condition true.
  /// </summary>
  public class CondMacro : IMacro
  {
    public string Name => "cond";
    public int MinArgs => 3;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      for (int i = 0; i < args.Count; i += 2)
      {
        var condition = args[i];
        if (i == args.Count - 1 || condition.IsError) return condition;
        if (condition.Kind != HarloweValueKind.Bool)
          return HarloweValue.OfError(i == 0
            ? $"The (cond:) macro's 1st value is {condition.ObjectName}, but should be a boolean."
            : $"(cond:)'s {i + 1}{MacroRegistry.OrdinalSuffix(i + 1)} value is {condition.ObjectName}, but should be a boolean.");
        if (condition.AsBool) return args[i + 1];
      }
      return HarloweValue.OfError($"An odd number of values must be given to (cond:), not {args.Count}");
    }
  }
}
