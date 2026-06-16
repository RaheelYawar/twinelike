using System.Collections.Generic;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A composable rendering transformation. Returned by changer macros
  /// (<c>(text-style: "bold")</c>, <c>(for: each _x, ...$xs)</c>, etc.);
  /// combined via <see cref="Compose"/> when the <c>+</c> operator joins two
  /// changers; applied by <see cref="BodyRenderer"/> when the changer sits
  /// in front of an attached hook.
  ///
  /// <para>Internally a Changer is a flat list of <see cref="IChangerPatch"/>
  /// patches. <see cref="Apply"/> walks the patches against a fresh
  /// <see cref="HookDescriptor"/> to build up the full rendering instruction
  /// (styles, iteration, future fields), then executes against the finished
  /// descriptor. Matches the descriptor-patch architecture of the reference
  /// Harlowe runtime — every changer kind drops in by adding a patch type
  /// and an executor branch without re-breaking Apply's signature.</para>
  ///
  /// <para>The styling shape is engine-agnostic: a style layer is described
  /// by a <see cref="StyleSpec"/>, not an HTML snippet. Engine integrations
  /// translate one spec to whatever their text renderer accepts. Use
  /// <see cref="HtmlRenderOutput"/> for the HTML mapping.</para>
  ///
  /// <para>Changers stay opaque values — they don't render visibly on their
  /// own. <see cref="HarloweValue.ToHarloweString"/> returns an empty string
  /// for them, so <c>(print: (text-style: "bold"))</c> emits nothing rather
  /// than dumping internal state.</para>
  /// </summary>
  public class Changer
  {
    private readonly List<IChangerPatch> _patches;

    private Changer(List<IChangerPatch> patches)
    {
      _patches = patches;
    }

    /// <summary>
    /// Construct a changer that adds one styling layer to the descriptor.
    /// Most style changer macros call this once with the spec describing
    /// what they want around the hook content (e.g. <c>new StyleSpec { Bold = true }</c>).
    /// </summary>
    public static Changer FromStyle(StyleSpec style)
      => new Changer(new List<IChangerPatch> { new StylePatch { Style = style ?? new StyleSpec() } });

    /// <summary>
    /// Construct a changer that wraps a hook in a per-item iteration. The
    /// renderer reads the descriptor's <see cref="IterationSpec"/> and runs
    /// the hook contents once per item, with the lambda's parameter (and
    /// the <c>it</c> slot) bound to that item. Used by <c>(for:)</c>.
    /// </summary>
    public static Changer FromIteration(IterationSpec iteration)
      => new Changer(new List<IChangerPatch> { new IterationPatch { Iteration = iteration } });

    /// <summary>
    /// Construct a revision changer (<c>(replace:)</c> / <c>(append:)</c> /
    /// <c>(prepend:)</c>). When applied, the changer renders its hook into a
    /// detached subtree and splices that into the targeted nodes of the live
    /// render tree instead of rendering inline — see <see cref="Apply"/>.
    /// </summary>
    public static Changer FromRevision(RevisionSpec revision)
      => new Changer(new List<IChangerPatch> { new RevisionPatch { Revision = revision } });

    /// <summary>
    /// Construct an interaction changer (<c>(click:)</c>, <c>(mouseover-append:)</c>,
    /// …). When applied, the changer wraps every match of the spec's target in
    /// a <see cref="RenderInteractiveNode"/> and registers a deferred
    /// <see cref="ClickHandler"/> on <see cref="MacroContext.ClickHandlers"/>
    /// keyed by the wrap's region id — see <see cref="Apply"/>.
    /// </summary>
    public static Changer FromInteraction(InteractionSpec interaction)
      => new Changer(new List<IChangerPatch> { new InteractionPatch { Interaction = interaction } });

    /// <summary>
    /// Construct a changer from a fixed sequence of patches. Used by macros
    /// that need to express more than one patch in a single produced changer —
    /// e.g. <c>(text-style: "none", "bold")</c> emits a
    /// <see cref="ClearStylesPatch"/> followed by a <see cref="StylePatch"/>.
    /// Most macros should prefer the single-patch factories above.
    /// </summary>
    public static Changer FromPatches(params IChangerPatch[] patches)
      => new Changer(new List<IChangerPatch>(patches ?? new IChangerPatch[0]));

    /// <summary>
    /// Compose this changer with <paramref name="other"/>, producing a new
    /// changer whose patch list is <c>this</c>'s followed by
    /// <paramref name="other"/>'s. Reading order: <c>A + B</c> means A's
    /// patches run before B's against the shared descriptor — so A's styles
    /// land first (outermost). Matches left-to-right authoring order in
    /// source.
    /// </summary>
    public Changer Compose(Changer other)
    {
      if (other == null) return this;
      var combined = new List<IChangerPatch>(_patches.Count + other._patches.Count);
      combined.AddRange(_patches);
      combined.AddRange(other._patches);
      return new Changer(combined);
    }

    /// <summary>
    /// Build the descriptor from this changer's patches and execute against
    /// it. <paramref name="renderHook"/> renders the attached hook's content
    /// into whatever <see cref="IRenderOutput"/> it is handed — usually
    /// <paramref name="output"/> itself, but for a revision changer a detached
    /// builder, so the source can be spliced rather than shown inline.
    ///
    /// <list type="bullet">
    /// <item><b>Revision</b> (<see cref="RevisionSpec"/>): render the hook into
    /// a detached subtree, resolve the target against the live render tree, and
    /// splice. Takes precedence over iteration/styles on the same descriptor.</item>
    /// <item><b>Iteration</b> (<see cref="IterationSpec"/>): loop, binding
    /// parameter + <c>it</c> per item and emitting the style wrappers around
    /// each iteration's hook render.</item>
    /// <item><b>Styles</b>: emitted once around a single
    /// <paramref name="renderHook"/> call.</item>
    /// </list>
    ///
    /// <para>
    /// <paramref name="ctx"/> is only required when iteration is in play —
    /// pure style and revision changers can pass null. The lambda binding
    /// routes through <see cref="IVariableStore.PushBinding"/> /
    /// <see cref="IVariableStore.PushItBinding"/>, so per-iteration scope is
    /// correctly restored even on early hook exit.
    /// </para>
    /// </summary>
    public void Apply(IRenderOutput output, System.Action<IRenderOutput> renderHook, MacroContext ctx = null)
    {
      var descriptor = new HookDescriptor();
      for (int i = 0; i < _patches.Count; i++) _patches[i].Apply(descriptor);

      // Interaction, Revision, and Iteration are mutually-exclusive ways of
      // consuming the hook (wrap-and-defer, splice-elsewhere, loop-in-place);
      // this engine executes exactly one. Composing two — e.g. (replace: ?x) +
      // (for: each _i, ...) — used to silently drop the lower-priority one.
      // Surface an in-prose error instead so the author sees the unsupported
      // combination rather than half of it. (Styles compose with any single
      // one of the three and are unaffected.)
      int exclusiveCount = (descriptor.Interaction != null ? 1 : 0)
                         + (descriptor.Revision != null ? 1 : 0)
                         + (descriptor.Iteration != null ? 1 : 0);
      if (exclusiveCount > 1)
      {
        output.Error("changers can't be combined here: (click:)/revision/(for:) each consume the hook differently");
        return;
      }

      if (descriptor.Interaction != null)
      {
        RunInteraction(descriptor, output, renderHook, ctx);
      }
      else if (descriptor.Revision != null)
      {
        RunRevision(descriptor, output, renderHook, ctx);
      }
      else if (descriptor.Iteration != null)
      {
        if (ctx == null)
        {
          output.Error("(for:) requires a render context with a variable store");
          return;
        }
        RunIteration(descriptor, output, renderHook, ctx);
      }
      else
      {
        RunStyles(descriptor.Styles, output, renderHook);
      }
    }

    private static void RunIteration(HookDescriptor d, IRenderOutput output, System.Action<IRenderOutput> renderHook, MacroContext ctx)
    {
      var iter = d.Iteration;
      if (iter.Items == null) return;
      for (int i = 0; i < iter.Items.Count; i++)
      {
        var item = iter.Items[i];
        if (item != null && item.IsError) { output.Error(item.ErrorMessage); continue; }
        using (ctx.Store.PushItBinding(item))
        using (ctx.Store.PushPosBinding(i + 1))
        using (ctx.Store.PushBinding(iter.ParamName, iter.ParamIsTemporary, item))
        {
          RunStyles(d.Styles, output, renderHook);
        }
      }
    }

    private static void RunStyles(List<StyleSpec> styles, IRenderOutput output, System.Action<IRenderOutput> renderHook)
    {
      for (int i = 0; i < styles.Count; i++) output.PushStyle(styles[i]);
      renderHook?.Invoke(output);
      for (int i = styles.Count - 1; i >= 0; i--) output.PopStyle();
    }

    /// <summary>
    /// Execute a revision changer. The hook content is rendered into a detached
    /// <see cref="RenderTreeBuilder"/> — with any composed style layers wrapped
    /// around it — then spliced into every node the target resolves to in the
    /// live render tree. Targeting prefers <see cref="MacroContext.LiveRoot"/>
    /// so a dispatch-time deferred render (whose own output is a detached
    /// builder, not the live tree) still mutates the right tree; it falls back
    /// to the output's root if no context is supplied. A target not in the
    /// live tree (declared after this macro on the main render, or absent
    /// entirely) means no match and the source is simply not shown —
    /// Harlowe no-ops the same way. Each match gets its own deep clone of the
    /// source so the tree stays a tree.
    /// </summary>
    private static void RunRevision(HookDescriptor d, IRenderOutput output, System.Action<IRenderOutput> renderHook, MacroContext ctx)
    {
      // Render the source into a detached subtree, styles wrapping the content.
      var detached = new RenderTreeBuilder();
      for (int i = 0; i < d.Styles.Count; i++) detached.PushStyle(d.Styles[i]);
      renderHook?.Invoke(detached);
      for (int i = d.Styles.Count - 1; i >= 0; i--) detached.PopStyle();
      var source = detached.Root.Children;

      var liveRoot = MacroContext.ResolveLiveRoot(ctx, output);
      if (liveRoot == null) return;

      var rev = d.Revision;
      IReadOnlyList<RenderNode> targets;
      if (rev.HookTarget != null)
      {
        targets = HookResolver.Resolve(liveRoot, rev.HookTarget);
      }
      else if (rev.StringTarget != null)
      {
        var wraps = TextOccurrenceFinder.FindAndWrap(liveRoot, rev.StringTarget);
        var list = new List<RenderNode>(wraps.Count);
        for (int i = 0; i < wraps.Count; i++) list.Add(wraps[i]);
        targets = list;
      }
      else
      {
        return;
      }

      for (int i = 0; i < targets.Count; i++)
        if (targets[i] is IRenderContainer container)
          RenderNodes.Splice(container, source, rev.Mode);
    }

    /// <summary>
    /// Execute an interaction changer. Records a persistent
    /// <see cref="Interaction"/> on <see cref="MacroContext.Interactions"/>
    /// (target query, composed style layers, deferred-hook renderer, mode,
    /// kind, and a stable region id) rather than resolving and wrapping inline.
    /// <see cref="InteractionPass"/> re-resolves and re-wraps it after the body
    /// render and after every dispatch, so a target declared later in the
    /// passage — or created by a click-deferred hook — is still caught. (Eager
    /// apply-time resolution used to miss those; see TODO #24.)
    /// </summary>
    private static void RunInteraction(HookDescriptor d, IRenderOutput output, System.Action<IRenderOutput> renderHook, MacroContext ctx)
    {
      if (ctx == null) { output.Error("interaction changers require a render context"); return; }

      var spec = d.Interaction;
      if (spec?.HookTarget == null) return;

      // Snapshot the composed style layers; InteractionPass clones them again
      // per match, so these stay an immutable template across passes.
      var styles = new List<StyleSpec>(d.Styles.Count);
      for (int i = 0; i < d.Styles.Count; i++) styles.Add(d.Styles[i]?.Clone());

      ctx.Interactions.Add(new Interaction
      {
        Target = spec.HookTarget,
        Kind = spec.Kind,
        Mode = spec.Mode,
        Styles = styles,
        RenderDeferredHook = renderHook,
        RegionId = ctx.AllocateRegionId()
      });
    }

    /// <summary>
    /// Apply this changer's effect to an <em>already-rendered</em> container in
    /// place — the mechanism behind <c>(change:)</c> and <c>(enchant:)</c>,
    /// which target existing render-tree nodes rather than an attached hook.
    /// The target's children are wrapped in nested <see cref="RenderStyleNode"/>s
    /// (outermost = first composed layer), matching the nesting
    /// <see cref="Apply"/> produces for a hook.
    ///
    /// <para>
    /// Only the changer's style layers are meaningful here: iteration and
    /// revision patches describe how to <em>produce</em> content, not how to
    /// restyle existing content, so they are ignored. A changer with no style
    /// layers is a no-op.
    /// </para>
    ///
    /// <para>
    /// <paramref name="source"/> tags every created <see cref="RenderStyleNode"/>
    /// with the enchantment that produced it, so a future
    /// <see cref="EnchantmentPass.Update"/> can unwrap the prior pass before
    /// re-applying — necessary now that dispatch re-renders run the enchant
    /// pass more than once. Pass <c>null</c> (the default) for one-shot
    /// <c>(change:)</c> applications, whose wraps survive re-passes intact.
    /// </para>
    /// </summary>
    public void ApplyTo(IRenderContainer target, Enchantment source = null)
    {
      if (target == null) return;

      var descriptor = new HookDescriptor();
      for (int i = 0; i < _patches.Count; i++) _patches[i].Apply(descriptor);
      var styles = descriptor.Styles;
      if (styles.Count == 0) return;

      var content = new List<RenderNode>(target.Children);
      for (int i = styles.Count - 1; i >= 0; i--)
      {
        // Clone the descriptor's style on the way onto the tree so the
        // enchantment can be re-applied to multiple targets across passes
        // without nodes sharing a StyleSpec reference.
        var styleNode = new RenderStyleNode { Style = styles[i]?.Clone(), SourceEnchantment = source };
        styleNode.Children.AddRange(content);
        content = new List<RenderNode> { styleNode };
      }
      target.Children.Clear();
      target.Children.AddRange(content);
    }

    /// <summary>
    /// Structural equality: two changers are equal iff their patch lists are
    /// equal pair-wise (each <see cref="IChangerPatch"/> implements its own
    /// structural equality). Lets <see cref="HarloweValue.Equals"/> recurse
    /// into Changer values without special-casing.
    /// </summary>
    public override bool Equals(object obj)
    {
      if (!(obj is Changer other)) return false;
      if (_patches.Count != other._patches.Count) return false;
      for (int i = 0; i < _patches.Count; i++)
      {
        if (!Equals(_patches[i], other._patches[i])) return false;
      }
      return true;
    }

    public override int GetHashCode()
    {
      int h = _patches.Count;
      for (int i = 0; i < _patches.Count; i++)
        h = (h * 397) ^ (_patches[i]?.GetHashCode() ?? 0);
      return h;
    }
  }
}
