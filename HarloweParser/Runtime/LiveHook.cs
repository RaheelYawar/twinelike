using System;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A hook a <c>(live:)</c> or <c>(after:)</c> changer is attached to, waiting
  /// to be rendered. Reference's <c>runLiveHook</c> in <c>ts/section.ts</c>
  /// counts a <c>(live:)</c> hook down by the time between animation frames,
  /// renders it once the delay has passed, replacing what it showed, and starts
  /// again, until a render runs <c>(stop:)</c>. An <c>(after:)</c> hook has no
  /// count: each frame it checks <c>time</c> and renders once. Here each
  /// <see cref="StorySession.Tick"/> is a frame.
  /// </summary>
  internal sealed class LiveHook
  {
    /// <summary>The node it was planted as, and renders into. It's rendered into even off the page, as reference keeps running a live hook whose element was replaced.</summary>
    public RenderHookNode Node;

    /// <summary>Milliseconds between renders. At 0 or less it renders every frame.</summary>
    public double Delay;

    /// <summary>Milliseconds left until the next render.</summary>
    public double Remaining;

    /// <summary>
    /// False until the first frame after it was made, which only starts the
    /// count, as reference's first frame callback has no earlier timestamp to
    /// subtract from.
    /// </summary>
    public bool Started;

    /// <summary>
    /// For <c>(after:)</c>: the <c>time</c> it renders after, lowered by
    /// <see cref="Skip"/> on each frame a key or button is held, as reference's
    /// event filter lowers its own copy. Null for <c>(live:)</c>.
    /// </summary>
    public double? After;

    /// <summary>For <c>(after:)</c>: milliseconds taken off <see cref="After"/> on each frame a key or button is held.</summary>
    public double Skip;

    /// <summary>Renders the hook's content into a node, given the page's root.</summary>
    public Action<RenderHookNode, RenderRoot> Render;

    /// <summary>Set once a render ran <c>(stop:)</c>, or an <c>(after:)</c> hook rendered.</summary>
    public bool Stopped;
  }
}
