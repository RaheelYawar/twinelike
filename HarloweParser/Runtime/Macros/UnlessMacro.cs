using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(unless: $cond)[content]</c>. Inverse of <see cref="IfMacro"/>:
  /// evaluates a Boolean and renders its hook iff the value is false.
  /// Stores the negated value on <see cref="MacroContext.LastConditional"/>
  /// so a following <c>(else:)</c> sees the same render-or-not decision an
  /// <c>(if:)</c> would have produced.
  /// </summary>
  public class UnlessMacro : IMacro
  {
    public string Name => "unless";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"(unless:) requires a Boolean, got {v.Kind}");
      bool decision = !v.AsBool;
      context.LastConditional = decision;
      return HarloweValue.OfBool(decision);
    }
  }
}
