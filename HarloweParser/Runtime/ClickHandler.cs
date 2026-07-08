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
  /// then either fills <see cref="RevealAnchor"/> with the result (plain
  /// macros — the attached hook shows at its own position) or splices it into
  /// every target using <see cref="Mode"/> (combos — the same revision
  /// machinery <c>(replace:)</c> uses). Single-use unless <see cref="Once"/> is
  /// false (<c>(click-rerun:)</c>).
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

    /// <summary>Hook-name target — re-resolved against the live tree at dispatch time. Null for string targets.</summary>
    public HookNameValue Target;

    /// <summary>
    /// The string-occurrence wraps the last <see cref="InteractionPass.Update"/>
    /// produced for a string target — the combo splice targets, still in the
    /// tree because nothing mutates it between the pass and the dispatch.
    /// Null for hook-name targets.
    /// </summary>
    public List<Rendering.IRenderContainer> StringWraps;

    /// <summary>Combo splice mode, or null for plain macros (reveal at <see cref="RevealAnchor"/>).</summary>
    public RevisionMode? Mode;

    /// <summary>False for <c>(click-rerun:)</c>: the interaction survives the dispatch and re-renders on each activation.</summary>
    public bool Once = true;

    /// <summary>Which kind of event the engine reports for this region.</summary>
    public InteractionKind Kind;

    /// <summary>Composed style layers wrapped around the deferred content when it renders (outermost first).</summary>
    public List<StyleSpec> Styles;

    /// <summary>The reveal anchor planted at the macro's position, for plain (non-combo) interactions. Null for combos.</summary>
    public Rendering.RenderHookNode RevealAnchor;
  }
}
