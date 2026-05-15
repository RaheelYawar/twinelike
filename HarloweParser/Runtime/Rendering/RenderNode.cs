using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// Base type for a node in the internal render tree. <see cref="BodyRenderer"/>
  /// builds a tree of these (via <see cref="RenderTreeBuilder"/>) instead of
  /// pushing events straight at an <see cref="IRenderOutput"/>; the finished
  /// tree is an addressable, mutable representation of rendered passage content.
  /// <see cref="RenderTreeFlusher"/> walks it and emits the same event stream
  /// the renderer used to emit directly.
  ///
  /// <para>
  /// The tree exists so revision/enchantment macros (later sub-slices) can
  /// target content that is <em>already rendered</em> — a pure linear event
  /// push has nothing to point at. In this foundation slice nothing mutates the
  /// tree: it is flushed verbatim and the emitted stream is byte-identical to
  /// the pre-refactor renderer.
  /// </para>
  ///
  /// <para>
  /// Mirrors the reference Harlowe runtime's DOM-of-<c>&lt;tw-hook&gt;</c>
  /// elements; <see cref="RenderHookNode"/> is the analogue of a hook element,
  /// the addressable unit a <c>?name</c> reference resolves against.
  /// </para>
  /// </summary>
  public abstract class RenderNode
  {
  }

  /// <summary>A render node that holds ordered child nodes.</summary>
  public interface IRenderContainer
  {
    /// <summary>Child nodes in document order.</summary>
    List<RenderNode> Children { get; }
  }

  /// <summary>Plain prose text. Flushes to <see cref="IRenderOutput.Text"/>.</summary>
  public class RenderTextNode : RenderNode
  {
    public string Content;
  }

  /// <summary>Raw author HTML pass-through. Flushes to <see cref="IRenderOutput.Html"/>.</summary>
  public class RenderHtmlNode : RenderNode
  {
    public string RawHtml;
  }

  /// <summary>A passage-to-passage navigation link. Flushes to <see cref="IRenderOutput.Link"/>.</summary>
  public class RenderLinkNode : RenderNode
  {
    public string Text;
    public string Target;
  }

  /// <summary>An in-prose error message. Flushes to <see cref="IRenderOutput.Error"/>.</summary>
  public class RenderErrorNode : RenderNode
  {
    public string Message;
  }

  /// <summary>
  /// A styling layer. Replaces a <see cref="IRenderOutput.PushStyle"/> /
  /// content / <see cref="IRenderOutput.PopStyle"/> bracket: the flusher emits
  /// <c>PushStyle</c>, flushes <see cref="Children"/>, then <c>PopStyle</c>.
  /// </summary>
  public class RenderStyleNode : RenderNode, IRenderContainer
  {
    public StyleSpec Style;
    public List<RenderNode> Children { get; } = new List<RenderNode>();
  }

  /// <summary>
  /// A hook — the addressable unit. Anonymous hooks still produce one
  /// (<see cref="Name"/> is <c>null</c>) so position/string targeting can find
  /// them; named hooks carry their name. The flusher emits no event of its own
  /// for a hook — it is structural — so flushing a hook is just flushing its
  /// <see cref="Children"/>, matching the pre-refactor renderer where a hook
  /// produced no output of its own.
  /// </summary>
  public class RenderHookNode : RenderNode, IRenderContainer
  {
    /// <summary>The hook's name without delimiters; <c>null</c> for anonymous hooks.</summary>
    public string Name;

    /// <summary>Where the name was anchored in source.</summary>
    public HookAnchor Anchor;

    public List<RenderNode> Children { get; } = new List<RenderNode>();
  }

  /// <summary>Top of a passage render. The tree a <see cref="RenderTreeBuilder"/> produces.</summary>
  public class RenderRoot : RenderNode, IRenderContainer
  {
    public List<RenderNode> Children { get; } = new List<RenderNode>();
  }
}
