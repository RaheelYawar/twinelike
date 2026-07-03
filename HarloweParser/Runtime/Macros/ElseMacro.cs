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
  /// <para>The changer is pre-stamped with the equivalent <c>(if:)</c> source:
  /// a stored <c>(else:)</c> changer is just its baked boolean, and the natural
  /// <c>(else:)</c> stamp would error on save-load re-evaluation (no pairing in
  /// scope at load time).</para>
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
      var changer = Changer.FromConditional(ConditionalKind.Else, show);
      changer.Source = show ? "(if:true)" : "(if:false)";
      return HarloweValue.OfChanger(changer);
    }
  }
}
