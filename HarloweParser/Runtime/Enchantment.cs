using System.Collections.Generic;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A registered, persistent restyling: a <c>(enchant:)</c> macro's target
  /// query plus the changer to apply to every match. Unlike <c>(change:)</c>
  /// — which applies once, at the point it runs — an enchantment is held on
  /// <see cref="MacroContext.Enchantments"/> and re-applied by
  /// <see cref="EnchantmentPass"/> after the passage finishes rendering, so it
  /// catches hooks declared after the macro and content rewritten by revision
  /// macros. The analogue of the reference Harlowe runtime's
  /// <c>section.enchantments</c> list + <c>updateEnchantments()</c>.
  /// </summary>
  public class Enchantment
  {
    /// <summary>The hook-name query re-resolved against the live render tree on each pass.</summary>
    public HookNameValue Target;

    /// <summary>The changer applied to every node the target resolves to.</summary>
    public Changer Changer;
  }

  /// <summary>
  /// Runs the registered enchantments over a finished render tree — the
  /// analogue of Harlowe's <c>updateEnchantments()</c>. Invoked once after a
  /// passage's main render (by which point every later-declared hook is in the
  /// tree and every revision mutation has already happened), so a single pass
  /// catches everything.
  /// </summary>
  public static class EnchantmentPass
  {
    /// <summary>
    /// Apply every enchantment in <paramref name="enchantments"/> to
    /// <paramref name="root"/>. Each enchantment's target is resolved fresh —
    /// it is a query, not a cached node list — and its changer wraps every
    /// matching container's content. Null-safe; tolerates malformed entries.
    /// </summary>
    public static void Update(RenderRoot root, IReadOnlyList<Enchantment> enchantments)
    {
      if (root == null || enchantments == null) return;
      for (int i = 0; i < enchantments.Count; i++)
      {
        var enchantment = enchantments[i];
        if (enchantment?.Target == null || enchantment.Changer == null) continue;

        var targets = HookResolver.Resolve(root, enchantment.Target);
        for (int j = 0; j < targets.Count; j++)
        {
          if (targets[j] is IRenderContainer container)
            enchantment.Changer.ApplyTo(container);
        }
      }
    }
  }
}
