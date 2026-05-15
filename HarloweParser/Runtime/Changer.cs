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

      if (descriptor.Revision != null)
      {
        RunRevision(descriptor, output, renderHook);
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
    /// live render tree. Targeting sees the tree built so far (the content
    /// above this macro), matching Harlowe's <c>selectHook</c> scope: a target
    /// declared <em>after</em> this macro, or no render tree at all (a plain
    /// buffer in a unit test), means no match and the source is simply not
    /// shown — Harlowe no-ops the same way, since the DOM doesn't exist yet.
    /// Each match gets its own deep clone of the source so the tree stays a
    /// tree.
    /// </summary>
    private static void RunRevision(HookDescriptor d, IRenderOutput output, System.Action<IRenderOutput> renderHook)
    {
      // Render the source into a detached subtree, styles wrapping the content.
      var detached = new RenderTreeBuilder();
      for (int i = 0; i < d.Styles.Count; i++) detached.PushStyle(d.Styles[i]);
      renderHook?.Invoke(detached);
      for (int i = d.Styles.Count - 1; i >= 0; i--) detached.PopStyle();
      var source = detached.Root.Children;

      // Without a live render tree there is nothing to target — no-op.
      if (!(output is RenderTreeBuilder builder)) return;

      var rev = d.Revision;
      IReadOnlyList<RenderNode> targets;
      if (rev.HookTarget != null)
      {
        targets = HookResolver.Resolve(builder.Root, rev.HookTarget);
      }
      else if (rev.StringTarget != null)
      {
        var wraps = TextOccurrenceFinder.FindAndWrap(builder.Root, rev.StringTarget);
        var list = new List<RenderNode>(wraps.Count);
        for (int i = 0; i < wraps.Count; i++) list.Add(wraps[i]);
        targets = list;
      }
      else
      {
        return;
      }

      for (int i = 0; i < targets.Count; i++)
        Splice(targets[i], source, rev.Mode);
    }

    /// <summary>
    /// Splice a deep clone of <paramref name="source"/> into
    /// <paramref name="target"/>. Non-container targets are skipped — there is
    /// nowhere to put content. <see cref="RevisionMode.Replace"/> clears the
    /// target's children first; append/prepend add at the end/start.
    /// </summary>
    private static void Splice(RenderNode target, List<RenderNode> source, RevisionMode mode)
    {
      if (!(target is IRenderContainer container)) return;
      var copy = RenderNodes.CloneAll(source);
      switch (mode)
      {
        case RevisionMode.Replace:
          container.Children.Clear();
          container.Children.AddRange(copy);
          break;
        case RevisionMode.Append:
          container.Children.AddRange(copy);
          break;
        case RevisionMode.Prepend:
          container.Children.InsertRange(0, copy);
          break;
      }
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
    /// </summary>
    public void ApplyTo(IRenderContainer target)
    {
      if (target == null) return;

      var descriptor = new HookDescriptor();
      for (int i = 0; i < _patches.Count; i++) _patches[i].Apply(descriptor);
      var styles = descriptor.Styles;
      if (styles.Count == 0) return;

      var content = new List<RenderNode>(target.Children);
      for (int i = styles.Count - 1; i >= 0; i--)
      {
        var styleNode = new RenderStyleNode { Style = styles[i] };
        styleNode.Children.AddRange(content);
        content = new List<RenderNode> { styleNode };
      }
      target.Children.Clear();
      target.Children.AddRange(content);
    }

    /// <summary>
    /// Read-only view of the styling layers this changer contributes — built
    /// on demand by running the patches against a fresh descriptor. Engine
    /// integrations rarely need this; prefer <see cref="Apply"/>. Exposed for
    /// adapter authors and diagnostics. Note: iteration changers may have an
    /// empty layer list; check <see cref="HasIteration"/> too.
    /// </summary>
    public IReadOnlyList<StyleSpec> Layers
    {
      get
      {
        var d = new HookDescriptor();
        for (int i = 0; i < _patches.Count; i++) _patches[i].Apply(d);
        return d.Styles;
      }
    }

    /// <summary>True if any patch contributes an iteration spec.</summary>
    public bool HasIteration
    {
      get
      {
        for (int i = 0; i < _patches.Count; i++)
          if (_patches[i] is IterationPatch) return true;
        return false;
      }
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
