using System.Collections.Generic;

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
    /// it. When the descriptor carries an <see cref="IterationSpec"/> the
    /// renderer loops, binding parameter + <c>it</c> per item and emitting
    /// the style wrappers around each iteration's hook render; otherwise
    /// styles are emitted once around a single <paramref name="renderHook"/>
    /// call.
    ///
    /// <para>
    /// <paramref name="ctx"/> is only required when iteration is in play —
    /// pure style changers can pass null. The lambda binding routes through
    /// <see cref="IVariableStore.PushBinding"/> / <see cref="IVariableStore.PushItBinding"/>,
    /// so per-iteration scope is correctly restored even on early hook exit.
    /// </para>
    /// </summary>
    public void Apply(IRenderOutput output, System.Action renderHook, MacroContext ctx = null)
    {
      var descriptor = new HookDescriptor();
      for (int i = 0; i < _patches.Count; i++) _patches[i].Apply(descriptor);

      if (descriptor.Iteration != null)
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

    private static void RunIteration(HookDescriptor d, IRenderOutput output, System.Action renderHook, MacroContext ctx)
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

    private static void RunStyles(List<StyleSpec> styles, IRenderOutput output, System.Action renderHook)
    {
      for (int i = 0; i < styles.Count; i++) output.PushStyle(styles[i]);
      renderHook?.Invoke();
      for (int i = styles.Count - 1; i >= 0; i--) output.PopStyle();
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
