using System;
using System.Collections.Generic;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A registered, persistent interaction: a <c>(click:)</c>/<c>(mouseover:)</c>
  /// -family macro's target query plus everything needed to re-wrap its matches
  /// and register a handler. The interaction analogue of <see cref="Enchantment"/>
  /// — held on <see cref="MacroContext.Interactions"/> and re-applied by
  /// <see cref="InteractionPass"/> after the body render and after every
  /// dispatch, so a target declared <em>after</em> the macro (or created by a
  /// click-deferred hook) is still caught. Mirrors reference Harlowe, which
  /// models clicks/hovers as enchantments refreshed whenever the tree changes.
  /// </summary>
  public class Interaction
  {
    /// <summary>The hook-name query, re-resolved against the live tree each pass.</summary>
    public HookNameValue Target;

    /// <summary>Which event kind the engine reports for this region.</summary>
    public InteractionKind Kind;

    /// <summary>How the deferred hook's source splices into the target on dispatch.</summary>
    public RevisionMode Mode;

    /// <summary>Composed style layers wrapped around the interactive node (innermost = last layer); cloned per wrap.</summary>
    public List<StyleSpec> Styles;

    /// <summary>Renders the deferred hook into a given output on dispatch.</summary>
    public Action<IRenderOutput> RenderDeferredHook;

    /// <summary>
    /// Region id, allocated once when the interaction is recorded and kept
    /// stable across passes — so an id the host already holds from a prior
    /// <see cref="RenderResult"/> keeps resolving to the same interaction.
    /// </summary>
    public string RegionId;
  }

  /// <summary>
  /// Runs the registered interactions over a finished render tree — the
  /// interaction analogue of <see cref="EnchantmentPass"/>. Invoked after the
  /// main render and again after every dispatch, so an interaction catches
  /// hooks declared after the macro and content spliced in by a click event.
  ///
  /// <para>
  /// Idempotent by construction: <see cref="Update"/> first <see cref="StripWraps"/>
  /// — removes every interactive wrap and region-tagged style node — then
  /// re-wraps each live interaction fresh and re-registers its handler. So
  /// running the pass any number of times leaves a single correct application,
  /// regardless of how the tree mutated between passes. A consumed interaction
  /// (removed from the list on dispatch) simply stops being re-wrapped, which
  /// is the single-use semantics.
  /// </para>
  /// </summary>
  public static class InteractionPass
  {
    /// <summary>
    /// Strip prior wraps, then re-resolve and re-wrap every interaction in
    /// <paramref name="interactions"/> against <paramref name="root"/>, rebuilding
    /// <paramref name="clickHandlers"/> from scratch. Null-safe; tolerant of
    /// malformed entries.
    /// </summary>
    public static void Update(RenderRoot root, IReadOnlyList<Interaction> interactions, Dictionary<string, ClickHandler> clickHandlers)
    {
      if (root == null || interactions == null || clickHandlers == null) return;

      StripWraps(root);
      clickHandlers.Clear();

      for (int i = 0; i < interactions.Count; i++)
      {
        var interaction = interactions[i];
        if (interaction?.Target == null) continue;

        var targets = HookResolver.Resolve(root, interaction.Target);
        bool wrappedAny = false;
        for (int j = 0; j < targets.Count; j++)
        {
          if (!(targets[j] is IRenderContainer container)) continue;
          wrappedAny = true;

          var content = new List<RenderNode>(container.Children);
          // Each wrap gets its own InteractiveRegion instance (sharing the id
          // and kind) so a later in-place mutation on one match's region can't
          // propagate to its siblings; matching identity is the id string.
          var interactiveNode = new RenderInteractiveNode
          {
            Region = new InteractiveRegion { Id = interaction.RegionId, Kind = interaction.Kind }
          };
          interactiveNode.Children.AddRange(content);

          // Fold the composed style layers around the interactive node,
          // innermost = last layer, each cloned + tagged with the region id so
          // StripWraps removes it alongside the interactive node next pass.
          RenderNode wrapped = interactiveNode;
          if (interaction.Styles != null)
          {
            for (int s = interaction.Styles.Count - 1; s >= 0; s--)
            {
              var styleNode = new RenderStyleNode { Style = interaction.Styles[s]?.Clone(), SourceRegionId = interaction.RegionId };
              styleNode.Children.Add(wrapped);
              wrapped = styleNode;
            }
          }

          container.Children.Clear();
          container.Children.Add(wrapped);
        }

        // No match wrapped → no event can fire, so don't register a stale
        // handler. A later pass (once the target exists) registers it.
        if (wrappedAny)
        {
          clickHandlers[interaction.RegionId] = new ClickHandler
          {
            RenderDeferredHook = interaction.RenderDeferredHook,
            Target = interaction.Target,
            Mode = interaction.Mode,
            Kind = interaction.Kind
          };
        }
      }
    }

    /// <summary>
    /// Remove every <see cref="RenderInteractiveNode"/> and every
    /// <see cref="RenderStyleNode"/> tagged with a non-null
    /// <see cref="RenderStyleNode.SourceRegionId"/> from
    /// <paramref name="container"/>, hoisting their children in place — the
    /// id-agnostic generalization of the former per-id unwrap sweep. Recurse
    /// first so descendants are processed before this level's list is rebuilt
    /// (symmetric with <see cref="EnchantmentPass.Disenchant"/>).
    /// </summary>
    public static void StripWraps(IRenderContainer container)
      => RenderNodes.UnwrapWhere(container, n =>
           n is RenderInteractiveNode || (n is RenderStyleNode sn && sn.SourceRegionId != null));
  }
}
