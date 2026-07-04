using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(else:)[content]</c> → Changer. Reads the pairing state at <em>call</em>
  /// time and bakes the decision into a conditional changer that shows the hook
  /// iff the preceding conditional hook was hidden — reference's
  /// <c>new Changer(`else`, [section.stackTop.lastHookShown === false])</c>
  /// (<c>ts/macrolib/stylechangers.ts</c>). With no preceding conditional in
  /// scope it errors, matching reference's <c>lastHookShown === undefined</c>
  /// check. The pairing itself is updated by the renderer when the changer is
  /// applied to a hook.
  ///
  /// <para>The result is a baked <c>(if:)</c> changer
  /// (<see cref="Changer.FromBakedConditional"/>): a stored <c>(else:)</c> is
  /// just its baked boolean, and both the natural <c>(else:)</c> source stamp
  /// and an Else-kind patch would make the reloaded value differ from the
  /// stored one.</para>
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
      bool show = context.LastConditional == false;
      return HarloweValue.OfChanger(Changer.FromBakedConditional(show));
    }
  }
}
