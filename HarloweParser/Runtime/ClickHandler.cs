using System;
using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A registered interaction handler: the closure needed to render the
  /// deferred hook on event, plus where its source goes. One handler per
  /// <see cref="InteractiveRegion"/> id; the <see cref="StorySession"/> keeps
  /// these alive between renders so an engine can report events whenever the
  /// user actually interacts. Rebuilt from <see cref="Interaction"/> by
  /// <see cref="InteractionPass.Update"/> every pass.
  ///
  /// <para>
  /// The handler is fired by <see cref="StorySession.DispatchEvent"/>: it runs
  /// <see cref="RenderDeferredHook"/> into a detached
  /// <see cref="Rendering.RenderTreeBuilder"/> (wrapped in <see cref="Styles"/>),
  /// then either fills the reveal anchor(s) with the result (plain macros —
  /// the attached hook shows at the macro's own position; anchors are found in
  /// the live tree by their <see cref="Rendering.RenderHookNode.RevealRegionId"/>
  /// tag) or splices it into every target using <see cref="Mode"/> (combos —
  /// the same revision machinery <c>(replace:)</c> uses; string-occurrence
  /// targets are found by their <see cref="Rendering.RenderHookNode.SourceRegionId"/>
  /// tag). Tag-based resolution means a node that reached the tree as a clone
  /// still resolves. Single-use unless <see cref="Once"/> is false
  /// (<c>(click-rerun:)</c>).
  /// </para>
  /// </summary>
  public class ClickHandler
  {
    /// <summary>
    /// Render the deferred hook into the given output. Closes over the
    /// original <see cref="BodyRenderer"/> from the render that registered
    /// this handler, so its registry, store, and context are preserved.
    /// </summary>
    public Action<IRenderOutput> RenderDeferredHook;

    /// <summary>Hook-name target — re-resolved against the live tree at dispatch time. Null for string targets, whose occurrence wraps are resolved by region-id tag instead.</summary>
    public HookNameValue Target;

    /// <summary>Combo splice mode, or null for plain macros (reveal at the tagged anchor).</summary>
    public RevisionMode? Mode;

    /// <summary>False for <c>(click-rerun:)</c>: the interaction survives the dispatch and re-renders on each activation.</summary>
    public bool Once = true;

    /// <summary>Which kind of event the engine reports for this region.</summary>
    public InteractionKind Kind;

    /// <summary>Composed style layers wrapped around the deferred content when it renders (outermost first).</summary>
    public List<StyleSpec> Styles;
  }
}
