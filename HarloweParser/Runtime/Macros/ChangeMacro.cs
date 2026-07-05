using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(change: target, changer-or-lambda)</c>. A one-shot enchantment
  /// command: resolves the target — a hook name, or a string matched against
  /// rendered prose — in the render tree built so far and applies the changer
  /// (or the changer a <c>via</c> lambda produces per match) to every match,
  /// right now. It does not persist — a hook declared after this macro is not
  /// affected (use <c>(enchant:)</c> for that). Produces no visible output of
  /// its own.
  /// </summary>
  public class ChangeMacro : IMacro
  {
    public string Name => "change";
    public int MinArgs => 2;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = EnchantmentMacroSupport.Validate(args, "(change:)", out var enchantment);
      if (error != null) return error;

      // Resolve against the session's live tree rather than the current
      // renderer's output, so a (change:) inside a deferred dispatch hook
      // (whose output is a detached builder) still targets the live tree.
      // Without a live tree (a plain buffer unit test) — no-op. A null source
      // leaves the wraps untagged, so the enchant pass's disenchant sweep
      // never unwinds them — one-shot semantics.
      var liveRoot = MacroContext.ResolveLiveRoot(context, context.Output);
      if (liveRoot != null)
        EnchantmentPass.Apply(liveRoot, enchantment, context, source: null);

      return null;
    }
  }
}
