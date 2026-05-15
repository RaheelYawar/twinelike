using System;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A registered interaction handler: the closure needed to render the
  /// deferred hook on event, plus the spec describing where its source goes.
  /// One handler per <see cref="InteractiveRegion"/> id; the
  /// <see cref="StorySession"/> keeps these alive between renders so an engine
  /// can report events whenever the user actually interacts.
  ///
  /// <para>
  /// The handler is fired by <see cref="StorySession.DispatchEvent"/>: it
  /// unwraps the interactive region (consuming it), runs
  /// <see cref="RenderDeferredHook"/> into a detached
  /// <see cref="Rendering.RenderTreeBuilder"/>, then splices the result into
  /// every node re-resolved from <see cref="Target"/> using <see cref="Mode"/>
  /// — the same revision machinery <c>(replace:)</c> uses, just deferred.
  /// Single-use: after firing, the handler is removed from the registry.
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

    /// <summary>Hook-name target — re-resolved against the live tree at dispatch time.</summary>
    public HookNameValue Target;

    /// <summary>How the deferred hook's source is spliced into the target.</summary>
    public RevisionMode Mode;

    /// <summary>Which kind of event the engine reports for this region.</summary>
    public InteractionKind Kind;
  }
}
