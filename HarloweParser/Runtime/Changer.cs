using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A composable styling primitive. Returned by changer macros like
  /// <c>(text-style: "bold")</c>; combined via <see cref="Compose"/> when the
  /// <c>+</c> operator joins two changers; applied by <see cref="BodyRenderer"/>
  /// when the changer sits in front of an attached hook.
  ///
  /// <para>Internally a Changer is a flat list of <see cref="StyleSpec"/>
  /// layers. <see cref="Apply"/> emits a <see cref="IRenderOutput.PushStyle"/>
  /// for each layer in list order, then asks the caller to render the hook
  /// contents, then emits a matching <see cref="IRenderOutput.PopStyle"/> per
  /// layer (no order argument needed — engines pop a stack). The first layer
  /// is the *outermost* in the rendered output, matching the reading order
  /// of <c>A + B</c>: A wraps B wraps content. Composition is concatenation
  /// of the layer lists.</para>
  ///
  /// <para>The shape is engine-agnostic: a layer is described by a
  /// <see cref="StyleSpec"/>, not an HTML snippet. Engine integrations
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
    private readonly List<StyleSpec> _layers;

    private Changer(List<StyleSpec> layers)
    {
      _layers = layers;
    }

    /// <summary>
    /// Construct a changer with a single styling layer. Most changer macros
    /// call this once with the spec describing what they want around the
    /// hook content (e.g. <c>new StyleSpec { Bold = true }</c>).
    /// </summary>
    public static Changer FromStyle(StyleSpec style)
      => new Changer(new List<StyleSpec> { style ?? new StyleSpec() });

    /// <summary>
    /// Compose this changer with <paramref name="other"/>, producing a new
    /// changer whose layer list is <c>this</c>'s followed by
    /// <paramref name="other"/>'s. Reading order: <c>A + B</c> means A wraps
    /// B wraps content — matches the left-to-right authoring order in source.
    /// </summary>
    public Changer Compose(Changer other)
    {
      if (other == null) return this;
      var combined = new List<StyleSpec>(_layers.Count + other._layers.Count);
      combined.AddRange(_layers);
      combined.AddRange(other._layers);
      return new Changer(combined);
    }

    /// <summary>
    /// Apply this changer to a hook: emit one <see cref="IRenderOutput.PushStyle"/>
    /// per layer in order, invoke <paramref name="renderHook"/> to emit the hook's
    /// contents, then emit the matching <see cref="IRenderOutput.PopStyle"/> calls
    /// in reverse so the wrapping nests correctly.
    /// </summary>
    public void Apply(IRenderOutput output, System.Action renderHook)
    {
      for (int i = 0; i < _layers.Count; i++)
        output.PushStyle(_layers[i]);
      renderHook?.Invoke();
      for (int i = _layers.Count - 1; i >= 0; i--)
        output.PopStyle();
    }

    /// <summary>
    /// Read-only view of the styling layers, outermost first. Engine
    /// integrations rarely need this — prefer <see cref="Apply"/>. Exposed
    /// for adapter authors and diagnostics.
    /// </summary>
    public IReadOnlyList<StyleSpec> Layers => _layers;

    /// <summary>
    /// Structural equality: two changers are equal iff their layer lists are
    /// equal pair-wise. Lets <see cref="HarloweValue.Equals"/> recurse into
    /// Changer values without special-casing.
    /// </summary>
    public override bool Equals(object obj)
    {
      if (!(obj is Changer other)) return false;
      if (_layers.Count != other._layers.Count) return false;
      for (int i = 0; i < _layers.Count; i++)
      {
        if (!Equals(_layers[i], other._layers[i])) return false;
      }
      return true;
    }

    public override int GetHashCode()
    {
      int h = _layers.Count;
      for (int i = 0; i < _layers.Count; i++)
        h = (h * 397) ^ (_layers[i]?.GetHashCode() ?? 0);
      return h;
    }
  }
}
