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

      // Without a live render tree (a plain buffer in a unit test) there is
      // nothing to target — no-op, matching the revision macros.
      if (context.Output is RenderTreeBuilder builder)
      {
        var targets = HookResolver.Resolve(builder.Root, target);
        for (int i = 0; i < targets.Count; i++)
        {
          if (targets[i] is IRenderContainer container)
            changer.ApplyTo(container);
        }
      }

      return null;
    }
  }
}
