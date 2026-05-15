using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Shared argument validation for the enchantment macros <c>(change:)</c>
  /// and <c>(enchant:)</c> — both take a hook-name target and a changer, and
  /// differ only in <em>when</em> the changer is applied (once now, vs.
  /// persistently after every render pass).
  /// </summary>
  internal static class EnchantmentMacroSupport
  {
    /// <summary>
    /// Validate the <c>(?target, changer)</c> argument pair. On success returns
    /// <c>null</c> and sets <paramref name="target"/> / <paramref name="changer"/>;
    /// on failure returns the in-prose error to surface and leaves both out
    /// parameters null. <paramref name="macroName"/> is the display name used
    /// in error messages (e.g. <c>"(change:)"</c>).
    /// </summary>
    public static HarloweValue Validate(List<HarloweValue> args, string macroName,
                                        out HookNameValue target, out Changer changer)
    {
      target = null;
      changer = null;

      if (args[0].IsError) return args[0];
      if (args[1].IsError) return args[1];

      if (args[0].Kind != HarloweValueKind.HookName)
        return HarloweValue.OfError($"{macroName} requires a hook name as its first argument, got {args[0].Kind}");
      if (args[1].Kind != HarloweValueKind.Changer)
        return HarloweValue.OfError($"{macroName} requires a changer as its second argument, got {args[1].Kind}");

      target = args[0].AsHookName;
      changer = args[1].AsChanger;
      return null;
    }
  }
}
