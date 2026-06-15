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
  /// analogue of Harlowe's <c>updateEnchantments()</c>. Invoked after a
  /// passage's main render and again after every dispatch re-render, so an
  /// enchantment catches hooks declared after the macro, revision-rewritten
  /// content, and content spliced in by a click event.
  ///
  /// <para>
  /// Idempotent by construction: <see cref="Update"/> first <em>disenchants</em>
  /// — unwraps every <see cref="RenderStyleNode"/> tagged with a non-null
  /// <see cref="RenderStyleNode.SourceEnchantment"/> — and then re-applies each
  /// enchantment fresh. So running the pass N times on the same tree gives the
  /// same result as running it once, no matter how the tree mutated between
  /// passes. <c>(change:)</c>'s style wraps carry no source tag and are left
  /// intact (one-shot semantics — survive across passes).
  /// </para>
  /// </summary>
  public static class EnchantmentPass
  {
    /// <summary>
    /// Apply every enchantment in <paramref name="enchantments"/> to
    /// <paramref name="root"/>. Each enchantment's target is resolved fresh —
    /// it is a query, not a cached node list — and its changer wraps every
    /// matching container's content. A disenchant sweep runs first so prior
    /// applications don't double up. Null-safe; tolerates malformed entries.
    /// </summary>
    public static void Update(RenderRoot root, IReadOnlyList<Enchantment> enchantments)
    {
      if (root == null || enchantments == null) return;

      Disenchant(root);

      for (int i = 0; i < enchantments.Count; i++)
      {
        var enchantment = enchantments[i];
        if (enchantment?.Target == null || enchantment.Changer == null) continue;

        var targets = HookResolver.Resolve(root, enchantment.Target);
        for (int j = 0; j < targets.Count; j++)
        {
          if (targets[j] is IRenderContainer container)
            enchantment.Changer.ApplyTo(container, enchantment);
        }
      }
    }

    /// <summary>
    /// Walk <paramref name="container"/> and unwrap every
    /// <see cref="RenderStyleNode"/> whose <see cref="RenderStyleNode.SourceEnchantment"/>
    /// is non-null — i.e. every style layer produced by a previous
    /// <see cref="Update"/>. Style layers from <c>(text-style:)</c> /
    /// <c>(change:)</c> have a null source tag and stay intact.
    /// </summary>
    public static void Disenchant(IRenderContainer container)
      => RenderNodes.UnwrapWhere(container, n => n is RenderStyleNode style && style.SourceEnchantment != null);
  }
}
