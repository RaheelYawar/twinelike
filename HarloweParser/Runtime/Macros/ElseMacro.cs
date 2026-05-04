using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(else:)[content]</c>. Has no arguments. Returns the inverse of
  /// <see cref="MacroContext.LastConditional"/>: renders its hook iff the
  /// preceding <c>(if:)</c>/<c>(unless:)</c> did not. With no preceding
  /// conditional in scope, returns false so the hook stays hidden — matching
  /// Harlowe's "stray else does nothing" handling.
  /// </summary>
  public class ElseMacro : IMacro
  {
    public string Name => "else";
    public int MinArgs => 0;
    public int MaxArgs => 0;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      bool render = context.LastConditional == false;
      context.LastConditional = render;
      return HarloweValue.OfBool(render);
    }
  }
}
