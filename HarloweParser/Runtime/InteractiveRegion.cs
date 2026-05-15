namespace Harlowe.Runtime
{
  /// <summary>
  /// Kind of interaction a click/hover macro registers on a region. Engine
  /// integrations map each kind to whatever native event their UI exposes —
  /// pointer click, hover-enter, hover-leave.
  /// </summary>
  public enum InteractionKind
  {
    /// <summary>Pointer click — <c>(click:)</c>, <c>(click-replace:)</c>, etc.</summary>
    Click,

    /// <summary>Pointer entered the region — <c>(mouseover:)</c>, <c>(mouseover-append:)</c>, etc.</summary>
    MouseOver,

    /// <summary>Pointer left the region — <c>(mouseout:)</c>, <c>(mouseout-append:)</c>, etc.</summary>
    MouseOut
  }

  /// <summary>
  /// One region of rendered content that participates in an interaction —
  /// emitted around the wrapped content by
  /// <see cref="IRenderOutput.BeginInteractive"/> / <see cref="IRenderOutput.EndInteractive"/>
  /// and identified to the host engine by <see cref="Id"/>. The engine reports
  /// an event for this region back via <see cref="StorySession.DispatchEvent"/>,
  /// which fires the deferred prose into the target.
  ///
  /// <para>
  /// Ids are opaque tokens chosen by the runtime. They are unique within a
  /// single render pass — fresh per <see cref="StorySession.Render"/> or
  /// <see cref="StorySession.Goto"/> — so an engine that caches them across
  /// renders should discard the cache after each render.
  /// </para>
  /// </summary>
  public class InteractiveRegion
  {
    /// <summary>Opaque dispatch key; pass back to <see cref="StorySession.DispatchEvent"/>.</summary>
    public string Id;

    /// <summary>Which interaction kind fires this region.</summary>
    public InteractionKind Kind;
  }
}
