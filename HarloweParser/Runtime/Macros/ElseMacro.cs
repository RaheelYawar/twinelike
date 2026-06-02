using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(else:)[content]</c>. Has no arguments. Renders its hook iff the
  /// immediately preceding <c>(if:)</c>/<c>(unless:)</c>/<c>(else-if:)</c> hook
  /// was hidden (the inverse of <see cref="MacroContext.LastConditional"/>).
  /// With no preceding conditional in scope — a stray <c>(else:)</c>, or one
  /// after an intervening non-conditional macro that reset the pairing — it
  /// surfaces an in-prose error rather than silently doing nothing, matching
  /// reference Harlowe, which errors when <c>lastHookShown</c> is undefined.
  /// </summary>
  public class ElseMacro : IMacro
  {
    public string Name => "else";
    public int MinArgs => 0;
    public int MaxArgs => 0;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (context.LastConditional == null)
        return HarloweValue.OfError("There's nothing before this to do (else:) with.");
      bool render = context.LastConditional == false;
      context.LastConditional = render;
      return HarloweValue.OfBool(render);
    }
  }
}
