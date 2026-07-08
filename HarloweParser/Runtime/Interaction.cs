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
    /// <summary>The hook-name query, re-resolved against the live tree each pass. Null when <see cref="StringTarget"/> is set.</summary>
    public HookNameValue Target;

    /// <summary>Literal prose to match (<c>(click: "gold")</c>). Each occurrence is wrapped as an armed region per pass. Null when <see cref="Target"/> is set.</summary>
    public string StringTarget;

    /// <summary>Which event kind the engine reports for this region.</summary>
    public InteractionKind Kind;

    /// <summary>
    /// Combo splice mode (<c>(click-replace:)</c> etc.), or <c>null</c> for the
    /// plain macros, whose deferred hook reveals at <see cref="RevealAnchor"/>
    /// instead of rewriting the target.
    /// </summary>
    public RevisionMode? Mode;

    /// <summary>
    /// False for <c>(click-rerun:)</c>: the interaction survives dispatch and
    /// each activation re-renders the deferred hook over the previous run's
    /// content (reference's <c>once: false</c>).
    /// </summary>
    public bool Once = true;

    /// <summary>
    /// Composed style layers for the <em>deferred content</em> (outermost =
    /// first layer): <c>(text-style:"bold")+(click: ?a)[x]</c> bolds the
    /// revealed <c>x</c>, not the armed region — reference applies the
    /// descriptor's styles when the event's <c>renderInto</c> runs. The armed
    /// region is styled by <see cref="ArmChanger"/>/<see cref="ArmLambda"/>.
    /// </summary>
    public List<StyleSpec> Styles;

    /// <summary>Optional second-argument changer styling the armed region while it waits; cloned style layers re-wrap every pass.</summary>
    public Changer ArmChanger;

    /// <summary>Optional second-argument <c>via</c> lambda producing the armed-region changer per match (1-based <c>pos</c>), as in <c>(enchant:)</c>.</summary>
    public LambdaValue ArmLambda;

    /// <summary>Renders the deferred hook into a given output on dispatch.</summary>
    public Action<IRenderOutput> RenderDeferredHook;

    /// <summary>
    /// The empty anonymous hook planted at the macro's own position when the
    /// changer applied — where a plain (non-combo) interaction's deferred hook
    /// renders on dispatch, mirroring reference's hidden attached-hook element.
    /// Null for combos, and when the changer applied outside a tree builder.
    /// </summary>
    public RenderHookNode RevealAnchor;

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
  /// — removes every interactive wrap, region-tagged style node, and
  /// region-tagged string-occurrence hook wrap — then re-wraps each live
  /// interaction fresh and re-registers its handler. So running the pass any
  /// number of times leaves a single correct application, regardless of how
  /// the tree mutated between passes. A consumed interaction (removed from the
  /// list on dispatch) simply stops being re-wrapped, which is the single-use
  /// semantics — and how a fired <c>(click:)</c>'s targets lose their armed
  /// styling.
  /// </para>
  /// </summary>
  public static class InteractionPass
  {
    /// <summary>
    /// Strip prior wraps, then re-resolve and re-wrap every interaction in
    /// <paramref name="interactions"/> against <paramref name="root"/>, rebuilding
    /// <paramref name="clickHandlers"/> from scratch. <paramref name="ctx"/>
    /// supplies the store and invoker for armed-region <c>via</c>-lambda
    /// evaluation; when null, lambda-styled interactions still arm, just
    /// unstyled. Null-safe; tolerant of malformed entries.
    /// </summary>
    public static void Update(RenderRoot root, IReadOnlyList<Interaction> interactions, Dictionary<string, ClickHandler> clickHandlers, MacroContext ctx = null)
    {
      if (root == null || interactions == null || clickHandlers == null) return;

      StripWraps(root);
      clickHandlers.Clear();

      for (int i = 0; i < interactions.Count; i++)
      {
        var interaction = interactions[i];
        if (interaction == null) continue;

        IReadOnlyList<RenderNode> targets;
        List<IRenderContainer> stringWraps = null;
        if (interaction.Target != null)
        {
          targets = HookResolver.Resolve(root, interaction.Target);
        }
        else if (interaction.StringTarget != null)
        {
          // Wrap each occurrence fresh (StripWraps unwound the previous
          // pass's), tagged with the region id so the next strip finds them.
          var wraps = TextOccurrenceFinder.FindAndWrap(root, interaction.StringTarget);
          var list = new List<RenderNode>(wraps.Count);
          stringWraps = new List<IRenderContainer>(wraps.Count);
          for (int j = 0; j < wraps.Count; j++)
          {
            wraps[j].SourceRegionId = interaction.RegionId;
            list.Add(wraps[j]);
            stringWraps.Add(wraps[j]);
          }
          targets = list;
        }
        else
        {
          continue;
        }

        bool wrappedAny = false;
        int pos = 0;
        bool lambdaFailed = false;
        for (int j = 0; j < targets.Count; j++)
        {
          // A leaf match (a ?link RenderLinkNode) is skipped here: wrapping it as a
          // clickable region is easy, but the click's dispatch reveal splices content
          // *into* the target, which needs the link to be a container — the same
          // BeginLink/EndLink follow-up that (replace: ?link) needs. A clickable-but-
          // dead link is worse than a no-op, so click/hover on ?link waits for that.
          // (?link styling via (enchant:)/(change:) works — see Changer.ApplyToNode.)
          if (!(targets[j] is IRenderContainer container)) continue;

          // Reference never arms completely empty hooks (`[]<foo|` gets no
          // <tw-enchantment> — the `:empty` filter), and they don't advance pos.
          if (container.Children.Count == 0) continue;
          pos++;

          // Armed-region styling: the fixed second-arg changer, or the changer
          // its via-lambda produces for this match. A lambda failure replaces
          // the match with the in-prose error and stops producing styles for
          // the rest of the scope, but later matches still arm (reference
          // nulls the lambda out and keeps enchanting).
          Changer armChanger = interaction.ArmChanger;
          if (interaction.ArmLambda != null)
          {
            armChanger = null;
            if (!lambdaFailed && ctx != null)
            {
              var item = interaction.Target != null
                ? HarloweValue.OfHookName(interaction.Target)
                : HarloweValue.OfString(interaction.StringTarget);
              armChanger = EnchantmentPass.EvaluateViaLambda(root, interaction.ArmLambda, item, pos, ctx, targets[j], out lambdaFailed);
              if (lambdaFailed) continue; // the match is now the error node — don't arm it
            }
          }
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

          // Fold the armed-region style layers around the interactive node,
          // innermost = last layer, each cloned + tagged with the region id so
          // StripWraps removes it alongside the interactive node next pass.
          RenderNode wrapped = interactiveNode;
          var armStyles = armChanger?.GetStyleLayers();
          if (armStyles != null)
          {
            for (int s = armStyles.Count - 1; s >= 0; s--)
            {
              var styleNode = new RenderStyleNode { Style = armStyles[s]?.Clone(), SourceRegionId = interaction.RegionId };
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
            StringWraps = stringWraps,
            Mode = interaction.Mode,
            Once = interaction.Once,
            Kind = interaction.Kind,
            Styles = interaction.Styles,
            RevealAnchor = interaction.RevealAnchor
          };
        }
      }
    }

    /// <summary>
    /// Remove every <see cref="RenderInteractiveNode"/>, every
    /// <see cref="RenderStyleNode"/> tagged with a non-null
    /// <see cref="RenderStyleNode.SourceRegionId"/>, and every string-occurrence
    /// <see cref="RenderHookNode"/> tagged likewise from
    /// <paramref name="container"/>, hoisting their children in place — the
    /// id-agnostic generalization of the former per-id unwrap sweep. Recurse
    /// first so descendants are processed before this level's list is rebuilt
    /// (symmetric with <see cref="EnchantmentPass.Disenchant"/>).
    /// </summary>
    public static void StripWraps(IRenderContainer container)
      => RenderNodes.UnwrapWhere(container, n =>
           n is RenderInteractiveNode
        || (n is RenderStyleNode sn && sn.SourceRegionId != null)
        || (n is RenderHookNode hn && hn.SourceRegionId != null));
  }
}
