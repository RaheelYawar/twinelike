using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(if: $cond)[content]</c>. Evaluates its boolean argument and stores
  /// the result on <see cref="MacroContext.LastConditional"/> so a following
  /// <c>(else:)</c> can pair against it. Returns the same boolean so the body
  /// renderer can decide whether to render the attached hook.
  /// </summary>
  public class IfMacro : IMacro
  {
    public string Name => "if";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"(if:) requires a Boolean, got {v.Kind}");
      context.LastConditional = v.AsBool;
      return v;
    }
  }
}
