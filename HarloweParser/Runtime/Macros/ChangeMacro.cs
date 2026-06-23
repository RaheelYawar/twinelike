using System.Collections.Generic;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(change: ?target, changer)</c>. A one-shot enchantment command:
  /// resolves <c>?target</c> against the render tree built so far and applies
  /// <c>changer</c> to every match, right now. It does not persist — a hook
  /// declared after this macro is not affected (use <c>(enchant:)</c> for
  /// that). Produces no visible output of its own.
  /// </summary>
  public class ChangeMacro : IMacro
  {
    public string Name => "change";
    public int MinArgs => 2;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = EnchantmentMacroSupport.Validate(args, "(change:)", out var target, out var changer);
      if (error != null) return error;

      // Resolve against the session's live tree rather than the current
      // renderer's output, so a (change:) inside a deferred dispatch hook
      // (whose output is a detached builder) still targets the live tree.
      // Without a live tree (a plain buffer unit test) — no-op.
      var liveRoot = MacroContext.ResolveLiveRoot(context, context.Output);
      if (liveRoot != null)
      {
        var targets = HookResolver.Resolve(liveRoot, target);
        for (int i = 0; i < targets.Count; i++)
          changer.ApplyToTarget(liveRoot, targets[i]);
      }

      return null;
    }
  }
}
