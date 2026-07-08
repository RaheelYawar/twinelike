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
  /// own. <see cref="HarloweValue.ToHarloweString"/> prints them as
  /// <c>[A (text-style:) changer]</c>, mirroring reference's
  /// <c>print()</c> in <c>ts/datatypes/changer.ts</c>.</para>
  /// </summary>
  public class Changer
  {
    private readonly List<IChangerPatch> _patches;

    /// <summary>
    /// The Harlowe source that created this changer — <c>(text-style:"bold")</c>,
    /// or <c>(text-style:"bold")+(text-colour:"red")</c> for a composed one. Stamped
    /// by the <see cref="ExpressionEvaluator"/> at the macro-call and <c>+</c>-compose
    /// sites (reference keeps <c>macroName</c>+<c>params</c> and regenerates source the
    /// same way in <c>changer.ts</c>'s <c>TwineScript_ToSource</c>). Travels with the
    /// value, so a changer read back from a <c>$var</c> keeps its creation-time source —
    /// which is how <see cref="HarloweValue.ToSource"/> serialises it. Null when
    /// unstamped or composed from an unstamped operand: such a changer has no source
    /// form and can't be saved (the save fails loudly).
    /// </summary>
    public string Source;

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
    /// …). When applied, the changer records a persistent <see cref="Interaction"/>
    /// (and, for plain non-combo macros, plants the reveal anchor the dispatch
    /// fills); <see cref="InteractionPass"/> wraps every match of the spec's
    /// target in a <see cref="RenderInteractiveNode"/> and registers the
    /// deferred <see cref="ClickHandler"/> keyed by the region id — see
    /// <see cref="Apply"/>.
    /// </summary>
    public static Changer FromInteraction(InteractionSpec interaction)
      => new Changer(new List<IChangerPatch> { new InteractionPatch { Interaction = interaction } });

    /// <summary>
    /// Construct a conditional changer (<c>(if:)</c>/<c>(unless:)</c>/
    /// <c>(else-if:)</c>/<c>(else:)</c>): a single <see cref="ConditionalPatch"/>
    /// that ANDs its decision into <see cref="HookDescriptor.Enabled"/>, so
    /// <c>(if: $cond) + (text-style: "bold")</c> composes like any other changer
    /// (reference's <c>new Changer(`if`, [expr])</c>).
    /// </summary>
    public static Changer FromConditional(ConditionalKind kind, bool value)
      => new Changer(new List<IChangerPatch> { new ConditionalPatch { Kind = kind, Value = value } });

    /// <summary>
    /// Construct the changer <c>(else:)</c>/<c>(else-if:)</c> return: their
    /// decision is baked from the pairing state at call time, so the result is
    /// an <see cref="ConditionalKind.If"/>-kind patch pre-stamped with the
    /// equivalent <c>(if:)</c> source. Baking the kind too keeps the stored
    /// changer structurally identical to what a save/load re-evaluates it into
    /// (a natural <c>(else:)</c> stamp would error on load, where no pairing is
    /// in scope — reference's <c>toSource()</c> emits a bare <c>(else:)</c> and
    /// has that exact problem).
    /// </summary>
    public static Changer FromBakedConditional(bool show)
    {
      var changer = FromConditional(ConditionalKind.If, show);
      changer.Source = show ? "(if:true)" : "(if:false)";
      return changer;
    }

    /// <summary>
    /// The macro name at the front of <see cref="Source"/> — "if" for
    /// <c>(if:true)+(text-style:"bold")</c> — or null when unstamped. The
    /// analogue of reference's <c>macroName</c> (the head of a composed chain):
    /// used by the unattached-changer error and the printed form.
    /// </summary>
    public string FrontMacroName
    {
      get
      {
        if (Source == null || Source.Length < 3 || Source[0] != '(') return null;
        int colon = Source.IndexOf(':');
        return colon > 1 ? Source.Substring(1, colon - 1) : null;
      }
    }

    /// <summary>
    /// Whether this changer may be given to <c>(change:)</c>/<c>(enchant:)</c>:
    /// false when any patch is a revision or interaction — those describe how to
    /// <em>produce</em> content, which makes no sense against already-rendered
    /// targets. The analogue of reference Harlowe's <c>canEnchant</c> flag on
    /// <c>ts/datatypes/changer.ts</c> (set false for the revision/interaction/link
    /// macros, ANDed on compose — our derived form ANDs for free because
    /// <see cref="Compose"/> concatenates patch lists).
    /// </summary>
    public bool CanEnchant
    {
      get
      {
        for (int i = 0; i < _patches.Count; i++)
          if (_patches[i] is RevisionPatch || _patches[i] is InteractionPatch) return false;
        return true;
      }
    }

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
    ///
    /// <para>Returns whether the descriptor was <em>enabled</em>: false when a
    /// composed conditional (<see cref="ConditionalPatch"/>) suppressed the hook,
    /// in which case nothing renders and nothing registers. The renderer feeds
    /// this into the <c>(else:)</c> pairing (reference's <c>lastHookShown</c>).</para>
    /// </summary>
    public bool Apply(IRenderOutput output, System.Action<IRenderOutput> renderHook, MacroContext ctx = null)
    {
      var descriptor = BuildDescriptor();

      // A conditional in the composition disabled the hook: render nothing,
      // register nothing — (if: false) + (click: ?a) must not arm a click.
      if (!descriptor.Enabled) return false;

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
        return true;
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
          return true;
        }
        RunIteration(descriptor, output, renderHook, ctx);
      }
      else
      {
        RunStyles(descriptor.Styles, output, renderHook);
      }
      return true;
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
    /// apply-time resolution used to miss those.)
    ///
    /// <para>
    /// For a plain (non-combo) interaction, also plants an empty anonymous
    /// hook at the current output position, tagged with the region id — the
    /// reveal anchor the dispatch fills with the deferred hook's render,
    /// mirroring reference's hidden attached-hook element. The dispatch finds
    /// it by tag (clones inherit it), so an anchor planted in a detached
    /// builder and spliced into the live tree still reveals. The composed
    /// style layers wrap that deferred render, not the armed region
    /// (reference applies the descriptor's styles at the event's
    /// <c>renderInto</c>); the armed region is styled by the macro's optional
    /// second argument.
    /// </para>
    /// </summary>
    private static void RunInteraction(HookDescriptor d, IRenderOutput output, System.Action<IRenderOutput> renderHook, MacroContext ctx)
    {
      if (ctx == null) { output.Error("interaction changers require a render context"); return; }

      var spec = d.Interaction;
      if (spec == null || (spec.HookTarget == null && spec.StringTarget == null)) return;

      // Snapshot the composed style layers; the dispatch clones them again
      // when rendering, so these stay an immutable template across passes.
      var styles = new List<StyleSpec>(d.Styles.Count);
      for (int i = 0; i < d.Styles.Count; i++) styles.Add(d.Styles[i]?.Clone());

      string regionId = ctx.AllocateRegionId();
      if (spec.Mode == null && output is Rendering.RenderTreeBuilder builder)
        builder.PlantAnchor(regionId);

      ctx.Interactions.Add(new Interaction
      {
        Target = spec.HookTarget,
        StringTarget = spec.StringTarget,
        Kind = spec.Kind,
        Mode = spec.Mode,
        Once = spec.Once,
        ArmChanger = spec.ArmChanger,
        ArmLambda = spec.ArmLambda,
        Styles = styles,
        RenderDeferredHook = renderHook,
        RegionId = regionId
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
      var styles = BuildStyles();
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
    /// Apply this changer's style layers to a resolved target, dispatching on its
    /// shape: a container (a hook) has its children wrapped via
    /// <see cref="ApplyTo(IRenderContainer, Enchantment)"/>; a <em>leaf</em> (a
    /// <see cref="RenderLinkNode"/> matched by <c>?link</c>) is wrapped node-and-all
    /// in place within <paramref name="root"/> via <see cref="ApplyToNode"/>. Lets
    /// <c>(enchant:)</c>/<c>(change:)</c> style links — reference Harlowe's <c>?Link</c>
    /// built-in target.
    /// </summary>
    public void ApplyToTarget(IRenderContainer root, RenderNode target, Enchantment source = null)
    {
      if (target is IRenderContainer container) ApplyTo(container, source);
      else ApplyToNode(root, target, source);
    }

    /// <summary>
    /// Wrap a single leaf <paramref name="target"/> in this changer's style layers,
    /// replacing it in place within <paramref name="root"/> — the leaf has no children
    /// of its own, so the wrap goes around the node itself. The style nodes carry
    /// <paramref name="source"/> so <see cref="EnchantmentPass.Disenchant"/> unwraps
    /// them by tag, exactly as on the container path.
    /// </summary>
    public void ApplyToNode(IRenderContainer root, RenderNode target, Enchantment source = null)
    {
      if (root == null || target == null) return;
      var styles = BuildStyles();
      if (styles.Count == 0) return;

      RenderNode wrapped = target;
      for (int i = styles.Count - 1; i >= 0; i--)
      {
        var styleNode = new RenderStyleNode { Style = styles[i]?.Clone(), SourceEnchantment = source };
        styleNode.Children.Add(wrapped);
        wrapped = styleNode;
      }
      RenderNodes.ReplaceChild(root, target, wrapped);
    }

    /// <summary>Run every patch against a fresh descriptor — the single place the patch list is folded, shared by <see cref="Apply"/> and <see cref="BuildStyles"/>.</summary>
    private HookDescriptor BuildDescriptor()
    {
      var descriptor = new HookDescriptor();
      for (int i = 0; i < _patches.Count; i++) _patches[i].Apply(descriptor);
      return descriptor;
    }

    /// <summary>Build the descriptor and return its style layers — the shared front of <see cref="ApplyTo"/> / <see cref="ApplyToNode"/>, also read by <see cref="InteractionPass"/> for armed-region styling. Empty when a composed conditional disabled the descriptor, so <c>(change: ?x, (if: false) + (colour: red))</c> applies nothing.</summary>
    internal List<StyleSpec> GetStyleLayers() => BuildStyles();

    private List<StyleSpec> BuildStyles()
    {
      var descriptor = BuildDescriptor();
      if (!descriptor.Enabled) descriptor.Styles.Clear();
      return descriptor.Styles;
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
